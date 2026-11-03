namespace BrightBlueDeer
{
    // Parses the colour setting, kept free of game types so it can be tested without Valheim.
    public static class ColorSetting
    {
        // Accepts "red, green, blue" with each part from 0 to 255.
        public static bool TryParse(string text, out byte red, out byte green, out byte blue)
        {
            red = green = blue = 0;
            string[] parts = (text ?? "").Split(',');
            return parts.Length == 3 &&
                byte.TryParse(parts[0].Trim(), out red) &&
                byte.TryParse(parts[1].Trim(), out green) &&
                byte.TryParse(parts[2].Trim(), out blue);
        }
    }
}
