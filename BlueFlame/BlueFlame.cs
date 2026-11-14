using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BlueFlame
{
    // Makes fires burn like the Standing Blue-burning Iron Torch, on this client
    // only. The prefabs are changed once when the world loads, so every fire
    // built or loaded afterwards is blue.
    //
    // Flames using the gradient-mapped shader get their colour from the particle
    // system's custom data; those copy the blue torch's. Other flames, sparks
    // and glows are tinted, so their warm colours are turned blue. Warm lights
    // take the blue torch's light colour.
    [BepInPlugin("com.parallel53.blueflame", "BlueFlame", "1.0.0")]
    public class BlueFlamePlugin : BaseUnityPlugin
    {
        private static ConfigEntry<bool> Fires;
        private static ConfigEntry<bool> HeldTorch;
        private static ConfigEntry<bool> Portals;
        private static ConfigEntry<bool> CraftingStations;
        private static BepInEx.Logging.ManualLogSource Log;

        private const string BlueTorch = "piece_groundtorch_blue";
        private const string BlueBrazier = "piece_brazierfloor02";
        // Effects these create (crafting bursts, adding fuel or ore, a portal
        // activating) are recoloured along with the piece. Fireworks keep their colours.
        private static readonly System.Type[] EffectOwners =
        {
            typeof(Fireplace), typeof(CraftingStation), typeof(Smelter), typeof(CookingStation), typeof(TeleportWorld),
        };
        private static readonly HashSet<GameObject> effectsDone = new HashSet<GameObject>();

        // Fallbacks, read from the game files, in case the blue torch is missing.
        private static Color blueLight = new Color(0.48f, 0.77f, 1f);
        private static ParticleSystem blueFlame;
        private static Material blueFlameMaterial;
        private static readonly Dictionary<string, Material> blueMaterials = new Dictionary<string, Material>();

        private void Awake()
        {
            Log = Logger;
            Fires = Config.Bind("Blue", "Fires", true,
                "Campfires, hearths, bonfires, standing and wall torches, braziers and other fires you refuel. On by default.");
            HeldTorch = Config.Bind("Blue", "HeldTorch", true, "The torch you carry. On by default.");
            Portals = Config.Bind("Blue", "Portals", true, "Portal fire and glow. On by default.");
            CraftingStations = Config.Bind("Blue", "CraftingStations", true,
                "Forges, smelters, kilns, blast furnaces, ovens, cauldrons and other crafting and cooking stations, " +
                "including the bursts of fire while you craft. On by default.");
            new Harmony("com.parallel53.blueflame").PatchAll(typeof(BlueFlamePlugin));
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake"), HarmonyPostfix]
        private static void AfterSceneAwake(ZNetScene __instance)
        {
            ReadBlueTorch(__instance);
            effectsDone.Clear();
            int changed = 0;

            if (Fires.Value || Portals.Value || CraftingStations.Value)
            {
                foreach (GameObject prefab in __instance.m_prefabs)
                {
                    if (prefab == null)
                    {
                        continue;
                    }

                    bool wanted = (Fires.Value && prefab.GetComponent<Fireplace>() != null)
                        || (Portals.Value && prefab.GetComponent<TeleportWorld>() != null)
                        || (CraftingStations.Value && (prefab.GetComponent<CraftingStation>() != null
                            || prefab.GetComponent<Smelter>() != null || prefab.GetComponent<CookingStation>() != null));
                    if (wanted && Recolour(prefab) | RecolourEffects(prefab))
                    {
                        changed++;
                    }
                }
            }

            if (HeldTorch.Value)
            {
                GameObject torch = __instance.GetPrefab("Torch");
                if (torch != null && Recolour(torch))
                {
                    changed++;
                }
            }

            Log.LogInfo("Turned " + changed + " kinds of fire blue");
        }

        private static void ReadBlueTorch(ZNetScene scene)
        {
            GameObject torch = scene.GetPrefab(BlueTorch);
            Fireplace fire = torch != null ? torch.GetComponent<Fireplace>() : null;
            if (fire == null || fire.m_enabledObject == null)
            {
                Log.LogWarning("No " + BlueTorch + "; tinting flames instead of copying its colours");
                return;
            }

            Light light = fire.m_enabledObject.GetComponentInChildren<Light>(true);
            if (light != null)
            {
                blueLight = light.color;
            }

            foreach (ParticleSystem system in fire.m_enabledObject.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.customData.enabled)
                {
                    blueFlame = system;
                    ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                    blueFlameMaterial = renderer != null ? renderer.sharedMaterial : null;
                    break;
                }
            }

            GameObject brazier = scene.GetPrefab(BlueBrazier);
            if (brazier != null)
            {
                foreach (Renderer renderer in brazier.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null && material.name.EndsWith("_blue"))
                        {
                            blueMaterials[material.name] = material;
                        }
                    }
                }
            }
        }

        private static bool RecolourEffects(GameObject prefab)
        {
            bool changed = false;
            foreach (System.Type type in EffectOwners)
            {
                Component owner = prefab.GetComponent(type);
                if (owner == null)
                {
                    continue;
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.FieldType != typeof(EffectList) || field.Name == "m_fireworksEffects")
                    {
                        continue;
                    }

                    var list = (EffectList)field.GetValue(owner);
                    if (list == null || list.m_effectPrefabs == null)
                    {
                        continue;
                    }

                    foreach (EffectList.EffectData effect in list.m_effectPrefabs)
                    {
                        if (effect != null && effect.m_prefab != null && effectsDone.Add(effect.m_prefab))
                        {
                            changed |= Recolour(effect.m_prefab);
                        }
                    }
                }
            }

            return changed;
        }

        // Returns true if anything in the prefab changed. Running it again on the
        // same prefab changes nothing, since only warm colours are touched.
        private static bool Recolour(GameObject prefab)
        {
            if (prefab.name == BlueTorch)
            {
                return false;
            }

            bool changed = false;
            TeleportWorld portal = prefab.GetComponent<TeleportWorld>();
            if (portal != null)
            {
                // The portal frame's glow is coloured from these while it runs.
                portal.m_colorUnconnected = Shift(portal.m_colorUnconnected, ref changed);
                portal.m_colorTargetfound = Shift(portal.m_colorTargetfound, ref changed);
            }

            foreach (Light light in prefab.GetComponentsInChildren<Light>(true))
            {
                if (IsWarm(light.color))
                {
                    light.color = new Color(blueLight.r, blueLight.g, blueLight.b, light.color.a);
                    changed = true;
                }
            }

            foreach (ParticleSystem system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                changed |= RecolourParticles(system);
            }

            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                bool swapped = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && blueMaterials.TryGetValue(materials[i].name + "_blue", out Material blue))
                    {
                        materials[i] = blue;
                        swapped = true;
                    }
                }

                if (swapped)
                {
                    renderer.sharedMaterials = materials;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool RecolourParticles(ParticleSystem system)
        {
            bool changed = false;
            ParticleSystem.MainModule main = system.main;
            if (Shift(main.startColor, out ParticleSystem.MinMaxGradient start))
            {
                main.startColor = start;
                changed = true;
            }

            ParticleSystem.ColorOverLifetimeModule overLife = system.colorOverLifetime;
            if (overLife.enabled && Shift(overLife.color, out ParticleSystem.MinMaxGradient life))
            {
                overLife.color = life;
                changed = true;
            }

            ParticleSystem.CustomDataModule custom = system.customData;
            if (!custom.enabled)
            {
                return changed;
            }

            var streams = new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 };
            var shifted = new ParticleSystem.MinMaxGradient[streams.Length];
            bool warm = false;
            for (int i = 0; i < streams.Length; i++)
            {
                shifted[i] = custom.GetColor(streams[i]);
                if (custom.GetMode(streams[i]) == ParticleSystemCustomDataMode.Color
                    && Shift(custom.GetColor(streams[i]), out ParticleSystem.MinMaxGradient blue))
                {
                    shifted[i] = blue;
                    warm = true;
                }
            }

            if (!warm)
            {
                return changed;
            }

            // A flame drawn with the blue torch's material takes both of the
            // torch's colour streams, so it looks the same. Others are shifted.
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            bool sameFlame = blueFlame != null && blueFlameMaterial != null
                && renderer != null && renderer.sharedMaterial == blueFlameMaterial;
            for (int i = 0; i < streams.Length; i++)
            {
                if (sameFlame && blueFlame.customData.GetMode(streams[i]) == ParticleSystemCustomDataMode.Color)
                {
                    custom.SetMode(streams[i], ParticleSystemCustomDataMode.Color);
                    custom.SetColor(streams[i], blueFlame.customData.GetColor(streams[i]));
                }
                else if (custom.GetMode(streams[i]) == ParticleSystemCustomDataMode.Color)
                {
                    custom.SetColor(streams[i], shifted[i]);
                }
            }

            return true;
        }

        // Turns the warm colours in a particle colour blue. Returns false if
        // nothing in it was warm.
        private static bool Shift(ParticleSystem.MinMaxGradient value, out ParticleSystem.MinMaxGradient result)
        {
            result = value;
            bool changed = false;
            switch (value.mode)
            {
                case ParticleSystemGradientMode.Color:
                    result = new ParticleSystem.MinMaxGradient(Shift(value.color, ref changed));
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    result = new ParticleSystem.MinMaxGradient(Shift(value.colorMin, ref changed), Shift(value.colorMax, ref changed));
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    result = new ParticleSystem.MinMaxGradient(Shift(value.gradient, ref changed));
                    result.mode = value.mode;
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    result = new ParticleSystem.MinMaxGradient(Shift(value.gradientMin, ref changed), Shift(value.gradientMax, ref changed));
                    break;
            }

            return changed;
        }

        private static Gradient Shift(Gradient gradient, ref bool changed)
        {
            if (gradient == null)
            {
                return null;
            }

            GradientColorKey[] keys = gradient.colorKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].color = Shift(keys[i].color, ref changed);
            }

            var copy = new Gradient { mode = gradient.mode };
            copy.SetKeys(keys, gradient.alphaKeys);
            return copy;
        }

        // Orange becomes the blue torch's blue: yellow-white cores turn cyan and
        // red edges turn a deeper blue, the way the blue torch's flame does.
        // Brightness and saturation stay, so greys and smoke are unchanged.
        private static Color Shift(Color colour, ref bool changed)
        {
            if (!IsWarm(colour))
            {
                return colour;
            }

            Color.RGBToHSV(colour, out float h, out float s, out float v);
            if (h > 0.5f)
            {
                h -= 1f;
            }

            float blue = Mathf.Clamp(0.555f - (h - 0.083f) * 0.6f, 0.5f, 0.68f);
            Color shifted = Color.HSVToRGB(blue, s, v, true);
            shifted.a = colour.a;
            changed = true;
            return shifted;
        }

        private static bool IsWarm(Color colour)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            return s > 0.15f && v > 0.05f && (h <= 0.19f || h >= 0.92f);
        }
    }
}
