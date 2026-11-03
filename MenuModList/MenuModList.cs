using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MenuModList
{
    // Shows BepInEx and every loaded plugin in a panel on the main menu.
    [BepInPlugin("com.parallel53.menumodlist", "MenuModList", "1.0.0")]
    public class MenuModListPlugin : BaseUnityPlugin
    {
        private const float ReferenceHeight = 1080f;
        private const float PanelWidth = 260f;
        private const float Margin = 20f;
        private const int FontSize = 14;
        private const string GameFont = "AveriaSerifLibre-Bold";

        private string text;
        private int lineCount;
        private GUIStyle style;
        private bool logged;

        private void OnGUI()
        {
            FejdStartup menu = FejdStartup.instance;
            if (menu == null || menu.m_mainMenu == null || !menu.m_mainMenu.activeInHierarchy)
            {
                return;
            }

            if (text == null)
            {
                BuildText();
            }

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    font = FindGameFont(),
                    fontSize = FontSize,
                    richText = true,
                    wordWrap = false,
                    padding = new RectOffset(12, 12, 10, 10)
                };
                style.normal.textColor = Color.white;
            }

            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float height = style.lineHeight * lineCount + style.padding.vertical;
            float x = Screen.width / scale - PanelWidth - Margin;
            GUI.Box(new Rect(x, Margin, PanelWidth, height), text, style);

            if (!logged)
            {
                logged = true;
                Logger.LogInfo("Showing mod list on main menu:\n" + text);
            }
        }

        // The game's fonts are already in memory by the time the menu shows.
        private Font FindGameFont()
        {
            Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
            Font font = fonts.FirstOrDefault(f => f.name == GameFont);
            Logger.LogInfo(font != null
                ? "Using font " + font.name + " (dynamic: " + font.dynamic + ")"
                : "No " + GameFont + " font. Loaded: " + string.Join(", ", fonts.Select(f => f.name).ToArray()));
            return font;
        }

        private void BuildText()
        {
            string[] plugins = Chainloader.PluginInfos.Values
                .Select(p => p.Metadata.Name + " <color=#ffcc66>" + p.Metadata.Version + "</color>")
                .OrderBy(line => line)
                .ToArray();

            string header = "<b>Mods loaded: " + plugins.Length + "</b>\nBepInEx " + typeof(BaseUnityPlugin).Assembly.GetName().Version;
            text = header + "\n" + string.Join("\n", plugins);
            lineCount = plugins.Length + 2;
        }
    }
}
