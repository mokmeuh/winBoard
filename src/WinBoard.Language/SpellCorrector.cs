using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>
/// Correction orthographique dans l'esprit de Norvig (suppression, transposition, remplacement, insertion),
/// pondérée par la fréquence des mots, la proximité des touches et les accents oubliés.
/// La recherche parcourt un trie avec une distance bornée plutôt que de générer tous les mots à deux éditions.
/// </summary>
public sealed class SpellCorrector
{
    // Un accent n'est rétabli sur un mot connu que si la variante est nettement plus fréquente (ca -> ça).
    private const double AccentUpgradeRatio = 20.0;
    private const double MaxAutoCorrectCost = 4.01;

    private readonly ILexicon _lexicon;
    private readonly UserModel? _user;
    private readonly CostModel _costs;
    private readonly Lazy<LexiconIndex> _index;

    public SpellCorrector(ILexicon lexicon, KeyboardLayout? layout = null, UserModel? user = null)
    {
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
        _user = user;
        _costs = new CostModel(layout);
        _index = new Lazy<LexiconIndex>(() => LexiconIndex.For(lexicon));
    }

    public ILexicon Lexicon => _lexicon;

    /// <summary>Construit les index de recherche à l'avance (sinon ils le sont au premier appel).</summary>
    public void Warmup() => _ = _index.Value;

    /// <summary>Vrai si le mot est au dictionnaire, appris par l'utilisateur, ou composé de parties connues (l'homme, peut-être).</summary>
    public bool IsKnown(string word)
    {
        var w = TextUtil.Normalize(word.Trim());
        if (w.Length == 0) return false;
        if (KnownDirect(w)) return true;
        int e = TextUtil.ElisionLength(w);
        if (e > 0 && KnownDirect(w[..e]) && IsKnown(w[e..])) return true;
        if (w.Contains('-') && !w.StartsWith('-') && !w.EndsWith('-'))
            return w.Split('-').All(p => p.Length > 0 && KnownDirect(p));
        return false;
    }

    private bool KnownDirect(string w) => _lexicon.Contains(w) || (_user?.IsKnown(w) ?? false);

    private sealed record Candidate(string Word, double Cost, double Score);

    private double EffectiveFrequency(string word, long lexFreq)
    {
        double f = lexFreq;
        int uc = _user?.GetCount(word) ?? 0;
        if (uc > 0 && (_user?.IsKnown(word) ?? false)) f += uc * (_lexicon.TotalCount / 100000.0 + 1);
        return f;
    }

    private static double Budget(int length) => length switch
    {
        <= 2 => 1.6,
        3 => 5.0,
        <= 5 => 8.0,
        _ => 8.5,
    };

    private List<Candidate> Search(string lower)
    {
        var best = new Dictionary<string, double>(StringComparer.Ordinal);   // mot -> coût minimal
        void Add(string w, double cost) { if (!best.TryGetValue(w, out var c) || cost < c) best[w] = cost; }

        var index = _index.Value;
        double budget = Budget(lower.Length);
        foreach (var (w, _, cost) in index.Search(lower, _costs, budget)) Add(w, cost);
        foreach (var e in index.AccentVariants(lower)) Add(e.Word, _costs.Distance(e.Word, lower));

        int el = TextUtil.ElisionLength(lower);
        if (el > 0)
        {
            var head = lower[..el];
            var tail = lower[el..];
            foreach (var (w, _, cost) in index.Search(tail, _costs, Budget(tail.Length))) Add(head + w, cost);
            foreach (var e in index.AccentVariants(tail)) Add(head + e.Word, _costs.Distance(e.Word, tail));
        }

        if (_user != null)
            foreach (var uw in _user.KnownWords())
            {
                if (Math.Abs(uw.Length - lower.Length) > 3) continue;
                double d = _costs.Distance(uw, lower);
                if (d <= budget) Add(uw, d);
            }

        var list = new List<Candidate>(best.Count);
        foreach (var (w, cost) in best)
        {
            long lf = _lexicon.GetFrequency(w);
            if (lf == 0 && el > 0 && w.Length > el) lf = _lexicon.GetFrequency(w[el..]);
            double f = EffectiveFrequency(w, lf);
            if (f <= 0) f = 1;
            list.Add(new Candidate(w, cost, Math.Log(f) - cost));
        }
        list.Sort((a, b) => b.Score.CompareTo(a.Score));
        return list;
    }

    /// <summary>Mots proposés pour <paramref name="word"/>, du plus probable au moins probable. Un mot connu est renvoyé en premier.</summary>
    public IReadOnlyList<string> Suggest(string word, int max = 3)
    {
        if (max <= 0 || string.IsNullOrWhiteSpace(word)) return Array.Empty<string>();
        var original = word.Trim();
        if (!TextUtil.IsWordLike(original)) return Array.Empty<string>();
        var lower = TextUtil.Normalize(original);

        var res = new List<string>(max);
        bool known = IsKnown(lower);
        if (known) res.Add(lower);
        foreach (var c in Search(lower))
        {
            if (res.Count >= max) break;
            // Pour un mot connu on ne propose que des variantes à une seule erreur plausible.
            if (known && c.Cost > MaxAutoCorrectCost) continue;
            if (!res.Contains(c.Word)) res.Add(c.Word);
        }
        return res.Select(w => TextUtil.ApplyCase(original, w)).ToList();
    }

    /// <summary>
    /// Correction à appliquer automatiquement, ou null si le mot est connu (hors accent manquant évident)
    /// ou si aucune correction n'est assez sûre.
    /// </summary>
    public string? AutoCorrect(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;
        var original = word.Trim();
        if (!TextUtil.IsWordLike(original)) return null;
        int letters = original.Count(char.IsLetter);
        if (letters < 2) return null;
        // Sigles (CAPITALES) et mots à majuscule interne (iPhone) : on n'y touche pas.
        if (letters > 1 && original.Where(char.IsLetter).All(char.IsUpper)) return null;
        if (original.Skip(1).Any(char.IsUpper)) return null;

        var lower = TextUtil.Normalize(original);
        if (IsKnown(lower)) return AccentUpgrade(original, lower);

        var cands = Search(lower);
        if (cands.Count == 0) return null;
        var top = cands[0];
        if (top.Cost > MaxAutoCorrectCost) return null;
        if (lower.Length <= 2 && top.Cost >= 1.0) return null;
        double margin = cands.Count > 1 ? top.Score - cands[1].Score : double.PositiveInfinity;
        if (margin < (lower.Length <= 3 ? 2.0 : 1.0)) return null;
        return TextUtil.ApplyCase(original, top.Word);
    }

    private string? AccentUpgrade(string original, string lower)
    {
        if (_user?.IsKnown(lower) ?? false) return null;
        long own = _lexicon.GetFrequency(lower);
        if (own == 0) return null;
        string? bestWord = null;
        long bestFreq = 0;
        foreach (var e in _index.Value.AccentVariants(lower))
            if (e.Word != lower && e.Frequency > bestFreq) { bestWord = e.Word; bestFreq = e.Frequency; }
        if (bestWord == null || bestFreq < own * AccentUpgradeRatio) return null;
        return TextUtil.ApplyCase(original, bestWord);
    }
}
