using System;
using BrightBlueDeer;

public static class ColorSettingTests
{
    private static int failures;

    private static void Check(string name, bool expected, int red, int green, int blue, string text)
    {
        bool parsed = ColorSetting.TryParse(text, out byte r, out byte g, out byte b);
        if (parsed == expected && (!expected || (r == red && g == green && b == blue)))
        {
            return;
        }

        failures++;
        Console.WriteLine("FAIL " + name + ": got " + parsed + " " + r + "," + g + "," + b);
    }

    public static int Main()
    {
        Check("default", true, 20, 115, 255, "20, 115, 255");
        Check("no spaces", true, 1, 2, 3, "1,2,3");
        Check("extra spaces", true, 1, 2, 3, "  1 ,  2 , 3 ");
        Check("limits", true, 0, 0, 255, "0, 0, 255");
        Check("above 255", false, 0, 0, 0, "20, 115, 256");
        Check("negative", false, 0, 0, 0, "-1, 115, 255");
        Check("too few", false, 0, 0, 0, "20, 115");
        Check("too many", false, 0, 0, 0, "1, 2, 3, 4");
        Check("words", false, 0, 0, 0, "blue");
        Check("empty", false, 0, 0, 0, "");
        Check("null", false, 0, 0, 0, null);

        Console.WriteLine(failures == 0 ? "All tests passed" : failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
