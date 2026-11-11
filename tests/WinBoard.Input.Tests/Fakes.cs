using System.Globalization;
using System.Text;
using WinBoard.Input;

namespace WinBoard.Input.Tests;

/// <summary>Puits de texte qui reconstitue le texte résultant.</summary>
internal sealed class FakeSink : ITextSink
{
    private string _text = "";
    public string Text => _text;
    public List<SpecialKey> Keys { get; } = new();
    public List<int> Backspaces { get; } = new();

    public void SendText(string text) => _text += text;

    public void SendBackspace(int count)
    {
        Backspaces.Add(count);
        for (int i = 0; i < count && _text.Length > 0; i++)
        {
            var starts = StringInfo.ParseCombiningCharacters(_text);
            _text = _text[..starts[^1]];
        }
    }

    public void SendKey(SpecialKey key)
    {
        Keys.Add(key);
        if (key == SpecialKey.Enter) _text += "\n";
        else if (key == SpecialKey.Tab) _text += "\t";
    }
}

/// <summary>Moteur à dictionnaires codés en dur.</summary>
internal sealed class FakeEngine : ISuggestionEngine
{
    public Dictionary<string, string> Corrections { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bonjuor"] = "bonjour",
        ["exmple"] = "exemple",
        ["teh"] = "the",
    };

    // Clé : fin du texte en minuscules (« bonj », « bonjour »).
    public Dictionary<string, string[]> Suggestions { get; } = new()
    {
        ["bonj"] = new[] { "bonjour", "bonne", "bon" },
        ["bonjour "] = new[] { "le", "tout", "à" },
        [""] = new[] { "je", "le", "la" },
    };

    public List<string> Learned { get; } = new();

    public IReadOnlyList<string> GetSuggestions(string textBeforeCursor, int max)
    {
        string key = textBeforeCursor.ToLowerInvariant();
        foreach (var k in Suggestions.Keys.OrderByDescending(k => k.Length))
            if (k.Length > 0 && key.EndsWith(k, StringComparison.Ordinal))
                return Suggestions[k].Take(max).ToArray();
        return key.Length == 0 ? Suggestions[""].Take(max).ToArray() : Array.Empty<string>();
    }

    public string? GetAutoCorrection(string word)
        => Corrections.TryGetValue(word, out var c) ? c : null;

    public void Learn(string text) => Learned.Add(text);
}

internal sealed class Harness
{
    public FakeSink Sink { get; } = new();
    public FakeEngine Engine { get; } = new();
    public TypingOptions Options { get; } = new();
    public TypingController Controller { get; }

    public Harness() => Controller = new TypingController(Sink, Engine, Options);

    public Harness Type(string text)
    {
        // Une touche à la fois, comme l'interface.
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) Controller.TypeText((string)e.Current);
        return this;
    }

    public string Text => Sink.Text;
}
