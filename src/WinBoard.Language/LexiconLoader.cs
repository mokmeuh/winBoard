using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>Charge (paresseusement, avec mise en cache) les données embarquées par langue.</summary>
public static class LexiconLoader
{
    private static readonly ConcurrentDictionary<string, Lazy<ILexicon>> Lexicons = new();
    private static readonly ConcurrentDictionary<string, Lazy<BigramModel>> Bigrams = new();

    /// <summary>Langues disponibles.</summary>
    public static IReadOnlyList<string> Supported { get; } = new[] { "fr", "en" };

    /// <summary>Ramène « fr-FR », « EN » ... au code à deux lettres ; lève une exception si la langue est absente.</summary>
    public static string NormalizeLanguage(string lang)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lang);
        var code = lang.Trim().ToLowerInvariant();
        int i = code.IndexOfAny(new[] { '-', '_' });
        if (i > 0) code = code[..i];
        if (!Supported.Contains(code))
            throw new NotSupportedException($"Langue non prise en charge : {lang}");
        return code;
    }

    public static ILexicon Load(string lang)
    {
        var code = NormalizeLanguage(lang);
        return Lexicons.GetOrAdd(code, c => new Lazy<ILexicon>(() => ReadLexicon(c))).Value;
    }

    /// <summary>Bigrammes embarqués de la langue (modèle vide si le fichier est absent).</summary>
    public static BigramModel LoadBigrams(string lang)
    {
        var code = NormalizeLanguage(lang);
        return Bigrams.GetOrAdd(code, c => new Lazy<BigramModel>(() => ReadBigrams(c))).Value;
    }

    private static Stream? Open(string file) =>
        typeof(LexiconLoader).Assembly.GetManifestResourceStream("WinBoard.Language.Data." + file);

    private static ILexicon ReadLexicon(string code)
    {
        using var stream = Open(code + ".txt")
            ?? throw new InvalidOperationException($"Ressource de dictionnaire introuvable pour « {code} ».");
        using var reader = new StreamReader(stream);
        var words = new List<WordEntry>(52000);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int sp = line.IndexOf(' ');
            if (sp <= 0) continue;
            if (long.TryParse(line.AsSpan(sp + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var f))
                words.Add(new WordEntry(line[..sp], f));
        }
        return new EmbeddedLexicon(code, words);
    }

    private static BigramModel ReadBigrams(string code)
    {
        using var stream = Open(code + ".bigrams.txt");
        if (stream == null) return BigramModel.Empty;
        using var reader = new StreamReader(stream);
        return BigramModel.Parse(reader);
    }
}
