using System.Diagnostics;
using WinBoard.Core;
using WinBoard.Language;
using Xunit;

namespace WinBoard.Language.Tests;

public class LanguageTests
{
    private static readonly KeyboardLayout Azerty = KeyboardLayout.Azerty();
    private static readonly KeyboardLayout Qwerty = KeyboardLayout.Qwerty();

    private static TextEngine Fr(UserModel? u = null) => TextEngine.Create("fr", Azerty, u ?? new UserModel());
    private static TextEngine En(UserModel? u = null) => TextEngine.Create("en", Qwerty, u ?? new UserModel());

    // --- Chargement ---

    [Theory]
    [InlineData("fr", "bonjour")]
    [InlineData("fr", "école")]
    [InlineData("en", "keyboard")]
    [InlineData("en", "the")]
    public void Lexicon_loads_and_contains_common_words(string lang, string word)
    {
        var lex = LexiconLoader.Load(lang);
        Assert.Equal(lang, lex.Language);
        Assert.InRange(lex.Count, 40000, 60000);
        Assert.True(lex.Contains(word));
        Assert.True(lex.GetFrequency(word) > 0);
        Assert.True(lex.TotalCount > 0);
    }

    [Fact]
    public void Lexicon_is_cached_and_accepts_locale_codes()
    {
        Assert.Same(LexiconLoader.Load("fr"), LexiconLoader.Load("fr-FR"));
        Assert.Throws<NotSupportedException>(() => LexiconLoader.Load("xx"));
    }

    [Fact]
    public void WithPrefix_returns_most_frequent_first()
    {
        var lex = LexiconLoader.Load("en");
        var res = lex.WithPrefix("th", 5).ToList();
        Assert.Equal(5, res.Count);
        Assert.All(res, e => Assert.StartsWith("th", e.Word));
        Assert.Equal(res.OrderByDescending(e => e.Frequency).Select(e => e.Word), res.Select(e => e.Word));
        Assert.Equal("the", res[0].Word);
    }

    [Fact]
    public void Bigrams_are_loaded()
    {
        Assert.True(LexiconLoader.LoadBigrams("en").Count > 5000);
        Assert.True(LexiconLoader.LoadBigrams("fr").Count > 100);
    }

    // --- Correction ---

    [Theory]
    [InlineData("bonjuor", "bonjour")]
    [InlineData("ecole", "école")]
    [InlineData("ca", "ça")]
    [InlineData("Bonjuor", "Bonjour")]
    [InlineData("tres", "très")]
    [InlineData("jai", "j'ai")]
    public void Fr_autocorrect(string typed, string expected) =>
        Assert.Equal(expected, Fr().GetAutoCorrection(typed));

    [Theory]
    [InlineData("teh", "the")]
    [InlineData("recieve", "receive")]
    [InlineData("helllo", "hello")]
    [InlineData("Teh", "The")]
    public void En_autocorrect(string typed, string expected) =>
        Assert.Equal(expected, En().GetAutoCorrection(typed));

    [Fact]
    public void Suggest_puts_expected_word_first()
    {
        Assert.Equal("bonjour", Fr().Corrector.Suggest("bonjuor")[0]);
        Assert.Equal("the", En().Corrector.Suggest("teh")[0]);
        Assert.Equal("école", Fr().Corrector.Suggest("ecole")[0]);
        Assert.Equal("ÉCOLE", Fr().Corrector.Suggest("ECOLE")[0]);
    }

    [Theory]
    [InlineData("fr", "bonjour")]
    [InlineData("fr", "à")]
    [InlineData("fr", "a")]
    [InlineData("fr", "ou")]
    [InlineData("fr", "la")]
    [InlineData("fr", "sur")]
    [InlineData("fr", "l'homme")]
    [InlineData("en", "the")]
    [InlineData("en", "keyboard")]
    [InlineData("en", "NASA")]
    public void Known_words_are_not_corrected(string lang, string word)
    {
        var e = TextEngine.Create(lang, lang == "fr" ? Azerty : Qwerty, new UserModel());
        Assert.Null(e.GetAutoCorrection(word));
    }

    [Fact]
    public void Gibberish_is_not_corrected()
    {
        Assert.Null(En().GetAutoCorrection("xqzvbnmw"));
        Assert.Null(En().GetAutoCorrection("42"));
        Assert.Null(En().GetAutoCorrection(""));
    }

    [Fact]
    public void Neighbor_key_substitution_is_preferred()
    {
        // « bonjiur » : i voisin de o sur AZERTY.
        Assert.Equal("bonjour", Fr().GetAutoCorrection("bonjiur"));
    }

    // --- Prédiction ---

    [Fact]
    public void Predict_next_word_from_bigrams()
    {
        var fr = Fr();
        Assert.Contains("suis", fr.GetSuggestions("je ", 5));
        Assert.Contains("a", fr.GetSuggestions("il y ", 5));
        Assert.Equal("journée", fr.Predictor.Predict(new[] { "bonne" }, "", 5).First(w => w == "journée"));
        var en = En();
        Assert.Contains("the", en.GetSuggestions("one of ", 5));
        Assert.Contains("of", en.GetSuggestions("a lot ", 3));
    }

