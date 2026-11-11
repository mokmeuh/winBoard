namespace WinBoard.Core;

public readonly record struct PointD(double X, double Y)
{
    public double DistanceTo(PointD o)
    {
        double dx = X - o.X, dy = Y - o.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public enum KeyKind
{
    Character, Shift, Backspace, Enter, Space, Symbols, Emoji, Voice, Language, Settings, Tab, Left, Right, Hide
}

/// <summary>
/// Touche du clavier. Les coordonnées sont exprimées en unités de touche :
/// une touche lettre standard mesure 1 x 1, l'origine est le coin supérieur gauche du clavier.
/// </summary>
public sealed record KeyDef(string Id, KeyKind Kind, double X, double Y, double Width, double Height,
    string? Text = null, string? ShiftText = null, IReadOnlyList<string>? LongPress = null)
{
    public PointD Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(PointD p) => p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
}

/// <summary>Disposition géométrique d'un clavier (indépendante du rendu).</summary>
public sealed class KeyboardLayout
{
    private readonly Dictionary<char, KeyDef> _byChar = new();

    public KeyboardLayout(string name, IReadOnlyList<KeyDef> keys)
    {
        Name = name;
        Keys = keys;
        Width = keys.Max(k => k.X + k.Width);
        Height = keys.Max(k => k.Y + k.Height);
        foreach (var k in keys)
            if (k.Kind == KeyKind.Character && k.Text is { Length: 1 } t)
                _byChar[char.ToLowerInvariant(t[0])] = k;
        // Les variantes accentuées sont rattachées à la touche de base.
        foreach (var k in keys)
            if (k.Kind == KeyKind.Character && k.LongPress != null)
                foreach (var alt in k.LongPress)
                    if (alt.Length == 1 && char.IsLetter(alt[0]))
                        _byChar.TryAdd(char.ToLowerInvariant(alt[0]), k);
    }

    public string Name { get; }
    public IReadOnlyList<KeyDef> Keys { get; }
    public double Width { get; }
    public double Height { get; }

    public bool TryGetKey(char c, out KeyDef key) => _byChar.TryGetValue(char.ToLowerInvariant(c), out key!);

    public bool TryGetCenter(char c, out PointD center)
    {
        if (TryGetKey(c, out var k)) { center = k.Center; return true; }
        center = default;
        return false;
    }

    public KeyDef? HitTest(PointD p) => Keys.FirstOrDefault(k => k.Contains(p));

    public KeyDef? NearestCharacterKey(PointD p) =>
        Keys.Where(k => k.Kind == KeyKind.Character).MinBy(k => k.Center.DistanceTo(p));

    /// <summary>Lettres dont la touche est à moins de <paramref name="radius"/> unités.</summary>
    public IEnumerable<char> Neighbors(char c, double radius = 1.5)
    {
        if (!TryGetKey(c, out var key)) yield break;
        foreach (var k in Keys)
            if (k.Kind == KeyKind.Character && !ReferenceEquals(k, key) && k.Text is { Length: 1 } t
                && char.IsLetter(t[0]) && k.Center.DistanceTo(key.Center) <= radius)
                yield return char.ToLowerInvariant(t[0]);
    }

    public static KeyboardLayout Qwerty() => Build("QWERTY", new[] { "qwertyuiop", "asdfghjkl", "zxcvbnm" }, new[] { 0.0, 0.5, 1.5 });
    public static KeyboardLayout Azerty() => Build("AZERTY", new[] { "azertyuiop", "qsdfghjklm", "wxcvbn" }, new[] { 0.0, 0.0, 2.0 });

    public static KeyboardLayout ByName(string name) =>
        string.Equals(name, "AZERTY", StringComparison.OrdinalIgnoreCase) ? Azerty() : Qwerty();

    private static readonly Dictionary<char, string[]> Accents = new()
    {
        ['a'] = new[] { "à", "â", "æ", "á", "ä" }, ['c'] = new[] { "ç" },
        ['e'] = new[] { "é", "è", "ê", "ë" }, ['i'] = new[] { "î", "ï", "í" },
        ['o'] = new[] { "ô", "œ", "ö", "ó" }, ['u'] = new[] { "ù", "û", "ü", "ú" },
        ['y'] = new[] { "ÿ" }, ['n'] = new[] { "ñ" },
    };

    private static KeyboardLayout Build(string name, string[] rows, double[] offsets)
    {
        var keys = new List<KeyDef>();
        const string digits = "1234567890";
        for (int i = 0; i < digits.Length; i++)
            keys.Add(new KeyDef("d" + digits[i], KeyKind.Character, i, 0, 1, 1, digits[i].ToString(), digits[i].ToString()));
        for (int r = 0; r < rows.Length; r++)
            for (int i = 0; i < rows[r].Length; i++)
            {
                char c = rows[r][i];
                keys.Add(new KeyDef(c.ToString(), KeyKind.Character, offsets[r] + i, r + 1, 1, 1,
                    c.ToString(), char.ToUpperInvariant(c).ToString(),
                    Accents.TryGetValue(c, out var alts) ? alts : null));
            }
        double lettersEnd = offsets[2] + rows[2].Length;
        keys.Add(new KeyDef("shift", KeyKind.Shift, 0, 3, offsets[2], 1, "⇧"));
        keys.Add(new KeyDef("backspace", KeyKind.Backspace, lettersEnd, 3, 10 - lettersEnd, 1, "⌫"));
        keys.Add(new KeyDef("symbols", KeyKind.Symbols, 0, 4, 1.5, 1, "?123"));
        keys.Add(new KeyDef("emoji", KeyKind.Emoji, 1.5, 4, 1, 1, "☺"));
        keys.Add(new KeyDef("comma", KeyKind.Character, 2.5, 4, 1, 1, ",", ";"));
        keys.Add(new KeyDef("space", KeyKind.Space, 3.5, 4, 3, 1, " "));
        keys.Add(new KeyDef("period", KeyKind.Character, 6.5, 4, 1, 1, ".", ":", new[] { "?", "!", "'", "-", "…" }));
        keys.Add(new KeyDef("voice", KeyKind.Voice, 7.5, 4, 1, 1, "🎤"));
        keys.Add(new KeyDef("enter", KeyKind.Enter, 8.5, 4, 1.5, 1, "⏎"));
        return new KeyboardLayout(name, keys);
    }
}
