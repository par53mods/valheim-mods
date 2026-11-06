using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DudeWheresMySpear
{
    // Makes the sparkles on items lying on the ground brighter, more numerous and
    // more spread out. They are a particle effect inside each item's prefab, so
    // changing the prefabs once changes every item dropped afterwards.
    [BepInPlugin("com.parallel53.dudewheresmyspear", "DudeWheresMySpear", "0.1.0")]
    public class DudeWheresMySpearPlugin : BaseUnityPlugin
    {
        // The game's sparkle effect, by material name.
        private const string SparkleMaterial = "item_particle";

        // Equipped items are drawn from this child, which keeps its own effects.
        private const string AttachChild = "attach";

        private const int EffectsLogged = 8;

        // Share of items an effect must be on to count as the sparkle when its name doesn't match.
        private const float SharedShare = 0.2f;

        private static readonly HashSet<ParticleSystem> Boosted = new HashSet<ParticleSystem>();
        private static bool logged;

        private static ManualLogSource log;
        private static ConfigEntry<float> intensity;
        private static ConfigEntry<float> count;
        private static ConfigEntry<float> spread;
        private static ConfigEntry<float> size;
        private static ConfigEntry<float> lifetime;

        private void Awake()
        {
            log = Logger;
            intensity = Multiplier("Intensity", 2f, "Brightness of each sparkle.");
            count = Multiplier("Count", 3f, "How many sparkles each item gives off.");
            spread = Multiplier("Spread", 2f, "How far around the item the sparkles appear.");
            size = Multiplier("Size", 3f, "How big each sparkle is.");
            lifetime = Multiplier("Lifetime", 3f, "How long each sparkle lasts.");

            new Harmony("com.parallel53.dudewheresmyspear").PatchAll(typeof(DudeWheresMySpearPlugin));
        }

        private ConfigEntry<float> Multiplier(string key, float value, string description)
        {
            return Config.Bind("Sparkles", key, value,
                new ConfigDescription(description + " A multiple of the game's. Takes effect the next time the game starts.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static void BoostSparkles(ObjectDB __instance)
        {
            // Every particle effect on a dropped item, grouped by what it is.
            List<GameObject> items = __instance.m_items.Where(item => item != null).ToList();
            Dictionary<string, List<ParticleSystem>> effects = new Dictionary<string, List<ParticleSystem>>();
            Dictionary<string, HashSet<GameObject>> owners = new Dictionary<string, HashSet<GameObject>>();
            foreach (GameObject item in items)
            {
                foreach (ParticleSystem particles in item.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (Equipped(item, particles))
                    {
                        continue;
                    }

                    string key = Key(particles);
                    if (!effects.ContainsKey(key))
                    {
                        effects[key] = new List<ParticleSystem>();
                        owners[key] = new HashSet<GameObject>();
                    }

                    effects[key].Add(particles);
                    owners[key].Add(item);
                }
            }

            // The main menu's item list is empty; the world's comes later.
            if (items.Count == 0)
            {
                return;
            }

            if (effects.Count == 0)
            {
                log.LogWarning("No items with particle effects among " + items.Count);
                return;
            }

            List<string> common = owners.Keys.OrderByDescending(key => owners[key].Count).ToList();
            if (!logged)
            {
                logged = true;
                log.LogInfo("Most common item effects among " + items.Count + " items: " + string.Join("; ",
                    common.Take(EffectsLogged).Select(key => key + " on " + owners[key].Count).ToArray()));
            }

            // The sparkle by its material, or else the effect most items share.
            List<string> sparkles = common.Where(key => key.StartsWith(SparkleMaterial)).ToList();
            if (sparkles.Count == 0 && owners[common[0]].Count >= items.Count * SharedShare)
            {
                sparkles.Add(common[0]);
            }

            if (sparkles.Count == 0)
            {
                log.LogWarning("Found no effect shared by most items, so nothing was boosted");
                return;
            }

            int boosted = 0;
            foreach (ParticleSystem particles in sparkles.SelectMany(key => effects[key]))
            {
                if (Boosted.Add(particles))
                {
                    if (Boosted.Count == 1)
                    {
                        log.LogInfo("Game's sparkles: " + Describe(particles));
                    }

                    Boost(particles);
                    boosted++;
                    if (Boosted.Count == 1)
                    {
                        log.LogInfo("Boosted to: " + Describe(particles));
                    }
                }
            }

            if (boosted > 0)
            {
                log.LogInfo("Boosted the sparkles on " + boosted + " items");
            }
        }

        // Effects are told apart by material: the sparkle sits on each item's root
        // under the item's own name, but always uses the same material.
        private static string Key(ParticleSystem particles)
        {
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            return renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none";
        }

        private static bool Equipped(GameObject item, ParticleSystem particles)
        {
            for (Transform parent = particles.transform; parent != null && parent != item.transform; parent = parent.parent)
            {
                if (parent.name == AttachChild)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Boost(ParticleSystem particles)
        {
            ParticleSystem.MainModule main = particles.main;
            // Room for the extra sparkles, which also linger longer.
            main.maxParticles = Mathf.CeilToInt(main.maxParticles * count.Value * Mathf.Max(lifetime.Value, 1f));
            main.startLifetimeMultiplier *= lifetime.Value;
            main.startSizeMultiplier *= size.Value;
            main.startColor = Brighter(main.startColor, intensity.Value);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTimeMultiplier *= count.Value;
            emission.rateOverDistanceMultiplier *= count.Value;

            // Some effects release particles in timed bursts instead of a steady stream.
            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            for (int i = 0; i < bursts.Length; i++)
            {
                ParticleSystem.MinMaxCurve burstCount = bursts[i].count;
                burstCount.constant *= count.Value;
                burstCount.constantMin *= count.Value;
                burstCount.constantMax *= count.Value;
                burstCount.curveMultiplier *= count.Value;
                bursts[i].count = burstCount;
            }

            emission.SetBursts(bursts);

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.radius *= spread.Value;
            shape.scale *= spread.Value;
        }

        private static string Describe(ParticleSystem particles)
        {
            ParticleSystem.MainModule main = particles.main;
            ParticleSystem.EmissionModule emission = particles.emission;
            ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emission.burstCount];
            emission.GetBursts(bursts);
            return particles.name + " max " + main.maxParticles +
                ", lifetime " + main.startLifetime.constant + "-" + main.startLifetime.constantMax +
                ", duration " + main.duration + (main.loop ? " looping" : "") +
                ", rate " + emission.rateOverTimeMultiplier + " (" + emission.rateOverTime.mode + ")" +
                ", bursts " + string.Join(" ", bursts.Select(burst => burst.count.constant + "@" + burst.time + "/" + burst.repeatInterval).ToArray()) +
                ", shape " + particles.shape.shapeType + " radius " + particles.shape.radius +
                ", emitting " + emission.enabled;
        }

        // Brighter colour, and more opaque where the game left room.
        private static ParticleSystem.MinMaxGradient Brighter(ParticleSystem.MinMaxGradient color, float factor)
        {
            switch (color.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Brighter(color.color, factor));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Brighter(color.colorMin, factor), Brighter(color.colorMax, factor));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Brighter(color.gradient, factor));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Brighter(color.gradientMin, factor), Brighter(color.gradientMax, factor));
                default:
                    return color;
            }
        }

        private static Gradient Brighter(Gradient gradient, float factor)
        {
            Gradient brighter = new Gradient { mode = gradient.mode };
            brighter.SetKeys(
                gradient.colorKeys.Select(key => new GradientColorKey(Brighter(key.color, factor), key.time)).ToArray(),
                gradient.alphaKeys.Select(key => new GradientAlphaKey(Mathf.Clamp01(key.alpha * factor), key.time)).ToArray());
            return brighter;
        }

        private static Color Brighter(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, Mathf.Clamp01(color.a * factor));
        }
    }
}
