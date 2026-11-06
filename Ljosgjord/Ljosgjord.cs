using System.Linq;
using BepInEx;
using UnityEngine;

namespace Ljosgjord
{
    // Gives the Megingjord the Dverger Circlet's light while it is worn. Only this
    // client adds the light, so other players don't see it.
    [BepInPlugin("com.parallel53.ljosgjord", "Lj\u00f3sgj\u00f6r\u00f0", "1.0.1")]
    public class LjosgjordPlugin : BaseUnityPlugin
    {
        private const string BeltPrefab = "BeltStrength";
        private const string CircletPrefab = "HelmetDverger";

        private GameObject glow;
        private bool warned;

        private void Update()
        {
            Player player = Player.m_localPlayer;
            bool wearing = player != null && player.GetInventory().GetEquippedItems()
                .Any(item => item.m_dropPrefab != null && item.m_dropPrefab.name == BeltPrefab);

            if (wearing && glow == null)
            {
                glow = CreateGlow(player);
            }

            if (glow != null && glow.activeSelf != wearing)
            {
                glow.SetActive(wearing);
            }
        }

        // The light hangs off the head so it aims where the circlet's would.
        private GameObject CreateGlow(Player player)
        {
            GameObject circlet = ObjectDB.instance.GetItemPrefab(CircletPrefab);
            Light source = circlet != null ? circlet.GetComponentInChildren<Light>(true) : null;
            Transform head = player.GetComponent<VisEquipment>().m_helmet;
            if (source == null || head == null)
            {
                if (!warned)
                {
                    warned = true;
                    Logger.LogWarning("No circlet light or head to attach it to");
                }

                return null;
            }

            GameObject copy = Instantiate(source.gameObject, head);
            copy.transform.localPosition = source.transform.localPosition;
            copy.transform.localRotation = source.transform.localRotation;
            copy.SetActive(true);
            foreach (Light light in copy.GetComponentsInChildren<Light>(true))
            {
                light.enabled = true;
            }

            Logger.LogInfo("Added the circlet light to the belt wearer");
            return copy;
        }
    }
}
