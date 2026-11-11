using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>
/// Suggestions contextuelles : mot suivant par bigrammes quand le préfixe est vide, sinon complétion
/// du préfixe re-classée par le contexte (interpolation bigramme/unigramme), avec repli sur la correction orthographique.
/// </summary>
public sealed class ContextPredictor
{
    private const int CandidatePool = 80;
    private const double BigramWeight = 0.6;
    private const double UserBigramShare = 0.5;

    private readonly ILexicon _lexicon;
    private readonly BigramModel _bigrams;
    private readonly UserModel? _user;
    private readonly SpellCorrector? _corrector;

    public ContextPredictor(ILexicon lexicon, BigramModel? bigrams = null, UserModel? user = null, SpellCorrector? corrector = null)
    {
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
        _bigrams = bigrams ?? BigramModel.Empty;
        _user = user;
        _corrector = corrector;
    }

    /// <summary>
    /// Propose jusqu'à <paramref name="max"/> mots. <paramref name="previousWords"/> contient les mots déjà saisis
    /// (le dernier sert de contexte). Les résultats reprennent la casse du préfixe.
    /// </summary>
    public IReadOnlyList<string> Predict(IReadOnlyList<string> previousWords, string currentPrefix, int max = 3)
    {
        if (max <= 0) return Array.Empty<string>();
        string? prev = previousWords is { Count: > 0 } ? TextUtil.Normalize(previousWords[^1]) : null;
        var original = currentPrefix ?? "";
        var prefix = TextUtil.Normalize(original);

        if (prefix.Length == 0) return NextWords(prev, max);

        var res = Complete(prev, prefix, max);

        // Élision : « l'ho » sans résultat entier -> compléter « ho » et conserver « l' ».
        int el = TextUtil.ElisionLength(prefix);
        if (res.Count == 0 && el > 0)
        {
            var head = prefix[..el];
            res = Complete(head, prefix[el..], max).Select(w => head + w).ToList();
        }

        // Le préfixe ne mène à aucun mot : repli sur les corrections orthographiques.
        if (res.Count == 0 && _corrector != null)
            res = _corrector.Suggest(prefix, max).Select(TextUtil.Normalize).ToList();

        return res.Distinct().Take(max).Select(w => TextUtil.ApplyCase(original, w)).ToList();
    }

    // Prédiction du mot suivant.
    private List<string> NextWords(string? prev, int max)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        if (prev != null)
        {
            var emb = _bigrams.GetNext(prev);
            long embTotal = _bigrams.Total(prev);
            var usr = _user?.GetNext(prev) ?? Array.Empty<(string, int)>();
            long usrTotal = usr.Sum(x => (long)x.Item2);
            double wu = usrTotal > 0 ? UserBigramShare : 0;
            if (embTotal == 0) wu = usrTotal > 0 ? 1 : 0;
            int n = 0;
            foreach (var (w, c) in emb)
            {
                if (n++ >= CandidatePool) break;
                scores[w] = (1 - wu) * c / embTotal;
            }
            foreach (var (w, c) in usr.Take(CandidatePool))
                scores[w] = scores.GetValueOrDefault(w) + wu * c / usrTotal;
        }
        var res = scores.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(max).ToList();

        // Complément par les mots les plus fréquents quand le contexte ne suffit pas.
        if (res.Count < max)
        {
            foreach (var w in TopUnigrams(max * 3))
            {
                if (res.Count >= max) break;
                if (!res.Contains(w)) res.Add(w);
            }
        }
        return res;
    }

    private IEnumerable<string> TopUnigrams(int n)
    {
        var user = _user?.WordsWithPrefix("", n).Select(x => x.Word) ?? Enumerable.Empty<string>();
        return user.Concat(_lexicon.WithPrefix("", n).Select(e => e.Word)).Distinct();
    }

    // Complétion d'un préfixe non vide.
    private List<string> Complete(string? prev, string prefix, int max)
    {
        var pool = new Dictionary<string, double>(StringComparer.Ordinal);    // mot -> probabilité unigramme
        foreach (var e in _lexicon.WithPrefix(prefix, CandidatePool))
            pool[e.Word] = (double)e.Frequency / Math.Max(1, _lexicon.TotalCount);

        long usrTotalWords = _user?.TotalWords ?? 0;
        double mu = usrTotalWords > 0 ? Math.Min(0.5, usrTotalWords / (usrTotalWords + 200.0)) : 0;
        if (_user != null)
            foreach (var (w, c) in _user.WordsWithPrefix(prefix, CandidatePool))
                if (!pool.ContainsKey(w)) pool[w] = 0;

        // Les suivants contextuels du préfixe sont ajoutés même s'ils sont rares dans le dictionnaire.
        IReadOnlyList<(string Word, long Count)> emb = Array.Empty<(string, long)>();
        IReadOnlyList<(string Word, int Count)> usr = Array.Empty<(string, int)>();
        long embTotal = 0, usrTotal = 0;
        if (prev != null)
        {
            emb = _bigrams.GetNext(prev);
            embTotal = _bigrams.Total(prev);
            usr = _user?.GetNext(prev) ?? Array.Empty<(string, int)>();
            usrTotal = usr.Sum(x => (long)x.Item2);
            foreach (var (w, _) in emb)
                if (w.StartsWith(prefix, StringComparison.Ordinal) && !pool.ContainsKey(w))
                    pool[w] = (double)_lexicon.GetFrequency(w) / Math.Max(1, _lexicon.TotalCount);
            foreach (var (w, _) in usr)
                if (w.StartsWith(prefix, StringComparison.Ordinal) && !pool.ContainsKey(w))
                    pool[w] = 0;
        }

        double wu = usrTotal > 0 ? UserBigramShare : 0;
        if (embTotal == 0 && usrTotal > 0) wu = 1;
        var embMap = emb.Count > 0 ? emb.ToDictionary(x => x.Word, x => x.Count) : null;
        var usrMap = usr.Count > 0 ? usr.ToDictionary(x => x.Word, x => x.Count) : null;

        var scored = new List<(string Word, double Score)>(pool.Count);
        foreach (var (w, pLex) in pool)
        {
            double pUni = pLex;
            if (_user != null && usrTotalWords > 0)
                pUni = (1 - mu) * pLex + mu * _user.GetCount(w) / (double)usrTotalWords;
            double pBig = 0;
            if (embMap != null && embTotal > 0 && embMap.TryGetValue(w, out var ce)) pBig += (1 - wu) * ce / embTotal;
            if (usrMap != null && usrTotal > 0 && usrMap.TryGetValue(w, out var cu)) pBig += wu * cu / usrTotal;
            double lambda = (embTotal > 0 || usrTotal > 0) ? BigramWeight : 0;
            scored.Add((w, lambda * pBig + (1 - lambda) * pUni));
        }
        return scored.OrderByDescending(x => x.Score).Take(max).Select(x => x.Word).ToList();
    }
}
