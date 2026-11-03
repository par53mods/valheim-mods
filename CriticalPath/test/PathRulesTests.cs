using System;
using System.Collections.Generic;
using System.Linq;
using CriticalPath;

public static class PathRulesTests
{
    private static int failures;

    private static void Check(string name, object expected, object actual)
    {
        if (Equals(expected, actual))
        {
            return;
        }

        failures++;
        Console.WriteLine("FAIL " + name + ": expected " + expected + ", got " + actual);
    }

    private static Suggestion Make(string id, Tier tier, int sequence = 0)
    {
        return new Suggestion { Id = id, Name = id, Tier = tier, Sequence = sequence };
    }

    private static string Ids(IEnumerable<Suggestion> suggestions)
    {
        return string.Join(",", suggestions.Select(s => s.Id).ToArray());
    }

    private static ComfortPiece Comfort(string id, int group, int comfort, bool built)
    {
        return new ComfortPiece { Id = id, Group = group, Comfort = comfort, Built = built };
    }

    private static Making Needs(string station, int level, params string[] materials)
    {
        return new Making { Station = station, Level = level, Materials = materials.ToList() };
    }

    // Early Black Forest: a forge, copper and tin found, no bronze yet.
    private static Planner BronzeAge()
    {
        Planner plan = new Planner();
        plan.Known = new HashSet<string> { "wood", "stone", "copper", "tin", "antlerPickaxe", "carrot" };
        plan.StationLevels = new Dictionary<string, int> { { "bench", 1 }, { "forge", 1 } };
        plan.Built = new HashSet<string> { "piece_bench", "piece_forge" };
        plan.Makings["bronzeAxe"] = Needs("forge", 1, "wood", "bronze");
        plan.Makings["bronze"] = Needs("forge", 1, "copper", "tin");
        plan.Makings["ironPickaxe"] = Needs("forge", 2, "wood", "iron");
        plan.Makings["carrotSoup"] = Needs("cauldron", 1, "carrot");
        plan.Makings["piece_cauldron"] = Needs("bench", 1, "tin");
        plan.Makings["piece_cooler"] = Needs("bench", 1, "copper");
        plan.Makings["piece_anvil"] = Needs("bench", 1, "bronze");
        plan.StationPieces["cauldron"] = "piece_cauldron";
        plan.StationPieces["forge"] = "piece_forge";
        plan.Extensions["forge"] = new List<string> { "piece_anvil", "piece_cooler" };
        plan.Gathers["fineWood"] = "bronzeAxe";
        plan.Gathers["copperOre"] = "antlerPickaxe";
        return plan;
    }

