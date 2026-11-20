using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarelessWisp
{
    // Widens the area your own wisp clears of mist and clears it at once.
    //
    // How the game's mist works (ParticleMist): no mist forms inside a wisp's
    // force field (Demister.m_forceField.endRange), new mist is drawn out to
    // 350 m from you, and each puff lasts up to 5 seconds. The vanilla wisp
    // clears 15 m and pushes mist out very gently (gravity -0.08).
    //
    // The wisp comes from the Wisplight's status effect (SE_Demister), which is
    // there whether the Wisplight is equipped or carried by another mod, such as
    // RossQoL's WisplightCarry, so that is where this mod finds it.
    [BepInPlugin("com.parallel53.carelesswisp", "CarelessWisp", "1.0.0")]
    public class CarelessWispPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> Radius;
        internal static ConfigEntry<bool> ClearInstantly;
        internal static ConfigEntry<float> PushStrength;
        internal static ConfigEntry<bool> ThinFog;
        internal static ConfigEntry<float> FogDensity;
        internal static BepInEx.Logging.ManualLogSource Log;

        private static readonly AccessTools.FieldRef<SE_Demister, GameObject> BallInstance =
            AccessTools.FieldRefAccess<SE_Demister, GameObject>("m_ballInstance");
        private static readonly AccessTools.FieldRef<ParticleMist, ParticleSystem> MistParticles =
            AccessTools.FieldRefAccess<ParticleMist, ParticleSystem>("m_ps");

        private static Demister wisp;
        private static float wispSeen = -10f;
        private static float loggedRadius = -1f;
        private static float fogBlend;
        private static ParticleSystem.Particle[] particles = new ParticleSystem.Particle[0];

        private void Awake()
        {
            Log = Logger;
            Radius = Config.Bind("Wisp", "Radius", 350f, new ConfigDescription(
                "How far around your wisp mist is cleared, in metres. 350 by default, the farthest the game " +
                "draws mist from you. The vanilla Wisplight clears 15.",
                new AcceptableValueRange<float>(15f, 1000f)));
            ClearInstantly = Config.Bind("Wisp", "ClearInstantly", true,
                "Mist inside the radius disappears at once. On by default. Turn it off to let the wisp push mist " +
                "out instead, at PushStrength.");
            PushStrength = Config.Bind("Wisp", "PushStrength", 2f, new ConfigDescription(
                "How hard the wisp pushes mist outward. 2 by default; the vanilla Wisplight pushes at 0.08.",
                new AcceptableValueRange<float>(0f, 20f)));
            ThinFog = Config.Bind("Fog", "ThinFog", true,
                "While your wisp is out in the Mistlands, the biome's fog is thinned to FogDensity. On by default. " +
                "The vanilla Wisplight never changes the fog.");
            FogDensity = Config.Bind("Fog", "FogDensity", 0.003f, new ConfigDescription(
                "Fog density used in the Mistlands while your wisp is out. 0.003 by default, the game's clear-weather " +
                "fog. Mistlands weather uses 0.02 to 0.05. Fog that is already thinner is left alone.",
                new AcceptableValueRange<float>(0f, 0.05f)));
            new Harmony("com.parallel53.carelesswisp").PatchAll(typeof(CarelessWispPlugin));
        }

        private static bool WispActive => wisp != null && Time.time - wispSeen < 0.5f;

        // Runs every frame for every Wisplight effect; only your own wisp is changed.
        // Other players see your wisp with the game's normal radius.
        [HarmonyPatch(typeof(SE_Demister), nameof(SE_Demister.UpdateStatusEffect)), HarmonyPostfix]
        private static void AfterWispUpdate(SE_Demister __instance)
        {
            if (Player.m_localPlayer == null || __instance.m_character != Player.m_localPlayer)
            {
                return;
            }

            GameObject ball = BallInstance(__instance);
            if (ball == null)
            {
                return;
            }

            if (wisp == null || !wisp.transform.IsChildOf(ball.transform))
            {
                wisp = ball.GetComponentInChildren<Demister>(true);
            }

            if (wisp == null || wisp.m_forceField == null)
            {
                return;
            }

            float radius = Radius.Value;
            wisp.m_forceField.endRange = radius;
            wisp.m_forceField.gravity = -PushStrength.Value;
            wispSeen = Time.time;

            if (!Mathf.Approximately(loggedRadius, radius))
            {
                loggedRadius = radius;
                Log.LogInfo("Your wisp clears mist " + radius + " m around it.");
            }
        }

        // The game spawns mist at the edge of each wisp's clearing, but only keeps
        // what lands within m_maxDistance (20 to 40 m) of you. Past that, a wide
        // radius would only cost time picking points that are all thrown away.
        [HarmonyPatch(typeof(ParticleMist), "Emit"), HarmonyPrefix]
        private static bool BeforeEmit(ParticleMist __instance, Vector3 center, float radius, Demister pf)
        {
            if (pf == null || pf != wisp)
            {
                return true;
            }

            float distance = Vector3.Distance(__instance.transform.position, center);
            return radius <= distance + __instance.m_maxDistance;
        }

        // Mist already inside the radius is removed, so the clearing appears at once
        // instead of waiting up to 5 seconds for each puff to fade.
        [HarmonyPatch(typeof(ParticleMist), "Update"), HarmonyPostfix]
        private static void AfterMistUpdate(ParticleMist __instance)
        {
            if (!ClearInstantly.Value || !WispActive)
            {
                return;
            }

            ParticleSystem system = MistParticles(__instance);
            if (system == null || system.particleCount == 0)
            {
                return;
            }

            if (particles.Length < system.main.maxParticles)
            {
                particles = new ParticleSystem.Particle[system.main.maxParticles];
            }

            int count = system.GetParticles(particles);
            bool local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            Transform frame = system.transform;
            Vector3 centre = wisp.transform.position;
            float range = wisp.m_forceField.endRange;
            float rangeSquared = range * range;
            bool changed = false;

            for (int i = 0; i < count; i++)
            {
                Vector3 position = local ? frame.TransformPoint(particles[i].position) : particles[i].position;
                if ((position - centre).sqrMagnitude < rangeSquared && particles[i].remainingLifetime > 0f)
                {
                    particles[i].remainingLifetime = 0f;
                    changed = true;
                }
            }

            if (changed)
            {
                system.SetParticles(particles, count);
            }
        }

        // The environment sets fog every physics step; this thins it afterwards,
        // easing in and out over about a second as the wisp comes and goes.
        [HarmonyPatch(typeof(EnvMan), "SetEnv"), HarmonyPostfix]
        private static void AfterSetEnv(EnvMan __instance, float dt)
        {
            bool thin = ThinFog.Value && WispActive &&
                __instance.GetCurrentBiome() == Heightmap.Biome.Mistlands;
            fogBlend = Mathf.MoveTowards(fogBlend, thin ? 1f : 0f, dt);
            if (fogBlend <= 0f)
            {
                return;
            }

            float fog = RenderSettings.fogDensity;
            RenderSettings.fogDensity = Mathf.Lerp(fog, Mathf.Min(fog, FogDensity.Value), fogBlend);
        }
    }
}
