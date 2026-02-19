using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MCUScope.Services
{
    public class ThemeService
    {
        private static ThemeService? _instance;
        public static ThemeService Instance => _instance ??= new ThemeService();

        public string CurrentTheme { get; private set; } = "Dark";
        public event EventHandler? ThemeChanged;

        public static readonly string[] AvailableThemes =
        {
            "Dark", "Monokai", "Solarized Dark", "Nord", "Light"
        };

        private static readonly Dictionary<string, Dictionary<string, Color>> Palettes = new()
        {
            ["Dark"] = new()
            {
                ["PrimaryBg"] = Color(0x1A, 0x1A, 0x1A),
                ["PanelBg"] = Color(0x21, 0x21, 0x25),
                ["ElevatedBg"] = Color(0x2A, 0x2A, 0x2E),
                ["SurfaceBg"] = Color(0x32, 0x32, 0x36),
                ["BorderColor"] = Color(0x48, 0x48, 0x50),
                ["AccentColor"] = Color(0x00, 0x7A, 0xCC),
                ["AccentHover"] = Color(0x1C, 0x97, 0xEA),
                ["TextPrimary"] = Color(0xF0, 0xF0, 0xF0),
                ["TextSecondary"] = Color(0xA0, 0xA0, 0xA0),
                ["TextMuted"] = Color(0x6A, 0x6A, 0x6A),
                ["SuccessColor"] = Color(0x2E, 0xA0, 0x43),
                ["WarningColor"] = Color(0xD2, 0x99, 0x22),
                ["ErrorColor"] = Color(0xF8, 0x51, 0x49),
            },
            ["Monokai"] = new()
            {
                ["PrimaryBg"] = Color(0x27, 0x28, 0x22),
                ["PanelBg"] = Color(0x2E, 0x2F, 0x28),
                ["ElevatedBg"] = Color(0x3E, 0x3D, 0x32),
                ["SurfaceBg"] = Color(0x49, 0x48, 0x3E),
                ["BorderColor"] = Color(0x60, 0x58, 0x50),
                ["AccentColor"] = Color(0xF9, 0x26, 0x72),
                ["AccentHover"] = Color(0xFF, 0x66, 0x9D),
                ["TextPrimary"] = Color(0xF8, 0xF8, 0xF2),
                ["TextSecondary"] = Color(0xB0, 0xB0, 0xA0),
                ["TextMuted"] = Color(0x75, 0x71, 0x5E),
                ["SuccessColor"] = Color(0xA6, 0xE2, 0x2E),
                ["WarningColor"] = Color(0xE6, 0xDB, 0x74),
                ["ErrorColor"] = Color(0xF9, 0x26, 0x72),
            },
            ["Solarized Dark"] = new()
            {
                ["PrimaryBg"] = Color(0x00, 0x2B, 0x36),
                ["PanelBg"] = Color(0x07, 0x36, 0x42),
                ["ElevatedBg"] = Color(0x0A, 0x3F, 0x4C),
                ["SurfaceBg"] = Color(0x15, 0x4B, 0x56),
                ["BorderColor"] = Color(0x3A, 0x6C, 0x76),
                ["AccentColor"] = Color(0x26, 0x8B, 0xD2),
                ["AccentHover"] = Color(0x4E, 0xA5, 0xE0),
                ["TextPrimary"] = Color(0x93, 0xA1, 0xA1),
                ["TextSecondary"] = Color(0x83, 0x94, 0x96),
                ["TextMuted"] = Color(0x58, 0x6E, 0x75),
                ["SuccessColor"] = Color(0x85, 0x99, 0x00),
                ["WarningColor"] = Color(0xB5, 0x89, 0x00),
                ["ErrorColor"] = Color(0xDC, 0x32, 0x2F),
            },
            ["Nord"] = new()
            {
                ["PrimaryBg"] = Color(0x2E, 0x34, 0x40),
                ["PanelBg"] = Color(0x3B, 0x42, 0x52),
                ["ElevatedBg"] = Color(0x43, 0x4C, 0x5E),
                ["SurfaceBg"] = Color(0x4C, 0x56, 0x6A),
                ["BorderColor"] = Color(0x69, 0x73, 0x86),
                ["AccentColor"] = Color(0x88, 0xC0, 0xD0),
                ["AccentHover"] = Color(0x8F, 0xBC, 0xBB),
                ["TextPrimary"] = Color(0xEC, 0xEF, 0xF4),
                ["TextSecondary"] = Color(0xE5, 0xE9, 0xF0),
                ["TextMuted"] = Color(0x7B, 0x88, 0xA1),
                ["SuccessColor"] = Color(0xA3, 0xBE, 0x8C),
                ["WarningColor"] = Color(0xEB, 0xCB, 0x8B),
                ["ErrorColor"] = Color(0xBF, 0x61, 0x6A),
            },
            ["Light"] = new()
            {
                // High-contrast neutral-blue light palette for oscilloscope UI readability.
                ["PrimaryBg"] = Color(0xF3, 0xF6, 0xFA),
                ["PanelBg"] = Color(0xE9, 0xEE, 0xF4),
                ["ElevatedBg"] = Color(0xDD, 0xE6, 0xEF),
                ["SurfaceBg"] = Color(0xF8, 0xFB, 0xFF),
                ["BorderColor"] = Color(0x88, 0x9A, 0xB0),
                ["AccentColor"] = Color(0x0B, 0x64, 0xB8),
                ["AccentHover"] = Color(0x08, 0x56, 0x9E),
                ["TextPrimary"] = Color(0x13, 0x1A, 0x22),
                ["TextSecondary"] = Color(0x24, 0x31, 0x3F),
                ["TextMuted"] = Color(0x4D, 0x5B, 0x6B),
                ["SuccessColor"] = Color(0x16, 0x78, 0x2B),
                ["WarningColor"] = Color(0xA8, 0x66, 0x00),
                ["ErrorColor"] = Color(0xC2, 0x2D, 0x2D),
            },
        };

        public void ApplyTheme(string themeName)
        {
            if (!Palettes.ContainsKey(themeName)) themeName = "Dark";
            var palette = Palettes[themeName];
            var resources = Application.Current.Resources;

            foreach (var kvp in palette)
            {
                resources[kvp.Key] = kvp.Value;
                // Also update the SolidColorBrush resources
                string brushKey = kvp.Key + "Brush";
                if (resources.Contains(brushKey) && resources[brushKey] is SolidColorBrush brush)
                {
                    // Update existing brush instance so controls bound with StaticResource
                    // still repaint correctly after theme switch.
                    brush.Color = kvp.Value;
                }
            }

            CurrentTheme = themeName;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            LogService.Info($"Theme changed to: {themeName}");
        }

        private static Color Color(byte r, byte g, byte b) => System.Windows.Media.Color.FromRgb(r, g, b);
    }
}
