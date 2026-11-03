using System;

namespace ChestSalvage
{
    // Refund rules, kept free of game types so they can be tested without Valheim.
    public static class SalvageMath
    {
        // baseCost is the crafting cost; upgradeCosts holds one cost per upgrade
        // already applied. Each part rounds up on its own so a small cost still
        // returns something, and a rate of 0 returns nothing for that part.
        public static int Refund(int baseCost, int[] upgradeCosts, float baseRate, float upgradeRate)
        {
            int upgrades = 0;
            foreach (int cost in upgradeCosts)
            {
                upgrades += Math.Max(cost, 0);
            }

            return Scale(Math.Max(baseCost, 0), baseRate) + Scale(upgrades, upgradeRate);
        }

        // Crafting never spends an upgrader resource such as a protection idol.
        public static bool PaidOnCraft(bool upgraderResource)
        {
            return !upgraderResource;
        }

        // The upgrade levels this resource actually paid for. Normal stations raise
        // an item up to maxQuality without upgrader resources; past that the Forge
        // of Potential spends only upgrader resources.
        public static int[] PaidUpgradeLevels(int quality, int maxQuality, bool upgraderResource)
        {
            int first = upgraderResource ? Math.Max(maxQuality, 1) + 1 : 2;
            int last = upgraderResource ? quality : Math.Min(quality, maxQuality);
            if (last < first)
            {
                return new int[0];
            }

            int[] levels = new int[last - first + 1];
            for (int i = 0; i < levels.Length; i++)
            {
                levels[i] = first + i;
            }

            return levels;
        }

        private static int Scale(int amount, float rate)
        {
            float clamped = Math.Max(0f, Math.Min(rate, 1f));
            return (int)Math.Ceiling(Math.Round(amount * clamped, 4));
        }
    }
}
