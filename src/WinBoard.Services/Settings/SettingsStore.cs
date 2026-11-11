using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinBoard.Services.Settings;

/// <summary>Lecture et écriture de settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public SettingsStore(string directory)
    {
        DirectoryPath = directory;
        _path = AppPaths.SettingsFile(directory);
    }

    private string DirectoryPath { get; }

    public AppSettings Current { get; private set; } = new();

    /// <summary>Déclenché après chaque <see cref="Save"/>.</summary>
    public event EventHandler<AppSettings>? Changed;

    /// <summary>Charge les réglages ; fichier absent ou corrompu : valeurs par défaut.</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
                if (s != null)
                {
                    s.Custom ??= new Theme { Name = "Personnalisé" };
                    s.Window ??= new WindowPlacement();
                    Current = s;
                    return Current;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        Current = new AppSettings();
        return Current;
    }

    /// <summary>Enregistre de façon atomique (fichier temporaire puis remplacement).</summary>
    public void Save(AppSettings? settings = null)
    {
        if (settings != null) Current = settings;
        Directory.CreateDirectory(DirectoryPath);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
        Changed?.Invoke(this, Current);
    }
}
