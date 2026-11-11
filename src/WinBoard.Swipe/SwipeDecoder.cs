using WinBoard.Core;

namespace WinBoard.Swipe;

/// <summary>Mot candidat pour un tracé ; un score plus élevé est meilleur.</summary>
public readonly record struct SwipeCandidate(string Word, double Score);

/// <summary>
/// Décodeur de saisie par glissement : compare le tracé aux gabarits idéaux des mots du lexique
/// (centres des touches) après rééchantillonnage, puis combine géométrie, fréquence et contexte.
/// </summary>
public sealed class SwipeDecoder
{
    private const int N = 32;                 // points de comparaison
    private const double Sigma = 0.45;        // écart-type géométrique (en touches)
    private const double FreqWeight = 0.12;   // poids du log de fréquence
    private const double StartEndRadius = 1.3;
    private const double MaxMeanDistance = 1.6;
    private const double SwipeMinLength = 0.6;

    private sealed class Template
    {
        public string Word = "";
        public double LogFreq;
        public double Length;
        public float[] Pts = Array.Empty<float>();
    }

    private readonly KeyboardLayout _layout;
    private readonly ILexicon _lexicon;
    private readonly KeyDef[] _letterKeys;
    private readonly object _lock = new();
    private Dictionary<(string, string), List<Template>>? _index;

    public SwipeDecoder(KeyboardLayout layout, ILexicon lexicon)
    {
        _layout = layout;
        _lexicon = lexicon;
        _letterKeys = layout.Keys
            .Where(k => k.Kind == KeyKind.Character && k.Text is { Length: 1 } t && char.IsLetter(t[0]))
            .ToArray();
    }

    /// <summary>Pré-calcule les gabarits (sinon fait au premier appel de <see cref="Decode"/>).</summary>
    public void Warmup() => GetIndex();

    /// <summary>Un tracé est un glissement s'il comporte au moins deux points et dépasse ~0,6 touche de longueur.</summary>
    public static bool IsSwipe(IReadOnlyList<PointD> path) => path.Count >= 2 && PathLength(path) > SwipeMinLength;

    public IReadOnlyList<SwipeCandidate> Decode(IReadOnlyList<PointD> path, int max = 5, Func<string, double>? contextBoost = null)
    {
        if (max <= 0 || !IsSwipe(path)) return Array.Empty<SwipeCandidate>();
        var index = GetIndex();

        var starts = KeysNear(path[0]);
        var ends = KeysNear(path[^1]);
        if (starts.Count == 0 || ends.Count == 0) return Array.Empty<SwipeCandidate>();

        double pathLen = PathLength(path);
        var sample = Resample(path, N);
        double sumLimit = MaxMeanDistance * N;
        double inv2s2 = 1.0 / (2 * Sigma * Sigma);

        var found = new List<SwipeCandidate>();
        foreach (var s in starts)
            foreach (var e in ends)
            {
                if (!index.TryGetValue((s.Id, e.Id), out var bucket)) continue;
                foreach (var t in bucket)
                {
                    // Longueur du tracé compatible avec celle du gabarit.
                    if (pathLen < 0.5 * t.Length - 0.5 || pathLen > 2.5 * t.Length + 1.0) continue;
                    double sum = 0;
                    var p = t.Pts;
                    for (int i = 0; i < N; i++)
                    {
                        double dx = sample[2 * i] - p[2 * i], dy = sample[2 * i + 1] - p[2 * i + 1];
                        sum += Math.Sqrt(dx * dx + dy * dy);
                        if (sum > sumLimit) break;
                    }
                    if (sum > sumLimit) continue;
                    double d = sum / N;
                    found.Add(new SwipeCandidate(t.Word, -d * d * inv2s2 + t.LogFreq));
                }
            }

        found.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (contextBoost != null)
        {
            int k = Math.Min(found.Count, Math.Max(max * 4, 20));
            for (int i = 0; i < k; i++)
            {
                double boost = contextBoost(found[i].Word);
                if (double.IsNaN(boost) || boost < 0) boost = 0;
                found[i] = found[i] with { Score = found[i].Score + Math.Log(Math.Max(boost, 1e-9)) };
            }
            found.Sort(0, k, Comparer<SwipeCandidate>.Create((a, b) => b.Score.CompareTo(a.Score)));
            found.RemoveRange(k, found.Count - k);
        }
        if (found.Count > max) found.RemoveRange(max, found.Count - max);
        return found;
    }

    private List<KeyDef> KeysNear(PointD p)
    {
        var list = new List<KeyDef>();
        foreach (var k in _letterKeys)
            if (k.Center.DistanceTo(p) <= StartEndRadius) list.Add(k);
        return list;
    }

    private Dictionary<(string, string), List<Template>> GetIndex()
    {
        if (_index != null) return _index;
        lock (_lock)
        {
            return _index ??= BuildIndex();
        }
    }

    private Dictionary<(string, string), List<Template>> BuildIndex()
    {
        var index = new Dictionary<(string, string), List<Template>>();
        double total = Math.Max(1, _lexicon.TotalCount);
        foreach (var entry in _lexicon.Words)
        {
            var keys = SwipePathBuilder.KeySequence(_layout, entry.Word.ToLowerInvariant(), strict: true);
            if (keys == null || keys.Count < 2) continue;

            var centers = new PointD[keys.Count];
            for (int i = 0; i < keys.Count; i++) centers[i] = keys[i].Center;
            var t = new Template
            {
                Word = entry.Word,
                LogFreq = FreqWeight * Math.Log(Math.Max(1, entry.Frequency) / total),
                Length = PathLength(centers),
            };
            var r = Resample(centers, N);
            t.Pts = new float[r.Length];
            for (int i = 0; i < r.Length; i++) t.Pts[i] = (float)r[i];

            var key = (keys[0].Id, keys[^1].Id);
            if (!index.TryGetValue(key, out var list)) index[key] = list = new List<Template>();
            list.Add(t);
        }
        return index;
    }

    private static double PathLength(IReadOnlyList<PointD> pts)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceTo(pts[i]);
        return len;
    }

    /// <summary>Rééchantillonne la polyligne en n points équidistants (suite x0,y0,x1,y1...).</summary>
    private static double[] Resample(IReadOnlyList<PointD> pts, int n)
    {
        var res = new double[2 * n];
        double total = PathLength(pts);
        if (total <= 0)
        {
            for (int i = 0; i < n; i++) { res[2 * i] = pts[0].X; res[2 * i + 1] = pts[0].Y; }
            return res;
        }
        double step = total / (n - 1);
        int seg = 1;
        double segStart = 0;
        double segLen = pts[0].DistanceTo(pts[1]);
        for (int i = 0; i < n; i++)
        {
            double target = i * step;
            while (seg < pts.Count - 1 && segStart + segLen < target)
            {
                segStart += segLen;
                seg++;
                segLen = pts[seg - 1].DistanceTo(pts[seg]);
            }
            double f = segLen > 0 ? Math.Clamp((target - segStart) / segLen, 0, 1) : 0;
            var a = pts[seg - 1];
            var b = pts[seg];
            res[2 * i] = a.X + (b.X - a.X) * f;
            res[2 * i + 1] = a.Y + (b.Y - a.Y) * f;
        }
        return res;
    }
}
