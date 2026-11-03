using System;
using System.Collections.Generic;
using System.Linq;

namespace CriticalPath
{
    // Most important first.
    public enum Tier
    {
        Harvest,
        Food,

        // Meads the next boss fight calls for.
        Boss,
        Armor,
        Comfort,
        Gear
    }

    public class Suggestion
    {
        public string Id;
        public string Name;
        public string Reason;
        public string Detail;
        public Tier Tier;

        // The goal this is a step towards; its own id when it is the goal.
        public string For = "";

        // Position in the unlock history. Newer is better; 0 means unlocked before the mod.
        public int Sequence;

        // Unlocked by the most recent discovery, which outranks everything else.
        public bool Latest;

        // Everything needed is on hand, so it comes before the rest of its tier.
        public bool Ready;

        // Survivability for the time spent, compared within a tier.
        public float Value;
    }

    // One way to raise a slot's armor: a new piece or an upgrade to the worn one.
    public class ArmorOption
    {
        public string Id;
        public int Slot;
        public float Gain;
        public int Cost;

        public float GainPerMaterial
        {
            get { return Gain / Math.Max(Cost, 1); }
        }
    }

    public class ComfortPiece
    {
        public string Id;
        public int Group;
        public int Comfort;
        public bool Built;
    }

    // How to make one thing: a crafted item, a built piece or an upgrade.
    public class Making
    {
        public string Station = "";
        public int Level = 1;

        // A piece that has to be built first, such as the smelter for copper.
        public string Piece = "";
        public List<string> Materials = new List<string>();
    }

    // Works back from a goal to the first thing to do for it. Each answer is ""
    // when it can be done now, null when something it needs is out of reach, or
    // otherwise the id of the first step.
    public class Planner
    {
        private const int MaxDepth = 8;
        public const string GatherPrefix = "gather:";

        public HashSet<string> Known = new HashSet<string>();
        public Dictionary<string, int> StationLevels = new Dictionary<string, int>();
        public HashSet<string> Built = new HashSet<string>();
        public Dictionary<string, Making> Makings = new Dictionary<string, Making>();

        // Station name to the piece that is that station.
        public Dictionary<string, string> StationPieces = new Dictionary<string, string>();

        // Station name to its unbuilt upgrade pieces, cheapest first.
        public Dictionary<string, List<string>> Extensions = new Dictionary<string, List<string>>();

        // Material to the tool that gathers it.
        public Dictionary<string, string> Gathers = new Dictionary<string, string>();

        private readonly HashSet<string> visiting = new HashSet<string>();

        public string Make(string id)
        {
            visiting.Clear();
            return MakeStep(id);
        }

        public string Get(string item)
        {
            visiting.Clear();
            return MaterialStep(item);
        }

        private string MakeStep(string id)
        {
            if (!Makings.TryGetValue(id, out Making making) || visiting.Count >= MaxDepth || !visiting.Add(id))
            {
                return null;
            }

            try
            {
                // Everything has to be reachable before any one step is worth taking.
                List<string> steps = new List<string> { StationStep(making.Station, making.Level) };
                if (making.Piece.Length > 0)
                {
                    steps.Add(PieceStep(making.Piece));
                }

                steps.AddRange(making.Materials.Select(MaterialStep));
                return steps.Contains(null) ? null : steps.FirstOrDefault(step => step != "") ?? "";
            }
            finally
            {
                visiting.Remove(id);
            }
        }

        private string StationStep(string station, int level)
        {
            if (station.Length == 0)
            {
                return "";
            }

            StationLevels.TryGetValue(station, out int known);
            if (known == 0)
            {
                string step = StationPieces.TryGetValue(station, out string piece) ? PieceStep(piece) : null;
                if (step != "")
                {
                    return step;
                }

                known = 1;
            }

            if (known >= level)
            {
                return "";
            }

            if (Extensions.TryGetValue(station, out List<string> extensions))
            {
                foreach (string extension in extensions)
                {
                    string step = PieceStep(extension);
                    if (step != null)
                    {
                        return step;
                    }
                }
            }

            return null;
        }

        private string PieceStep(string piece)
        {
            if (Built.Contains(piece))
            {
                return "";
            }

            string step = MakeStep(piece);
            return step == "" ? piece : step;
        }

        private string MaterialStep(string item)
        {
            if (Known.Contains(item))
            {
                return "";
            }

            if (Makings.ContainsKey(item))
            {
                string step = MakeStep(item);
                return step == "" ? item : step;
            }

            if (Gathers.TryGetValue(item, out string tool))
            {
                string step = MaterialStep(tool);
                return step == "" ? GatherPrefix + item : step;
            }

            return null;
        }
    }

    // Ranking rules, kept free of game types so they can be tested without Valheim.
    public static class PathRules
    {
        private const int MaxPerTier = 2;
        private const int NoGroup = 0;
        private const char RecordSeparator = ';';
        private const char FieldSeparator = '|';
        private const char LevelSeparator = '@';

        // Takes what the latest discovery opened up, then the best of the rest,
        // leaving room for other tiers before repeating one.
        public static List<Suggestion> Choose(IEnumerable<Suggestion> suggestions, int count)
        {
            List<Suggestion> ranked = suggestions
                .OrderByDescending(s => s.Latest)
                .ThenBy(s => s.Tier)
                .ThenByDescending(s => s.Ready)
                .ThenByDescending(s => s.Value)
                .ThenByDescending(s => s.Sequence)
                .ThenBy(s => s.Name, StringComparer.Ordinal)
                .GroupBy(s => s.Id)
                .Select(sameStep => sameStep.First())
                .ToList();

            List<Suggestion> chosen = ranked.Where(s => s.Latest).Take(count).ToList();
            foreach (Suggestion suggestion in ranked.Where(s => !s.Latest))
            {
                if (chosen.Count < count && chosen.Count(s => s.Tier == suggestion.Tier) < MaxPerTier)
                {
                    chosen.Add(suggestion);
                }
            }

            foreach (Suggestion suggestion in ranked)
            {
                if (chosen.Count < count && !chosen.Contains(suggestion))
                {
                    chosen.Add(suggestion);
                }
            }

            return chosen.OrderBy(s => ranked.IndexOf(s)).ToList();
        }

