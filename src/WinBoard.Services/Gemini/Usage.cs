using System.Text.Json;

namespace WinBoard.Services.Gemini;

/// <summary>Compteur journalier persisté.</summary>
public sealed record DailyUsage(string Day, int Count);

/// <summary>Stockage du compteur journalier.</summary>
public interface IUsageStore
{
    DailyUsage Load();
    void Save(DailyUsage usage);
}

/// <summary>Mémoire seule (par défaut, ou pour les tests).</summary>
public sealed class InMemoryUsageStore : IUsageStore
{
    private DailyUsage _usage = new("", 0);
    public DailyUsage Load() => _usage;
    public void Save(DailyUsage usage) => _usage = usage;
}

/// <summary>Stockage dans un fichier JSON, écrit de façon atomique.</summary>
public sealed class FileUsageStore : IUsageStore
{
    private readonly string _path;
    public FileUsageStore(string path) => _path = path;

    public DailyUsage Load()
    {
        try
        {
            if (!File.Exists(_path)) return new DailyUsage("", 0);
            var u = JsonSerializer.Deserialize<DailyUsage>(File.ReadAllText(_path));
            return u is { Count: >= 0 } && u.Day != null ? u : new DailyUsage("", 0);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new DailyUsage("", 0);
        }
    }

    public void Save(DailyUsage usage)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(usage));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Échec de persistance non bloquant : le compteur reste valable en mémoire.
        }
    }
}

/// <summary>État des quotas locaux, pour l'affichage.</summary>
public sealed record UsageSnapshot(
    int MinuteRemaining, int MinuteLimit,
    int DayRemaining, int DayLimit,
    TimeSpan? MinuteResetIn);

/// <summary>Limiteur local : fenêtre glissante par minute et compteur journalier persistant.</summary>
public sealed class RateLimiter
{
    private readonly GeminiOptions _options;
    private readonly IUsageStore _store;
    private readonly TimeProvider _time;
    private readonly Queue<DateTimeOffset> _window = new();
    private readonly object _lock = new();
    private DailyUsage _daily;

    public RateLimiter(GeminiOptions options, IUsageStore store, TimeProvider time)
    {
        _options = options;
        _store = store;
        _time = time;
        _daily = store.Load();
    }

    // Le quota journalier se réinitialise à minuit heure du Pacifique ; on approxime par UTC-8.
    private string DayKey(DateTimeOffset now) => now.ToUniversalTime().AddHours(-8).ToString("yyyy-MM-dd");

    private void Refresh(DateTimeOffset now)
    {
        while (_window.Count > 0 && now - _window.Peek() >= TimeSpan.FromMinutes(1)) _window.Dequeue();
        if (_daily.Day != DayKey(now)) _daily = new DailyUsage(DayKey(now), 0);
    }

    /// <summary>Réserve une requête ou lève <see cref="GeminiQuotaException"/> sans appel réseau.</summary>
    public void Acquire()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            Refresh(now);
            if (_daily.Count >= _options.RequestsPerDay)
                throw new GeminiQuotaException(
                    $"Quota journalier local atteint ({_options.RequestsPerDay} requêtes). Il se réinitialise à minuit (heure du Pacifique).",
                    null, true);
            if (_window.Count >= _options.RequestsPerMinute)
            {
                var wait = _window.Peek() + TimeSpan.FromMinutes(1) - now;
                throw new GeminiQuotaException(
                    $"Limite locale de {_options.RequestsPerMinute} requêtes par minute atteinte. Réessayez dans {Math.Ceiling(wait.TotalSeconds)} s.",
                    wait, true);
            }
            _window.Enqueue(now);
            _daily = _daily with { Count = _daily.Count + 1 };
            _store.Save(_daily);
        }
    }

    public UsageSnapshot Snapshot()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            Refresh(now);
            TimeSpan? reset = _window.Count > 0 ? _window.Peek() + TimeSpan.FromMinutes(1) - now : null;
            return new UsageSnapshot(
                Math.Max(0, _options.RequestsPerMinute - _window.Count), _options.RequestsPerMinute,
                Math.Max(0, _options.RequestsPerDay - _daily.Count), _options.RequestsPerDay, reset);
        }
    }
}
