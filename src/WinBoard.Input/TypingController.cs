using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinBoard.Input;

/// <summary>
/// Machine à états de la saisie : reçoit les actions de l'utilisateur et pilote un
/// <see cref="ITextSink"/>. Comme le texte des autres applications n'est pas lisible,
/// le contrôleur tient son propre tampon du texte récemment envoyé.
/// Cette classe n'est PAS thread-safe : elle doit être appelée depuis le fil d'interface.
/// </summary>
public sealed class TypingController
{
    private const int MaxContextLength = 200;

    private static readonly Regex UrlLike = new(
        @"^www\.|[@/\\]|[\p{L}\p{N}]\.[\p{L}\p{N}]",
        RegexOptions.CultureInvariant);

    private readonly ITextSink _sink;
    private readonly ISuggestionEngine _engine;

    private string _buffer = "";
    private IReadOnlyList<string> _suggestions = Array.Empty<string>();

    // Le tampon se termine par une espace ajoutée automatiquement (swipe, suggestion).
    private bool _autoSpace;
    // Dernière autocorrection, annulable par Backspace tant que rien d'autre n'a été fait.
    private AutoCorrection? _lastAuto;
    // Dernier swipe, remplaçable par une suggestion ou effaçable en un Backspace.
    private SwipeRecord? _lastSwipe;
    // Mot d'origine rétabli par l'utilisateur : ne plus le corriger au prochain séparateur.
    private string? _noCorrectWord;

    private sealed record AutoCorrection(string Original, string Corrected, string Separator);

    private sealed record SwipeRecord(string Inserted, string Word, bool Capitalized);