    public static int Main()
    {
        Check("tier order wins", "axe,soup,chair", Ids(PathRules.Choose(new[]
        {
            Make("chair", Tier.Comfort), Make("soup", Tier.Food), Make("axe", Tier.Harvest)
        }, 3)));
        Check("newest unlock breaks ties", "new,old", Ids(PathRules.Choose(new[]
        {
            Make("old", Tier.Gear, 3), Make("new", Tier.Gear, 8)
        }, 2)));
        Check("name settles a full tie", "a,b", Ids(PathRules.Choose(new[]
        {
            Make("b", Tier.Food), Make("a", Tier.Food)
        }, 2)));
        Check("a tier leaves room for others", "s1,s2,chair", Ids(PathRules.Choose(new[]
        {
            Make("s1", Tier.Harvest), Make("s2", Tier.Harvest), Make("s3", Tier.Harvest), Make("chair", Tier.Comfort)
        }, 3)));
        Check("one tier still fills the list", "s1,s2,s3", Ids(PathRules.Choose(new[]
        {
            Make("s1", Tier.Harvest), Make("s2", Tier.Harvest), Make("s3", Tier.Harvest)
        }, 3)));
        Check("count is respected", 1, PathRules.Choose(new[] { Make("a", Tier.Gear), Make("b", Tier.Gear) }, 1).Count);
        Check("nothing to suggest", 0, PathRules.Choose(new Suggestion[0], 3).Count);
        Check("a shared step shows once, at its best rank", "forge,chair", Ids(PathRules.Choose(new[]
        {
            Make("forge", Tier.Armor), Make("chair", Tier.Comfort), Make("forge", Tier.Harvest)
        }, 3)));
        Check("a shared step keeps its best goal", Tier.Harvest, PathRules.Choose(new[]
        {
            Make("forge", Tier.Armor), Make("forge", Tier.Harvest)
        }, 3)[0].Tier);

        Check("finding fine wood puts the table first", "chair,table,axe", Ids(PathRules.Choose(new[]
        {
            Make("axe", Tier.Harvest), Make("soup", Tier.Food),
            new Suggestion { Id = "table", Name = "table", Tier = Tier.Comfort, Latest = true },
            new Suggestion { Id = "chair", Name = "chair", Tier = Tier.Comfort, Latest = true }
        }, 3)));
        Check("latest batch can fill the list", "a,b", Ids(PathRules.Choose(new[]
        {
            new Suggestion { Id = "a", Name = "a", Tier = Tier.Gear, Latest = true },
            new Suggestion { Id = "b", Name = "b", Tier = Tier.Gear, Latest = true },
            Make("axe", Tier.Harvest)
        }, 2)));

        Planner plan = BronzeAge();
        Check("bronze axe needs bronze first", "bronze", plan.Make("bronzeAxe"));
        Check("fine wood leads back to bronze", "bronze", plan.Get("fineWood"));
        Check("a known tool means go gathering", "gather:copperOre", plan.Get("copperOre"));
        Check("tin and a recipe at a new station", "piece_cauldron", plan.Make("carrotSoup"));
        Check("nothing missing", "", plan.Make("bronze"));
        Check("unknown material is out of reach", null, plan.Make("ironPickaxe"));
        plan.Known.Add("iron");
        Check("upgrade the forge for iron tools", "bronze", plan.Make("ironPickaxe"));
        plan.Makings["piece_anvil"] = Needs("bench", 1, "blackMetal");
        Check("an upgrade skips the one it can't make", "piece_cooler", plan.Make("ironPickaxe"));
        plan.Known.Remove("iron");
        Check("no recipe or tool is out of reach", null, plan.Get("silverOre"));

        plan.Known.Add("bronze");
        Check("with bronze the axe is next", "", plan.Make("bronzeAxe"));
        plan.Known.Add("bronzeAxe");
        Check("with the axe go chop", "gather:fineWood", plan.Get("fineWood"));

        Planner smelting = new Planner();
        smelting.Known = new HashSet<string> { "copperOre", "stone" };
        smelting.Makings["copper"] = new Making { Piece = "piece_smelter", Materials = new List<string> { "copperOre" } };
        smelting.Makings["piece_smelter"] = Needs("", 1, "stone", "surtlingCore");
        Check("smelter waits on its own materials", null, smelting.Get("copper"));
        smelting.Known.Add("surtlingCore");
        Check("copper needs the smelter", "piece_smelter", smelting.Get("copper"));
        smelting.Built.Add("piece_smelter");
        Check("then the copper itself", "copper", smelting.Get("copper"));

        Planner loop = new Planner();
        loop.Makings["a"] = Needs("", 1, "b");
        loop.Makings["b"] = Needs("", 1, "a");
        Check("a loop is out of reach", null, loop.Get("a"));

        Check("food you can cook comes first", "stew,jam", Ids(PathRules.Choose(new[]
        {
            Make("jam", Tier.Food), new Suggestion { Id = "stew", Name = "stew", Tier = Tier.Food, Ready = true }
        }, 2)));
        Check("ready food stays below tools", "axe,stew", Ids(PathRules.Choose(new[]
        {
            new Suggestion { Id = "stew", Name = "stew", Tier = Tier.Food, Ready = true }, Make("axe", Tier.Harvest)
        }, 2)));
        Check("better food comes first", "stew,soup", Ids(PathRules.Choose(new[]
        {
            new Suggestion { Id = "soup", Name = "soup", Tier = Tier.Food, Value = 25f },
            new Suggestion { Id = "stew", Name = "stew", Tier = Tier.Food, Value = 40f }
        }, 2)));
        Check("cookable beats better food", "soup,stew", Ids(PathRules.Choose(new[]
        {
            new Suggestion { Id = "soup", Name = "soup", Tier = Tier.Food, Value = 25f, Ready = true },
            new Suggestion { Id = "stew", Name = "stew", Tier = Tier.Food, Value = 40f }
        }, 2)));
        Check("food beats armor", "soup,helm", Ids(PathRules.Choose(new[]
        {
            Make("helm", Tier.Armor), Make("soup", Tier.Food)
        }, 2)));
        Check("boss meads sit between food and armor", "soup,mead,helm", Ids(PathRules.Choose(new[]
        {
            Make("helm", Tier.Armor), Make("mead", Tier.Boss), Make("soup", Tier.Food)
        }, 3)));

        Planner brewing = new Planner();
        brewing.Known = new HashSet<string> { "honey", "neck", "piece_cauldron" };
        brewing.StationLevels = new Dictionary<string, int> { { "cauldron", 1 }, { "bench", 1 } };
        brewing.Makings["poisonMead"] = new Making { Piece = "piece_fermenter", Materials = new List<string> { "poisonBase" } };
        brewing.Makings["poisonBase"] = Needs("cauldron", 1, "honey", "neck");
        brewing.Makings["piece_fermenter"] = Needs("bench", 1, "fineWood");
        Check("mead needs fine wood for the fermenter", null, brewing.Make("poisonMead"));
        brewing.Known.Add("fineWood");
        Check("then the fermenter", "piece_fermenter", brewing.Make("poisonMead"));
        brewing.Built.Add("piece_fermenter");
        Check("then the mead base", "poisonBase", brewing.Make("poisonMead"));

        var armor = new[]
        {
            new ArmorOption { Id = "bronzeHelm", Slot = 0, Gain = 6f, Cost = 10 },
            new ArmorOption { Id = "upgradeHelm", Slot = 0, Gain = 2f, Cost = 2 },
            new ArmorOption { Id = "bronzeChest", Slot = 1, Gain = 6f, Cost = 10 },
            new ArmorOption { Id = "bronzeLegs", Slot = 2, Gain = 4f, Cost = 10 },
            new ArmorOption { Id = "worseBoots", Slot = 3, Gain = -1f, Cost = 1 }
        };
        Check("cheap upgrade beats a costly piece", "upgradeHelm,bronzeChest,bronzeLegs",
            string.Join(",", PathRules.KeyArmor(armor, 50f).ToArray()));
        Check("stops once armor is enough", "upgradeHelm,bronzeChest",
            string.Join(",", PathRules.KeyArmor(armor, 7f).ToArray()));
        Check("nothing needed", 0, PathRules.KeyArmor(armor, 0f).Count);
        Check("big gain beats a small cheap upgrade", "bronzeHelm",
            string.Join(",", PathRules.KeyArmor(new[]
            {
                new ArmorOption { Id = "bronzeHelm", Slot = 0, Gain = 8f, Cost = 5 },
                new ArmorOption { Id = "upgradeHelm", Slot = 0, Gain = 2f, Cost = 4 }
            }, 2f).ToArray()));

        Check("ready leaves out gear and long jobs", "cauldron,soup", Ids(PathRules.WhenReady(new[]
        {
            new Suggestion { Id = "cauldron", Tier = Tier.Food, Latest = true },
            new Suggestion { Id = "sword", Tier = Tier.Gear, Latest = true },
            new Suggestion { Id = "helm", Tier = Tier.Armor, Latest = true },
            new Suggestion { Id = "soup", Tier = Tier.Food, Ready = true },
            new Suggestion { Id = "stew", Tier = Tier.Food },
            new Suggestion { Id = "kiln", Tier = Tier.Harvest }
        })));

        Check("more health only", "health", PathRules.FoodGain(30, 10, 0, 20, 15, 0, false));
        Check("more of both", "health/stamina", PathRules.FoodGain(30, 20, 0, 20, 15, 0, false));
        Check("eitr ignored without magic", "stamina", PathRules.FoodGain(10, 20, 40, 20, 15, 0, false));
        Check("all three with magic, eitr first", "eitr/health/stamina", PathRules.FoodGain(30, 20, 40, 20, 15, 0, true));
        Check("eitr and health", "eitr/health", PathRules.FoodGain(30, 10, 40, 20, 15, 0, true));
        Check("under the minimum is low", "low", PathRules.ArmorRating(20f, 30f));
        Check("at the minimum", "min", PathRules.ArmorRating(30f, 30f));
        Check("a third over is good", "good", PathRules.ArmorRating(40f, 30f));
        Check("two thirds over is great", "great", PathRules.ArmorRating(50f, 30f));

        var stock = new Dictionary<string, int> { { "$carrot", 3 }, { "$mushroom", 1 } };
        var soupCost = new[] { new KeyValuePair<string, int>("$carrot", 3), new KeyValuePair<string, int>("$mushroom", 1) };
        Check("stock covers the cost", true, PathRules.HasAll(soupCost, stock));
        Check("too few of an item", false, PathRules.HasAll(new[] { new KeyValuePair<string, int>("$carrot", 4) }, stock));
        Check("missing item", false, PathRules.HasAll(new[] { new KeyValuePair<string, int>("$honey", 1) }, stock));

        var history = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("old", "$copper"),
            new KeyValuePair<string, string>("chair", "$finewood"),
            new KeyValuePair<string, string>("table", "$finewood")
        };
        Check("batch shares the newest cause", 2, PathRules.LatestBatch(history).Count);
        Check("batch leaves older causes out", false, PathRules.LatestBatch(history).Contains("old"));
        Check("no history, no batch", 0, PathRules.LatestBatch(new List<KeyValuePair<string, string>>()).Count);
        history.Add(new KeyValuePair<string, string>("start", ""));
        Check("unknown cause, no batch", 0, PathRules.LatestBatch(history).Count);

