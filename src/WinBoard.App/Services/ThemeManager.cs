using System.Windows;
using System.Windows.Media;
using WinBoard.Services.Settings;

namespace WinBoard.App.Services;

/// <summary>Publie les couleurs du thème actif comme ressources de l'application (utilisées en DynamicResource).</summary>
public static class ThemeManager
{
    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex.Trim()) is Color c) return c;
        }
        catch (FormatException) { }
        return fallback;
    }

    private static SolidColorBrush Brush(string? hex, Color fallback)
    {
        var b = new SolidColorBrush(Parse(hex, fallback));
        b.Freeze();
        return b;
    }

    public static void Apply(Theme t)
    {
        var r = Application.Current.Resources;
        r["BackgroundBrush"] = Brush(t.Background, Color.FromRgb(0x1E, 0x1E, 0x1E));
        r["KeyBrush"] = Brush(t.Key, Color.FromRgb(0x3A, 0x3A, 0x3C));
        r["KeyPressedBrush"] = Brush(t.KeyPressed, Color.FromRgb(0x5A, 0x5A, 0x5E));
        r["SpecialKeyBrush"] = Brush(t.SpecialKey, Color.FromRgb(0x2A, 0x2A, 0x2C));
        r["TextBrush"] = Brush(t.Text, Colors.White);
        r["AccentBrush"] = Brush(t.Accent, Color.FromRgb(0x0A, 0x84, 0xFF));
        r["SuggestionBarBrush"] = Brush(t.SuggestionBar, Color.FromRgb(0x25, 0x25, 0x27));
        r["TrailBrush"] = Brush(t.Trail, Color.FromRgb(0x0A, 0x84, 0xFF));
        r["KeyCornerRadius"] = new CornerRadius(Math.Clamp(t.CornerRadius, 0, 30));
        r["ThemeFontFamily"] = new FontFamily(string.IsNullOrWhiteSpace(t.FontFamily) ? "Segoe UI" : t.FontFamily);
        r["ThemeFontSize"] = Math.Clamp(t.FontSize <= 0 ? 16 : t.FontSize, 8, 40);
    }
}
