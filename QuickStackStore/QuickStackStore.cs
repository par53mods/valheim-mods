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
    [BepInPlugin("com.parallel53.quickstackstore", "QuickStackStore", "1.0.1")]
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
                player.Message(MessageHud.MessageType.Center, "Nothing to stack nearby");
                return;
            }

            Requested.Clear();
            foreach (Container chest in chests)
            {
                Requested.Add(chest);
                chest.StackAll();
            }
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
        private static void SilenceChestMessage(ref bool __state, object[] __args)
        {
            __state = answeringRequest;
            if (answeringRequest)
            {
                __args[1] = false;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), "StackAll")]
        private static void ReportStacked(bool __state, int __result)
        {
            if (__state && __result > 0)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Quick stacked " + __result + " items");
            }
        }
    }
}
