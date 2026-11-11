using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>
/// Dictionnaire en mémoire : tableau trié (recherche de préfixe par dichotomie) et table de fréquences.
/// Chargé par <see cref="LexiconLoader"/> depuis les ressources embarquées, mais utilisable avec n'importe quelle liste.
/// </summary>
public sealed class EmbeddedLexicon : ILexicon
{
    private readonly Dictionary<string, long> _freq;
    private readonly WordEntry[] _sorted;

    public EmbeddedLexicon(string language, IEnumerable<WordEntry> words)
    {
        Language = language;
        _freq = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var w in words)
        {
            var key = TextUtil.Normalize(w.Word);
            if (key.Length == 0 || w.Frequency <= 0) continue;
            _freq[key] = _freq.TryGetValue(key, out var f) ? f + w.Frequency : w.Frequency;
        }
        _sorted = _freq.Select(kv => new WordEntry(kv.Key, kv.Value))
                       .OrderBy(e => e.Word, StringComparer.Ordinal).ToArray();
        TotalCount = _freq.Values.Sum();
    }

    public string Language { get; }
    public long TotalCount { get; }
    public int Count => _sorted.Length;
    public IEnumerable<WordEntry> Words => _sorted;

    public bool Contains(string word) => _freq.ContainsKey(TextUtil.Normalize(word));

    public long GetFrequency(string word) =>
        _freq.TryGetValue(TextUtil.Normalize(word), out var f) ? f : 0;

    public IEnumerable<WordEntry> WithPrefix(string prefix, int max)
    {
        if (max <= 0) return Array.Empty<WordEntry>();
        prefix = TextUtil.Normalize(prefix);
        int lo = 0, hi = _sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (string.CompareOrdinal(_sorted[mid].Word, prefix) < 0) lo = mid + 1; else hi = mid;
        }
        // Sélection des "max" plus fréquents, sans trier toute la plage.
        var best = new List<WordEntry>(max + 1);
        for (int i = lo; i < _sorted.Length && _sorted[i].Word.StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            var e = _sorted[i];
            if (best.Count == max && e.Frequency <= best[^1].Frequency) continue;
            int pos = best.Count;
            while (pos > 0 && best[pos - 1].Frequency < e.Frequency) pos--;
            best.Insert(pos, e);
            if (best.Count > max) best.RemoveAt(best.Count - 1);
        }
        return best;
    }
}