    public TypingController(ITextSink sink, ISuggestionEngine engine, TypingOptions options)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _suggestions = ComputeSuggestions();
    }

    public TypingOptions Options { get; }

    /// <summary>Levé après toute action modifiant les suggestions ou l'état de majuscule.</summary>
    public event Action? StateChanged;

    public IReadOnlyList<string> Suggestions => _suggestions;

    /// <summary>Début de texte, après « . ! ? » suivi d'une espace, ou après Entrée.</summary>
    public bool ShouldCapitalizeNext => Options.AutoCapitalize && CapitalizeAt(_buffer);

    /// <summary>Tampon du texte avant le curseur (borné à environ 200 caractères).</summary>
    public string Context => _buffer;

    /// <summary>Mot en cours de frappe (éventuellement vide).</summary>
    public string CurrentWord
    {
        get
        {
            int i = _buffer.Length;
            while (i > 0 && IsWordChar(_buffer[i - 1])) i--;
            while (i < _buffer.Length && IsApostrophe(_buffer[i])) i++;
            return _buffer[i..];
        }
    }

    // ---------------------------------------------------------------- Actions

    /// <summary>Touche caractère déjà mise en casse par l'interface.</summary>
    public void TypeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var run = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var el = (string)elements.Current;
            if (el is "\n" or "\r\n" or "\r")
            {
                FlushRun(run);
                EnterCore();
            }
            else if (el == "\t")
            {
                FlushRun(run);
                MoveKeyCore(SpecialKey.Tab);
            }
            else if (el.Length == 1 && IsSeparator(el[0]))
            {
                FlushRun(run);
                Separator(el[0]);
            }
            else
            {
                run.Append(el);
            }
        }
        FlushRun(run);
        Finish();
    }

    public void Backspace()
    {
        BackspaceCore();
        Finish();
    }

    public void Enter()
    {
        EnterCore();
        Finish();
    }

    /// <summary>Tab, Left et Right envoient la touche et réinitialisent le contexte.</summary>
    public void Key(SpecialKey key)
    {
        switch (key)
        {
            case SpecialKey.Backspace:
                BackspaceCore();
                break;
            case SpecialKey.Enter:
                EnterCore();
                break;
            default:
                MoveKeyCore(key);
                break;
        }
        Finish();
    }

    public void CommitSwipe(IReadOnlyList<string> candidates)
    {
        if (candidates == null) return;
        var list = candidates.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (list.Count == 0) return;

        // Un mot en cours est validé comme si l'utilisateur avait tapé une espace.
        CommitWord();
        ClearTransient();

        string prefix = NeedsSpaceBefore(_buffer) ? " " : "";
        // La majuscule est évaluée comme si l'espace de séparation était déjà posée.
        bool capitalize = Options.AutoCapitalize && CapitalizeAt(_buffer + prefix);
        string word = capitalize ? Capitalize(list[0]) : list[0];
        string inserted = prefix + word + " ";

        _sink.SendText(inserted);
        Append(inserted);
        _autoSpace = true;
        _lastSwipe = new SwipeRecord(inserted, word, capitalize);

        var others = list.Skip(1)
            .Where(c => !string.Equals(c, list[0], StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Take(Math.Max(0, Options.MaxSuggestions))
            .ToList();
        Finish(others);
    }

    public void PickSuggestion(int index)
    {
        if (index < 0 || index >= _suggestions.Count) return;
        string suggestion = _suggestions[index];
        if (string.IsNullOrEmpty(suggestion)) return;

        string inserted;
        if (_lastSwipe is { } swipe)
        {
            // Remplace le mot glissé et son espace automatique.
            string old = swipe.Word + " ";
            inserted = (swipe.Capitalized ? Capitalize(suggestion) : suggestion);
            _sink.SendBackspace(Graphemes(old));
            _sink.SendText(inserted + " ");
            RemoveTail(old.Length);
            Append(inserted + " ");
        }
        else
        {
            string word = CurrentWord;
            if (word.Length > 0)
            {
                // Pour « l'ho », une suggestion sans apostrophe ne remplace que « ho ».
                string replaced = word;
                int apostrophe = Math.Max(word.LastIndexOf('\''), word.LastIndexOf('’'));
                if (apostrophe >= 0 && suggestion.IndexOfAny(['\'', '’']) < 0)
                    replaced = word[(apostrophe + 1)..];

                string before = _buffer[..^replaced.Length];
                inserted = ApplyCase(replaced, suggestion);
                if (Options.AutoCapitalize && CapitalizeAt(before)) inserted = Capitalize(inserted);

                if (inserted == replaced)
                {
                    _sink.SendText(" ");
                    Append(" ");
                }
                else
                {
                    if (replaced.Length > 0) _sink.SendBackspace(Graphemes(replaced));
                    _sink.SendText(inserted + " ");
                    RemoveTail(replaced.Length);
                    Append(inserted + " ");
                }
            }
            else
            {
                // Prédiction du mot suivant.
                string prefix = NeedsSpaceBefore(_buffer) ? " " : "";
                inserted = suggestion;
                if (Options.AutoCapitalize && CapitalizeAt(_buffer + prefix)) inserted = Capitalize(inserted);
                _sink.SendText(prefix + inserted + " ");
                Append(prefix + inserted + " ");
            }
        }

        if (Options.LearnFromTyping) _engine.Learn(inserted);
        ClearTransient();
        _noCorrectWord = null;
        _autoSpace = true;
        Finish();
    }

    /// <summary>Insère le texte tel quel (émoji, dictée) : aucune correction.</summary>
    public void InsertRaw(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ClearTransient();
        _noCorrectWord = null;
        _sink.SendText(text);
        Append(text);
        Finish();
    }

    /// <summary>Changement de fenêtre ou de focus : le tampon est oublié.</summary>
    public void ResetContext()
    {
        ResetBuffer();
        Finish();
    }

    // ------------------------------------------------------------ Mécanique

    private void FlushRun(StringBuilder run)
    {
        if (run.Length == 0) return;
        string text = run.ToString();
        run.Clear();

        // « exemple.com » : si une lettre suit directement un point qui avait déclenché
        // une correction, il s'agit d'une adresse : on rétablit le mot d'origine.
        if (_lastAuto is { Separator: "." } auto && char.IsLetterOrDigit(text[0]))
        {
            _sink.SendBackspace(Graphemes(auto.Corrected) + 1);
            _sink.SendText(auto.Original + ".");
            RemoveTail(auto.Corrected.Length + 1);
            Append(auto.Original + ".");
        }

        ClearTransient();
        _sink.SendText(text);
        Append(text);
    }

    private void Separator(char c)
    {
        string sep = c.ToString();
        bool wasAutoSpace = _autoSpace;

        if (c == ' ')
        {
            if (Options.DoubleSpacePeriod && !wasAutoSpace && EndsWithSpaceAfterLetterOrDigit())
            {
                ClearTransient();
                _noCorrectWord = null;
                _sink.SendBackspace(1);
                _sink.SendText(". ");
                RemoveTail(1);
                Append(". ");
                return;
            }
        }
        else if (wasAutoSpace && _buffer.EndsWith(' '))
        {
            // « mot␣ » + « . » -> « mot. »
            ClearTransient();
            _noCorrectWord = null;
            _sink.SendBackspace(1);
            RemoveTail(1);
            _sink.SendText(sep);
            Append(sep);
            return;
        }

        var correction = CommitWord();
        ClearTransient();
        _sink.SendText(sep);
        Append(sep);
        if (correction is { } c2) _lastAuto = new AutoCorrection(c2.Original, c2.Corrected, sep);
    }

    /// <summary>Valide le mot en cours : autocorrection éventuelle puis apprentissage.</summary>
    private (string Original, string Corrected)? CommitWord()
    {
        string word = CurrentWord;
        string? noCorrect = _noCorrectWord;
        _noCorrectWord = null;
        if (word.Length == 0) return null;

        bool eligible = IsEligible();
        string final = word;
        (string, string)? result = null;

        if (eligible && Options.AutoCorrect && !string.Equals(word, noCorrect, StringComparison.Ordinal))
        {
            string? fix = _engine.GetAutoCorrection(word);
            if (!string.IsNullOrEmpty(fix))
            {
                string cased = ApplyCase(word, fix);
                if (!string.Equals(cased, word, StringComparison.Ordinal))
                {
                    _sink.SendBackspace(Graphemes(word));
                    _sink.SendText(cased);
                    RemoveTail(word.Length);
                    Append(cased);
                    final = cased;
                    result = (word, cased);
                }
            }
        }

        if (eligible && Options.LearnFromTyping) _engine.Learn(final);
        return result;
    }

    private void BackspaceCore()
    {
        if (_lastSwipe is { } swipe)
        {
            _sink.SendBackspace(Graphemes(swipe.Inserted));
            RemoveTail(swipe.Inserted.Length);
            ClearTransient();
            _noCorrectWord = null;
            return;
        }

        if (_lastAuto is { } auto)
        {
            // Annulation : on rétablit le mot tapé, sans le séparateur.
            _sink.SendBackspace(Graphemes(auto.Corrected) + Graphemes(auto.Separator));
            _sink.SendText(auto.Original);
            RemoveTail(auto.Corrected.Length + auto.Separator.Length);
            Append(auto.Original);
            ClearTransient();
            _noCorrectWord = auto.Original;
            return;
        }

        ClearTransient();
        _noCorrectWord = null;
        _sink.SendBackspace(1);
        if (_buffer.Length > 0)
        {
            var starts = StringInfo.ParseCombiningCharacters(_buffer);
            _buffer = _buffer[..starts[^1]];
        }
    }

    private void EnterCore()
    {
        CommitWord();
        ClearTransient();
        _noCorrectWord = null;
        _sink.SendKey(SpecialKey.Enter);
        Append("\n");
    }

    private void MoveKeyCore(SpecialKey key)
    {
        _sink.SendKey(key);
        ResetBuffer();
    }

    private void ResetBuffer()
    {
        _buffer = "";
        ClearTransient();
        _noCorrectWord = null;
    }

    private void ClearTransient()
    {
        _autoSpace = false;
        _lastAuto = null;
        _lastSwipe = null;
    }

    private void Finish(IReadOnlyList<string>? explicitSuggestions = null)
    {
        if (!Options.ShowSuggestions) _suggestions = Array.Empty<string>();
        else _suggestions = explicitSuggestions ?? ComputeSuggestions();
        StateChanged?.Invoke();
    }

    private IReadOnlyList<string> ComputeSuggestions()
    {
        int max = Options.MaxSuggestions;
        if (!Options.ShowSuggestions || max <= 0) return Array.Empty<string>();
        var result = _engine.GetSuggestions(_buffer, max);
        return result == null ? Array.Empty<string>() : result.Take(max).ToArray();
    }

    // ------------------------------------------------------------- Tampon

    private void Append(string text)
    {
        _buffer += text;
        if (_buffer.Length > MaxContextLength)
        {
            int excess = _buffer.Length - MaxContextLength;
            var starts = StringInfo.ParseCombiningCharacters(_buffer);
            int cut = starts.FirstOrDefault(s => s >= excess, _buffer.Length);
            _buffer = _buffer[cut..];
        }
    }

    private void RemoveTail(int chars)
        => _buffer = chars >= _buffer.Length ? "" : _buffer[..^chars];

    // ------------------------------------------------------------ Utilitaires

    private static bool IsSeparator(char c) => c is ' ' or '.' or ',' or ';' or ':' or '!' or '?';

    private static bool IsApostrophe(char c) => c is '\'' or '’';

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || IsApostrophe(c) || c == '_';

    private static int Graphemes(string s)
        => s.Length == 0 ? 0 : new StringInfo(s).LengthInTextElements;

    private bool EndsWithSpaceAfterLetterOrDigit()
        => _buffer.Length >= 2 && _buffer[^1] == ' ' && char.IsLetterOrDigit(_buffer[^2]);

    /// <summary>Faux si le mot ressemble à une adresse, un courriel ou contient un chiffre.</summary>
    private bool IsEligible()
    {
        int i = _buffer.Length;
        while (i > 0 && !char.IsWhiteSpace(_buffer[i - 1])) i--;
        string token = _buffer[i..];
        if (token.Contains("://", StringComparison.Ordinal)) return false;
        if (token.Any(char.IsDigit)) return false;
        return !UrlLike.IsMatch(token);
    }

    private static bool CapitalizeAt(string text)
    {
        if (text.Length == 0) return true;
        int i = text.Length - 1;
        if (text[i] == '\n') return true;
        if (text[i] != ' ') return false;
        while (i >= 0 && text[i] == ' ') i--;
        if (i < 0) return true;
        return text[i] is '.' or '!' or '?' or '…' or '\n';
    }

    private static bool NeedsSpaceBefore(string text)
    {
        if (text.Length == 0) return false;
        char last = text[^1];
        return !char.IsWhiteSpace(last) && last is not ('(' or '[' or '{' or '-' or '\'' or '’');
    }

    private static string Capitalize(string s)
    {
        if (s.Length == 0) return s;
        int len = StringInfo.GetNextTextElementLength(s);
        return s[..len].ToUpperInvariant() + s[len..];
    }

    /// <summary>Reporte sur le remplacement la casse du mot d'origine.</summary>
    private static string ApplyCase(string original, string replacement)
    {
        if (original.Length == 0 || replacement.Length == 0) return replacement;
        if (replacement.Any(char.IsUpper)) return replacement;

        var letters = original.Where(char.IsLetter).ToArray();
        if (letters.Length > 1 && letters.All(char.IsUpper)) return replacement.ToUpperInvariant();
        if (char.IsUpper(original[0])) return Capitalize(replacement);
        return replacement;
    }
}
