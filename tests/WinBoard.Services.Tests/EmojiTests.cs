using WinBoard.Services.Emoji;

namespace WinBoard.Services.Tests;

public class EmojiTests
{
    [Fact]
    public void Load_HasManyEmojis_AndNonEmptyGroups()
    {
        var c = EmojiCatalog.Load();
        Assert.Same(c, EmojiCatalog.Load());
        Assert.True(c.All.Count > 1500);
        Assert.NotEmpty(c.Groups);
        Assert.All(c.Groups, g => Assert.NotEmpty(g.Items));
        Assert.Contains(c.All, e => e.SkinVariants.Count == 5);
    }

    [Fact]
    public void Search_Heart_English()
    {
        var r = EmojiCatalog.Load().Search("heart", "en");
        Assert.NotEmpty(r);
        Assert.Contains(r, e => e.Character == "❤️");
        Assert.True(r.Count <= 40);
    }

    [Theory]
    [InlineData("coeur")]
    [InlineData("CŒUR")]
    [InlineData("cœur")]
    public void Search_Coeur_French_AccentInsensitive(string q)
    {
        var r = EmojiCatalog.Load().Search(q, "fr");
        Assert.Contains(r, e => e.Character == "❤️");
    }

    [Fact]
    public void Search_Rire_French()
    {
        var r = EmojiCatalog.Load().Search("rire", "fr");
        Assert.Contains(r, e => e.Character == "😂");
    }

    [Fact]
    public void Search_PrefixBeforeSubstring_AndMax()
    {
        var c = EmojiCatalog.Load();
        var r = c.Search("hea", "en", 5);
        Assert.Equal(5, r.Count);
        Assert.Empty(c.Search("  ", "en"));
        Assert.Empty(c.Search("zzzzqqqq", "en"));
    }

    [Fact]
    public void Recents_OrderMaxAndPersistence()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var r = new RecentEmojis(path, 3);
            r.Add("😀"); r.Add("😂"); r.Add("❤️"); r.Add("😀"); r.Add("👍");
            Assert.Equal(new[] { "👍", "😀", "❤️" }, r.Items);
            var r2 = new RecentEmojis(path, 3);
            Assert.Equal(r.Items, r2.Items);
            Assert.Equal(40, new RecentEmojis().Max);
        }
        finally { File.Delete(path); }
    }
}
