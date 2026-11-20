using System.Collections.Generic;
using System.Globalization;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarelessWisp
{
    // Lets the Wisplight be upgraded, and each level widens the area your own
    // wisp clears of mist and thins the Mistlands fog. Level 1 is the vanilla wisp.
    //
    // How the game's mist works (ParticleMist): no mist forms inside a wisp's
    // force field (Demister.m_forceField.endRange), new mist is drawn out to
    // 350 m from you, and each puff lasts up to 5 seconds. The vanilla wisp
    // clears 15 m and pushes mist out very gently (gravity -0.08). The biome's
    // fog (EnvMan) is separate, and no vanilla wisp changes it.
    //
    // The wisp comes from the Wisplight's status effect (SE_Demister). With
    // EquipRequired off, this mod adds that effect while a Wisplight is anywhere
    // in your inventory, as RossQoL's WisplightCarry does; the game never stacks
    // a second copy, so both can be on. The level is that of the best Wisplight
    // you carry, or with EquipRequired on, the one you have equipped.
    [BepInPlugin("com.parallel53.carelesswisp", "CarelessWisp", "1.0.1")]
    public class CarelessWispPlugin : BaseUnityPlugin
    {
        private const string WisplightPrefab = "Demister";
        private const float VanillaPush = 0.08f;

        internal static ConfigEntry<bool> EquipRequired;
        internal static ConfigEntry<bool> UseLevels;
        internal static ConfigEntry<int> MaxLevel;
        internal static ConfigEntry<string> RadiusPerLevel;
        internal static ConfigEntry<string> FogPerLevel;
        internal static ConfigEntry<string> WispsPerLevel;
        internal static ConfigEntry<string> SilverPerLevel;
        internal static ConfigEntry<string> EitrPerLevel;
        internal static ConfigEntry<string> SoftTissuePerLevel;
        internal static ConfigEntry<string> CarapacePerLevel;
        internal static ConfigEntry<bool> FastClear;
        internal static ConfigEntry<float> ClearSeconds;
        internal static ConfigEntry<float> PushStrength;
        internal static ConfigEntry<bool> ThinFog;
        internal static ConfigEntry<float> FogDensity;
        internal static BepInEx.Logging.ManualLogSource Log;

        private static readonly AccessTools.FieldRef<SE_Demister, GameObject> BallInstance =
            AccessTools.FieldRefAccess<SE_Demister, GameObject>("m_ballInstance");
        private static readonly AccessTools.FieldRef<ParticleMist, ParticleSystem> MistParticles =
            AccessTools.FieldRefAccess<ParticleMist, ParticleSystem>("m_ps");

        // The Wisplight recipe's parts this mod prices per level, by material.
        private static readonly Dictionary<Piece.Requirement, ConfigEntry<string>> upgradeCosts =
            new Dictionary<Piece.Requirement, ConfigEntry<string>>();

        private static Demister wisp;
        private static Vector3 wispRest;
        private static int carriedEffect;
        private static int level = 1;
        private static float wispSeen = -10f;
        private static string logged = "";
        private static float fogBlend;
        private static ParticleSystem.Particle[] particles = new ParticleSystem.Particle[0];

        private void Awake()
        {
            Log = Logger;
            EquipRequired = Config.Bind("Wisp", "EquipRequired", false,
                "Off by default: a Wisplight anywhere in your inventory gives you its wisp, without using the utility " +
                "slot, and the best one you carry sets the level. On: only an equipped Wisplight gives a wisp and sets the level.");
            UseLevels = Config.Bind("Levels", "UseLevels", true,
                "Your best Wisplight's level picks the radius and fog below. On by default. Turn it off to always " +
                "use the highest level's values, whatever your Wisplight's level.");
            MaxLevel = Config.Bind("Levels", "MaxLevel", 5, new ConfigDescription(
                "How far the Wisplight can be upgraded. 5 by default; the vanilla Wisplight can't be upgraded. " +
                "Each level needs a workbench of that level.",
                new AcceptableValueRange<int>(1, 5)));
            RadiusPerLevel = Config.Bind("Levels", "RadiusPerLevel", "15, 30, 70, 200, 350",
                "How far around your wisp mist is cleared at each level, in metres, from level 1. " +
                "15, 30, 70, 200, 350 by default. 15 is the vanilla Wisplight; 350 is the farthest the game draws mist from you.");
            FogPerLevel = Config.Bind("Levels", "FogPerLevel", "100, 85, 65, 30, 0",
                "How much of the Mistlands fog is left while your wisp is out, at each level, as a percentage, from level 1. " +
                "100, 85, 65, 30, 0 by default. 100 leaves the fog as it is; 0 thins it to FogDensity.");
            WispsPerLevel = Config.Bind("Upgrades", "WispsPerLevel", "5, 10, 15, 30",
                "Wisps needed to upgrade the Wisplight to level 2, 3, 4 and 5. 5, 10, 15, 30 by default. " +
                "Later levels use the last number.");
            SilverPerLevel = Config.Bind("Upgrades", "SilverPerLevel", "20, 30, 0, 0",
                "Silver needed for each upgrade, from level 2. 20, 30, 0, 0 by default, so only the upgrades to levels 2 and 3 need it.");
            EitrPerLevel = Config.Bind("Upgrades", "EitrPerLevel", "0, 0, 20, 0",
                "Eitr needed for each upgrade, from level 2. 0, 0, 20, 0 by default, so only the upgrade to level 4 needs it.");
            SoftTissuePerLevel = Config.Bind("Upgrades", "SoftTissuePerLevel", "0, 10, 0, 0",
                "Soft tissue needed for each upgrade, from level 2. 0, 10, 0, 0 by default, so the upgrade to " +
                "level 3 needs something from the Mistlands.");
            CarapacePerLevel = Config.Bind("Upgrades", "MajesticCarapacePerLevel", "0, 0, 0, 1",
                "Majestic carapace, the Seeker Queen's drop, needed for each upgrade, from level 2. 0, 0, 0, 1 by default, " +
                "so level 5 waits until the Queen is beaten. She drops 5, and the artisan press extension needs 1.");
            FastClear = Config.Bind("Wisp", "FastClear", true,
                "From level 2, mist inside the radius fades out over ClearSeconds. On by default. Turn it off to let the wisp " +
                "push mist out instead, at PushStrength.");
            ClearSeconds = Config.Bind("Wisp", "ClearSeconds", 1.5f, new ConfigDescription(
                "How long mist inside the radius takes to fade out with FastClear on, in seconds. 1.5 by default; " +
                "0 removes it at once. Without FastClear, the game takes up to 5 seconds.",
                new AcceptableValueRange<float>(0f, 5f)));
            PushStrength = Config.Bind("Wisp", "PushStrength", 2f, new ConfigDescription(
                "How hard the wisp pushes mist outward from level 2 when FastClear is off. 2 by default; the vanilla " +
                "Wisplight pushes at 0.08. Stronger pushes clear faster but throw mist past the edge for a moment.",
                new AcceptableValueRange<float>(0f, 20f)));
            ThinFog = Config.Bind("Fog", "ThinFog", true,
                "Lets the Wisplight thin the Mistlands fog, as FogPerLevel sets. On by default. " +
                "The vanilla Wisplight never changes the fog.");
            FogDensity = Config.Bind("Fog", "FogDensity", 0.003f, new ConfigDescription(
                "The thinnest the Mistlands fog gets. 0.003 by default, the game's clear-weather fog. " +
                "Mistlands weather uses 0.02 to 0.05.",
                new AcceptableValueRange<float>(0f, 0.05f)));
            MaxLevel.SettingChanged += (sender, args) => SetUpRecipe(ObjectDB.instance);
            new Harmony("com.parallel53.carelesswisp").PatchAll(typeof(CarelessWispPlugin));
        }

        // Gives you the wisp while a Wisplight is carried, and takes back only a wisp
        // this mod gave once the Wisplight is gone or EquipRequired is turned on.
        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                carriedEffect = 0;
                return;
            }

            SEMan effects = player.GetSEMan();
            ItemDrop.ItemData carried = BestWisplight(player, false);
            StatusEffect effect = carried != null ? carried.m_shared.m_equipStatusEffect : null;
            if (!EquipRequired.Value && effect != null)
            {
                if (!effects.HaveStatusEffect(effect.NameHash()))
                {
                    effects.AddStatusEffect(effect);
                    carriedEffect = effect.NameHash();
                }

                return;
            }

            if (carriedEffect != 0)
            {
                if (BestWisplight(player, true) == null)
                {
                    effects.RemoveStatusEffect(carriedEffect, true);
                }

                carriedEffect = 0;
            }
        }

        private void LateUpdate()
        {
            if (WispActive && Player.m_localPlayer != null)
            {
                Centre(Player.m_localPlayer);
            }
        }

        // The game moves the wisp ahead of you while you run and lets it swing back
        // when you stop, and the clearing followed it, so its front edge pulled back.
        // From level 2 the part that clears mist (the force field, apart from the
        // wisp's light and flames) stays on you instead. Level 1 is left as vanilla.
        private static void Centre(Player player)
        {
            if (wisp == null)
            {
                return;
            }

            if (EffectiveLevel >= 2)
            {
                wisp.transform.position = player.GetCenterPoint();
            }
            else
            {
                wisp.transform.localPosition = wispRest;
            }
        }

        private static ItemDrop.ItemData BestWisplight(Player player, bool equippedOnly)
        {
            ItemDrop.ItemData best = null;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == WisplightPrefab &&
                    (!equippedOnly || item.m_equipped) && (best == null || item.m_quality > best.m_quality))
                {
                    best = item;
                }
            }

            return best;
        }

        // A comma-separated setting, read at a position; past the end, the last value.
        private static float ListValue(ConfigEntry<string> entry, int index)
        {
            float found = 0f;
            int position = 0;
            foreach (string part in entry.Value.Split(','))
            {
                if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    found = value;
                    if (position == index)
                    {
                        break;
                    }

                    position++;
                }
            }

            return found;
        }

        private static int EffectiveLevel => UseLevels.Value ? level : Mathf.Max(1, MaxLevel.Value);

        private static bool WispActive => wisp != null && Time.time - wispSeen < 0.5f;

        // The Wisplight gets more levels, and each upgrade costs what the settings say.
        // Materials it doesn't already use are added at 0 for crafting, so the first
        // Wisplight costs what it always has.
        [HarmonyPatch(typeof(ObjectDB), "Awake"), HarmonyPostfix]
        private static void AfterObjectDBAwake(ObjectDB __instance)
        {
            SetUpRecipe(__instance);
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)), HarmonyPostfix]
        private static void AfterCopyOtherDB(ObjectDB __instance)
        {
            SetUpRecipe(__instance);
        }

        private static void SetUpRecipe(ObjectDB db)
        {
            if (db == null || db.m_items == null || db.m_recipes == null)
            {
                return;
            }

            GameObject light = db.m_items.Find(item => item != null && item.name == WisplightPrefab);
            Recipe recipe = db.m_recipes.Find(r => r != null && r.m_item != null && r.m_item.name == WisplightPrefab);
            if (light == null || recipe == null)
            {
                return;
            }

            light.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality = Mathf.Max(1, MaxLevel.Value);
            upgradeCosts.Clear();
            Price(db, recipe, "Wisp", WispsPerLevel);
            Price(db, recipe, "Silver", SilverPerLevel);
            Price(db, recipe, "Eitr", EitrPerLevel);
            Price(db, recipe, "Softtissue", SoftTissuePerLevel);
            Price(db, recipe, "QueenDrop", CarapacePerLevel);
        }

        private static void Price(ObjectDB db, Recipe recipe, string material, ConfigEntry<string> costs)
        {
            foreach (Piece.Requirement existing in recipe.m_resources)
            {
                if (existing.m_resItem != null && existing.m_resItem.name == material)
                {
                    upgradeCosts[existing] = costs;
                    return;
                }
            }

            GameObject prefab = db.m_items.Find(item => item != null && item.name == material);
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                Log.LogWarning("Couldn't find " + material + " for the Wisplight upgrades.");
                return;
            }

            Piece.Requirement added = new Piece.Requirement { m_resItem = drop, m_amount = 0, m_amountPerLevel = 0, m_recover = true };
            recipe.m_resources = recipe.m_resources.AddToArray(added);
            upgradeCosts[added] = costs;
        }

        // The game asks each part for its amount at a level when showing, checking
        // and taking an upgrade's cost.
        [HarmonyPatch(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount)), HarmonyPostfix]
        private static void AfterGetAmount(Piece.Requirement __instance, int qualityLevel, ref int __result)
        {
            if (qualityLevel >= 2 && upgradeCosts.TryGetValue(__instance, out ConfigEntry<string> costs))
            {
                __result = Mathf.Max(0, Mathf.RoundToInt(ListValue(costs, qualityLevel - 2)));
            }
        }

        // The Wisplight's tooltip says what its level does and what the next one
        // brings. The workbench shows the level an upgrade would reach.
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) }), HarmonyPostfix]
        private static void AfterGetTooltip(ItemDrop.ItemData item, int qualityLevel, ref string __result)
        {
            if (item == null || item.m_dropPrefab == null || item.m_dropPrefab.name != WisplightPrefab)
            {
                return;
            }

            int max = Mathf.Max(1, MaxLevel.Value);
            int shown = UseLevels.Value ? Mathf.Clamp(qualityLevel, 1, max) : max;
            string text = "\n\nWisp clears mist <color=orange>" + Radius(shown) + " m</color> around you" + FogText(shown);
            if (UseLevels.Value && shown < max)
            {
                text += "\nNext level: " + Radius(shown + 1) + " m" + FogText(shown + 1);
            }

            if (!EquipRequired.Value)
            {
                text += "\nWorks from your inventory; no need to equip it.";
            }

            __result += text;
        }

        private static string Radius(int atLevel)
        {
            return Mathf.Max(1f, ListValue(RadiusPerLevel, atLevel - 1)).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string FogText(int atLevel)
        {
            float left = Mathf.Clamp(ListValue(FogPerLevel, atLevel - 1), 0f, 100f);
            if (!ThinFog.Value || left >= 100f)
            {
                return "";
            }

            return left <= 0f ? ", Mistlands fog thinned to clear weather" : ", " + left.ToString("0", CultureInfo.InvariantCulture) + "% of the Mistlands fog left";
        }

        // Runs every frame for every Wisplight effect; only your own wisp is changed.
        // Other players see your wisp with the game's normal radius.
        [HarmonyPatch(typeof(SE_Demister), nameof(SE_Demister.UpdateStatusEffect)), HarmonyPostfix]
        private static void AfterWispUpdate(SE_Demister __instance)
        {
            Player player = Player.m_localPlayer;
            if (player == null || __instance.m_character != player)
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
                if (wisp != null)
                {
                    wispRest = wisp.transform.localPosition;
                }
            }

            if (wisp == null || wisp.m_forceField == null)
            {
                return;
            }

            ItemDrop.ItemData light = BestWisplight(player, EquipRequired.Value);
            level = light != null ? light.m_quality : 1;

            int current = EffectiveLevel;
            float radius = Mathf.Max(1f, ListValue(RadiusPerLevel, current - 1));
            wisp.m_forceField.endRange = radius;
            // A strong push throws the mist the game spawns at the clearing's edge
            // past the radius while you move, and it drifts back when you stop.
            // FastClear doesn't need it, so the edge keeps the vanilla push.
            bool push = current >= 2 && !FastClear.Value;
            wisp.m_forceField.gravity = -(push ? PushStrength.Value : VanillaPush);
            wispSeen = Time.time;
            Centre(player);

            string state = "Wisplight level " + current + ": your wisp clears mist " + radius + " m around it.";
            if (state != logged)
            {
                logged = state;
                Log.LogInfo(state);
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

        // Mist already inside the radius fades out over ClearSeconds, instead of
        // lingering for up to 5 seconds or vanishing in a single frame.
        [HarmonyPatch(typeof(ParticleMist), "Update"), HarmonyPostfix]
        private static void AfterMistUpdate(ParticleMist __instance)
        {
            if (!FastClear.Value || !WispActive || EffectiveLevel < 2)
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
            float seconds = ClearSeconds.Value;
            int fade = seconds <= 0f ? 255 : Mathf.Max(1, Mathf.CeilToInt(255f * Time.deltaTime / seconds));

            for (int i = 0; i < count; i++)
            {
                Vector3 position = local ? frame.TransformPoint(particles[i].position) : particles[i].position;
                if ((position - centre).sqrMagnitude >= rangeSquared || particles[i].remainingLifetime <= 0f)
                {
                    continue;
                }

                Color32 colour = particles[i].startColor;
                if (colour.a <= fade)
                {
                    particles[i].remainingLifetime = 0f;
                }
                else
                {
                    colour.a = (byte)(colour.a - fade);
                    particles[i].startColor = colour;
                }

                changed = true;
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
            float left = Mathf.Clamp(ListValue(FogPerLevel, EffectiveLevel - 1), 0f, 100f) / 100f;
            bool thin = ThinFog.Value && WispActive && left < 1f &&
                __instance.GetCurrentBiome() == Heightmap.Biome.Mistlands;
            fogBlend = Mathf.MoveTowards(fogBlend, thin ? 1f : 0f, dt);
            if (fogBlend <= 0f)
            {
                return;
            }

            float fog = RenderSettings.fogDensity;
            float thinned = Mathf.Min(fog, Mathf.Max(fog * left, FogDensity.Value));
            RenderSettings.fogDensity = Mathf.Lerp(fog, thinned, fogBlend);
        }
    }
}
