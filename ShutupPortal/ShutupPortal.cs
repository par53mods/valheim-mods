using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ShutupPortal
{
    // Quietens portals on this client only: the sound when a portal links up
    // (TeleportWorld.m_connected) and the hum while it is active (the sound
    // inside m_target_found, faded by EffectFade). Both are silent by default.
    [BepInPlugin("com.parallel53.shutupportal", "ShutupPortal", "1.0.0")]
    public class ShutupPortalPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> ActivationVolume;
        internal static ConfigEntry<float> HumVolume;
        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            ActivationVolume = Config.Bind("Volume", "ActivationVolume", 0f, new ConfigDescription(
                "Volume of the sound a portal makes when it activates, from 0 (silent) to 1 (normal). 0 by default.",
                new AcceptableValueRange<float>(0f, 1f)));
            HumVolume = Config.Bind("Volume", "HumVolume", 0f, new ConfigDescription(
                "Volume of the hum an active portal makes, from 0 (silent) to 1 (normal). 0 by default. " +
                "Applies to portals that load after you change it.",
                new AcceptableValueRange<float>(0f, 1f)));
            new Harmony("com.parallel53.shutupportal").PatchAll(typeof(ShutupPortalPlugin));
        }

        private static readonly AccessTools.FieldRef<EffectFade, float> BaseVolume =
            AccessTools.FieldRefAccess<EffectFade, float>("m_baseVolume");
        private static readonly AccessTools.FieldRef<EffectFade, AudioSource> FadeSource =
            AccessTools.FieldRefAccess<EffectFade, AudioSource>("m_audioSource");

        private static bool updatingPortal;
        private static bool listed;

        // Turns one sound down. Muting is separate from volume, so nothing the
        // game does to the volume later can bring a silenced sound back.
        private static void Quieten(AudioSource source, float volume)
        {
            if (source == null)
            {
                return;
            }

            if (volume <= 0f)
            {
                source.mute = true;
                return;
            }

            ZSFX sfx = source.GetComponent<ZSFX>();
            if (sfx != null)
            {
                sfx.SetVolumeModifier(sfx.GetVolumeModifier() * volume);
            }
            else
            {
                source.volume *= volume;
            }
        }

        // The activation sound is created inside UpdatePortal, so only effects
        // created while it runs are turned down.
        [HarmonyPatch(typeof(TeleportWorld), "UpdatePortal"), HarmonyPrefix]
        private static void BeforeUpdatePortal()
        {
            updatingPortal = true;
        }

        [HarmonyPatch(typeof(TeleportWorld), "UpdatePortal"), HarmonyFinalizer]
        private static void AfterUpdatePortal()
        {
            updatingPortal = false;
        }

        [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create)), HarmonyPostfix]
        private static void AfterCreate(GameObject[] __result)
        {
            if (!updatingPortal || __result == null)
            {
                return;
            }

            float volume = Mathf.Clamp01(ActivationVolume.Value);
            foreach (GameObject created in __result)
            {
                if (created == null)
                {
                    continue;
                }

                foreach (AudioSource source in created.GetComponentsInChildren<AudioSource>(true))
                {
                    Quieten(source, volume);
                }
            }
        }

        // The hum fades between 0 and the volume read here, every frame.
        [HarmonyPatch(typeof(EffectFade), "Awake"), HarmonyPostfix]
        private static void AfterFadeAwake(EffectFade __instance)
        {
            if (__instance.GetComponentInParent<TeleportWorld>() == null)
            {
                return;
            }

            float volume = Mathf.Clamp01(HumVolume.Value);
            BaseVolume(__instance) *= volume;
            if (volume <= 0f && FadeSource(__instance) != null)
            {
                FadeSource(__instance).mute = true;
            }
        }

        // Any other sound on the portal itself gets the hum volume too. Sounds
        // owned by a fade are left to AfterFadeAwake so they aren't turned down twice.
        [HarmonyPatch(typeof(TeleportWorld), "Awake"), HarmonyPostfix]
        private static void AfterPortalAwake(TeleportWorld __instance)
        {
            float volume = Mathf.Clamp01(HumVolume.Value);
            AudioSource[] sources = __instance.GetComponentsInChildren<AudioSource>(true);
            foreach (AudioSource source in sources)
            {
                EffectFade fade = source.GetComponentInParent<EffectFade>();
                bool fadeOwns = fade != null && fade.GetComponentInChildren<AudioSource>() == source;
                if (volume <= 0f)
                {
                    source.mute = true;
                }
                else if (!fadeOwns)
                {
                    Quieten(source, volume);
                }
            }

            if (!listed)
            {
                listed = true;
                foreach (AudioSource source in sources)
                {
                    Log.LogInfo("Portal sound: " + source.name + " (" +
                        (source.clip != null ? source.clip.name : "no clip") + ")");
                }
            }
        }
    }
}
