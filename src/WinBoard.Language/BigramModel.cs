using System.Globalization;

namespace WinBoard.Language;

/// <summary>Modèle de bigrammes en lecture seule : pour un mot, les mots qui le suivent, triés par fréquence.</summary>
public sealed class BigramModel
{
    private readonly Dictionary<string, (string Word, long Count)[]> _next;
    private readonly Dictionary<string, long> _totals;

    public static BigramModel Empty { get; } = new(Array.Empty<(string, string, long)>());

    public BigramModel(IEnumerable<(string First, string Second, long Count)> pairs)
    {
        var tmp = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        foreach (var (a, b, c) in pairs)
        {
            if (c <= 0) continue;
            if (!tmp.TryGetValue(a, out var d)) tmp[a] = d = new Dictionary<string, long>(StringComparer.Ordinal);
            d[b] = d.TryGetValue(b, out var old) ? old + c : c;
        }
        _next = new Dictionary<string, (string, long)[]>(tmp.Count, StringComparer.Ordinal);
        _totals = new Dictionary<string, long>(tmp.Count, StringComparer.Ordinal);
        foreach (var (a, d) in tmp)
        {
            _next[a] = d.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Value).ToArray();
            _totals[a] = d.Values.Sum();
        }
    }

    public int Count => _next.Count;

    /// <summary>Lit des lignes « mot1 mot2 compte ».</summary>
    public static BigramModel Parse(TextReader reader)
    {
        var list = new List<(string, string, long)>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 3 && long.TryParse(p[2], NumberStyles.None, CultureInfo.InvariantCulture, out var c))
                list.Add((p[0], p[1], c));
        }
        return new BigramModel(list);
    }

    /// <summary>Suivants de <paramref name="previous"/>, du plus fréquent au moins fréquent.</summary>
    public IReadOnlyList<(string Word, long Count)> GetNext(string previous) =>
        _next.TryGetValue(previous, out var a) ? a : Array.Empty<(string, long)>();

    /// <summary>Somme des comptes des suivants de <paramref name="previous"/>.</summary>
    public long Total(string previous) => _totals.TryGetValue(previous, out var t) ? t : 0;

    public long GetCount(string previous, string word)
    {
        if (!_next.TryGetValue(previous, out var a)) return 0;
        foreach (var (w, c) in a) if (w == word) return c;
        return 0;
    }
}
