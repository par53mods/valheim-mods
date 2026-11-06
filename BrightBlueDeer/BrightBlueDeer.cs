using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BrightBlueDeer
{
    // Recolours deer, alive or dead, along with deer hides, the deer rug, crows,
    // boars, bears and wolves.
    // Only this client's visuals change, so saves and other players are untouched.
    [BepInPlugin("com.parallel53.brightbluedeer", "Bright Blue Deer", "1.0.2")]
    public class BrightBlueDeerPlugin : BaseUnityPlugin
    {
        private const string DeerPrefab = "Deer";
        private const string HidePrefab = "DeerHide";
        private const string RugPrefab = "rug_deer";
        private const string CrowPrefab = "Crow";
        private const float DeerMinBrightness = 0.55f;

        // Crows are nearly black, so they need a higher floor to read as gold.
        private const float CrowMinBrightness = 0.9f;
        private const float BrightnessGain = 0.9f;
        private const string DefaultColor = "20, 115, 255";
        private const string DefaultCrowColor = "255, 200, 40";

        // Other animals painted like deer, with their young and their corpses.
        private static readonly Animal[] Animals =
        {
            new Animal("BoarColor", "Boar", "255, 105, 180", "Boar", "Boar_piggy") { Extras = new[] { "LeatherScraps" } },
            new Animal("BearColor", "Bear", "60, 255, 60", "Bjorn", "Unbjorn") { Extras = new[] { "BjornHide", "rug_bjorn" } },
            new Animal("WolfColor", "Wolf", "64, 224, 208", "Wolf", "Wolf_cub") { Extras = new[] { "WolfPelt", "rug_wolf" } },
            new Animal("NeckColor", "Neck", "160, 60, 255", "Neck") { Extras = new[] { "NeckTail" } }
        };

        private class Animal
        {
            public readonly string Setting;
            public readonly string Title;
            public readonly string DefaultColor;
            public readonly string[] Prefabs;

            // Loot and rugs made from it, painted the same colour.
            public string[] Extras = new string[0];
            public ConfigEntry<string> Color;

            public Animal(string setting, string title, string defaultColor, params string[] prefabs)
            {
                Setting = setting;
                Title = title;
                DefaultColor = defaultColor;
                Prefabs = prefabs;
            }
        }

        // Original material to painted copy. Prefabs outlive a world, so a second
        // world load finds its painted materials here instead of repainting them.
        private static readonly Dictionary<Material, Material> PaintedMaterials = new Dictionary<Material, Material>();

        private static ManualLogSource log;
        private static Color deerColor;
        private static Color crowColor;

        private void Awake()
        {
            log = Logger;

            ConfigEntry<bool> enabled = Config.Bind("General", "Enabled", true,
                "Paint deer, deer hides, the deer rug, crows, boars, bears, wolves and necks. Takes effect the next time the game starts.");
            ConfigEntry<string> color = Config.Bind("General", "Color", DefaultColor,
                "Deer colour as red, green, blue, each from 0 to 255. Takes effect the next time the game starts.");
            ConfigEntry<string> birdColor = Config.Bind("General", "BirdColor", DefaultCrowColor,
                "Crow colour as red, green, blue, each from 0 to 255. Takes effect the next time the game starts.");
            foreach (Animal animal in Animals)
            {
                animal.Color = Config.Bind("General", animal.Setting, animal.DefaultColor,
                    animal.Title + " colour as red, green, blue, each from 0 to 255. Takes effect the next time the game starts.");
            }

            if (!enabled.Value)
            {
                log.LogInfo("Disabled in config");
                return;
            }

            deerColor = ParseColor(color.Value, DefaultColor);
            crowColor = ParseColor(birdColor.Value, DefaultCrowColor);
            new Harmony("com.parallel53.brightbluedeer").PatchAll(typeof(BrightBlueDeerPlugin));
        }

        private static Color ParseColor(string text, string fallback)
        {
            if (!ColorSetting.TryParse(text, out byte red, out byte green, out byte blue))
            {
                log.LogWarning("Color \"" + text + "\" is not red, green, blue from 0 to 255. Using " + fallback);
                ColorSetting.TryParse(fallback, out red, out green, out blue);
            }

            return new Color32(red, green, blue, 255);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static void PaintDeer(ZNetScene __instance)
        {
            GameObject deer = __instance.GetPrefab(DeerPrefab);
            if (deer == null)
            {
                log.LogWarning("No Deer prefab found");
                return;
            }

            PaintCreature(deer, deerColor);
            PaintIfFound(__instance, HidePrefab, deerColor, DeerMinBrightness);
            PaintIfFound(__instance, RugPrefab, deerColor, DeerMinBrightness);
            PaintIfFound(__instance, CrowPrefab, crowColor, CrowMinBrightness);

            foreach (Animal animal in Animals)
            {
                Color color = ParseColor(animal.Color.Value, animal.DefaultColor);
                foreach (string name in animal.Prefabs)
                {
                    GameObject prefab = __instance.GetPrefab(name);
                    if (prefab == null)
                    {
                        log.LogWarning("No " + name + " prefab found");
                        continue;
                    }

                    PaintCreature(prefab, color);
                }

                foreach (string name in animal.Extras)
                {
                    PaintIfFound(__instance, name, color, DeerMinBrightness);
                }
            }
        }

        private static void PaintCreature(GameObject prefab, Color color)
        {
            Paint(prefab, color, DeerMinBrightness);

            // The corpse is a separate prefab, spawned by the death effects.
            Character character = prefab.GetComponent<Character>();
            if (character == null)
            {
                return;
            }

            foreach (EffectList.EffectData effect in character.m_deathEffects.m_effectPrefabs)
            {
                if (effect.m_prefab != null && effect.m_prefab.GetComponent<Ragdoll>() != null)
                {
                    Paint(effect.m_prefab, color, DeerMinBrightness);
                }
            }
        }

        private static void PaintIfFound(ZNetScene scene, string name, Color color, float minBrightness)
        {
            GameObject prefab = scene.GetPrefab(name);
            if (prefab == null)
            {
                string[] birds = scene.m_prefabs
                    .Where(candidate => candidate.GetComponent<RandomFlyingBird>() != null)
                    .Select(candidate => candidate.name)
                    .ToArray();
                log.LogWarning("No " + name + " prefab found" +
                    (name == CrowPrefab ? ". Birds in the game: " + string.Join(", ", birds) : ""));
                return;
            }

            Paint(prefab, color, minBrightness);
        }

        private static void Paint(GameObject prefab, Color color, float minBrightness)
        {
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = Painted(materials[i], prefab.name, color, minBrightness);
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static Material Painted(Material material, string prefabName, Color color, float minBrightness)
        {
            if (material == null || PaintedMaterials.ContainsValue(material))
            {
                return material;
            }

            if (PaintedMaterials.TryGetValue(material, out Material painted))
            {
                return painted;
            }

            // The antlers have no texture and keep their own colour.
            if (material.mainTexture == null)
            {
                return material;
            }

            painted = new Material(material) { mainTexture = PaintedTexture(material.mainTexture, color, minBrightness) };
            if (painted.HasProperty("_Color"))
            {
                painted.color = Color.white;
            }

            PaintedMaterials[material] = painted;
            log.LogInfo(prefabName + ": painted " + material.name + " (" + material.shader.name + ")");
            return painted;
        }

        // Game textures can't be read directly, so copy through a render texture.
        // A tint would only darken the fur; this keeps its shading and replaces its hue.
        private static Texture2D PaintedTexture(Texture source, Color color, float minBrightness)
        {
            RenderTexture copy = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, copy);
            RenderTexture.active = copy;

            Texture2D result = new Texture2D(source.width, source.height, TextureFormat.ARGB32, true);
            result.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(copy);

            Color[] pixels = result.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                float brightness = Mathf.Clamp01(minBrightness + pixels[i].maxColorComponent * BrightnessGain);
                Color painted = color * brightness;
                painted.a = pixels[i].a;
                pixels[i] = painted;
            }

            result.SetPixels(pixels);
            result.Apply(true, true);
            return result;
        }
    }
}
