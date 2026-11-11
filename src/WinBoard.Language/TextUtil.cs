using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace WinBoard.Language;

/// <summary>Utilitaires de texte partagés (normalisation, casse, découpage en mots).</summary>
internal static class TextUtil
{
    private static readonly ConcurrentDictionary<char, char> FoldCache = new();

    /// <summary>Normalise l'apostrophe typographique et passe en minuscules.</summary>
    public static string Normalize(string word) =>
        word.Replace('’', '\'').Replace('ʼ', '\'').ToLowerInvariant();

    /// <summary>Retire l'accent d'un caractère (é -> e). Les ligatures sont ramenées à leur première lettre.</summary>
    public static char FoldChar(char c)
    {
        if (c < 128) return c;
        return FoldCache.GetOrAdd(c, static ch =>
        {
            if (ch == 'œ' || ch == 'Œ') return 'o';
            if (ch == 'æ' || ch == 'Æ') return 'a';
            var d = ch.ToString().Normalize(NormalizationForm.FormD);
            return d.Length > 0 ? d[0] : ch;
        });
    }

    /// <summary>Retire les accents d'un mot ; « œ » devient « oe » et « æ » « ae ».</summary>
    public static string Fold(string word)
    {
        bool plain = true;
        foreach (var c in word) if (c >= 128) { plain = false; break; }
        if (plain) return word;
        var sb = new StringBuilder(word.Length + 1);
        foreach (var c in word)
        {
            if (c == 'œ') sb.Append("oe");
            else if (c == 'æ') sb.Append("ae");
            else sb.Append(FoldChar(c));
        }
        return sb.ToString();
    }

    public static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '-' || c == '’';

    /// <summary>Vrai si la chaîne contient au moins une lettre et uniquement lettres, apostrophes ou traits d'union.</summary>
    public static bool IsWordLike(string s)
    {
        bool letter = false;
        foreach (var c in s)
        {
            if (char.IsLetter(c)) letter = true;
            else if (c != '\'' && c != '-' && c != '’') return false;
        }
        return letter;
    }

    /// <summary>Applique à <paramref name="result"/> la casse du mot saisi (MAJUSCULES, Capitale initiale ou minuscules).</summary>
    public static string ApplyCase(string original, string result)
    {
        if (original.Length == 0 || result.Length == 0) return result;
        int letters = 0, upper = 0;
        foreach (var c in original)
            if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
        if (letters > 1 && upper == letters) return result.ToUpperInvariant();
        if (char.IsUpper(original[0])) return char.ToUpperInvariant(result[0]) + result[1..];
        return result;
    }

    /// <summary>Longueur de l'élision initiale (« l' », « qu' »...) ou 0 s'il n'y en a pas.</summary>
    public static int ElisionLength(string word)
    {
        int i = word.IndexOf('\'');
        return i is > 0 and <= 6 && i < word.Length - 1 ? i + 1 : 0;
    }

    /// <summary>Découpe un texte en mots (lettres, apostrophes et traits d'union internes), en minuscules.</summary>
    public static List<string> Tokenize(string text)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            var w = sb.ToString().Trim('\'', '-');
            if (w.Length > 0 && IsWordLike(w)) res.Add(Normalize(w));
            sb.Clear();
        }
        foreach (var c in text)
        {
            if (IsWordChar(c)) sb.Append(c);
            else Flush();
        }
        Flush();
        return res;
    }

    public static bool IsSentenceEnd(char c) => c is '.' or '!' or '?' or '…' or '\n' or '\r';
}
