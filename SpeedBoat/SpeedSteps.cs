namespace SpeedBoat
{
    // The game's ship speeds, in the order of its Ship.Speed enum.
    public enum Setting
    {
        Stop,
        Back,
        Slow,
        Half,
        Full
    }

    // The extra speeds stacked on the game's own. Extra counts levels above Full
    // when positive, and fast reverse when negative. Kept free of game types so
    // it can be tested without Valheim.
    public static class SpeedSteps
    {
        public const int MaxForward = 2;
        public const int FastReverse = -1;

        // Whether a forward press is ours to handle, and the extra level after it.
        public static bool Forward(Setting setting, ref int extra)
        {
            if (setting == Setting.Full && extra < MaxForward)
            {
                extra++;
                return true;
            }

            if (setting == Setting.Back && extra < 0)
            {
                extra = 0;
                return true;
            }

            // Already at the top, which the game ignores too.
            return setting == Setting.Full;
        }

        public static bool Backward(Setting setting, ref int extra)
        {
            if (setting == Setting.Full && extra > 0)
            {
                extra--;
                return true;
            }

            if (setting == Setting.Back)
            {
                extra = FastReverse;
                return true;
            }

            return false;
        }

        // An extra level only lasts while the game's speed is the one it builds on,
        // so stopping, or the game stopping an empty boat, clears it.
        public static int Settle(Setting setting, int extra)
        {
            if (extra > 0 && setting != Setting.Full)
            {
                return 0;
            }

            return extra < 0 && setting != Setting.Back ? 0 : extra;
        }

        public static string Label(int extra)
        {
            if (extra > 0)
            {
                return "+" + extra;
            }

            return extra < 0 ? "fast" : "";
        }
    }
}
