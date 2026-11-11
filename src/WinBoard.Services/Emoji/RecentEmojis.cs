using System.Text.Json;

namespace WinBoard.Services.Emoji;

/// <summary>Émojis récemment utilisés, persistés en JSON.</summary>
public sealed class RecentEmojis
{
    private readonly string? _path;
    private readonly List<string> _items = new();
    private readonly object _lock = new();

    public int Max { get; }

    /// <param name="filePath">Fichier de persistance, ou null pour rester en mémoire.</param>
    public RecentEmojis(string? filePath = null, int max = 40)
    {
        _path = filePath;
        Max = max;
        Load();
    }

    public IReadOnlyList<string> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    /// <summary>Place l'émoji en tête et enregistre.</summary>
    public void Add(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return;
        lock (_lock)
        {
            _items.Remove(emoji);
            _items.Insert(0, emoji);
            if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
            Save();
        }
    }

    public void Clear()
    {
        lock (_lock) { _items.Clear(); Save(); }
    }

    private void Load()
    {
        if (_path == null) return;
        try
        {
            if (!File.Exists(_path)) return;
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path));
            if (list != null) _items.AddRange(list.Where(s => !string.IsNullOrEmpty(s)).Distinct().Take(Max));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    private void Save()
    {
        if (_path == null) return;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
