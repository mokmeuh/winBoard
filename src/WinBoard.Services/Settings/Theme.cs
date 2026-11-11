namespace WinBoard.Services.Settings;

/// <summary>Thème visuel : couleurs hexadécimales (#RRGGBB ou #AARRGGBB) et typographie.</summary>
public sealed class Theme
{
    public string Name { get; set; } = "Sombre";
    public string Background { get; set; } = "#1E1E1E";
    public string Key { get; set; } = "#3A3A3C";
    public string KeyPressed { get; set; } = "#5A5A5E";
    public string SpecialKey { get; set; } = "#2A2A2C";
    public string Text { get; set; } = "#FFFFFF";
    public string Accent { get; set; } = "#0A84FF";
    public string SuggestionBar { get; set; } = "#252527";
    public string Trail { get; set; } = "#0A84FF";
    public double CornerRadius { get; set; } = 6;
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 16;

    public Theme Clone() => (Theme)MemberwiseClone();
}

/// <summary>Thèmes fournis avec l'application.</summary>
public static class Themes
{
    public static IReadOnlyList<Theme> BuiltIn { get; } = new[]
    {
        new Theme { Name = "Clair", Background = "#E9EAEE", Key = "#FFFFFF", KeyPressed = "#C9CCD6", SpecialKey = "#D3D6DE",
                    Text = "#1B1B1F", Accent = "#0067C0", SuggestionBar = "#DDDFE5", Trail = "#0067C0" },
        new Theme { Name = "Sombre" },
        new Theme { Name = "Bleu nuit", Background = "#0B1426", Key = "#1B2A4A", KeyPressed = "#2F4678", SpecialKey = "#13203A",
                    Text = "#E6EDFF", Accent = "#4C8DFF", SuggestionBar = "#0F1B33", Trail = "#7FB0FF" },
        new Theme { Name = "Contraste élevé", Background = "#000000", Key = "#000000", KeyPressed = "#FFFF00", SpecialKey = "#1A1A1A",
                    Text = "#FFFFFF", Accent = "#FFFF00", SuggestionBar = "#000000", Trail = "#FFFF00" },
        new Theme { Name = "Forêt", Background = "#14261C", Key = "#24402F", KeyPressed = "#3C6B4D", SpecialKey = "#1B3325",
                    Text = "#EAF5EC", Accent = "#4CD27A", SuggestionBar = "#183022", Trail = "#7BE3A0" },
        new Theme { Name = "Rose", Background = "#2A1822", Key = "#472B3A", KeyPressed = "#703F58", SpecialKey = "#341F2A",
                    Text = "#FFEAF3", Accent = "#FF5C9E", SuggestionBar = "#321C27", Trail = "#FF8DBB" },
        new Theme { Name = "Ambre", Background = "#2B2113", Key = "#473720", KeyPressed = "#7A5C30", SpecialKey = "#35291A",
                    Text = "#FFF3DC", Accent = "#FFA826", SuggestionBar = "#30251A", Trail = "#FFC15C" },
    };

    /// <summary>Retrouve un thème par son nom (insensible à la casse), ou null.</summary>
    public static Theme? Find(string? name) =>
        BuiltIn.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
