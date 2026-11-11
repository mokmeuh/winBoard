namespace WinBoard.Core;

/// <summary>Mot du dictionnaire avec sa fréquence d'usage.</summary>
public readonly record struct WordEntry(string Word, long Frequency);

/// <summary>Dictionnaire de mots en lecture seule pour une langue donnée. Les mots sont en minuscules.</summary>
public interface ILexicon
{
    /// <summary>Code de langue ISO ("fr", "en").</summary>
    string Language { get; }

    /// <summary>Somme de toutes les fréquences.</summary>
    long TotalCount { get; }

    int Count { get; }

    bool Contains(string word);

    /// <summary>Fréquence du mot, 0 s'il est inconnu.</summary>
    long GetFrequency(string word);

    IEnumerable<WordEntry> Words { get; }

    /// <summary>Mots commençant par le préfixe, triés par fréquence décroissante.</summary>
    IEnumerable<WordEntry> WithPrefix(string prefix, int max);
}

/// <summary>Implémentation simple en mémoire, utile pour les tests et les petits dictionnaires.</summary>
public sealed class InMemoryLexicon : ILexicon
{
    private readonly Dictionary<string, long> _freq;
    private readonly WordEntry[] _sorted;

    public InMemoryLexicon(string language, IEnumerable<WordEntry> words)
    {
        Language = language;
        _freq = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var w in words)
        {
            var key = w.Word.ToLowerInvariant();
            _freq[key] = _freq.TryGetValue(key, out var f) ? f + w.Frequency : w.Frequency;
        }
        _sorted = _freq.Select(kv => new WordEntry(kv.Key, kv.Value))
                       .OrderBy(e => e.Word, StringComparer.Ordinal).ToArray();
        TotalCount = _freq.Values.Sum();
    }

    public string Language { get; }
    public long TotalCount { get; }
    public int Count => _sorted.Length;
    public bool Contains(string word) => _freq.ContainsKey(word.ToLowerInvariant());
    public long GetFrequency(string word) => _freq.TryGetValue(word.ToLowerInvariant(), out var f) ? f : 0;
    public IEnumerable<WordEntry> Words => _sorted;

    public IEnumerable<WordEntry> WithPrefix(string prefix, int max)
    {
        prefix = prefix.ToLowerInvariant();
        int lo = 0, hi = _sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (string.CompareOrdinal(_sorted[mid].Word, prefix) < 0) lo = mid + 1; else hi = mid;
        }
        var matches = new List<WordEntry>();
        for (int i = lo; i < _sorted.Length && _sorted[i].Word.StartsWith(prefix, StringComparison.Ordinal); i++)
            matches.Add(_sorted[i]);
        return matches.OrderByDescending(e => e.Frequency).Take(max);
    }
}
