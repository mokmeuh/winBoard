using System.Diagnostics;
using WinBoard.Core;
using Xunit;

namespace WinBoard.Swipe.Tests;

public class SwipeDecoderTests
{
    private static readonly InMemoryLexicon Lex = TestData.Build();

    public static IEnumerable<object[]> Layouts() => new[]
    {
        new object[] { "QWERTY" }, new object[] { "AZERTY" },
    };

    private static KeyboardLayout Layout(string n) => KeyboardLayout.ByName(n);

    [Theory]
    [InlineData("QWERTY", "bonjour")] [InlineData("AZERTY", "bonjour")]
    [InlineData("QWERTY", "hello")] [InlineData("AZERTY", "hello")]
    [InlineData("QWERTY", "merci")] [InlineData("AZERTY", "merci")]
    [InlineData("QWERTY", "keyboard")] [InlineData("AZERTY", "keyboard")]
    public void IdealPath_FirstCandidateIsWord(string layoutName, string word)
    {
        var layout = Layout(layoutName);
        var dec = new SwipeDecoder(layout, Lex);
        var res = dec.Decode(SwipePathBuilder.IdealPath(layout, word));
        Assert.NotEmpty(res);
        Assert.Equal(word, res[0].Word);
    }

    [Theory]
    [InlineData("QWERTY", "belle")] [InlineData("AZERTY", "belle")]
    [InlineData("QWERTY", "hello")] [InlineData("AZERTY", "hello")]
    public void DoubledLetters(string layoutName, string word)
    {
        var layout = Layout(layoutName);
        var dec = new SwipeDecoder(layout, Lex);
        Assert.Equal(word, dec.Decode(SwipePathBuilder.IdealPath(layout, word))[0].Word);
    }

    [Theory]
    [InlineData("QWERTY", "été")] [InlineData("AZERTY", "été")]
    [InlineData("QWERTY", "français")] [InlineData("AZERTY", "français")]
    public void AccentedWords(string layoutName, string word)
    {
        var layout = Layout(layoutName);
        var dec = new SwipeDecoder(layout, Lex);
        Assert.Equal(word, dec.Decode(SwipePathBuilder.IdealPath(layout, word))[0].Word);
    }

    [Theory]
    [InlineData("QWERTY")] [InlineData("AZERTY")]
    public void NoisyPath_WordInTop3(string layoutName)
    {
        var layout = Layout(layoutName);
        var dec = new SwipeDecoder(layout, Lex);
        string[] words = { "bonjour", "hello", "merci", "keyboard", "belle", "français", "maison", "ordinateur", "beautiful", "toujours" };
        int seed = 1;
        foreach (var w in words)
            for (int k = 0; k < 3; k++)
            {
                var path = TestData.NoisyPath(layout, w, seed++);
                var top = dec.Decode(path, 3).Select(c => c.Word).ToList();
                Assert.True(top.Contains(w), $"{layoutName}: '{w}' absent du top 3 : {string.Join(",", top)}");
            }
    }

    [Fact]
    public void IsSwipe_FalseForTap()
    {
        var layout = KeyboardLayout.Qwerty();
        var c = layout.Keys.First(k => k.Id == "g").Center;
        Assert.False(SwipeDecoder.IsSwipe(new[] { c }));
        Assert.False(SwipeDecoder.IsSwipe(new[] { c, new PointD(c.X + 0.1, c.Y), new PointD(c.X + 0.2, c.Y - 0.1) }));
        Assert.True(SwipeDecoder.IsSwipe(SwipePathBuilder.IdealPath(layout, "hello")));
        Assert.Empty(new SwipeDecoder(layout, Lex).Decode(new[] { c, c }));
    }

    [Fact]
    public void EmptyOrSinglePoint_ReturnsEmpty()
    {
        var dec = new SwipeDecoder(KeyboardLayout.Qwerty(), Lex);
        Assert.Empty(dec.Decode(Array.Empty<PointD>()));
        Assert.Empty(dec.Decode(new[] { new PointD(1, 1) }));
    }

    [Fact]
    public void ContextBoost_ChangesRanking()
    {
        var layout = KeyboardLayout.Qwerty();
        var dec = new SwipeDecoder(layout, Lex);
        // « hello » et « hell » : tracés très proches ; on parcourt « hello ».
        var path = SwipePathBuilder.IdealPath(layout, "hello");
        var plain = dec.Decode(path, 5).Select(c => c.Word).ToList();
        Assert.Equal("hello", plain[0]);
        Assert.Contains("hell", plain);

        var boosted = dec.Decode(path, 5, w => w == "hell" ? 1e6 : 1.0).Select(c => c.Word).ToList();
        Assert.Equal("hell", boosted[0]);

        var neutral = dec.Decode(path, 5, _ => 1.0).Select(c => c.Word).ToList();
        Assert.Equal(plain, neutral);
    }

    [Fact]
    public void Performance_FiftyThousandWords()
    {
        var rnd = new Random(42);
        const string letters = "abcdefghijklmnopqrstuvwxyz";
        var entries = new List<WordEntry>();
        var seen = new HashSet<string>();
        entries.Add(new WordEntry("bonjour", 1_000_000));
        seen.Add("bonjour");
        while (entries.Count < 50_000)
        {
            int len = rnd.Next(3, 10);
            var w = new string(Enumerable.Range(0, len).Select(_ => letters[rnd.Next(26)]).ToArray());
            if (seen.Add(w)) entries.Add(new WordEntry(w, 1 + 1_000_000 / (entries.Count + 1)));
        }
        var lex = new InMemoryLexicon("xx", entries);
        var layout = KeyboardLayout.Azerty();
        var dec = new SwipeDecoder(layout, lex);
        dec.Warmup();
        var path = TestData.NoisyPath(layout, "bonjour", 7);
        dec.Decode(path);

        double best = double.MaxValue;
        IReadOnlyList<SwipeCandidate> res = Array.Empty<SwipeCandidate>();
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            res = dec.Decode(path, 5, w => 1.0);
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        Assert.Equal("bonjour", res[0].Word);
        Assert.True(best < 40, $"Decode trop lent : {best:F1} ms");
    }
}
