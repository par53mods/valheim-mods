using System;
using ChestSalvage;

public static class SalvageMathTests
{
    private static int failures;

    private static void Check(string name, int expected, int actual)
    {
        if (expected == actual)
        {
            return;
        }

        failures++;
        Console.WriteLine("FAIL " + name + ": expected " + expected + ", got " + actual);
    }

    public static int Main()
    {
        int[] none = new int[0];
        Check("base only, half", 5, SalvageMath.Refund(10, none, 0.5f, 1f));
        Check("odd base rounds up", 3, SalvageMath.Refund(5, none, 0.5f, 1f));
        Check("cost of one still returns one", 1, SalvageMath.Refund(1, none, 0.5f, 1f));
        Check("upgrades return in full", 25, SalvageMath.Refund(10, new[] { 5, 10, 5 }, 0.5f, 1f));
        Check("upgrade-only material", 6, SalvageMath.Refund(0, new[] { 3, 3 }, 0.5f, 1f));
        Check("base rate zero", 4, SalvageMath.Refund(10, new[] { 4 }, 0f, 1f));
        Check("both rates zero", 0, SalvageMath.Refund(10, new[] { 4 }, 0f, 0f));
        Check("nothing to refund", 0, SalvageMath.Refund(0, none, 0.5f, 1f));
        Check("rates above one clamp", 14, SalvageMath.Refund(10, new[] { 4 }, 2f, 2f));
        Check("negative rate clamps", 4, SalvageMath.Refund(10, new[] { 4 }, -1f, 1f));
        Check("negative cost ignored", 2, SalvageMath.Refund(-5, new[] { 2, -3 }, 1f, 1f));
        Check("float noise does not add one", 3, SalvageMath.Refund(10, none, 0.3f, 1f));

        Check("idol never paid on craft", 0, SalvageMath.PaidOnCraft(true) ? 1 : 0);
        Check("normal material paid on craft", 1, SalvageMath.PaidOnCraft(false) ? 1 : 0);
        Check("level 1 item paid no idol", 0, SalvageMath.PaidUpgradeLevels(1, 4, true).Length);
        Check("level 4 item at max paid no idol", 0, SalvageMath.PaidUpgradeLevels(4, 4, true).Length);
        Check("level 5 past max paid one idol level", 1, SalvageMath.PaidUpgradeLevels(5, 4, true).Length);
        Check("that idol level is 5", 5, SalvageMath.PaidUpgradeLevels(5, 4, true)[0]);
        Check("level 6 past max paid two idol levels", 2, SalvageMath.PaidUpgradeLevels(6, 4, true).Length);
        Check("level 1 paid no normal upgrades", 0, SalvageMath.PaidUpgradeLevels(1, 4, false).Length);
        Check("level 3 paid two normal upgrades", 2, SalvageMath.PaidUpgradeLevels(3, 4, false).Length);
        Check("normal upgrades stop at max", 3, SalvageMath.PaidUpgradeLevels(6, 4, false).Length);
        Check("single-level item has no normal upgrades", 0, SalvageMath.PaidUpgradeLevels(1, 1, false).Length);

        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
