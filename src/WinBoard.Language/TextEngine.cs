using WinBoard.Core;

namespace WinBoard.Language;

/// <summary>Façade pour l'interface : dictionnaire, correction et prédiction d'une langue, avec le modèle utilisateur.</summary>
public sealed class TextEngine
{
    private TextEngine(string language, ILexicon lexicon, SpellCorrector corrector, ContextPredictor predictor, UserModel user)
    {
        Language = language;
        Lexicon = lexicon;
        Corrector = corrector;
        Predictor = predictor;
        User = user;
    }

    public string Language { get; }
    public ILexicon Lexicon { get; }
    public SpellCorrector Corrector { get; }
    public ContextPredictor Predictor { get; }
    public UserModel User { get; }

    /// <summary>Crée le moteur d'une langue ("fr", "en"). Sans modèle utilisateur, un modèle vide est utilisé.</summary>
    public static TextEngine Create(string lang, KeyboardLayout? layout, UserModel? user)
    {
        var code = LexiconLoader.NormalizeLanguage(lang);
        user ??= new UserModel();
        var lexicon = LexiconLoader.Load(code);
        var corrector = new SpellCorrector(lexicon, layout, user);
        var predictor = new ContextPredictor(lexicon, LexiconLoader.LoadBigrams(code), user, corrector);
        return new TextEngine(code, lexicon, corrector, predictor, user);
    }

    /// <summary>
    /// Suggestions pour le texte situé avant le curseur : le mot en cours devient le préfixe,
    /// les mots précédents de la même phrase servent de contexte.
    /// </summary>
    public IReadOnlyList<string> GetSuggestions(string textBeforeCursor, int max = 3)
    {
        if (max <= 0 || textBeforeCursor == null) return Array.Empty<string>();
        var (previous, prefix, valid) = Analyze(textBeforeCursor);
        if (!valid) return Array.Empty<string>();
        var res = Predictor.Predict(previous, prefix, max);
        // Début de phrase : le premier mot proposé prend une majuscule.
        if (prefix.Length == 0 && previous.Count == 0)
            return res.Select(w => TextUtil.ApplyCase("A", w)).ToList();
        return res;
    }

    /// <summary>Correction automatique du mot qui vient d'être terminé, ou null.</summary>
    public string? GetAutoCorrection(string word) => Corrector.AutoCorrect(word);

    /// <summary>Apprend un texte validé par l'utilisateur.</summary>
    public void Learn(string text) => User.Learn(text);

    private static (List<string> Previous, string Prefix, bool Valid) Analyze(string text)
    {
        int end = text.Length;
        int start = end;
        while (start > 0 && (TextUtil.IsWordChar(text[start - 1]) || char.IsDigit(text[start - 1]))) start--;
        var prefix = text[start..end].TrimStart('\'', '-', '’');
        if (prefix.Any(char.IsDigit)) return (new List<string>(), "", false);
        // Une fin de phrase collée au curseur ("fin.") ne donne pas de suggestion.
        if (prefix.Length == 0 && end > 0 && TextUtil.IsSentenceEnd(text[end - 1]) && text[end - 1] != '\n' && text[end - 1] != '\r')
            return (new List<string>(), "", false);

        // Mots précédents : on remonte jusqu'à la dernière fin de phrase.
        var before = text[..start];
        int cut = before.LastIndexOfAny(new[] { '.', '!', '?', '…', '\n', '\r' });
        if (cut >= 0) before = before[(cut + 1)..];
        var words = TextUtil.Tokenize(before);
        if (words.Count > 3) words = words.GetRange(words.Count - 3, 3);
        return (words, prefix.Replace('\u2019', '\''), true);
    }
}
