using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuickStackStore
{
    // Adds a Quick Stack button to the bottom edge of the inventory panel. It moves
    // stackable items from your inventory into nearby chests already holding them.
    // Each chest goes through the game's own Stack All
    // request, so a chest in use or owned by another player is treated the same way.
    // Optional settings hold some items back, such as enough for a portal; a second
    // press while the inventory is open stacks those too.
    [BepInPlugin("com.parallel53.quickstackstore", "QuickStackStore", "1.0.5")]
    public class QuickStackStorePlugin : BaseUnityPlugin
    {
        private const int HotbarRow = 0;

        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static readonly Func<Container, long, bool> CheckAccess =
            AccessTools.MethodDelegate<Func<Container, long, bool>>(
                AccessTools.Method(typeof(Container), "CheckAccess"));

        private static readonly HashSet<Container> Requested = new HashSet<Container>();

        private static ConfigEntry<float> radius;
        private static ConfigEntry<bool> keepHotbar;
        private static ConfigEntry<bool> stackMaterials;
        private static ConfigEntry<bool> stackRawFood;
        private static ConfigEntry<bool> stackCookedFood;
        private static ConfigEntry<bool> stackMeads;
        private static ConfigEntry<bool> preservePortalMats;
        private static ConfigEntry<bool> preserveBoatMats;
        private static ConfigEntry<bool> preserveBossItems;
        private static ConfigEntry<bool> keepOutpostKit;
        private static ConfigEntry<bool> keepCoins;
        private static ConfigEntry<bool> keepSpareAmmo;
        private static ConfigEntry<string> keepItems;

        // Altar offerings, by item prefab, used until an altar seen in the world says otherwise.
        private static readonly Dictionary<string, int> BossOfferings = new Dictionary<string, int>
        {
            { "TrophyDeer", 2 },
            { "AncientSeed", 3 },
            { "WitheredBone", 10 },
            { "DragonEgg", 3 },
            { "GoblinTotem", 5 },
        };

        private static readonly string[] OutpostKit = { "piece_workbench", "fire_pit", "bed" };

        // For the current press: how many of each item to hold back from the chests,
        // on top of anything that stays with the player anyway, such as the hotbar.
        private static readonly Dictionary<string, int> Hold = new Dictionary<string, int>();

        // What the last press held back, shown in its report.
        private static string keptLine = "";

        // Set when a press held something back; the next press, until the inventory
        // closes, stacks everything.
        private static bool heldBack;
        private static bool stackEverything;

        private const float DropFraction = 0.25f;

        // How long to wait for chests to answer before reporting anyway.
        private const float AnswerSeconds = 3f;

        // Names in the report before the rest are counted instead.
        private const int NamesShown = 3;

        // What the last press is waiting on: each item name with a matching chest,
        // and how many of it the player had before stacking.
        private static readonly Dictionary<string, int> Before = new Dictionary<string, int>();
        private static float reportBy;
        private static bool reporting;

        private static ManualLogSource log;
        private static Button button;
        private static bool answeringRequest;

        // True while this mod moves the spare part of held items, so they aren't
        // reported as equipped to itself.
        private static bool movingHeld;

        private void Awake()
        {
            log = Logger;
            radius = Config.Bind("General", "Radius", 10f,
                new ConfigDescription("How far away a chest can be, in metres.",
                    new AcceptableValueRange<float>(1f, 50f)));
            keepHotbar = Config.Bind("General", "KeepHotbar", true,
                "Leave items on your hotbar where they are.");
            stackMaterials = Config.Bind("Items", "StackMaterials", true,
                "Stack materials such as wood, ore and hides.");
            stackRawFood = Config.Bind("Items", "StackRawFood", true,
                "Stack food eaten as found, such as berries, mushrooms and honey.");
            stackCookedFood = Config.Bind("Items", "StackCookedFood", false,
                "Stack food made at a cooking station, oven or cauldron.");
            stackMeads = Config.Bind("Items", "StackMeads", false,
                "Stack meads and other drinks.");

            const string again = " Press Quick Stack again to stack them anyway.";
            preservePortalMats = Config.Bind("Keep", "PreservePortalMats", false,
                "If you carry everything for a portal you know how to build, keep that much with you." + again);
            preserveBoatMats = Config.Bind("Keep", "PreserveBoatMats", false,
                "If you carry everything for a boat you know how to build, keep that much for the best one you can build." + again);
            preserveBossItems = Config.Bind("Keep", "PreserveBossItems", false,
                "If you carry exactly the number of an item a boss altar takes, such as 10 withered bones, keep them with you." + again);
            keepOutpostKit = Config.Bind("Keep", "KeepOutpostKit", false,
                "If you carry everything for a workbench, campfire and bed, keep that much with you." + again);
            keepCoins = Config.Bind("Keep", "KeepCoins", false,
                "Keep your coins with you." + again);
            keepSpareAmmo = Config.Bind("Keep", "KeepSpareAmmo", false,
                "Keep arrows and bolts you haven't equipped with you." + again);
            keepItems = Config.Bind("Keep", "KeepItems", "",
                "Items to keep with you, as name:amount separated by commas, e.g. Wood:20, Resin:10, Coins:all. " +
                "Names are item prefab names or the English item names." + again);

            new Harmony("com.parallel53.quickstackstore").PatchAll(typeof(QuickStackStorePlugin));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static void AddButton(InventoryGui __instance)
        {
            button = Instantiate(__instance.m_takeAllButton, __instance.m_player);
            button.name = "QuickStackButton";
            button.interactable = true;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OnClick);

            // The copy must not inherit Take All's gamepad binding or localized label.
            foreach (UIGamePad pad in button.GetComponentsInChildren<UIGamePad>(true))
            {
                DestroyImmediate(pad);
            }

            foreach (Localize localize in button.GetComponentsInChildren<Localize>(true))
            {
                DestroyImmediate(localize);
            }

            button.GetComponentInChildren<TMP_Text>(true).text = "Quick Stack";

            // Centred on the panel's bottom edge, a quarter of its height lower.
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -rect.rect.height * DropFraction);
        }

        // A chest panel opens below the inventory, over where the button sits.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static void RefreshButton(InventoryGui __instance)
        {
            if (reporting && Time.unscaledTime >= reportBy)
            {
                log.LogInfo(Requested.Count + " chest(s) had not answered after " + AnswerSeconds + "s; reporting anyway");
                Report();
            }

            if (!InventoryGui.IsVisible())
            {
                heldBack = false;
                stackEverything = false;
            }

            bool show = CurrentContainer(__instance) == null;
            if (button != null && button.gameObject.activeSelf != show)
            {
                button.gameObject.SetActive(show);
            }
        }

        private static void OnClick()
        {
            if (Player.m_localPlayer != null)
            {
                StackIntoNearbyChests(Player.m_localPlayer);
            }
        }

        private static void StackIntoNearbyChests(Player player)
        {
            Vector3 position = player.transform.position;
            List<Piece> pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(position, radius.Value, pieces);

            // A press after one that held something back stacks everything, until
            // the inventory closes.
            stackEverything = stackEverything || heldBack;
            Hold.Clear();
            List<KeyValuePair<string, List<string>>> kept = stackEverything
                ? new List<KeyValuePair<string, List<string>>>()
                : ItemsToKeep(player);

            List<ItemDrop.ItemData> items = player.GetInventory().GetAllItems()
                .Where(item => !player.IsItemEquiped(item) && !StaysWithPlayer(item))
                .ToList();

            List<Container> nearby = pieces
                .Select(piece => piece.GetComponentInChildren<Container>())
                .Where(chest => chest != null && chest.GetInventory() != null)
                .Where(chest => IsAvailable(chest, player))
                .OrderBy(chest => Vector3.Distance(position, chest.transform.position))
                .ToList();

            Func<string, bool> inAChest = name => nearby.Any(chest => chest.GetInventory().ContainsItemByName(name));

            // Only mention what would otherwise have gone into a chest.
            List<string> keptNames = kept
                .Where(group => group.Value.Any(name => Held(name) > 0 && inAChest(name)))
                .Select(group => group.Key)
                .ToList();
            heldBack = keptNames.Count > 0;
            keptLine = heldBack
                ? "Kept " + List(keptNames) + ". Press again to stack them too"
                : "";

            List<Container> chests = nearby
                .Where(chest => items.Any(item => Movable(player, item.m_shared.m_name) > 0 &&
                    chest.GetInventory().ContainsItemByName(item.m_shared.m_name)))
                .ToList();

            if (chests.Count == 0)
            {
                string nothing = heldBack ? "Nothing else to stack\n" + keptLine : "No nearby chest holds anything you're carrying";
                player.Message(MessageHud.MessageType.Center, Small(nothing));
                return;
            }

            Before.Clear();
            foreach (string name in items
                .Select(item => item.m_shared.m_name)
                .Where(name => Movable(player, name) > 0 && chests.Any(chest => chest.GetInventory().ContainsItemByName(name))))
            {
                Before[name] = Movable(player, name);
            }

            reporting = true;
            reportBy = Time.unscaledTime + AnswerSeconds;
            Requested.Clear();
            foreach (Container chest in chests)
            {
                Requested.Add(chest);
                chest.StackAll();
            }
        }

        private static int Carried(Player player, string name)
        {
            return player.GetInventory().GetAllItems()
                .Where(item => item.m_shared.m_name == name && !player.IsItemEquiped(item) && !StaysWithPlayer(item))
                .Sum(item => item.m_stack);
        }

        private static int Held(string name)
        {
            int amount;
            return Hold.TryGetValue(name, out amount) ? amount : 0;
        }

        // How many of an item can go into chests now, after what is held back.
        private static int Movable(Player player, string name)
        {
            return Math.Max(0, Carried(player, name) - Held(name));
        }

        // Works out what this press keeps, filling Hold. Each group is what the report
        // calls it, with the item names it covers.
        private static List<KeyValuePair<string, List<string>>> ItemsToKeep(Player player)
        {
            Hold.Clear();
            List<KeyValuePair<string, List<string>>> groups = new List<KeyValuePair<string, List<string>>>();

            // Everything carried, hotbar included, but not what is equipped.
            Dictionary<string, int> carried = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (!player.IsItemEquiped(item))
                {
                    int count;
                    carried.TryGetValue(item.m_shared.m_name, out count);
                    carried[item.m_shared.m_name] = count + item.m_stack;
                }
            }

            Dictionary<string, int> keep = new Dictionary<string, int>();
            Func<string, int> have = name => { int n; return carried.TryGetValue(name, out n) ? n : 0; };
            Func<string, int> reserved = name => { int n; return keep.TryGetValue(name, out n) ? n : 0; };
            Action<string, int> reserve = (name, amount) =>
                keep[name] = (int)Math.Min((long)reserved(name) + amount, int.MaxValue);
            Func<IEnumerable<Piece>, bool> affordable = set =>
            {
                Dictionary<string, int> need = new Dictionary<string, int>();
                foreach (Piece.Requirement req in set.SelectMany(piece => piece.m_resources))
                {
                    if (req.m_resItem != null && req.m_amount > 0)
                    {
                        string name = req.m_resItem.m_itemData.m_shared.m_name;
                        int n;
                        need.TryGetValue(name, out n);
                        need[name] = n + req.m_amount;
                    }
                }

                return need.Count > 0 && need.All(pair => have(pair.Key) - reserved(pair.Key) >= pair.Value);
            };
            Func<IEnumerable<Piece>, string, bool> keepPieces = (set, label) =>
            {
                List<Piece> list = set.ToList();
                if (!affordable(list))
                {
                    return false;
                }

                List<string> names = new List<string>();
                foreach (Piece.Requirement req in list.SelectMany(piece => piece.m_resources))
                {
                    if (req.m_resItem != null && req.m_amount > 0)
                    {
                        reserve(req.m_resItem.m_itemData.m_shared.m_name, req.m_amount);
                        names.Add(req.m_resItem.m_itemData.m_shared.m_name);
                    }
                }

                groups.Add(new KeyValuePair<string, List<string>>(label, names));
                return true;
            };

            // Fixed amounts first, so recipes only count what is left over.
            if (keepCoins.Value)
            {
                KeepAllOf(groups, keep, "$item_coins", "coins");
            }

            if (keepSpareAmmo.Value)
            {
                List<string> ammo = carried.Keys.Where(name => player.GetInventory().GetAllItems()
                    .Any(item => item.m_shared.m_name == name && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo))
                    .ToList();
                foreach (string name in ammo)
                {
                    keep[name] = int.MaxValue;
                }

                groups.Add(new KeyValuePair<string, List<string>>("spare ammo", ammo));
            }

            foreach (KeyValuePair<string, int> entry in ParseKeepItems())
            {
                reserve(entry.Key, entry.Value);
                groups.Add(new KeyValuePair<string, List<string>>(Shown(entry.Key), new List<string> { entry.Key }));
            }

            // Build pieces the player has learned, from the hammer.
            List<Piece> known = HammerPieces()
                .Where(piece => piece.m_enabled && player.IsRecipeKnown(piece.m_name))
                .ToList();
            Func<Piece, int> cost = piece => piece.m_resources.Where(req => req.m_resItem != null).Sum(req => req.m_amount);

            if (preservePortalMats.Value)
            {
                foreach (Piece portal in known.Where(piece => piece.GetComponentInChildren<TeleportWorld>(true) != null).OrderByDescending(cost))
                {
                    if (keepPieces(new[] { portal }, Shown(portal.m_name) + " materials"))
                    {
                        break;
                    }
                }
            }

            if (preserveBoatMats.Value)
            {
                foreach (Piece boat in known.Where(piece => piece.GetComponentInChildren<Ship>(true) != null).OrderByDescending(cost))
                {
                    if (keepPieces(new[] { boat }, Shown(boat.m_name) + " materials"))
                    {
                        break;
                    }
                }
            }

            if (keepOutpostKit.Value)
            {
                List<Piece> kit = known.Where(piece => OutpostKit.Contains(piece.gameObject.name)).ToList();
                if (kit.Count == OutpostKit.Length)
                {
                    keepPieces(kit, "outpost kit");
                }
            }

            // Boss offerings only when exactly one altar's worth is carried.
            if (preserveBossItems.Value)
            {
                foreach (KeyValuePair<string, int> offering in Offerings())
                {
                    if (have(offering.Key) == offering.Value)
                    {
                        keep[offering.Key] = int.MaxValue;
                        groups.Add(new KeyValuePair<string, List<string>>(Shown(offering.Key), new List<string> { offering.Key }));
                    }
                }
            }

            // Whatever already stays, such as the hotbar, counts towards what is kept.
            foreach (KeyValuePair<string, int> entry in keep)
            {
                int staying = 0;
                int going = 0;
                foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                {
                    if (item.m_shared.m_name != entry.Key || player.IsItemEquiped(item))
                    {
                        continue;
                    }

                    if (StaysWithPlayer(item))
                    {
                        staying += item.m_stack;
                    }
                    else
                    {
                        going += item.m_stack;
                    }
                }

                int hold = Math.Min(going, Math.Max(0, entry.Value - staying));
                if (hold > 0)
                {
                    Hold[entry.Key] = hold;
                }
            }

            return groups;
        }

        private static void KeepAllOf(List<KeyValuePair<string, List<string>>> groups, Dictionary<string, int> keep, string name, string label)
        {
            keep[name] = int.MaxValue;
            groups.Add(new KeyValuePair<string, List<string>>(label, new List<string> { name }));
        }

        // KeepItems, as item names and amounts; "all" or no amount keeps every one.
        private static List<KeyValuePair<string, int>> ParseKeepItems()
        {
            List<KeyValuePair<string, int>> result = new List<KeyValuePair<string, int>>();
            foreach (string entry in keepItems.Value.Split(','))
            {
                string[] parts = entry.Split(':');
                string wanted = parts[0].Trim();
                if (wanted.Length == 0)
                {
                    continue;
                }

                int amount = int.MaxValue;
                if (parts.Length > 1 && !parts[1].Trim().Equals("all", StringComparison.OrdinalIgnoreCase) &&
                    !int.TryParse(parts[1].Trim(), out amount))
                {
                    log.LogWarning("KeepItems: can't read the amount in \"" + entry.Trim() + "\"");
                    continue;
                }

                string name = ItemName(wanted);
                if (name == null)
                {
                    log.LogWarning("KeepItems: no item called \"" + wanted + "\"");
                    continue;
                }

                result.Add(new KeyValuePair<string, int>(name, Math.Max(0, amount)));
            }

            return result;
        }

        private static readonly HashSet<string> warned = new HashSet<string>();

        // An item's internal name from its prefab name or its English name.
        private static string ItemName(string wanted)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(wanted) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop != null)
            {
                return drop.m_itemData.m_shared.m_name;
            }

            string lower = wanted.ToLowerInvariant();
            if (ObjectDB.instance != null)
            {
                foreach (GameObject item in ObjectDB.instance.m_items)
                {
                    ItemDrop d = item != null ? item.GetComponent<ItemDrop>() : null;
                    if (d != null && Shown(d.m_itemData.m_shared.m_name) == lower)
                    {
                        return d.m_itemData.m_shared.m_name;
                    }
                }
            }

            return null;
        }

        private static IEnumerable<Piece> HammerPieces()
        {
            GameObject hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            ItemDrop drop = hammer != null ? hammer.GetComponent<ItemDrop>() : null;
            PieceTable table = drop != null ? drop.m_itemData.m_shared.m_buildPieces : null;
            if (table == null)
            {
                return Enumerable.Empty<Piece>();
            }

            return table.m_pieces
                .Where(prefab => prefab != null)
                .Select(prefab => prefab.GetComponent<Piece>())
                .Where(piece => piece != null && piece.m_resources != null);
        }

        // Altar offerings by item name. Altars loaded nearby are read directly;
        // the list above covers the rest.
        private static Dictionary<string, int> Offerings()
        {
            Dictionary<string, int> offerings = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> entry in BossOfferings)
            {
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(entry.Key) : null;
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null)
                {
                    offerings[drop.m_itemData.m_shared.m_name] = entry.Value;
                }
                else if (warned.Add(entry.Key))
                {
                    log.LogWarning("No item prefab called " + entry.Key);
                }
            }

            foreach (OfferingBowl bowl in UnityEngine.Object.FindObjectsByType<OfferingBowl>(FindObjectsSortMode.None))
            {
                if (bowl.m_bossItem != null && bowl.m_bossItems > 0)
                {
                    string name = bowl.m_bossItem.m_itemData.m_shared.m_name;
                    int listed;
                    if (offerings.TryGetValue(name, out listed) && listed != bowl.m_bossItems && warned.Add(name))
                    {
                        log.LogInfo("Altar takes " + bowl.m_bossItems + " " + name + ", not " + listed);
                    }

                    offerings[name] = bowl.m_bossItems;
                }
            }

            return offerings;
        }

        // Once every chest has answered: what went in, and what had nowhere to go.
        private static void Report()
        {
            reporting = false;
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            // What left the bags, worked out from what was carried before.
            List<string> moved = new List<string>();
            List<string> full = new List<string>();
            foreach (KeyValuePair<string, int> before in Before.OrderBy(pair => Shown(pair.Key), StringComparer.Ordinal))
            {
                int left = Movable(player, before.Key);
                if (left < before.Value)
                {
                    moved.Add((before.Value - left) + " " + Shown(before.Key));
                }

                if (left > 0)
                {
                    full.Add(Shown(before.Key));
                }
            }

            List<string> lines = new List<string>();
            if (moved.Count > 0)
            {
                lines.Add("Stacked " + List(moved));
            }

            if (full.Count > 0)
            {
                lines.Add("No room to stack " + List(full));
            }

            if (lines.Count == 0)
            {
                lines.Add("Nothing to stack");
            }

            if (keptLine.Length > 0)
            {
                lines.Add(keptLine);
            }

            string message = string.Join("\n", lines.ToArray());
            log.LogInfo(message.Replace("\n", ". "));
            player.Message(MessageHud.MessageType.Center, Small(message));
        }

        private static string Shown(string name)
        {
            return Localization.instance.Localize(name).ToLowerInvariant();
        }

        private static string List(List<string> names)
        {
            return string.Join(", ", names.Take(NamesShown).ToArray()) +
                (names.Count > NamesShown ? " and " + (names.Count - NamesShown) + " more" : "");
        }

        // The game's centre message is large; this one is half its size.
        private static string Small(string message)
        {
            return "<size=50%>" + message + "</size>";
        }

        private static bool IsAvailable(Container chest, Player player)
        {
            return !chest.IsInUse() &&
                CheckAccess(chest, Game.instance.GetPlayerProfile().GetPlayerID()) &&
                (!chest.m_checkGuardStone || PrivateArea.CheckAccess(chest.transform.position, 0f, false));
        }

        private static bool StaysWithPlayer(ItemDrop.ItemData item)
        {
            return item.m_shared.m_maxStackSize <= 1 ||
                (keepHotbar.Value && item.m_gridPos.y == HotbarRow) ||
                !StacksKind(item.m_shared);
        }

        // Other kinds, such as arrows and trophies, are always stacked.
        private static bool StacksKind(ItemDrop.ItemData.SharedData shared)
        {
            switch (shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Material:
                    return stackMaterials.Value;
                case ItemDrop.ItemData.ItemType.Consumable:
                    if (IsMead(shared))
                    {
                        return stackMeads.Value;
                    }

                    return IsCooked(shared) ? stackCookedFood.Value : stackRawFood.Value;
                default:
                    return true;
            }
        }

        // Names of food that something in the game makes: a recipe, which covers
        // the cauldron, or a cooking station, which covers the oven too.
        private static HashSet<string> cooked;

        private static bool IsCooked(ItemDrop.ItemData.SharedData shared)
        {
            if (cooked == null)
            {
                cooked = FindCooked();
            }

            return cooked.Contains(shared.m_name);
        }

        private static HashSet<string> FindCooked()
        {
            HashSet<string> names = new HashSet<string>();
            if (ObjectDB.instance != null)
            {
                foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                {
                    if (recipe != null && recipe.m_item != null)
                    {
                        names.Add(recipe.m_item.m_itemData.m_shared.m_name);
                    }
                }
            }

            if (ZNetScene.instance != null)
            {
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    CookingStation station = prefab != null ? prefab.GetComponent<CookingStation>() : null;
                    if (station == null)
                    {
                        continue;
                    }

                    foreach (CookingStation.ItemConversion conversion in station.m_conversion)
                    {
                        if (conversion != null && conversion.m_to != null)
                        {
                            names.Add(conversion.m_to.m_itemData.m_shared.m_name);
                        }
                    }
                }
            }

            log.LogInfo("Found " + names.Count + " items made by recipes or cooking stations");
            return names;
        }

        // Meads are drinks with an effect and no food value.
        private static bool IsMead(ItemDrop.ItemData.SharedData shared)
        {
            return shared.m_isDrink ||
                (shared.m_food <= 0f && shared.m_foodStamina <= 0f && shared.m_foodEitr <= 0f);
        }

        // The game answers each request here, possibly a moment after the key press.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        private static void BeforeStackResponse(Container __instance)
        {
            answeringRequest = Requested.Remove(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Container), "RPC_StackResponse")]
        private static void AfterStackResponse()
        {
            bool ours = answeringRequest;
            answeringRequest = false;
            if (!ours)
            {
                return;
            }

            // A chest that answers after the report has been shown still takes
            // items, so the report is redone to match.
            if (!reporting)
            {
                log.LogInfo("A chest answered late; updating the report");
                reporting = true;
                reportBy = Time.unscaledTime + AnswerSeconds;
            }

            if (Requested.Count == 0)
            {
                Report();
            }
        }

        // Stack All only skips equipped items, so report the kept items as equipped too.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Humanoid), "IsItemEquiped")]
        private static void KeepItems(ItemDrop.ItemData item, ref bool __result)
        {
            if (answeringRequest && !movingHeld && (StaysWithPlayer(item) || Held(item.m_shared.m_name) > 0))
            {
                __result = true;
            }
        }

        // The game moves whole stacks, so items with some held back are moved here
        // instead, leaving the held amount with the player.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), "StackAll")]
        private static void BeforeStackAll(Inventory __instance, Inventory fromInventory, ref bool message, out int __state)
        {
            __state = 0;
            if (!answeringRequest)
            {
                return;
            }

            message = false;
            Player player = Player.m_localPlayer;
            if (player == null || fromInventory != player.GetInventory())
            {
                return;
            }

            movingHeld = true;
            try
            {
                __state = MoveSpare(__instance, fromInventory, player);
            }
            finally
            {
                movingHeld = false;
            }
        }

        private static int MoveSpare(Inventory chest, Inventory fromInventory, Player player)
        {
            int total = 0;
            foreach (string name in Hold.Keys.ToList())
            {
                if (!chest.ContainsItemByName(name))
                {
                    continue;
                }

                int toMove = Movable(player, name);
                foreach (ItemDrop.ItemData item in fromInventory.GetAllItems()
                    .Where(item => item.m_shared.m_name == name && !player.IsItemEquiped(item) && !StaysWithPlayer(item))
                    .ToList())
                {
                    if (toMove <= 0)
                    {
                        break;
                    }

                    int take = Math.Min(item.m_stack, toMove);
                    ItemDrop.ItemData part = item.Clone();
                    part.m_stack = take;
                    bool all = chest.AddItem(part);
                    int moved = all ? take : take - part.m_stack;
                    if (moved > 0)
                    {
                        fromInventory.RemoveItem(item, moved);
                        toMove -= moved;
                        total += moved;
                    }

                    if (!all)
                    {
                        break;
                    }
                }
            }

            return total;
        }

        // Counts those moves too, so the game still plays its sound.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), "StackAll")]
        private static void AfterStackAll(int __state, ref int __result)
        {
            __result += __state;
        }
    }
}
