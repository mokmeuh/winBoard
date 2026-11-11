using WinBoard.Services.Gemini;

namespace WinBoard.Services.Settings;

/// <summary>Position de la fenêtre.</summary>
public sealed class WindowPlacement
{
    public double? Left { get; set; }
    public double? Top { get; set; }
}

/// <summary>Réglages de l'application (la clé API n'en fait jamais partie).</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "fr";
    public string Layout { get; set; } = "AZERTY";
    public string ThemeName { get; set; } = "Sombre";
    public Theme Custom { get; set; } = new() { Name = "Personnalisé" };
    public double KeyHeight { get; set; } = 48;
    public double WidthPercent { get; set; } = 100;
    public double Opacity { get; set; } = 1.0;
    public bool Docked { get; set; }
    public bool ShowNumberRow { get; set; } = true;
    public bool AutoCorrect { get; set; } = true;
    public bool ShowSuggestions { get; set; } = true;
    public bool SwipeEnabled { get; set; } = true;
    public bool SwipeTrail { get; set; } = true;
    public bool KeySound { get; set; }
    public bool LearnFromTyping { get; set; } = true;
    public bool AutoCapitalize { get; set; } = true;
    public bool DoubleSpacePeriod { get; set; } = true;
    public bool AutoShowOnTextFocus { get; set; } = true;
    public bool ReplaceSystemKeyboard { get; set; }
    public bool StartWithWindows { get; set; }
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public DictationStyle DictationStyle { get; set; } = DictationStyle.Clean;
    public string DictationLanguage { get; set; } = "fr";
    public WindowPlacement Window { get; set; } = new();

    /// <summary>Thème actif : le thème personnalisé s'il est choisi, sinon un thème fourni (Sombre par défaut).</summary>
    public Theme ResolveTheme()
    {
        if (string.Equals(ThemeName, Custom.Name, StringComparison.OrdinalIgnoreCase)) return Custom;
        return Themes.Find(ThemeName) ?? Themes.BuiltIn[1];
    }
}
