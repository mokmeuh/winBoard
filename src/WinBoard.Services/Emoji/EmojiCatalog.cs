using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace WinBoard.Services.Emoji;

/// <summary>Catalogue d'émojis chargé depuis la ressource embarquée.</summary>
public sealed class EmojiCatalog
{
    private static readonly Lazy<EmojiCatalog> Cache = new(LoadCore);

    private readonly Dictionary<string, string[][]> _index = new();

    public IReadOnlyList<EmojiGroup> Groups { get; }
    public IReadOnlyList<EmojiItem> All { get; }

    private EmojiCatalog(List<EmojiGroup> groups)
    {
        Groups = groups;
        All = groups.SelectMany(g => g.Items).ToList();
    }

    /// <summary>Charge le catalogue (mis en cache après le premier appel).</summary>
    public static EmojiCatalog Load() => Cache.Value;

    private static EmojiCatalog LoadCore()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinBoard.Services.Data.emoji.json")
            ?? throw new InvalidOperationException("Ressource emoji.json introuvable.");
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;

        var items = new Dictionary<string, List<EmojiItem>>();
        foreach (var e in root.GetProperty("emojis").EnumerateArray())
        {
            var n = e.GetProperty("n");
            var k = e.GetProperty("k");
            var item = new EmojiItem
            {
                Character = e.GetProperty("e").GetString()!,
                GroupId = e.GetProperty("g").GetString()!,
                NameEn = n[0].GetString()!,
                NameFr = n[1].GetString()!,
                KeywordsEn = k[0].EnumerateArray().Select(x => x.GetString()!).ToArray(),
                KeywordsFr = k[1].EnumerateArray().Select(x => x.GetString()!).ToArray(),
                SkinVariants = e.TryGetProperty("s", out var sk) ? sk.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>()
            };
            if (!items.TryGetValue(item.GroupId, out var list)) items[item.GroupId] = list = new();
            list.Add(item);
        }

        var groups = root.GetProperty("groups").EnumerateArray().Select(g =>
        {
            var id = g.GetProperty("id").GetString()!;
            return new EmojiGroup
            {
                Id = id,
                NameEn = g.GetProperty("en").GetString()!,
                NameFr = g.GetProperty("fr").GetString()!,
                Items = items.TryGetValue(id, out var l) ? l : new List<EmojiItem>()
            };
        }).ToList();
        return new EmojiCatalog(groups);
    }

    /// <summary>Normalise : minuscules, sans accents, ligatures dépliées.</summary>
    public static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Replace("œ", "oe").Replace("æ", "ae").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    private string[][] Index(string lang)
    {
        var key = EmojiItem.IsFrench(lang) ? "fr" : "en";
        lock (_index)
        {
            if (!_index.TryGetValue(key, out var idx))
            {
                // Pour chaque émoji : mots du nom puis mots-clés, normalisés.
                idx = All.Select(e => e.Name(key).Split(' ', ':', '-', ',', '\'', '’', '&')
                        .Concat(e.Keywords(key)).Select(Normalize).Where(w => w.Length > 0).ToArray()).ToArray();
                _index[key] = idx;
            }
            return idx;
        }
    }

    /// <summary>Recherche insensible aux accents et à la casse : préfixe d'un mot, puis sous-chaîne.</summary>
    public IReadOnlyList<EmojiItem> Search(string query, string lang, int max = 40)
    {
        var q = Normalize(query ?? "").Trim();
        if (q.Length == 0 || max <= 0) return Array.Empty<EmojiItem>();
        var idx = Index(lang);
        var prefix = new List<EmojiItem>();
        var substr = new List<EmojiItem>();
        for (int i = 0; i < All.Count; i++)
        {
            var words = idx[i];
            if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) prefix.Add(All[i]);
            else if (words.Any(w => w.Contains(q, StringComparison.Ordinal))) substr.Add(All[i]);
            if (prefix.Count >= max) break;
        }
        return prefix.Concat(substr).Take(max).ToList();
    }
}
