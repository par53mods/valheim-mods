using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
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
    [BepInPlugin("com.parallel53.quickstackstore", "QuickStackStore", "1.0.2")]
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

        private const float DropFraction = 0.25f;

        // How long to wait for chests to answer before reporting anyway.
        private const float AnswerSeconds = 3f;

        // Names in the report before the rest are counted instead.
        private const int NamesShown = 4;

        // What the last press is waiting on: each item name with a matching chest,
        // and how many of it the player had before stacking.
        private static readonly Dictionary<string, int> Before = new Dictionary<string, int>();
        private static int stacked;
        private static float reportBy;
        private static bool reporting;

        private static Button button;
        private static bool answeringRequest;

        private void Awake()
        {
            radius = Config.Bind("General", "Radius", 10f,
                new ConfigDescription("How far away a chest can be, in metres.",
                    new AcceptableValueRange<float>(1f, 50f)));
            keepHotbar = Config.Bind("General", "KeepHotbar", true,
                "Leave items on your hotbar where they are.");

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
                Report();
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

            List<ItemDrop.ItemData> items = player.GetInventory().GetAllItems()
                .Where(item => !player.IsItemEquiped(item) && !StaysWithPlayer(item))
                .ToList();

            List<Container> chests = pieces
                .Select(piece => piece.GetComponentInChildren<Container>())
                .Where(chest => chest != null && chest.GetInventory() != null)
                .Where(chest => IsAvailable(chest, player))
                .Where(chest => items.Any(item => chest.GetInventory().ContainsItemByName(item.m_shared.m_name)))
                .OrderBy(chest => Vector3.Distance(position, chest.transform.position))
                .ToList();

            if (chests.Count == 0)
            {
                player.Message(MessageHud.MessageType.Center, "No nearby chest holds anything you're carrying");
                return;
            }

            Before.Clear();
            foreach (string name in items
                .Select(item => item.m_shared.m_name)
                .Where(name => chests.Any(chest => chest.GetInventory().ContainsItemByName(name))))
            {
                Before[name] = Carried(player, name);
            }

            stacked = 0;
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

        // Once every chest has answered: what went in, and what had nowhere to go.
        private static void Report()
        {
            reporting = false;
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            List<string> full = Before.Keys
                .Where(name => Carried(player, name) > 0)
                .Select(name => Localization.instance.Localize(name).ToLowerInvariant())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            string names = string.Join(", ", full.Take(NamesShown).ToArray()) +
                (full.Count > NamesShown ? " and " + (full.Count - NamesShown) + " more" : "");

            if (stacked == 0)
            {
                player.Message(MessageHud.MessageType.Center,
                    full.Count > 0 ? "No room to stack " + names : "Nothing to stack");
                return;
            }

            player.Message(MessageHud.MessageType.TopLeft, "Quick stacked " + stacked + " items" +
                (full.Count > 0 ? ". No room for " + names : ""));
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
                (keepHotbar.Value && item.m_gridPos.y == HotbarRow);
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
            answeringRequest = false;
            if (reporting && Requested.Count == 0)
            {
                Report();
            }
        }

        // Stack All only skips equipped items, so report the kept items as equipped too.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Humanoid), "IsItemEquiped")]
        private static void KeepItems(ItemDrop.ItemData item, ref bool __result)
        {
            if (answeringRequest && StaysWithPlayer(item))
            {
                __result = true;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), "StackAll")]
        private static void SilenceChestMessage(ref bool __state, ref bool message)
        {
            __state = answeringRequest;
            if (answeringRequest)
            {
                message = false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), "StackAll")]
        private static void CountStacked(bool __state, int __result)
        {
            if (__state)
            {
                stacked += __result;
            }
        }
    }
}
