using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChestSalvage
{
    // Adds a Salvage button to the chest panel. It breaks the gear in the open
    // chest back into materials, after a confirmation listing what comes back.
    [BepInPlugin("com.parallel53.chestsalvage", "Chest Salvage", "1.0.0")]
    public class ChestSalvagePlugin : BaseUnityPlugin
    {
        private const float RefreshSeconds = 0.5f;
        private const int MaxPreviewLines = 8;

        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        private static readonly HashSet<ItemDrop.ItemData.ItemType> GearTypes =
            new HashSet<ItemDrop.ItemData.ItemType>
            {
                ItemDrop.ItemData.ItemType.OneHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeapon,
                ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
                ItemDrop.ItemData.ItemType.Bow,
                ItemDrop.ItemData.ItemType.Shield,
                ItemDrop.ItemData.ItemType.Helmet,
                ItemDrop.ItemData.ItemType.Chest,
                ItemDrop.ItemData.ItemType.Legs,
                ItemDrop.ItemData.ItemType.Hands,
                ItemDrop.ItemData.ItemType.Shoulder,
                ItemDrop.ItemData.ItemType.Utility,
                ItemDrop.ItemData.ItemType.Tool,
                ItemDrop.ItemData.ItemType.Trinket
            };

        private static ConfigEntry<float> baseRate;
        private static ConfigEntry<float> upgradeRate;
        private static ConfigEntry<bool> requireStation;

        private static Button button;
        private static TMP_Text label;
        private static float nextRefresh;

        private void Awake()
        {
            baseRate = Config.Bind("Salvage", "BaseReturnRate", 0.5f,
                new ConfigDescription("Share of the crafting cost returned, rounded up.",
                    new AcceptableValueRange<float>(0f, 1f)));
            upgradeRate = Config.Bind("Salvage", "UpgradeReturnRate", 1f,
                new ConfigDescription("Share of the upgrade costs returned, rounded up.",
                    new AcceptableValueRange<float>(0f, 1f)));
            requireStation = Config.Bind("Salvage", "RequireStation", true,
                "Only salvage an item when the crafting station its recipe uses is in range.");

            new Harmony("com.parallel53.chestsalvage").PatchAll(typeof(ChestSalvagePlugin));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), "Awake")]
        private static void AddButton(InventoryGui __instance)
        {
            Button takeAll = __instance.m_takeAllButton;
            button = Instantiate(takeAll, takeAll.transform.parent);
            button.name = "ChestSalvageButton";
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

            label = button.GetComponentInChildren<TMP_Text>(true);

            // Take All and Stack All sit at the panel's left and right, leaving
            // the centre free. Use the same height.
            RectTransform take = (RectTransform)takeAll.transform;
            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0.5f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(0.5f, rect.anchorMax.y);
            rect.pivot = new Vector2(0.5f, rect.pivot.y);
            rect.anchoredPosition = new Vector2(0f, take.anchoredPosition.y);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static void RefreshButton(InventoryGui __instance)
        {
            if (button == null || Time.unscaledTime < nextRefresh)
            {
                return;
            }

            nextRefresh = Time.unscaledTime + RefreshSeconds;

            Container container = CurrentContainer(__instance);
            bool show = container != null && container.GetComponent<TombStone>() == null;
            if (button.gameObject.activeSelf != show)
            {
                button.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            label.text = "Salvage (" + Salvageable(container.GetInventory()).Count + ")";
        }

        private static List<ItemDrop.ItemData> Salvageable(Inventory inventory)
        {
            return inventory.GetAllItems().Where(CanSalvage).ToList();
        }

        private static bool CanSalvage(ItemDrop.ItemData item)
        {
            return IsGear(item) && StationInRange(item);
        }

        private static bool IsGear(ItemDrop.ItemData item)
        {
            if (!GearTypes.Contains(item.m_shared.m_itemType) || item.m_shared.m_questItem)
            {
                return false;
            }

            Recipe recipe = ObjectDB.instance.GetRecipe(item);
            return recipe != null && recipe.m_enabled && recipe.m_amount == 1 && !recipe.m_requireOnlyOneIngredient;
        }

        private static CraftingStation RequiredStation(ItemDrop.ItemData item)
        {
            return ObjectDB.instance.GetRecipe(item).GetRequiredStation(1);
        }

        private static bool StationInRange(ItemDrop.ItemData item)
        {
            CraftingStation station = RequiredStation(item);
            return !requireStation.Value || station == null ||
                CraftingStation.HaveBuildStationInRange(station.m_name, Player.m_localPlayer.transform.position) != null;
        }

        // Explains an empty result, naming the stations when gear is only out of range.
        private static string NothingToSalvageMessage(Inventory inventory)
        {
            string[] stations = inventory.GetAllItems()
                .Where(item => IsGear(item) && !StationInRange(item))
                .Select(item => Localization.instance.Localize(RequiredStation(item).m_name))
                .Distinct()
                .ToArray();

            return stations.Length == 0
                ? "Nothing to salvage in this chest"
                : "Salvaging this gear needs a nearby " + string.Join(" or ", stations);
        }

        // Materials returned per item prefab, in first-seen order.
        private static Dictionary<ItemDrop, int> Refunds(List<ItemDrop.ItemData> items)
        {
            Dictionary<ItemDrop, int> totals = new Dictionary<ItemDrop, int>();
            foreach (ItemDrop.ItemData item in items)
            {
                Recipe recipe = ObjectDB.instance.GetRecipe(item);
                foreach (Piece.Requirement requirement in recipe.m_resources)
                {
                    if (requirement.m_resItem == null)
                    {
                        continue;
                    }

                    bool upgrader = requirement.m_upgraderResource;
                    int[] upgrades = SalvageMath
                        .PaidUpgradeLevels(item.m_quality, item.m_shared.m_maxQuality, upgrader)
                        .Select(requirement.GetAmount)
                        .ToArray();
                    int craftCost = SalvageMath.PaidOnCraft(upgrader) ? requirement.GetAmount(1) : 0;
                    int refund = SalvageMath.Refund(craftCost, upgrades, baseRate.Value, upgradeRate.Value);
                    if (refund > 0)
                    {
                        totals.TryGetValue(requirement.m_resItem, out int total);
                        totals[requirement.m_resItem] = total + refund;
                    }
                }
            }

            return totals;
        }

        private static void OnClick()
        {
            Container container = CurrentContainer(InventoryGui.instance);
            if (container == null || UnifiedPopup.IsVisible())
            {
                return;
            }

            List<ItemDrop.ItemData> items = Salvageable(container.GetInventory());
            if (items.Count == 0)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, NothingToSalvageMessage(container.GetInventory()));
                return;
            }

            UnifiedPopup.Push(new YesNoPopup(
                "Salvage " + items.Count + (items.Count == 1 ? " item" : " items") + "?",
                Preview(Refunds(items)),
                () =>
                {
                    UnifiedPopup.Pop();
                    Salvage(container);
                },
                UnifiedPopup.Pop,
                false,
                false));
        }

        private static string Preview(Dictionary<ItemDrop, int> refunds)
        {
            List<string> lines = refunds
                .OrderByDescending(pair => pair.Value)
                .Select(pair => pair.Value + " " + Localization.instance.Localize(pair.Key.m_itemData.m_shared.m_name))
                .ToList();
            if (lines.Count == 0)
            {
                return "These items return no materials and will be destroyed.";
            }

            int hidden = lines.Count - MaxPreviewLines;
            IEnumerable<string> shown = lines.Take(MaxPreviewLines);
            string text = string.Join("\n", shown.ToArray());
            return "You get:\n" + text + (hidden > 0 ? "\n+ " + hidden + " more" : "");
        }

        private static void Salvage(Container container)
        {
            // Checked again because the chest may have closed behind the popup.
            if (container == null || CurrentContainer(InventoryGui.instance) != container)
            {
                return;
            }

            Inventory inventory = container.GetInventory();
            List<ItemDrop.ItemData> items = Salvageable(inventory);
            Dictionary<ItemDrop, int> refunds = Refunds(items);

            Debug.Log("Chest Salvage: salvaged " +
                string.Join(", ", items.Select(i => i.m_shared.m_name + " q" + i.m_quality).ToArray()) +
                " for " +
                string.Join(", ", refunds.Select(r => r.Value + " " + r.Key.name).ToArray()));

            foreach (ItemDrop.ItemData item in items)
            {
                inventory.RemoveItem(item);
            }

            foreach (KeyValuePair<ItemDrop, int> refund in refunds)
            {
                Give(refund.Key, refund.Value, inventory);
            }

            nextRefresh = 0f;
        }

        // Chest first, then the player, then the ground, so nothing is lost.
        private static void Give(ItemDrop item, int amount, Inventory chest)
        {
            Player player = Player.m_localPlayer;
            int maxStack = Mathf.Max(item.m_itemData.m_shared.m_maxStackSize, 1);
            while (amount > 0)
            {
                int stack = Mathf.Min(amount, maxStack);
                amount -= stack;

                if (chest.CanAddItem(item.gameObject, stack))
                {
                    chest.AddItem(item.gameObject, stack);
                }
                else if (player.GetInventory().CanAddItem(item.gameObject, stack))
                {
                    player.GetInventory().AddItem(item.gameObject, stack);
                }
                else
                {
                    ItemDrop.DropItem(item.m_itemData.Clone(), stack,
                        player.transform.position + player.transform.forward + Vector3.up,
                        Quaternion.identity);
                }
            }
        }
    }
}
