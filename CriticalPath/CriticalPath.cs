using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalPath
{
    // Shows what to make next on the inventory screen, worked out from what this
    // character has discovered, so a character mid-game starts in the right place.
    [BepInPlugin("com.parallel53.criticalpath", "CriticalPath", "0.1.3")]
    public class CriticalPathPlugin : BaseUnityPlugin
    {
        private const string BuiltKey = "parallel53.criticalpath.built";
        private const string UnlocksKey = "parallel53.criticalpath.unlocks";
        private const string BuildTool = "Hammer";

        // In the inventory screen's own units, which scale with the game's UI.
        private const float Padding = 14f;
        private const float Gap = 16f;
        private const float FontSize = 20f;

        // Dark wood brown, nearly opaque.
        private static readonly Color PanelBacking = new Color(0.12f, 0.09f, 0.07f, 0.96f);
        private const string QuickStackButton = "QuickStackButton";
        private const float ButtonGap = 6f;
        private const float RefreshSeconds = 2f;
        private const float ScanRadius = 40f;

        // Health keeps you alive; stamina helps, but less, and so does eitr for
        // someone who fights with magic.
        private const float StaminaWorth = 0.5f;
        private const float EitrWorth = 0.5f;

        private static readonly AccessTools.FieldRef<Player, Dictionary<string, int>> KnownStations =
            AccessTools.FieldRefAccess<Player, Dictionary<string, int>>("m_knownStations");

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        private static readonly AccessTools.FieldRef<Player, HashSet<string>> KnownMaterial =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownMaterial");

        // Recipes can't show what a tool lets you gather, so that link is listed here.
        // Each one is a goal. Silver can't be found without Bonemass's Wishbone, so
        // the iron pickaxe only matters after that fight.
        private static readonly Gate[] Gates =
        {
            new Gate("AxeFlint", "RoundLog", "Chop pine trees", ""),
            new Gate("PickaxeAntler", "CopperOre", "Mine copper deposits in the Black Forest", ""),
            new Gate("PickaxeAntler", "TinOre", "Mine tin on Black Forest shores", ""),
            new Gate("AxeBronze", "FineWood", "Chop birch and oak trees", ""),
            new Gate("CryptKey", "WitheredBone", "Search Sunken Crypts in the Swamp", ""),
            new Gate("PickaxeIron", "SilverOre", "Find silver in the Mountains with the Wishbone", "Bonemass")
        };

        // In order. Armor is the total worn that is enough to take the boss on, set
        // so one tier of base-level gear will do rather than a fully upgraded set.
        private static readonly Boss[] Bosses =
        {
            new Boss("Eikthyr", 6f, "Offer 2 deer trophies at the altar. Parry his headbutts to stagger him.",
                NoResistance, ShieldUse.Parry,
                new Need("TrophyDeer", 2, "Hunt deer in the Meadows"))
            {
                Find = "Read the Vegvisir stone in the Meadows to mark his altar."
            },
            new Boss("gd_king", 18f, "Offer 3 ancient seeds. Kite him with the bow from behind pillars.",
                NoResistance, ShieldUse.None,
                new Need("AncientSeed", 3, "Destroy greydwarf nests in the Black Forest"))
            {
                Arrows = 100,
                Find = "Read a Vegvisir in a Black Forest burial chamber to mark his altar."
            },
            new Boss("Bonemass", 30f, "Offer 10 withered bones. Parry to stagger him and bring blunt weapons.",
                HitData.DamageType.Poison, ShieldUse.Parry,
                new Need("WitheredBone", 10, "Search Sunken Crypts in the Swamp"))
            {
                Find = "Read a Vegvisir in a Swamp Sunken Crypt to mark his altar."
            },
            new Boss("Dragon", 44f, "Offer 3 dragon eggs. Dodge her breath behind boulders.",
                HitData.DamageType.Frost, ShieldUse.None,
                new Need("DragonEgg", 3, "Carry eggs one at a time from dragon nests in the Mountains"))
            {
                Arrows = 200,
                Ranged = true,
                Find = "Read a Vegvisir in the Mountains to mark her altar."
            },
            new Boss("GoblinKing", 60f, "Offer 5 fuling totems. Sprint behind his stone pillars.",
                HitData.DamageType.Fire, ShieldUse.None,
                new Need("GoblinTotem", 5, "Take totems from fuling villages in the Plains"))
            {
                Find = "Read a Vegvisir in a Plains fuling village to mark his altar."
            },
            new Boss("SeekerQueen", 82f, "Open the sealed door with the Sealbreaker. Dodge rather than block.",
                NoResistance, ShieldUse.None,
                new Need("DvergrKey", 1, ""),
                new Need("CapeFeather", 1, ""),
                new Need("Demister", 1, ""))
            {
                Find = "Read the Vegvisir in a Mistlands infested mine to mark her lair."
            },
            new Boss("Fader", 94f, "Offer 3 bells.", HitData.DamageType.Fire, ShieldUse.Strongest)
            {
                Find = "Read a Vegvisir in an Ashlands fortress to mark its altar."
            }
        };

        private const HitData.DamageType NoResistance = 0;

        // Whether a shield is worth carrying into the fight.
        private enum ShieldUse
        {
            None,

            // Any shield, to parry and stagger the boss.
            Parry,

            // The strongest shield that can be made, as a safety net against heavy hits.
            Strongest
        }

        private const string DefaultFind = "Read a Vegvisir to mark its altar on the map.";

        // Arrows to bring when a boss doesn't say otherwise.
        private const int DefaultArrows = 20;

        private static readonly HitData.DamageType[] DamageKinds =
        {
            HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce,
            HitData.DamageType.Chop, HitData.DamageType.Pickaxe, HitData.DamageType.Fire,
            HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison,
            HitData.DamageType.Spirit
        };

        private static readonly HashSet<ItemDrop.ItemData.ItemType> WeaponTypes =
            new HashSet<ItemDrop.ItemData.ItemType>
            {
                ItemDrop.ItemData.ItemType.OneHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
                ItemDrop.ItemData.ItemType.Bow
            };

        // Bosses the game has that the list above doesn't name, such as Kall
        // Fimbulbringer of the Deep North, come last with this armor each.
        private const float LaterBossArmor = 96f;
        private const string LaterBossHint = "Offer 3 malicious bloods at the altar.";

        private static readonly ItemDrop.ItemData.ItemType[] ArmorSlots =
        {
            ItemDrop.ItemData.ItemType.Helmet,
            ItemDrop.ItemData.ItemType.Chest,
            ItemDrop.ItemData.ItemType.Legs
        };

        private static readonly HashSet<ItemDrop.ItemData.ItemType> GearTypes =
            new HashSet<ItemDrop.ItemData.ItemType>
            {
                ItemDrop.ItemData.ItemType.OneHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
                ItemDrop.ItemData.ItemType.Bow,
                ItemDrop.ItemData.ItemType.Shield,
                ItemDrop.ItemData.ItemType.Shoulder,
                ItemDrop.ItemData.ItemType.Tool
            };

        private static readonly HashSet<string> MissingItems = new HashSet<string>();

        private static ManualLogSource log;
        private static ConfigEntry<int> suggestionCount;
        private static ConfigEntry<bool> notify;
        private static bool dirty = true;
        private static string lastCause = "";
        private static Player seenPlayer;
        private static HashSet<string> seenKnowledge;

        private string panelText;
        private string shownTop;
        private float nextRefresh;
        private RectTransform panel;
        private TMP_Text panelLabel;

        private class Gate
        {
            public readonly string Tool;
            public readonly string Target;
            public readonly string Where;
            public readonly string AfterBoss;

            public Gate(string tool, string target, string where, string afterBoss)
            {
                Tool = tool;
                Target = target;
                Where = where;
                AfterBoss = afterBoss;
            }
        }

        private class Boss
        {
            public readonly string Prefab;
            public readonly float TargetArmor;
            public readonly string Hint;

            // The damage the fight is won or lost on, covered by gear or a mead.
            public readonly HitData.DamageType Resist;

            public readonly ShieldUse Shield;

            // What to bring: the summoning offering and gear the fight calls for.
            public readonly Need[] Bring;

            // Arrows to bring when the fight is fought with a bow.
            public int Arrows = DefaultArrows;

            // Fought at range whatever its weaknesses, such as a boss that flies.
            public bool Ranged;

            // Where to find the Vegvisir that marks the altar on the map.
            public string Find = DefaultFind;

            public Boss(string prefab, float targetArmor, string hint, HitData.DamageType resist, ShieldUse shield,
                params Need[] bring)
            {
                Prefab = prefab;
                TargetArmor = targetArmor;
                Hint = hint;
                Resist = resist;
                Shield = shield;
                Bring = bring;
            }
        }

        private class Need
        {
            public readonly string Prefab;
            public readonly int Count;

            // Where to look, for things found rather than made.
            public readonly string Where;

            public Need(string prefab, int count, string where)
            {
                Prefab = prefab;
                Count = count;
                Where = where;
            }
        }

        private class Shortfall
        {
            public string Item;
            public int Short;
            public string Where;
        }

        private class BossGoal
        {
            public string Name;
            public string Hint;
            public float Armor;
            public float TargetArmor;

            // What to bring that isn't on hand yet.
            public List<Shortfall> Missing;

            public HitData.DamageType Resist;

            // The strongest resistance to it from worn gear or a mead on hand.
            public int Have;

            // Nothing stronger can be made, so what is on hand will do.
            public bool Resisted = true;

            // Damage the boss takes extra from, read from the boss itself.
            public List<HitData.DamageType> Weak;

            // A weapon or arrows on hand hit a weakness, or none can be made.
            public bool Armed = true;

            public ShieldUse Shield;

            // The shield the fight calls for is on hand, or none can be made.
            public bool Shielded = true;

            public int Arrows;
            public bool Ranged;

            // A bow and enough arrows are on hand for a ranged fight, or none can be made.
            public bool Shooting = true;

            // Its altar is pinned on the map. Finding it comes last, once the rest is ready.
            public bool Located;
            public string Find;

            public bool Ready
            {
                get { return Armor >= TargetArmor && Missing.Count == 0 && Resisted && Armed && Shielded && Shooting; }
            }
        }

        private void Awake()
        {
            log = Logger;
            suggestionCount = Config.Bind("General", "Suggestions", 3,
                new ConfigDescription("How many suggestions the panel shows.",
                    new AcceptableValueRange<int>(1, 6)));
            notify = Config.Bind("General", "Notify", true,
                "Show a top-left message when the top suggestion changes.");

            new Harmony("com.parallel53.criticalpath").PatchAll(typeof(CriticalPathPlugin));
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null || ObjectDB.instance == null)
            {
                panelText = null;
                shownTop = null;
                return;
            }

            bool visible = InventoryGui.IsVisible();
            if (!dirty && !(visible && Time.unscaledTime >= nextRefresh))
            {
                return;
            }

            dirty = false;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh(player);
        }

        // Shown beside the inventory screen's windows, never over them.
        private void LateUpdate()
        {
            InventoryGui gui = InventoryGui.instance;
            bool show = panelText != null && gui != null && InventoryGui.IsVisible();
            if (panel == null)
            {
                if (!show)
                {
                    return;
                }

                CreatePanel(gui);
            }

            if (panel.gameObject.activeSelf != show)
            {
                panel.gameObject.SetActive(show);
            }

            if (show)
            {
                panelLabel.text = panelText;
                PlacePanel(gui);
            }
        }

        // Built on the inventory screen's canvas, with the crafting panel's
        // background and font so it looks like part of the game.
        private void CreatePanel(InventoryGui gui)
        {
            Canvas canvas = gui.m_crafting.GetComponentInParent<Canvas>().rootCanvas;
            GameObject root = new GameObject("CriticalPathPanel", typeof(RectTransform), typeof(Image));
            panel = (RectTransform)root.transform;
            panel.SetParent(canvas.transform, false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0f, 1f);

            // A solid backing keeps the text readable whatever the artwork is like.
            Image backing = root.GetComponent<Image>();
            backing.color = PanelBacking;
            backing.raycastTarget = false;

            Image source = Background(gui.m_player) ?? Background(gui.m_crafting);
            if (source != null)
            {
                GameObject frameObject = new GameObject("Frame", typeof(RectTransform), typeof(Image));
                RectTransform frameRect = (RectTransform)frameObject.transform;
                frameRect.SetParent(panel, false);
                frameRect.anchorMin = Vector2.zero;
                frameRect.anchorMax = Vector2.one;
                frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;

                Image frame = frameObject.GetComponent<Image>();
                frame.sprite = source.sprite;
                frame.type = source.type;
                frame.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                // Exactly as the inventory draws it, so the dark backing shows through
                // the same way the world shows through the inventory window.
                frame.color = source.color;
                if (source.material != source.defaultMaterial)
                {
                    frame.material = source.material;
                }

                frame.raycastTarget = false;
                log.LogInfo("Panel artwork from " + source.name + " (" + source.sprite.name + ", colour " + source.color + ")");
            }
            else
            {
                log.LogInfo("No panel artwork found; using a plain backing");
            }

            // Kept off until the game's font is set, or the text looks for a default
            // font the game doesn't ship and warns about it.
            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.SetActive(false);
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(panel, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(Padding, Padding);
            textRect.offsetMax = new Vector2(-Padding, -Padding);

            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            TMP_Text sample = gui.m_crafting.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.font != null);
            if (sample != null)
            {
                label.font = sample.font;
                log.LogInfo("Panel font " + sample.font.name);
            }

            label.fontSize = FontSize;
            label.richText = true;
            label.enableWordWrapping = true;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = Color.white;
            label.raycastTarget = false;
            textObject.SetActive(true);
            panelLabel = label;
        }

        // The window's backdrop: an image named like one, else its largest image.
        private static Image Background(RectTransform window)
        {
            List<Image> images = window.GetComponentsInChildren<Image>(true)
                .Where(image => image.sprite != null && image.color.a > 0.5f)
                .OrderByDescending(image => image.rectTransform.rect.width * image.rectTransform.rect.height)
                .ToList();
            return images.FirstOrDefault(image =>
                    image.name.ToLowerInvariant().Contains("bkg") || image.name.ToLowerInvariant().Contains("background"))
                ?? images.FirstOrDefault();
        }

        // Under the inventory window and as wide, since smaller screens leave
        // little room beside it. An open chest sits below the inventory, so then
        // it goes under the chest.
        private void PlacePanel(InventoryGui gui)
        {
            RectTransform canvas = (RectTransform)panel.parent;
            Rect inventory = LocalRect(canvas, gui.m_player);
            float bottom = inventory.yMin;
            if (gui.m_container != null && gui.m_container.gameObject.activeInHierarchy)
            {
                bottom = Mathf.Min(bottom, LocalRect(canvas, gui.m_container).yMin);
            }

            // Below QuickStackStore's button, which hangs off the inventory's bottom edge.
            RectTransform button = (RectTransform)gui.m_player.Find(QuickStackButton);
            if (button != null && button.gameObject.activeInHierarchy)
            {
                bottom = Mathf.Min(bottom, LocalRect(canvas, button).yMin + Gap - ButtonGap);
            }

            float width = inventory.width;
            float height = panelLabel.GetPreferredValues(panelText, width - 2f * Padding, 0f).y + 2f * Padding;
            panel.sizeDelta = new Vector2(width, height);
            panel.anchoredPosition = new Vector2(inventory.xMin, bottom - Gap) - canvas.rect.center;
        }

        private static Rect LocalRect(RectTransform canvas, RectTransform window)
        {
            Vector3[] corners = new Vector3[4];
            window.GetWorldCorners(corners);
            Vector2 min = canvas.InverseTransformPoint(corners[0]);
            Vector2 max = canvas.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Refresh(Player player)
        {
            List<Piece> nearby = new List<Piece>();
            Piece.GetAllPiecesInRadius(player.transform.position, ScanRadius, nearby);
            RecordNearbyPieces(player, nearby);

            Dictionary<string, int> stock = Stock(player, nearby);
            BossGoal goal = NextBoss(player, stock);
            List<Suggestion> suggestions = PathRules.Choose(Candidates(player, stock, goal), suggestionCount.Value);
            if (goal != null && goal.Ready && !goal.Located)
            {
                suggestions.Insert(0, new Suggestion { Id = "find:" + goal.Name, Name = "Find " + goal.Name, Reason = goal.Find, Detail = "" });
                suggestions = suggestions.Take(suggestionCount.Value).ToList();
            }
            panelText = PanelText(suggestions, goal);

            string top = suggestions.Count > 0 ? suggestions[0].Id : "";
            if (shownTop != null && top != shownTop && suggestions.Count > 0)
            {
                Logger.LogInfo("Next: " + string.Join(", ", suggestions
                    .Select(s => s.Name + " (" + s.Reason + ")").ToArray()));
                if (notify.Value)
                {
                    player.Message(MessageHud.MessageType.TopLeft, "Critical Path: " + suggestions[0].Name);
                }
            }

            shownTop = top;
        }

        private static string PanelText(List<Suggestion> suggestions, BossGoal goal)
        {
            StringBuilder text = new StringBuilder("<b>Critical Path</b>");
            string armor = goal != null
                ? " Armor " + Mathf.FloorToInt(goal.Armor) + "/" + goal.TargetArmor +
                    " (" + PathRules.ArmorRating(goal.Armor, goal.TargetArmor) + ")."
                : "";
            if (goal != null && goal.Ready)
            {
                text.Append("\n<color=#ffcc66>Ready for ").Append(goal.Name)
                    .Append(goal.Located ? ". Go fight it.</color>" : ". Find it next.</color>").Append(armor);
                text.Append("\n").Append(goal.Hint);
            }
            else if (goal != null)
            {
                text.Append("\nNext boss: ").Append(goal.Name).Append(".").Append(armor);
                if (!goal.Resisted)
                {
                    text.Append(" Needs ").Append(goal.Resist.ToString().ToLowerInvariant()).Append(" resistance.");
                }

                if (!goal.Shooting)
                {
                    text.Append(" Needs a bow and ").Append(goal.Arrows).Append(" arrows.");
                }

                if (!goal.Shielded)
                {
                    text.Append(goal.Shield == ShieldUse.Strongest ? " Needs a stronger shield." : " Needs a shield.");
                }

                if (!goal.Armed)
                {
                    text.Append(" Needs a weapon that deals ").Append(WeakText(goal)).Append(".");
                }

                if (goal.Missing.Count > 0)
                {
                    text.Append(" Still to bring: ").Append(string.Join(", ", goal.Missing
                        .Select(missing => missing.Short + " " + Localized(missing.Item)).ToArray())).Append(".");
                }
            }

            if (suggestions.Count == 0 && (goal == null || !goal.Ready))
            {
                return text.Append("\nNothing new to make right now. Explore further.").ToString();
            }

            foreach (Suggestion suggestion in suggestions)
            {
                text.Append("\n\n<color=#ffcc66>").Append(suggestion.Name).Append("</color>");
                text.Append("\n").Append(suggestion.Reason);
                if (suggestion.Detail.Length > 0)
                {
                    text.Append("\n").Append(suggestion.Detail);
                }
            }

            return text.ToString();
        }

        // The first boss this world hasn't beaten, read from the boss itself so the
        // name and defeat key always match the game.
        private static BossGoal NextBoss(Player player, Dictionary<string, int> stock)
        {
            foreach (Boss boss in AllBosses())
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(boss.Prefab);
                if (prefab == null)
                {
                    WarnMissingBoss(boss.Prefab);
                    continue;
                }

                Character character = prefab.GetComponent<Character>();
                if (!Defeated(boss.Prefab))
                {
                    return new BossGoal
                    {
                        Name = Localized(character.m_name),
                        Hint = boss.Hint,
                        Missing = boss.Bring
                            .Select(need => new { need, item = Shared(need.Prefab) })
                            .Where(pair => pair.item != null)
                            .Select(pair =>
                            {
                                stock.TryGetValue(pair.item.m_name, out int have);
                                return new Shortfall { Item = pair.item.m_name, Short = pair.need.Count - have, Where = pair.need.Where };
                            })
                            .Where(missing => missing.Short > 0)
                            .ToList(),
                        Resist = boss.Resist,
                        Shield = boss.Shield,
                        Arrows = boss.Arrows,
                        Ranged = boss.Ranged,
                        Weak = DamageKinds.Where(type => IsWeakness(character.m_damageModifiers.GetModifier(type))).ToList(),
                        Have = boss.Resist == NoResistance ? 0 : Strongest(player, stock, boss.Resist),
                        Armor = player.GetBodyArmor(),
                        TargetArmor = boss.TargetArmor,
                        Located = Located(character.m_name),
                        Find = boss.Find
                    };
                }
            }

            return null;
        }

        // The listed bosses, then any other the game marks as a boss with a defeat key.
        private static IEnumerable<Boss> AllBosses()
        {
            HashSet<string> listed = new HashSet<string>(Bosses.Select(boss => boss.Prefab));
            IEnumerable<Boss> later = ZNetScene.instance.m_prefabs
                .Where(prefab => prefab != null && !listed.Contains(prefab.name))
                .Where(prefab =>
                {
                    Character character = prefab.GetComponent<Character>();
                    return character != null && character.m_boss && !string.IsNullOrEmpty(character.m_defeatSetGlobalKey);
                })
                .Select(prefab =>
                {
                    if (MissingItems.Add("boss:" + prefab.name))
                    {
                        log.LogInfo("Found another boss: " + prefab.name + " (" +
                            Localized(prefab.GetComponent<Character>().m_name) + ")");
                    }

                    return new Boss(prefab.name, LaterBossArmor, LaterBossHint, NoResistance, ShieldUse.None);
                });
            return Bosses.Concat(later);
        }

        // Resistances don't add up; the game keeps the strongest. So what counts is
        // the best one from worn gear or a mead to drink.
        private static int Strongest(Player player, Dictionary<string, int> stock, HitData.DamageType type)
        {
            int worn = player.GetInventory().GetEquippedItems()
                .Select(item => Strength(item.m_shared.m_damageModifiers, type))
                .DefaultIfEmpty(0).Max();

            Dictionary<string, ItemDrop.ItemData.SharedData> items = ItemsByName();
            int drink = stock
                .Where(pair => pair.Value > 0 && items.ContainsKey(pair.Key))
                .Select(pair => DrinkStrength(items[pair.Key], type))
                .DefaultIfEmpty(0).Max();
            return Mathf.Max(worn, drink);
        }

        private static int DrinkStrength(ItemDrop.ItemData.SharedData shared, HitData.DamageType type)
        {
            SE_Stats effect = shared.m_consumeStatusEffect as SE_Stats;
            return effect != null ? Strength(effect.m_mods, type) : 0;
        }

        // 0 for none, up to 4 for immune.
        private static int Strength(List<HitData.DamageModPair> mods, HitData.DamageType type)
        {
            if (mods == null)
            {
                return 0;
            }

            return mods.Where(mod => mod.m_type == type).Select(mod =>
            {
                switch (mod.m_modifier)
                {
                    case HitData.DamageModifier.SlightlyResistant: return 1;
                    case HitData.DamageModifier.Resistant: return 2;
                    case HitData.DamageModifier.VeryResistant: return 3;
                    case HitData.DamageModifier.Immune: return 4;
                    default: return 0;
                }
            }).DefaultIfEmpty(0).Max();
        }

        private static Dictionary<string, ItemDrop.ItemData.SharedData> ItemsByName()
        {
            Dictionary<string, ItemDrop.ItemData.SharedData> items = new Dictionary<string, ItemDrop.ItemData.SharedData>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null && !items.ContainsKey(drop.m_itemData.m_shared.m_name))
                {
                    items[drop.m_itemData.m_shared.m_name] = drop.m_itemData.m_shared;
                }
            }

            return items;
        }

        private static void WarnMissingBoss(string prefab)
        {
            if (MissingItems.Add(prefab))
            {
                log.LogWarning("No boss named " + prefab);
            }
        }

        // Read from the boss itself so the defeat key always matches the game.
        // A Vegvisir drops a boss pin named after the boss on the map.
        private static bool Located(string name)
        {
            if (Minimap.instance == null)
            {
                return true;
            }

            List<Minimap.PinData> bossPins = Pins(Minimap.instance).Where(pin => pin.m_type == Minimap.PinType.Boss).ToList();
            foreach (Minimap.PinData pin in bossPins.Where(pin => MissingItems.Add("pin:" + pin.m_name)))
            {
                log.LogInfo("Boss pin on the map: " + pin.m_name);
            }

            string localized = Localized(name);
            return bossPins.Any(pin => pin.m_name == name ||
                string.Equals(Localized(pin.m_name), localized, System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool Defeated(string prefab)
        {
            GameObject boss = ZNetScene.instance.GetPrefab(prefab);
            if (boss == null)
            {
                WarnMissingBoss(prefab);
                return false;
            }

            return ZoneSystem.instance.GetGlobalKey(boss.GetComponent<Character>().m_defeatSetGlobalKey);
        }

        // Goals are tools that open up a material, food, and the armor for the next
        // boss. Each shows as its first missing step, so stations only appear when
        // a goal needs them.
        private static List<Suggestion> Candidates(Player player, Dictionary<string, int> stock, BossGoal goal)
        {
            List<KeyValuePair<string, string>> unlocks = Unlocks(player);
            HashSet<string> latest = PathRules.LatestBatch(unlocks);
            HashSet<string> built = Built(player);
            Plan plan = new Plan(player, built);

            // Settled before readiness, since a stronger resistance to make means not ready yet.
            Suggestion resist = goal != null && goal.Resist != NoResistance ? ResistGoal(plan, stock, goal) : null;
            if (resist != null)
            {
                goal.Resisted = false;
            }

            Suggestion weapon = goal != null && goal.Weak.Count > 0 ? WeaponGoal(plan, stock, goal) : null;
            if (weapon != null)
            {
                goal.Armed = false;
            }

            Suggestion shield = goal != null && goal.Shield != ShieldUse.None ? ShieldGoal(plan, stock, goal) : null;
            if (shield != null)
            {
                goal.Shielded = false;
            }

            Suggestion ranged = goal != null && goal.Ranged ? RangedGoal(plan, stock, goal) : null;
            if (ranged != null)
            {
                goal.Shooting = false;
            }

            List<Suggestion> suggestions = new List<Suggestion>();
            suggestions.AddRange(GatherGoals(player, plan));
            suggestions.AddRange(FoodGoals(player, plan, stock));
            if (goal != null && !goal.Ready)
            {
                suggestions.AddRange(ArmorGoals(player, plan, goal.TargetArmor - goal.Armor));
                suggestions.AddRange(BringGoals(plan, stock, goal));
                if (resist != null)
                {
                    suggestions.Add(resist);
                }

                if (weapon != null)
                {
                    suggestions.Add(weapon);
                }

                if (shield != null)
                {
                    suggestions.Add(shield);
                }

                if (ranged != null)
                {
                    suggestions.Add(ranged);
                }
            }

            suggestions.AddRange(Comfort(plan.Pieces.Values.Where(piece => player.IsRecipeKnown(piece.m_name)).ToList(), built));
            suggestions.AddRange(plan.Recipes.Values
                .Where(recipe => GearTypes.Contains(recipe.m_item.m_itemData.m_shared.m_itemType) &&
                    player.IsRecipeKnown(ItemName(recipe)) && !player.IsMaterialKnown(ItemName(recipe)))
                .Select(recipe => plan.Describe(ItemName(recipe), Tier.Gear, "New gear.")));

            foreach (Suggestion suggestion in suggestions)
            {
                // A new food only jumps the queue once it can actually be cooked,
                // though a station it needs can.
                bool food = suggestion.Tier == Tier.Food && suggestion.Id == suggestion.For;
                suggestion.Latest = (latest.Contains(suggestion.Id) || latest.Contains(suggestion.For)) &&
                    (!food || suggestion.Ready);

                int index = unlocks.FindLastIndex(pair => pair.Key == suggestion.Id || pair.Key == suggestion.For);
                if (index >= 0)
                {
                    suggestion.Sequence = index + 1;
                    if (unlocks[index].Value.Length > 0 && unlocks[index].Key == suggestion.Id)
                    {
                        suggestion.Reason += " Unlocked by " + Localized(unlocks[index].Value) + ".";
                    }
                }
            }

            return goal != null && goal.Ready ? PathRules.WhenReady(suggestions) : suggestions;
        }

        private static IEnumerable<Suggestion> GatherGoals(Player player, Plan plan)
        {
            // A boss offering is a goal of its own, once the armor allows.
            HashSet<string> offerings = new HashSet<string>(Bosses.SelectMany(boss => boss.Bring).Select(need => need.Prefab));
            foreach (Gate gate in plan.OpenGates)
            {
                string target = Shared(gate.Target).m_name;
                if (player.IsMaterialKnown(target) || offerings.Contains(gate.Target))
                {
                    continue;
                }

                Suggestion suggestion = plan.ForGoal(target, Planner.GatherPrefix + target, plan.Planner.Get(target),
                    Tier.Harvest, Localized(target), "");
                if (suggestion != null)
                {
                    yield return suggestion;
                }
            }
        }

        // The single best food not made yet, or the first step towards it, and only
        // if it beats the third-best food already known, since three are eaten at once.
        private static IEnumerable<Suggestion> FoodGoals(Player player, Plan plan, Dictionary<string, int> stock)
        {
            Dictionary<string, ItemDrop.ItemData.SharedData> items = ItemsByName();
            bool magic = stock.Any(pair => pair.Value > 0 && items.ContainsKey(pair.Key) && UsesEitr(items[pair.Key]));
            // The food a new one would push out of the three eaten at once.
            ItemDrop.ItemData.SharedData replaced = items.Values
                .Where(shared => shared.m_food > 0f && player.IsMaterialKnown(shared.m_name))
                .OrderByDescending(shared => FoodValue(shared, magic))
                .Skip(2)
                .FirstOrDefault();
            float toBeat = replaced != null ? FoodValue(replaced, magic) : 0f;

            Suggestion best = null;
            foreach (Recipe recipe in plan.Recipes.Values)
            {
                ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData.m_shared;
                if (shared.m_food <= 0f || player.IsMaterialKnown(shared.m_name) || FoodValue(shared, magic) <= toBeat)
                {
                    continue;
                }

                string step = plan.Planner.Make(shared.m_name);
                bool ready = step == "" && CanMake(recipe, stock);
                string gain = replaced != null
                    ? PathRules.FoodGain(shared.m_food, shared.m_foodStamina, shared.m_foodEitr,
                        replaced.m_food, replaced.m_foodStamina, replaced.m_foodEitr, magic)
                    : PathRules.FoodGain(shared.m_food, shared.m_foodStamina, shared.m_foodEitr, 0f, 0f, 0f, magic);
                Suggestion suggestion = plan.ForGoal(shared.m_name, shared.m_name, step, Tier.Food,
                    Localized(shared.m_name) + " (" + gain + ")",
                    "New food (" + gain + ")." + (ready ? " You have the ingredients." : ""));
                if (suggestion == null)
                {
                    continue;
                }

                suggestion.Ready = ready;
                suggestion.Value = FoodValue(shared, magic);
                if (best == null || suggestion.Value > best.Value || (suggestion.Value == best.Value && ready && !best.Ready))
                {
                    best = suggestion;
                }
            }

            if (best != null)
            {
                yield return best;
            }
        }

        private static float FoodValue(ItemDrop.ItemData.SharedData shared, bool magic)
        {
            return shared.m_food + shared.m_foodStamina * StaminaWorth + (magic ? shared.m_foodEitr * EitrWorth : 0f);
        }

        // A staff or anything else that spends eitr to attack.
        private static bool UsesEitr(ItemDrop.ItemData.SharedData shared)
        {
            return (shared.m_attack != null && shared.m_attack.m_attackEitr > 0f) ||
                (shared.m_secondaryAttack != null && shared.m_secondaryAttack.m_attackEitr > 0f);
        }

        // Fetching the offering means surviving the boss's biome, so it waits until
        // the armor is good enough for the fight.
        private static IEnumerable<Suggestion> BringGoals(Plan plan, Dictionary<string, int> stock, BossGoal goal)
        {
            bool armored = goal.Armor >= goal.TargetArmor;
            foreach (Shortfall missing in goal.Missing)
            {
                if (!armored && !plan.Planner.Makings.ContainsKey(missing.Item))
                {
                    continue;
                }

                Suggestion suggestion = BringSuggestion(plan, stock, goal, missing, "Bring it to fight " + goal.Name + ".");
                if (suggestion != null)
                {
                    yield return suggestion;
                }
            }
        }

        // Something stronger than what is on hand: the strongest first, then one
        // ready to make, then a mead over gear, since brewing beats hunting for parts.
        private static Suggestion ResistGoal(Plan plan, Dictionary<string, int> stock, BossGoal goal)
        {
            string resist = goal.Resist.ToString().ToLowerInvariant() + " resist";
            string reason = "Gives " + resist + " for " + goal.Name + ".";
            return ItemsByName()
                .Where(pair => plan.Planner.Makings.ContainsKey(pair.Key))
                .Select(pair => new
                {
                    drink = DrinkStrength(pair.Value, goal.Resist),
                    wear = Strength(pair.Value.m_damageModifiers, goal.Resist),
                    item = pair.Key
                })
                .Where(provider => Mathf.Max(provider.drink, provider.wear) > goal.Have)
                .Select(provider => new
                {
                    strength = Mathf.Max(provider.drink, provider.wear),
                    mead = provider.drink > 0,
                    suggestion = BringSuggestion(plan, stock, goal,
                        new Shortfall { Item = provider.item, Short = 1, Where = "" }, reason, " (" + resist + ")")
                })
                .Where(provider => provider.suggestion != null)
                .OrderByDescending(provider => provider.strength)
                .ThenByDescending(provider => provider.suggestion.Id == provider.suggestion.For)
                .ThenByDescending(provider => provider.mead)
                .Select(provider => provider.suggestion)
                .FirstOrDefault();
        }

        private static bool IsWeakness(HitData.DamageModifier modifier)
        {
            return modifier == HitData.DamageModifier.Weak || modifier == HitData.DamageModifier.VeryWeak ||
                modifier == HitData.DamageModifier.SlightlyWeak;
        }

        private static string WeakText(BossGoal goal)
        {
            return string.Join(" or ", goal.Weak.Select(type => type.ToString().ToLowerInvariant()).ToArray());
        }

        private static float Damage(HitData.DamageTypes damages, HitData.DamageType type)
        {
            switch (type)
            {
                case HitData.DamageType.Blunt: return damages.m_blunt;
                case HitData.DamageType.Slash: return damages.m_slash;
                case HitData.DamageType.Pierce: return damages.m_pierce;
                case HitData.DamageType.Chop: return damages.m_chop;
                case HitData.DamageType.Pickaxe: return damages.m_pickaxe;
                case HitData.DamageType.Fire: return damages.m_fire;
                case HitData.DamageType.Frost: return damages.m_frost;
                case HitData.DamageType.Lightning: return damages.m_lightning;
                case HitData.DamageType.Poison: return damages.m_poison;
                case HitData.DamageType.Spirit: return damages.m_spirit;
                default: return 0f;
            }
        }

        private static float WeakDamage(ItemDrop.ItemData.SharedData shared, BossGoal goal)
        {
            return goal.Weak.Sum(type => Damage(shared.m_damages, type));
        }

        // A weapon used on its own, or arrows fired from a launcher on hand.
        private static bool Melee(ItemDrop.ItemData.SharedData shared)
        {
            return WeaponTypes.Contains(shared.m_itemType) && string.IsNullOrEmpty(shared.m_ammoType);
        }

        private static bool IsAmmo(ItemDrop.ItemData.SharedData shared)
        {
            return (shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
                    shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable) &&
                !string.IsNullOrEmpty(shared.m_ammoType);
        }

        private static bool Ammo(ItemDrop.ItemData.SharedData shared, HashSet<string> launchers)
        {
            return IsAmmo(shared) && launchers.Contains(shared.m_ammoType);
        }

        // Nothing on hand hits a weakness: the hardest-hitting weapon or batch of
        // arrows that does, one ready to make first. Arrows without a bow to fire
        // them make the bow the step.
        private static Suggestion WeaponGoal(Plan plan, Dictionary<string, int> stock, BossGoal goal)
        {
            Dictionary<string, ItemDrop.ItemData.SharedData> items = ItemsByName();
            List<ItemDrop.ItemData.SharedData> owned = stock
                .Where(pair => pair.Value > 0 && items.ContainsKey(pair.Key))
                .Select(pair => items[pair.Key])
                .ToList();
            HashSet<string> launchers = new HashSet<string>(owned
                .Where(shared => WeaponTypes.Contains(shared.m_itemType) && !string.IsNullOrEmpty(shared.m_ammoType))
                .Select(shared => shared.m_ammoType));

            bool armed = owned.Any(shared => WeakDamage(shared, goal) > 0f &&
                (Melee(shared) || (Ammo(shared, launchers) && stock[shared.m_name] >= goal.Arrows)));
            if (armed)
            {
                return null;
            }

            string reason = "Hits " + goal.Name + "'s weakness to " + WeakText(goal) + ".";
            return items
                .Where(pair => plan.Planner.Makings.ContainsKey(pair.Key) && WeakDamage(pair.Value, goal) > 0f &&
                    (Melee(pair.Value) || IsAmmo(pair.Value)))
                .Select(pair =>
                {
                    Suggestion suggestion;
                    if (IsAmmo(pair.Value) && !launchers.Contains(pair.Value.m_ammoType))
                    {
                        suggestion = Launcher(plan, stock, goal, items, pair.Value,
                            "Fires " + Localized(pair.Key) + ", which " + reason.Substring(0, 1).ToLowerInvariant() +
                            reason.Substring(1));
                    }
                    else
                    {
                        stock.TryGetValue(pair.Key, out int have);
                        int needed = Melee(pair.Value) ? 1 : goal.Arrows - have;
                        suggestion = BringSuggestion(plan, stock, goal,
                            new Shortfall { Item = pair.Key, Short = needed, Where = "" }, reason);
                    }

                    return new { damage = WeakDamage(pair.Value, goal), suggestion };
                })
                .Where(option => option.suggestion != null)
                .OrderByDescending(option => option.suggestion.Id == option.suggestion.For)
                .ThenByDescending(option => option.damage)
                .Select(option => option.suggestion)
                .FirstOrDefault();
        }

        // A bow and a full quiver for a boss fought at range: the bow first if there
        // is none, then the hardest-hitting arrows, favouring the boss's weaknesses.
        private static Suggestion RangedGoal(Plan plan, Dictionary<string, int> stock, BossGoal goal)
        {
            Dictionary<string, ItemDrop.ItemData.SharedData> items = ItemsByName();
            List<ItemDrop.ItemData.SharedData> owned = stock
                .Where(pair => pair.Value > 0 && items.ContainsKey(pair.Key))
                .Select(pair => items[pair.Key])
                .ToList();
            HashSet<string> launchers = new HashSet<string>(owned
                .Where(shared => WeaponTypes.Contains(shared.m_itemType) && !string.IsNullOrEmpty(shared.m_ammoType))
                .Select(shared => shared.m_ammoType));
            if (owned.Any(shared => Ammo(shared, launchers) && stock[shared.m_name] >= goal.Arrows))
            {
                return null;
            }

            string reason = goal.Name + " is fought at range. Bring " + goal.Arrows + " arrows.";
            return items
                .Where(pair => IsAmmo(pair.Value) && plan.Planner.Makings.ContainsKey(pair.Key))
                .Select(pair =>
                {
                    Suggestion suggestion;
                    if (launchers.Contains(pair.Value.m_ammoType))
                    {
                        stock.TryGetValue(pair.Key, out int have);
                        suggestion = BringSuggestion(plan, stock, goal,
                            new Shortfall { Item = pair.Key, Short = goal.Arrows - have, Where = "" }, reason);
                    }
                    else
                    {
                        suggestion = Launcher(plan, stock, goal, items, pair.Value,
                            "Fires " + Localized(pair.Key) + ". " + reason);
                    }

                    return new
                    {
                        weak = WeakDamage(pair.Value, goal),
                        total = DamageKinds.Sum(type => Damage(pair.Value.m_damages, type)),
                        suggestion
                    };
                })
                .Where(option => option.suggestion != null)
                .OrderByDescending(option => option.suggestion.Id == option.suggestion.For)
                .ThenByDescending(option => option.weak)
                .ThenByDescending(option => option.total)
                .Select(option => option.suggestion)
                .FirstOrDefault();
        }

        // For a parry fight any shield will do, so the quickest to make; for heavy
        // hitters, one stronger than what is on hand.
        private static Suggestion ShieldGoal(Plan plan, Dictionary<string, int> stock, BossGoal goal)
        {
            Dictionary<string, ItemDrop.ItemData.SharedData> items = ItemsByName();
            float owned = stock
                .Where(pair => pair.Value > 0 && items.ContainsKey(pair.Key) &&
                    items[pair.Key].m_itemType == ItemDrop.ItemData.ItemType.Shield)
                .Select(pair => items[pair.Key].m_blockPower)
                .DefaultIfEmpty(-1f).Max();
            if (goal.Shield == ShieldUse.Parry && owned >= 0f)
            {
                return null;
            }

            string reason = goal.Shield == ShieldUse.Parry
                ? "Parry " + goal.Name + "'s attacks to stagger it for double damage."
                : "A safety net against " + goal.Name + "'s heavy hits.";
            var options = items
                .Where(pair => pair.Value.m_itemType == ItemDrop.ItemData.ItemType.Shield &&
                    plan.Planner.Makings.ContainsKey(pair.Key) && pair.Value.m_blockPower > owned)
                .Select(pair => new
                {
                    block = pair.Value.m_blockPower,
                    suggestion = BringSuggestion(plan, stock, goal,
                        new Shortfall { Item = pair.Key, Short = 1, Where = "" }, reason)
                })
                .Where(option => option.suggestion != null);

            var ranked = goal.Shield == ShieldUse.Parry
                ? options.OrderByDescending(option => option.suggestion.Id == option.suggestion.For)
                    .ThenBy(option => option.block)
                : options.OrderByDescending(option => option.block)
                    .ThenByDescending(option => option.suggestion.Id == option.suggestion.For);
            return ranked.Select(option => option.suggestion).FirstOrDefault();
        }

        // The quickest bow, or other launcher, for this ammo.
        private static Suggestion Launcher(Plan plan, Dictionary<string, int> stock, BossGoal goal,
            Dictionary<string, ItemDrop.ItemData.SharedData> items, ItemDrop.ItemData.SharedData ammo, string reason)
        {
            return items
                .Where(pair => plan.Planner.Makings.ContainsKey(pair.Key) && WeaponTypes.Contains(pair.Value.m_itemType) &&
                    pair.Value.m_ammoType == ammo.m_ammoType)
                .Select(pair => BringSuggestion(plan, stock, goal,
                    new Shortfall { Item = pair.Key, Short = 1, Where = "" }, reason))
                .Where(suggestion => suggestion != null)
                .OrderByDescending(suggestion => suggestion.Id == suggestion.For)
                .FirstOrDefault();
        }

        // Made things go through the planner; found things say where to look,
        // unless a key or tool they need is missing.
        private static Suggestion BringSuggestion(Plan plan, Dictionary<string, int> stock, BossGoal goal,
            Shortfall missing, string reason, string label = "")
        {
            string item = missing.Item;
            string step;
            if (plan.Planner.Makings.TryGetValue(item, out Making making))
            {
                step = plan.Planner.Make(item);

                // Once a mead has been made its base counts as known, so check
                // the stock to tell whether to brew another base first.
                string ingredient = step == "" ? making.Materials.FirstOrDefault(material =>
                    !(stock.TryGetValue(material, out int have) && have > 0)) : null;
                if (ingredient != null)
                {
                    string again = plan.Planner.Make(ingredient);
                    step = again == "" ? ingredient : again;
                }
            }
            else if (plan.Planner.Gathers.ContainsKey(item) && plan.Planner.Get(item) != "")
            {
                step = plan.Planner.Get(item);
            }
            else
            {
                return new Suggestion
                {
                    Id = "collect:" + item,
                    For = item,
                    Name = "Collect " + missing.Short + " more " + Localized(item),
                    Tier = Tier.Boss,
                    Reason = missing.Where + ", to summon " + goal.Name + ".",
                    Detail = ""
                };
            }

            bool gather = step != null && step.StartsWith(Planner.GatherPrefix);
            return plan.ForGoal(item, gather ? step : item, step, Tier.Boss, Localized(item) + label, gather ? "" : reason);
        }

        // New pieces and upgrades to what is worn, compared slot by slot, keeping
        // only what it takes to reach the boss's armor.
        private static IEnumerable<Suggestion> ArmorGoals(Player player, Plan plan, float needed)
        {
            List<ItemDrop.ItemData> worn = player.GetInventory().GetEquippedItems();
            List<ArmorOption> options = new List<ArmorOption>();
            Dictionary<string, Suggestion> offers = new Dictionary<string, Suggestion>();

            for (int slot = 0; slot < ArmorSlots.Length; slot++)
            {
                ItemDrop.ItemData.ItemType type = ArmorSlots[slot];
                ItemDrop.ItemData current = worn.FirstOrDefault(item => item.m_shared.m_itemType == type);
                float armor = current != null ? current.GetArmor() : 0f;

                foreach (Recipe recipe in plan.Recipes.Values.Where(r =>
                    r.m_item.m_itemData.m_shared.m_itemType == type && !player.IsMaterialKnown(ItemName(r))))
                {
                    string id = ItemName(recipe);
                    float gain = recipe.m_item.m_itemData.m_shared.m_armor - armor;
                    Suggestion offer = plan.ForGoal(id, id, plan.Planner.Make(id), Tier.Armor, Localized(id),
                        "Adds " + Mathf.RoundToInt(gain) + " armor.");
                    if (offer != null)
                    {
                        options.Add(new ArmorOption { Id = id, Slot = slot, Gain = gain, Cost = Materials(recipe.m_resources, 1) });
                        offers[id] = offer;
                    }
                }

                string upgrade = current != null ? plan.AddUpgrade(current) : null;
                if (upgrade != null)
                {
                    float gain = current.m_shared.m_armorPerLevel;
                    Suggestion offer = plan.ForGoal(upgrade, upgrade, plan.Planner.Make(upgrade), Tier.Armor,
                        "Upgrade " + Localized(current.m_shared.m_name), "Adds " + Mathf.RoundToInt(gain) + " armor.");
                    if (offer != null)
                    {
                        Recipe recipe = ObjectDB.instance.GetRecipe(current);
                        options.Add(new ArmorOption
                        {
                            Id = upgrade,
                            Slot = slot,
                            Gain = gain,
                            Cost = Materials(recipe.m_resources, current.m_quality + 1)
                        });
                        offers[upgrade] = offer;
                    }
                }
            }

            return PathRules.KeyArmor(options, needed).Select(id =>
            {
                offers[id].Value = options.First(option => option.Id == id).GainPerMaterial;
                return offers[id];
            });
        }

        // The game's recipes and pieces as the planner sees them, plus what is
        // needed to describe each step on the panel.
        private class Plan
        {
            public readonly Planner Planner = new Planner();
            public readonly Dictionary<string, Recipe> Recipes;
            public readonly Dictionary<string, Piece> Pieces = new Dictionary<string, Piece>();
            public readonly List<Gate> OpenGates = new List<Gate>();
            private readonly Dictionary<string, Gate> gatesByTarget = new Dictionary<string, Gate>();
            private readonly Dictionary<string, Suggestion> upgrades = new Dictionary<string, Suggestion>();

            // Item made by a smelter, fermenter or cooking station, to how it is made there.
            private readonly Dictionary<string, string> conversions = new Dictionary<string, string>();

            public Plan(Player player, HashSet<string> built)
            {
                Planner.Known = new HashSet<string>(KnownMaterial(player));
                Planner.StationLevels = new Dictionary<string, int>(KnownStations(player));
                Planner.Built = built;

                Recipes = ObjectDB.instance.m_recipes
                    .Where(IsUsable)
                    .GroupBy(ItemName)
                    .ToDictionary(sameItem => sameItem.Key, sameItem => sameItem.OrderBy(recipe => recipe.m_amount).First());
                foreach (Recipe recipe in Recipes.Values)
                {
                    Planner.Makings[ItemName(recipe)] = new Making
                    {
                        Station = recipe.m_craftingStation != null ? recipe.m_craftingStation.m_name : "",
                        Level = Mathf.Max(recipe.m_minStationLevel, 1),
                        Materials = Needs(recipe.m_resources, 1)
                    };
                }

                // Built pieces first, so a material made at several stations is made
                // at one already standing; then cheapest first.
                IEnumerable<Piece> pieces = HammerPieces()
                    .OrderByDescending(piece => built.Contains(piece.m_name))
                    .ThenBy(piece => Materials(piece.m_resources, 1));
                foreach (Piece piece in pieces)
                {
                    AddPiece(piece, built);
                }

                foreach (Gate gate in Gates)
                {
                    ItemDrop.ItemData.SharedData tool = Shared(gate.Tool);
                    ItemDrop.ItemData.SharedData target = Shared(gate.Target);
                    if (tool != null && target != null && (gate.AfterBoss.Length == 0 || Defeated(gate.AfterBoss)))
                    {
                        Planner.Gathers[target.m_name] = tool.m_name;
                        gatesByTarget[target.m_name] = gate;
                        OpenGates.Add(gate);
                    }
                }
            }

            private void AddPiece(Piece piece, HashSet<string> built)
            {
                if (Pieces.ContainsKey(piece.m_name))
                {
                    return;
                }

                Pieces[piece.m_name] = piece;
                Planner.Makings[piece.m_name] = new Making
                {
                    Station = piece.m_craftingStation != null ? piece.m_craftingStation.m_name : "",
                    Materials = Needs(piece.m_resources, 1)
                };

                CraftingStation station = piece.GetComponent<CraftingStation>();
                if (station != null && !Planner.StationPieces.ContainsKey(station.m_name))
                {
                    Planner.StationPieces[station.m_name] = piece.m_name;
                }

                StationExtension extension = piece.GetComponent<StationExtension>();
                if (extension != null && extension.m_craftingStation != null && !built.Contains(piece.m_name))
                {
                    string name = extension.m_craftingStation.m_name;
                    if (!Planner.Extensions.ContainsKey(name))
                    {
                        Planner.Extensions[name] = new List<string>();
                    }

                    Planner.Extensions[name].Add(piece.m_name);
                }

                Smelter smelter = piece.GetComponent<Smelter>();
                if (smelter != null)
                {
                    foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
                    {
                        AddConversion(piece, conversion.m_from, conversion.m_to);
                    }
                }

                Fermenter fermenter = piece.GetComponent<Fermenter>();
                if (fermenter != null)
                {
                    foreach (Fermenter.ItemConversion conversion in fermenter.m_conversion)
                    {
                        AddConversion(piece, conversion.m_from, conversion.m_to);
                    }
                }

                CookingStation cooking = piece.GetComponent<CookingStation>();
                if (cooking != null)
                {
                    foreach (CookingStation.ItemConversion conversion in cooking.m_conversion)
                    {
                        AddConversion(piece, conversion.m_from, conversion.m_to);
                    }
                }
            }

            private void AddConversion(Piece piece, ItemDrop from, ItemDrop to)
            {
                if (from == null || to == null || Planner.Makings.ContainsKey(to.m_itemData.m_shared.m_name))
                {
                    return;
                }

                conversions[to.m_itemData.m_shared.m_name] = "Make at " + Localized(piece.m_name) + " from " +
                    Localized(from.m_itemData.m_shared.m_name);
                Planner.Makings[to.m_itemData.m_shared.m_name] = new Making
                {
                    Piece = piece.m_name,
                    Materials = new List<string> { from.m_itemData.m_shared.m_name }
                };
            }

            // Adds the next quality of a worn item as something the planner can make.
            public string AddUpgrade(ItemDrop.ItemData item)
            {
                Recipe recipe = ObjectDB.instance.GetRecipe(item);
                int next = item.m_quality + 1;
                if (recipe == null || recipe.m_craftingStation == null || next > item.m_shared.m_maxQuality)
                {
                    return null;
                }

                string id = "upgrade:" + item.m_shared.m_name;
                string station = recipe.m_craftingStation.m_name;
                int level = recipe.GetRequiredStationLevel(next);
                Planner.Makings[id] = new Making { Station = station, Level = level, Materials = Needs(recipe.m_resources, next) };

                string costs = string.Join(", ", recipe.m_resources
                    .Where(requirement => requirement.m_resItem != null && requirement.GetAmount(next) > 0)
                    .Select(requirement => requirement.GetAmount(next) + " " +
                        Localized(requirement.m_resItem.m_itemData.m_shared.m_name))
                    .ToArray());
                upgrades[id] = new Suggestion
                {
                    Id = id,
                    Name = "Upgrade " + Localized(item.m_shared.m_name),
                    Detail = "Upgrade at " + Localized(station) + " " + level + ": " + costs
                };
                return id;
            }

            // The goal itself when nothing is missing, otherwise its first step.
            public Suggestion ForGoal(string goal, string ownStep, string step, Tier tier, string goalName, string reason)
            {
                if (step == null)
                {
                    return null;
                }

                string id = step == "" ? ownStep : step;
                Suggestion suggestion = Describe(id, tier, id == ownStep ? reason : "Needed for " + goalName + ".");
                if (suggestion.Reason.Length == 0)
                {
                    suggestion.Reason = "Needed for " + goalName + ".";
                }

                suggestion.For = goal;
                return suggestion;
            }

            public Suggestion Describe(string id, Tier tier, string reason)
            {
                Suggestion suggestion;
                if (id.StartsWith(Planner.GatherPrefix))
                {
                    string target = id.Substring(Planner.GatherPrefix.Length);
                    Gate gate = gatesByTarget[target];
                    suggestion = new Suggestion
                    {
                        Name = "Find " + Localized(target),
                        Detail = "",
                        Reason = reason.Length > 0 ? reason : gate.Where + " with your " + ItemTitle(gate.Tool) + "."
                    };
                }
                else if (upgrades.TryGetValue(id, out Suggestion upgrade))
                {
                    suggestion = new Suggestion { Name = upgrade.Name, Detail = upgrade.Detail, Reason = reason };
                }
                else if (Pieces.TryGetValue(id, out Piece piece))
                {
                    string costs = Costs(piece.m_resources);
                    string at = piece.m_craftingStation != null ? " at " + Localized(piece.m_craftingStation.m_name) : "";
                    suggestion = new Suggestion
                    {
                        Name = Localized(piece.m_name),
                        Detail = costs.Length > 0 ? "Build" + at + ": " + costs : "",
                        Reason = reason
                    };
                }
                else if (Recipes.TryGetValue(id, out Recipe recipe))
                {
                    suggestion = new Suggestion { Name = Localized(id), Detail = CraftDetail(recipe), Reason = reason };
                }
                else
                {
                    // Made at a smelter, fermenter or cooking station rather than crafted.
                    conversions.TryGetValue(id, out string how);
                    suggestion = new Suggestion { Name = Localized(id), Detail = how ?? "", Reason = reason };
                }

                suggestion.Id = id;
                suggestion.For = id;
                suggestion.Tier = tier;
                return suggestion;
            }
        }

        private static List<string> Needs(Piece.Requirement[] requirements, int quality)
        {
            return requirements
                .Where(requirement => requirement.m_resItem != null && requirement.GetAmount(quality) > 0)
                .Select(requirement => requirement.m_resItem.m_itemData.m_shared.m_name)
                .ToList();
        }

        private static bool IsUsable(Recipe recipe)
        {
            return recipe != null && recipe.m_enabled && recipe.m_item != null;
        }

        private static string ItemName(Recipe recipe)
        {
            return recipe.m_item.m_itemData.m_shared.m_name;
        }

        private static IEnumerable<Piece> HammerPieces()
        {
            GameObject hammer = ObjectDB.instance.GetItemPrefab(BuildTool);
            if (hammer == null)
            {
                return Enumerable.Empty<Piece>();
            }

            return hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces
                .Where(prefab => prefab != null)
                .Select(prefab => prefab.GetComponent<Piece>())
                .Where(piece => piece != null && piece.m_enabled);
        }

        private static IEnumerable<Suggestion> Comfort(List<Piece> pieces, HashSet<string> built)
        {
            List<Piece> comfort = pieces.Where(piece => piece.m_comfort > 0).ToList();
            List<ComfortPiece> rows = comfort
                .Select(piece => new ComfortPiece
                {
                    Id = piece.m_name,
                    Group = (int)piece.m_comfortGroup,
                    Comfort = piece.m_comfort,
                    Built = built.Contains(piece.m_name)
                })
                .ToList();

            return PathRules.ComfortToBuild(rows).Select(id =>
            {
                Piece piece = comfort.First(candidate => candidate.m_name == id);
                string costs = Costs(piece.m_resources);
                return new Suggestion
                {
                    Id = id,
                    For = id,
                    Name = Localized(id),
                    Tier = Tier.Comfort,
                    Reason = "Adds " + piece.m_comfort + " comfort.",
                    Detail = costs.Length > 0 ? "Build: " + costs : ""
                };
            });
        }

        // What the character carries plus what is in chests close enough to fetch.
        private static Dictionary<string, int> Stock(Player player, List<Piece> nearby)
        {
            IEnumerable<ItemDrop.ItemData> items = nearby
                .Select(piece => piece.GetComponent<Container>())
                .Where(container => container != null)
                .SelectMany(container => container.GetInventory().GetAllItems())
                .Concat(player.GetInventory().GetAllItems());

            Dictionary<string, int> stock = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in items)
            {
                stock.TryGetValue(item.m_shared.m_name, out int total);
                stock[item.m_shared.m_name] = total + item.m_stack;
            }

            return stock;
        }

        private static bool CanMake(Recipe recipe, Dictionary<string, int> stock)
        {
            return PathRules.HasAll(recipe.m_resources
                .Where(requirement => requirement.m_resItem != null && requirement.m_amount > 0)
                .Select(requirement => new KeyValuePair<string, int>(
                    requirement.m_resItem.m_itemData.m_shared.m_name, requirement.m_amount)), stock);
        }

        private static string CraftDetail(Recipe recipe)
        {
            string at = recipe.m_craftingStation != null
                ? " at " + Localized(recipe.m_craftingStation.m_name) + " " + Mathf.Max(recipe.m_minStationLevel, 1)
                : "";
            return "Craft" + at + ": " + Costs(recipe.m_resources);
        }

        private static int Materials(Piece.Requirement[] requirements, int quality)
        {
            return requirements.Where(requirement => requirement.m_resItem != null)
                .Sum(requirement => requirement.GetAmount(quality));
        }

        private static ItemDrop.ItemData.SharedData Shared(string prefab)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            if (item == null)
            {
                if (MissingItems.Add(prefab))
                {
                    log.LogWarning("No item named " + prefab);
                }

                return null;
            }

            return item.GetComponent<ItemDrop>().m_itemData.m_shared;
        }

        private static string ItemTitle(string prefab)
        {
            ItemDrop.ItemData.SharedData shared = Shared(prefab);
            return shared != null ? Localized(shared.m_name) : prefab;
        }

        private static string Costs(Piece.Requirement[] requirements)
        {
            return string.Join(", ", requirements
                .Where(requirement => requirement.m_resItem != null && requirement.m_amount > 0)
                .Select(requirement => requirement.m_amount + " " +
                    Localized(requirement.m_resItem.m_itemData.m_shared.m_name))
                .ToArray());
        }

        private static string Localized(string name)
        {
            return Localization.instance.Localize(name);
        }

        // Finds what was built before the mod, or by someone else, once it is nearby.
        private static void RecordNearbyPieces(Player player, List<Piece> nearby)
        {
            HashSet<string> built = Built(player);
            int before = built.Count;
            foreach (Piece piece in nearby)
            {
                if (piece.IsPlacedByPlayer())
                {
                    built.Add(piece.m_name);
                }
            }

            if (built.Count != before)
            {
                player.m_customData[BuiltKey] = PathRules.FormatSet(built);
            }
        }

        private static HashSet<string> Built(Player player)
        {
            player.m_customData.TryGetValue(BuiltKey, out string text);
            return PathRules.ParseSet(text);
        }

        private static List<KeyValuePair<string, string>> Unlocks(Player player)
        {
            player.m_customData.TryGetValue(UnlocksKey, out string text);
            return PathRules.ParseUnlocks(text);
        }

        private static void RecordUnlock(Player player, string id)
        {
            List<KeyValuePair<string, string>> unlocks = Unlocks(player);
            unlocks.Add(new KeyValuePair<string, string>(id, lastCause));
            player.m_customData[UnlocksKey] = PathRules.FormatUnlocks(unlocks);
            dirty = true;
        }

        private static HashSet<string> Knowledge(Player player)
        {
            HashSet<string> knowledge = new HashSet<string>(KnownMaterial(player));
            foreach (KeyValuePair<string, int> station in KnownStations(player))
            {
                knowledge.Add(PathRules.StationEntry(station.Key, station.Value));
            }

            return knowledge;
        }

        // Awake also checks recipes, before the character's knowledge has loaded,
        // so comparing only starts here.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "Load")]
        private static void AfterLoad(Player __instance)
        {
            seenPlayer = __instance;
            seenKnowledge = Knowledge(__instance);
        }

        // The game checks recipes whenever an item or station is discovered, so
        // whatever is new since the last check is what caused these unlocks.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
        private static void BeforeRecipeCheck(Player __instance)
        {
            if (__instance == seenPlayer)
            {
                lastCause = PathRules.NewDiscovery(seenKnowledge, Knowledge(__instance));
                dirty |= lastCause.Length > 0;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdateKnownRecipesList")]
        private static void AfterRecipeCheck(Player __instance)
        {
            if (__instance == seenPlayer)
            {
                seenKnowledge = Knowledge(__instance);
                lastCause = "";
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        private static void BeforeRecipe(Player __instance, Recipe recipe, out bool __state)
        {
            __state = __instance.IsRecipeKnown(recipe.m_item.m_itemData.m_shared.m_name);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "AddKnownRecipe")]
        private static void AfterRecipe(Player __instance, Recipe recipe, bool __state)
        {
            if (!__state && __instance == Player.m_localPlayer)
            {
                RecordUnlock(__instance, recipe.m_item.m_itemData.m_shared.m_name);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "AddKnownPiece")]
        private static void BeforePiece(Player __instance, Piece piece, out bool __state)
        {
            __state = __instance.IsRecipeKnown(piece.m_name);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "AddKnownPiece")]
        private static void AfterPiece(Player __instance, Piece piece, bool __state)
        {
            if (!__state && __instance == Player.m_localPlayer)
            {
                RecordUnlock(__instance, piece.m_name);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "PlacePiece")]
        private static void AfterPlacePiece(Player __instance, Piece piece)
        {
            if (__instance != Player.m_localPlayer)
            {
                return;
            }

            HashSet<string> built = Built(__instance);
            if (built.Add(piece.m_name))
            {
                __instance.m_customData[BuiltKey] = PathRules.FormatSet(built);
                dirty = true;
            }
        }
    }
}