    [Fact]
    public void Predict_without_context_gives_frequent_words()
    {
        var res = En().GetSuggestions("", 3);
        Assert.Equal(3, res.Count);
        Assert.All(res, w => Assert.True(char.IsUpper(w[0])));   // début de phrase
    }

    [Fact]
    public void Prefix_completion()
    {
        Assert.Contains("bonjour", Fr().GetSuggestions("bonj", 3));
        Assert.Contains("keyboard", En().GetSuggestions("I need a keyb", 3));
        var res = En().GetSuggestions("Hel", 3);
        Assert.All(res, w => Assert.StartsWith("Hel", w));
    }

    [Fact]
    public void Context_reranks_completions()
    {
        var en = En();
        var noCtx = en.Predictor.Predict(Array.Empty<string>(), "t", 20);
        var ctx = en.Predictor.Predict(new[] { "ability" }, "t", 20);
        Assert.Equal("to", ctx[0]);
        Assert.NotEmpty(noCtx);
    }

    [Fact]
    public void Prefix_with_no_completion_falls_back_to_corrections()
    {
        var res = Fr().GetSuggestions("bonjuor", 3);
        Assert.Contains("bonjour", res);
    }

    [Fact]
    public void Elision_is_completed_with_head()
    {
        var res = Fr().GetSuggestions("je vois l'hom", 3);
        Assert.Contains("l'homme", res);
    }

    [Fact]
    public void Sentence_end_resets_context_and_blocks_suggestions()
    {
        var en = En();
        Assert.Empty(en.GetSuggestions("Hello.", 3));
        Assert.Equal(en.GetSuggestions("", 3), en.GetSuggestions("Bye. ", 3));
        Assert.Empty(en.GetSuggestions("abc123", 3));
    }

    // --- Utilisateur ---

    [Fact]
    public void Learned_word_is_no_longer_corrected()
    {
        var user = new UserModel();
        var fr = Fr(user);
        Assert.NotEqual("zorglub", fr.GetAutoCorrection("zorglub"));   // inconnu : corrigé ou ignoré, mais pas conservé
        user.Learn("Mon ami zorglub est arrivé");
        Assert.Null(fr.GetAutoCorrection("zorglub"));
        Assert.Contains("zorglub", fr.GetSuggestions("mon ami zorg", 3));
    }

    [Fact]
    public void Personal_word_is_known_and_suggested()
    {
        var user = new UserModel();
        user.AddWord("Winboard");
        var en = En(user);
        Assert.Null(en.GetAutoCorrection("winboard"));
        Assert.Contains("winboard", en.GetSuggestions("try winb", 3), StringComparer.OrdinalIgnoreCase);
        user.Forget("winboard");
        Assert.False(user.IsKnown("winboard"));
    }

    [Fact]
    public void User_bigrams_influence_next_word()
    {
        var user = new UserModel();
        for (int i = 0; i < 20; i++) user.Learn("on mange des pâtes");
        var res = Fr(user).GetSuggestions("des ", 3);
        Assert.Equal("pâtes", res[0]);
    }

    [Fact]
    public void User_model_persists_to_json()
    {
        var path = Path.Combine(Path.GetTempPath(), "winboard-user-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var a = new UserModel();
            a.Learn("rendez-vous chez Xanthippe. Xanthippe habite loin");
            a.AddWord("Qwertz");
            a.Save(path);
            var b = UserModel.FromFile(path);
            Assert.True(b.IsKnown("xanthippe"));
            Assert.True(b.IsKnown("qwertz"));
            Assert.Equal(2, b.GetCount("xanthippe"));
            Assert.Equal(1, b.GetBigramCount("chez", "xanthippe"));
            Assert.Equal(0, b.GetBigramCount("xanthippe", "xanthippe"));   // la fin de phrase coupe les bigrammes
            Assert.Equal(a.TotalWords, b.TotalWords);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_user_file_is_ignored()
    {
        var m = new UserModel();
        m.Load(Path.Combine(Path.GetTempPath(), "inexistant-" + Guid.NewGuid() + ".json"));
        Assert.Equal(0, m.TotalWords);
    }

    [Fact]
    public void User_model_is_thread_safe()
    {
        var m = new UserModel();
        Parallel.For(0, 200, i =>
        {
            m.Learn($"mot{(char)('a' + i % 10)} suivant{(char)('a' + i % 7)} encore");
            _ = m.GetNext("encore");
            _ = m.IsKnown("motd");
        });
        Assert.True(m.IsKnown("motd"));
        Assert.Equal(600, m.TotalWords);
    }

    // --- Performance ---

    [Fact]
    public void Suggest_is_fast_on_eight_letter_words()
    {
        foreach (var (e, words) in new[]
        {
            (Fr(), new[] { "bonjuour", "aujourdhu", "maintenat", "pourquio", "ordianteur", "ecolleas" }),
            (En(), new[] { "keybaord", "wellcome", "tomorrow", "beautifl", "computre", "practise" }),
        })
        {
            e.Corrector.Warmup();
            e.Corrector.Suggest(words[0]);   // échauffement
            var sw = Stopwatch.StartNew();
            int n = 0;
            for (int r = 0; r < 5; r++)
                foreach (var w in words) { e.Corrector.Suggest(w); n++; }
            double avg = sw.Elapsed.TotalMilliseconds / n;
            Assert.True(avg < 30, $"moyenne {avg:F1} ms");
        }
    }
}
