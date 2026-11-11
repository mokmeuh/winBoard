namespace WinBoard.Services.Settings;

/// <summary>Emplacements des données de l'application.</summary>
public static class AppPaths
{
    /// <summary>Dossier %APPDATA%/WinBoard.</summary>
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinBoard");

    public static string SettingsFile(string? dir = null) => Path.Combine(dir ?? Root, "settings.json");
    public static string RecentEmojisFile(string? dir = null) => Path.Combine(dir ?? Root, "recent-emojis.json");
    public static string UsageFile(string? dir = null) => Path.Combine(dir ?? Root, "gemini-usage.json");
}
