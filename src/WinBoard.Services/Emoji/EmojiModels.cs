namespace WinBoard.Services.Emoji;

/// <summary>Un émoji avec ses libellés et variantes de teinte de peau.</summary>
public sealed class EmojiItem
{
    public string Character { get; init; } = "";
    public string GroupId { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string NameFr { get; init; } = "";
    public IReadOnlyList<string> KeywordsEn { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> KeywordsFr { get; init; } = Array.Empty<string>();
    /// <summary>Variantes de teinte de peau (vide si aucune).</summary>
    public IReadOnlyList<string> SkinVariants { get; init; } = Array.Empty<string>();

    public string Name(string lang) => IsFrench(lang) ? NameFr : NameEn;
    public IReadOnlyList<string> Keywords(string lang) => IsFrench(lang) ? KeywordsFr : KeywordsEn;

    internal static bool IsFrench(string lang) => lang?.StartsWith("fr", StringComparison.OrdinalIgnoreCase) == true;
    public override string ToString() => Character;
}

/// <summary>Groupe d'émojis (smileys, animaux, etc.).</summary>
public sealed class EmojiGroup
{
    public string Id { get; init; } = "";
    public string NameEn { get; init; } = "";
    public string NameFr { get; init; } = "";
    public IReadOnlyList<EmojiItem> Items { get; init; } = Array.Empty<EmojiItem>();
    public string Name(string lang) => EmojiItem.IsFrench(lang) ? NameFr : NameEn;
}