        // The unlocks at the end of the history that share the newest cause.
        public static HashSet<string> LatestBatch(List<KeyValuePair<string, string>> unlocks)
        {
            HashSet<string> batch = new HashSet<string>();
            string cause = unlocks.Count > 0 ? unlocks[unlocks.Count - 1].Value : "";
            for (int i = unlocks.Count - 1; cause.Length > 0 && i >= 0 && unlocks[i].Value == cause; i--)
            {
                batch.Add(unlocks[i].Key);
            }

            return batch;
        }

        // Takes the cheapest armor per slot, best first, and stops once it is enough.
        public static List<string> KeyArmor(IEnumerable<ArmorOption> options, float needed)
        {
            IEnumerable<ArmorOption> bestPerSlot = options
                .Where(option => option.Gain > 0f)
                .GroupBy(option => option.Slot)
                .Select(slot => slot
                    .OrderByDescending(option => option.GainPerMaterial)
                    .ThenByDescending(option => option.Gain)
                    .First())
                .OrderByDescending(option => option.GainPerMaterial);

            List<string> ids = new List<string>();
            foreach (ArmorOption option in bestPerSlot)
            {
                if (needed <= 0f)
                {
                    break;
                }

                ids.Add(option.Id);
                needed -= option.Gain;
            }

            return ids;
        }

        // Ready for the boss: no more fight prep or gear, but the rest of the path
        // still shows.
        public static List<Suggestion> WhenReady(IEnumerable<Suggestion> suggestions)
        {
            return suggestions
                .Where(s => s.Tier != Tier.Armor && s.Tier != Tier.Gear && s.Tier != Tier.Boss)
                .ToList();
        }

        // What a new food does better than the one it would replace, such as
        // "health/stamina". Eitr only counts for magic users.
        public static string FoodGain(float health, float stamina, float eitr,
            float oldHealth, float oldStamina, float oldEitr, bool magic)
        {
            List<string> better = new List<string>();
            if (magic && eitr > oldEitr)
            {
                better.Add("eitr");
            }

            if (health > oldHealth)
            {
                better.Add("health");
            }

            if (stamina > oldStamina)
            {
                better.Add("stamina");
            }

            return better.Count > 0 ? string.Join("/", better.ToArray()) : "overall";
        }

        // How worn armor compares with the minimum for a fight.
        public static string ArmorRating(float armor, float minimum)
        {
            if (armor < minimum)
            {
                return "low";
            }

            if (armor >= minimum * 1.6f)
            {
                return "great";
            }

            return armor >= minimum * 1.3f ? "good" : "min";
        }

        // Whether the stock covers every cost, each given as item name and amount.
        public static bool HasAll(IEnumerable<KeyValuePair<string, int>> costs, Dictionary<string, int> stock)
        {
            return costs.All(cost => stock.TryGetValue(cost.Key, out int have) && have >= cost.Value);
        }

        // A station is known by level, so each upgrade counts as a new discovery.
        public static string StationEntry(string station, int level)
        {
            return station + LevelSeparator + level;
        }

        // What has been discovered since the last look, without any station level.
        public static string NewDiscovery(HashSet<string> before, HashSet<string> now)
        {
            string entry = now.FirstOrDefault(candidate => !before.Contains(candidate)) ?? "";
            return entry.Split(LevelSeparator)[0];
        }

        // Only the best unbuilt piece of a comfort group helps, and only if it beats
        // what is already built. Pieces without a group count on their own.
        public static List<string> ComfortToBuild(IEnumerable<ComfortPiece> pieces)
        {
            List<string> ids = new List<string>();
            foreach (IGrouping<int, ComfortPiece> group in pieces.Where(p => p.Comfort > 0).GroupBy(p => p.Group))
            {
                if (group.Key == NoGroup)
                {
                    ids.AddRange(group.Where(p => !p.Built).Select(p => p.Id));
                    continue;
                }

                int built = group.Where(p => p.Built).Select(p => p.Comfort).DefaultIfEmpty(0).Max();
                ComfortPiece best = group.Where(p => !p.Built).OrderByDescending(p => p.Comfort).FirstOrDefault();
                if (best != null && best.Comfort > built)
                {
                    ids.Add(best.Id);
                }
            }

            return ids;
        }

        public static HashSet<string> ParseSet(string text)
        {
            return new HashSet<string>((text ?? "").Split(RecordSeparator).Where(id => id.Length > 0));
        }

        public static string FormatSet(IEnumerable<string> ids)
        {
            return string.Join(RecordSeparator.ToString(), ids.ToArray());
        }

        // Unlock history as id|cause records, oldest first, so a record's position is its sequence.
        public static List<KeyValuePair<string, string>> ParseUnlocks(string text)
        {
            return (text ?? "").Split(RecordSeparator)
                .Select(record => record.Split(FieldSeparator))
                .Where(fields => fields.Length == 2 && fields[0].Length > 0)
                .Select(fields => new KeyValuePair<string, string>(fields[0], fields[1]))
                .ToList();
        }

        public static string FormatUnlocks(IEnumerable<KeyValuePair<string, string>> unlocks)
        {
            return string.Join(RecordSeparator.ToString(),
                unlocks.Select(pair => pair.Key + FieldSeparator + pair.Value).ToArray());
        }
    }
}
