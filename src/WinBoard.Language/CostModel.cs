using System.Collections.Concurrent;
using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>
/// Coûts d'erreur de saisie (distance de Damerau-Levenshtein pondérée). Le coût sert d'équivalent
/// à un logarithme de probabilité : une erreur ordinaire coûte environ 4, un accent oublié presque rien,
/// une touche voisine remplacée nettement moins qu'une touche éloignée.
/// </summary>
internal sealed class CostModel
{
    public const double Accent = 0.3;
    public const double AccentReverse = 0.8;
    public const double Neighbor = 1.5;
    public const double Substitution = 5.0;
    public const double Transposition = 2.5;
    public const double Edit = 4.0;
    public const double DoubledLetter = 2.5;
    public const double Punctuation = 1.0;
    public const double FirstLetterExtra = 1.5;

    private readonly KeyboardLayout? _layout;
    private readonly ConcurrentDictionary<char, HashSet<char>> _neighbors = new();

    public CostModel(KeyboardLayout? layout) => _layout = layout;

    private HashSet<char> NeighborsOf(char c) => _neighbors.GetOrAdd(c, ch =>
    {
        var set = new HashSet<char>();
        if (_layout != null)
            foreach (var n in _layout.Neighbors(ch)) set.Add(TextUtil.FoldChar(n));
        return set;
    });

    /// <summary>Coût pour que le mot voulu <paramref name="intended"/> soit saisi <paramref name="typed"/> à la même position.</summary>
    public double Sub(char intended, char typed, bool firstLetter)
    {
        if (intended == typed) return 0;
        char fi = TextUtil.FoldChar(intended), ft = TextUtil.FoldChar(typed);
        if (fi == ft) return typed == ft ? Accent : AccentReverse;
        double c = NeighborsOf(fi).Contains(ft) ? Neighbor : Substitution;
        return firstLetter ? c + FirstLetterExtra : c;
    }

    /// <summary>Coût d'une lettre voulue mais omise à la saisie.</summary>
    public double Omitted(char intended, char previousIntended)
    {
        if (intended == '\'' || intended == '-') return Punctuation;
        return intended == previousIntended ? DoubledLetter : Edit;
    }

    /// <summary>Coût d'une lettre saisie en trop.</summary>
    public double Extra(char typed, char previousTyped) => typed == previousTyped ? DoubledLetter : Edit;

    /// <summary>Distance pondérée entre un mot voulu et un mot saisi.</summary>
    public double Distance(string intended, string typed)
    {
        int n = intended.Length, m = typed.Length;
        var d = new double[n + 1, m + 1];
        for (int j = 1; j <= m; j++) d[0, j] = d[0, j - 1] + Extra(typed[j - 1], j > 1 ? typed[j - 2] : '\0');
        for (int i = 1; i <= n; i++)
        {
            char c = intended[i - 1];
            d[i, 0] = d[i - 1, 0] + Omitted(c, i > 1 ? intended[i - 2] : '\0');
            for (int j = 1; j <= m; j++)
            {
                char t = typed[j - 1];
                double best = d[i - 1, j - 1] + Sub(c, t, i == 1);
                best = Math.Min(best, d[i - 1, j] + Omitted(c, i > 1 ? intended[i - 2] : '\0'));
                best = Math.Min(best, d[i, j - 1] + Extra(t, j > 1 ? typed[j - 2] : '\0'));
                if (i > 1 && j > 1 && c == typed[j - 2] && intended[i - 2] == t)
                    best = Math.Min(best, d[i - 2, j - 2] + Transposition);
                d[i, j] = best;
            }
        }
        return d[n, m];
    }
}
