using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SpeedBoat
{
    // Adds two sail speeds above Full and a fast reverse, and lets rowing be
    // faster or slower. The game steers each ship from its owner's machine, which
    // is the person at the helm, so that is where the extra speed is kept.
    [BepInPlugin("com.parallel53.speedboat", "SpeedBoat", "0.1.0")]
    public class SpeedBoatPlugin : BaseUnityPlugin
    {
        private const float LabelFontSize = 18f;
        private static readonly Vector2 LabelOffset = new Vector2(4f, 0f);
        private static readonly Color LabelColor = new Color(1f, 0.85f, 0.4f);

        // Extra level per ship; see SpeedSteps.
        private static readonly Dictionary<Ship, int> Extras = new Dictionary<Ship, int>();

        private static ConfigEntry<float> fullPlusOne;
        private static ConfigEntry<float> fullPlusTwo;
        private static ConfigEntry<float> fastReverse;
        private static ConfigEntry<float> rowForward;
        private static ConfigEntry<float> rowBackward;

        private static TMP_Text sailLabel;
        private static TMP_Text reverseLabel;

        private struct Forces
        {
            public float Sail;
            public float Backward;
        }

        private void Awake()
        {
            fullPlusOne = Multiplier("FullPlusOne", 2f, "Sail force at the first speed above Full, as a multiple of Full.");
            fullPlusTwo = Multiplier("FullPlusTwo", 3f, "Sail force at the second speed above Full, as a multiple of Full.");
            fastReverse = Multiplier("FastReverse", 2f, "Rowing force in fast reverse, as a multiple of reverse.");
            rowForward = Multiplier("RowForward", 1f, "Rowing force going forward, as a multiple of the game's.");
            rowBackward = Multiplier("RowBackward", 1f, "Rowing force in reverse, as a multiple of the game's.");

            new Harmony("com.parallel53.speedboat").PatchAll(typeof(SpeedBoatPlugin));
        }

        private ConfigEntry<float> Multiplier(string key, float value, string description)
        {
            return Config.Bind("Speed", key, value,
                new ConfigDescription(description, new AcceptableValueRange<float>(0.1f, 10f)));
        }

        private static Setting SettingOf(Ship ship)
        {
            return (Setting)(int)ship.GetSpeedSetting();
        }

        private static int ExtraOf(Ship ship)
        {
            Extras.TryGetValue(ship, out int extra);
            return SpeedSteps.Settle(SettingOf(ship), extra);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Ship), "RPC_Forward")]
        private static bool BeforeForward(Ship __instance)
        {
            int extra = ExtraOf(__instance);
            bool handled = SpeedSteps.Forward(SettingOf(__instance), ref extra);
            Extras[__instance] = extra;
            return !handled;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Ship), "RPC_Backward")]
        private static bool BeforeBackward(Ship __instance)
        {
            int extra = ExtraOf(__instance);
            bool handled = SpeedSteps.Backward(SettingOf(__instance), ref extra);
            Extras[__instance] = extra;
            return !handled;
        }

        // The game reads its sail and rowing forces while moving the ship, so they
        // are scaled for the duration and put back after.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Ship), "CustomFixedUpdate")]
        private static void BeforeMove(Ship __instance, out Forces __state)
        {
            __state = new Forces { Sail = __instance.m_sailForceFactor, Backward = __instance.m_backwardForce };

            int extra = ExtraOf(__instance);
            Extras[__instance] = extra;
            if (extra == 1)
            {
                __instance.m_sailForceFactor *= fullPlusOne.Value;
            }
            else if (extra == SpeedSteps.MaxForward)
            {
                __instance.m_sailForceFactor *= fullPlusTwo.Value;
            }

            Setting setting = SettingOf(__instance);
            if (setting == Setting.Slow)
            {
                __instance.m_backwardForce *= rowForward.Value;
            }
            else if (setting == Setting.Back)
            {
                __instance.m_backwardForce *= rowBackward.Value * (extra < 0 ? fastReverse.Value : 1f);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Ship), "CustomFixedUpdate")]
        private static void AfterMove(Ship __instance, Forces __state)
        {
            __instance.m_sailForceFactor = __state.Sail;
            __instance.m_backwardForce = __state.Backward;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Ship), "OnDisable")]
        private static void Forget(Ship __instance)
        {
            Extras.Remove(__instance);
        }

        // The game's indicator shows Full or reverse; the label beside it says which level.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Hud), "UpdateShipHud")]
        private static void ShowLevel(Hud __instance, Player player)
        {
            Ship ship = player.GetControlledShip();
            int extra = ship != null ? ExtraOf(ship) : 0;

            if (sailLabel == null)
            {
                sailLabel = CreateLabel(__instance, __instance.m_fullSail);
                reverseLabel = CreateLabel(__instance, __instance.m_rudderBackward);
            }

            SetLabel(sailLabel, extra > 0 ? SpeedSteps.Label(extra) : "");
            SetLabel(reverseLabel, extra < 0 ? SpeedSteps.Label(extra) : "");
        }

        private static void SetLabel(TMP_Text label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        // Hangs off the right edge of the icon, so it shows and hides with it.
        private static TMP_Text CreateLabel(Hud hud, GameObject icon)
        {
            if (icon == null)
            {
                return null;
            }

            GameObject labelObject = new GameObject("SpeedBoatLabel", typeof(RectTransform));
            labelObject.SetActive(false);
            RectTransform rect = (RectTransform)labelObject.transform;
            rect.SetParent(icon.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = LabelOffset;
            rect.sizeDelta = new Vector2(60f, 30f);

            // The font has to be set before the text wakes, or it falls back to a default.
            TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
            TMP_Text source = hud.GetComponentInChildren<TMP_Text>(true);
            if (source != null)
            {
                label.font = source.font;
            }

            label.fontSize = LabelFontSize;
            label.fontStyle = FontStyles.Bold;
            label.color = LabelColor;
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            label.text = "";
            labelObject.SetActive(true);
            return label;
        }
    }
}
