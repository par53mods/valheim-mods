using System;
using SpeedBoat;

public static class SpeedStepsTests
{
    private static int failures;

    private static void Check(string name, object expected, object actual)
    {
        if (!Equals(expected, actual))
        {
            failures++;
            Console.WriteLine("FAIL " + name + ": expected " + expected + ", got " + actual);
        }
    }

    private static string Press(bool forward, Setting setting, int extra)
    {
        bool handled = forward ? SpeedSteps.Forward(setting, ref extra) : SpeedSteps.Backward(setting, ref extra);
        return handled + " " + extra;
    }

    public static int Main()
    {
        Check("full goes to +1", "True 1", Press(true, Setting.Full, 0));
        Check("+1 goes to +2", "True 2", Press(true, Setting.Full, 1));
        Check("+2 is the top", "True 2", Press(true, Setting.Full, 2));
        Check("half is the game's", "False 0", Press(true, Setting.Half, 0));
        Check("+2 drops to +1", "True 1", Press(false, Setting.Full, 2));
        Check("+1 drops to full", "True 0", Press(false, Setting.Full, 1));
        Check("full drops to half by the game", "False 0", Press(false, Setting.Full, 0));
        Check("reverse goes to fast reverse", "True -1", Press(false, Setting.Back, 0));
        Check("fast reverse stays", "True -1", Press(false, Setting.Back, -1));
        Check("fast reverse back to reverse", "True 0", Press(true, Setting.Back, -1));
        Check("reverse forward is the game's", "False 0", Press(true, Setting.Back, 0));
        Check("stop clears forward extra", 0, SpeedSteps.Settle(Setting.Stop, 2));
        Check("stop clears fast reverse", 0, SpeedSteps.Settle(Setting.Stop, -1));
        Check("full keeps its extra", 2, SpeedSteps.Settle(Setting.Full, 2));
        Check("back keeps fast reverse", -1, SpeedSteps.Settle(Setting.Back, -1));
        Check("label +2", "+2", SpeedSteps.Label(2));
        Check("label fast reverse", "fast", SpeedSteps.Label(-1));
        Check("no label", "", SpeedSteps.Label(0));

        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
