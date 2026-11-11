using WinBoard.Core;

namespace WinBoard.Swipe;

/// <summary>Construction de tracés théoriques, utile aux tests et au débogage.</summary>
public static class SwipePathBuilder
{
    /// <summary>
    /// Suite des touches d'un mot : lettres doublées fusionnées, accents ramenés à la touche de base.
    /// Renvoie null si le mot contient un caractère qui n'est pas une lettre du clavier.
    /// </summary>
    internal static List<KeyDef>? KeySequence(KeyboardLayout layout, string word, bool strict)
    {
        var keys = new List<KeyDef>(word.Length);
        foreach (char c in word)
        {
            if (!char.IsLetter(c) || !layout.TryGetKey(c, out var key))
            {
                if (strict) return null;
                continue;
            }
            if (keys.Count == 0 || !ReferenceEquals(keys[^1], key)) keys.Add(key);
        }
        return keys;
    }

    /// <summary>Tracé passant par le centre de chaque touche du mot, avec des points intermédiaires réguliers.</summary>
    public static IReadOnlyList<PointD> IdealPath(KeyboardLayout layout, string word, int pointsPerSegment = 8)
    {
        var keys = KeySequence(layout, word.ToLowerInvariant(), strict: false)!;
        var path = new List<PointD>();
        if (keys.Count == 0) return path;
        pointsPerSegment = Math.Max(1, pointsPerSegment);
        path.Add(keys[0].Center);
        for (int i = 1; i < keys.Count; i++)
        {
            var a = keys[i - 1].Center;
            var b = keys[i].Center;
            for (int s = 1; s <= pointsPerSegment; s++)
            {
                double t = (double)s / pointsPerSegment;
                path.Add(new PointD(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            }
        }
        return path;
    }
}
