using System.Runtime.CompilerServices;
using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>
/// Structures de recherche approchée construites à partir d'un dictionnaire : un trie (tableaux)
/// parcouru avec une distance pondérée bornée, et un index « sans accents » pour retrouver les mots accentués.
/// </summary>
internal sealed class LexiconIndex
{
    private static readonly ConditionalWeakTable<ILexicon, LexiconIndex> Cache = new();

    public static LexiconIndex For(ILexicon lexicon) => Cache.GetValue(lexicon, l => new LexiconIndex(l));

    private readonly char[] _ch;
    private readonly int[] _firstChild;
    private readonly int[] _nextSibling;
    private readonly int[] _wordAt;
    private readonly string[] _words;
    private readonly long[] _freqs;
    private readonly Dictionary<string, int[]> _folded = new(StringComparer.Ordinal);

    private LexiconIndex(ILexicon lexicon)
    {
        var entries = lexicon.Words.Where(e => e.Word.Length > 0).OrderBy(e => e.Word, StringComparer.Ordinal).ToArray();
        _words = entries.Select(e => e.Word).ToArray();
        _freqs = entries.Select(e => e.Frequency).ToArray();

        var ch = new List<char> { '\0' };
        var first = new List<int> { -1 };
        var next = new List<int> { -1 };
        var wordAt = new List<int> { -1 };
        var last = new List<int> { -1 };        // dernier enfant de chaque noeud (les mots arrivent triés)
        var path = new List<int> { 0 };          // noeuds du mot précédent, de la racine vers le bas
        string prev = "";
        for (int w = 0; w < _words.Length; w++)
        {
            var word = _words[w];
            int lcp = 0;
            while (lcp < prev.Length && lcp < word.Length && prev[lcp] == word[lcp]) lcp++;
            path.RemoveRange(lcp + 1, path.Count - lcp - 1);
            for (int i = lcp; i < word.Length; i++)
            {
                int parent = path[i];
                int id = ch.Count;
                ch.Add(word[i]); first.Add(-1); next.Add(-1); wordAt.Add(-1); last.Add(-1);
                if (first[parent] < 0) first[parent] = id; else next[last[parent]] = id;
                last[parent] = id;
                path.Add(id);
            }
            wordAt[path[word.Length]] = w;
            prev = word;
        }
        _ch = ch.ToArray(); _firstChild = first.ToArray(); _nextSibling = next.ToArray(); _wordAt = wordAt.ToArray();

        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (int w = 0; w < _words.Length; w++)
        {
            var key = TextUtil.Fold(_words[w]);
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<int>(1);
            g.Add(w);
        }
        // Seuls les groupes contenant une variante accentuée sont utiles.
        foreach (var (k, g) in groups)
            if (g.Any(i => _words[i] != k)) _folded[k] = g.ToArray();
    }

    public int WordCount => _words.Length;

    /// <summary>Mots dont la forme sans accents est celle de <paramref name="word"/> (le mot lui-même inclus s'il existe).</summary>
    public IEnumerable<WordEntry> AccentVariants(string word)
    {
        if (!_folded.TryGetValue(TextUtil.Fold(word), out var ids)) yield break;
        foreach (var i in ids) yield return new WordEntry(_words[i], _freqs[i]);
    }

    /// <summary>
    /// Mots à distance pondérée inférieure ou égale à <paramref name="budget"/> du mot saisi.
    /// Le parcours du trie abandonne toute branche dont le coût minimal dépasse le budget.
    /// </summary>
    public List<(string Word, long Freq, double Cost)> Search(string typed, CostModel costs, double budget)
    {
        var result = new List<(string, long, double)>();
        int m = typed.Length;
        int maxDepth = m + 3;
        var rows = new double[maxDepth + 2][];
        for (int i = 0; i < rows.Length; i++) rows[i] = new double[m + 1];
        var subCache = new Dictionary<(char, bool), double[]>();
        var chars = new char[maxDepth + 2];

        rows[0][0] = 0;
        for (int j = 1; j <= m; j++) rows[0][j] = rows[0][j - 1] + costs.Extra(typed[j - 1], j > 1 ? typed[j - 2] : '\0');

        double[] SubRow(char c, bool first)
        {
            if (!subCache.TryGetValue((c, first), out var row))
            {
                row = new double[m];
                for (int j = 0; j < m; j++) row[j] = costs.Sub(c, typed[j], first);
                subCache[(c, first)] = row;
            }
            return row;
        }

        void Visit(int node, int depth)
        {
            for (int child = _firstChild[node]; child >= 0; child = _nextSibling[child])
            {
                char c = _ch[child];
                chars[depth] = c;
                int d = depth + 1;
                var prow = rows[depth];
                var row = rows[d];
                char prevIntended = depth > 0 ? chars[depth - 1] : '\0';
                double omit = costs.Omitted(c, prevIntended);
                var sub = SubRow(c, depth == 0);
                row[0] = prow[0] + omit;
                double min = row[0];
                for (int j = 1; j <= m; j++)
                {
                    double best = prow[j - 1] + sub[j - 1];
                    double v = prow[j] + omit;
                    if (v < best) best = v;
                    v = row[j - 1] + costs.Extra(typed[j - 1], j > 1 ? typed[j - 2] : '\0');
                    if (v < best) best = v;
                    if (depth >= 1 && j >= 2 && c == typed[j - 2] && prevIntended == typed[j - 1])
                    {
                        v = rows[depth - 1][j - 2] + CostModel.Transposition;
                        if (v < best) best = v;
                    }
                    row[j] = best;
                    if (best < min) min = best;
                }
                if (_wordAt[child] >= 0 && row[m] <= budget)
                {
                    int w = _wordAt[child];
                    result.Add((_words[w], _freqs[w], row[m]));
                }
                if (min <= budget && d < maxDepth) Visit(child, d);
            }
        }
        Visit(0, 0);
        return result;
    }
}