        var seen = new HashSet<string> { "$wood", PathRules.StationEntry("$bench", 1) };
        var now = new HashSet<string>(seen) { "$tin" };
        Check("new material is the discovery", "$tin", PathRules.NewDiscovery(seen, now));
        Check("nothing new", "", PathRules.NewDiscovery(seen, seen));
        var upgraded = new HashSet<string>(seen) { PathRules.StationEntry("$bench", 2) };
        Check("station upgrade names the station", "$bench", PathRules.NewDiscovery(seen, upgraded));

        Check("best unbuilt in a group", "table2", string.Join(",", PathRules.ComfortToBuild(new[]
        {
            Comfort("table1", 1, 1, false), Comfort("table2", 1, 2, false)
        }).ToArray()));
        Check("skips what is no better", "", string.Join(",", PathRules.ComfortToBuild(new[]
        {
            Comfort("table1", 1, 2, true), Comfort("table2", 1, 2, false)
        }).ToArray()));
        Check("upgrade over what is built", "table2", string.Join(",", PathRules.ComfortToBuild(new[]
        {
            Comfort("table1", 1, 1, true), Comfort("table2", 1, 2, false)
        }).ToArray()));
        Check("ungrouped pieces all count", "rug,tree", string.Join(",", PathRules.ComfortToBuild(new[]
        {
            Comfort("rug", 0, 1, false), Comfort("tree", 0, 1, false), Comfort("done", 0, 1, true)
        }).ToArray()));
        Check("no comfort is ignored", "", string.Join(",", PathRules.ComfortToBuild(new[]
        {
            Comfort("wall", 0, 0, false)
        }).ToArray()));

        Check("set round trip", 2, PathRules.ParseSet(PathRules.FormatSet(new[] { "$a", "$b" })).Count);
        Check("empty set", 0, PathRules.ParseSet(null).Count);
        var unlocks = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("$a", "$x"),
            new KeyValuePair<string, string>("$b", "")
        };
        var parsed = PathRules.ParseUnlocks(PathRules.FormatUnlocks(unlocks));
        Check("unlocks keep order", "$b", parsed[1].Key);
        Check("unlocks keep cause", "$x", parsed[0].Value);
        Check("damaged unlock record skipped", 1, PathRules.ParseUnlocks("$a|$x;broken;").Count);
        Check("empty unlocks", 0, PathRules.ParseUnlocks("").Count);

        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
