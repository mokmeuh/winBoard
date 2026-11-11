using WinBoard.Input;
using Xunit;

namespace WinBoard.Input.Tests;

public class TypingControllerTests
{
    // ------------------------------------------------------- Frappe et suggestions

    [Fact]
    public void Letter_is_sent_immediately_and_suggestions_are_computed()
    {
        var h = new Harness();
        h.Type("bonj");
        Assert.Equal("bonj", h.Text);
        Assert.Equal("bonj", h.Controller.CurrentWord);
        Assert.Equal(new[] { "bonjour", "bonne", "bon" }, h.Controller.Suggestions);
    }

    [Fact]
    public void Suggestions_respect_max_and_can_be_disabled()
    {
        var h = new Harness();
        h.Options.MaxSuggestions = 2;
        h.Type("bonj");
        Assert.Equal(2, h.Controller.Suggestions.Count);

        h.Options.ShowSuggestions = false;
        h.Type("o");
        Assert.Empty(h.Controller.Suggestions);
    }

    [Fact]
    public void StateChanged_is_raised_on_actions()
    {
        var h = new Harness();
        int count = 0;
        h.Controller.StateChanged += () => count++;
        h.Type("a");
        h.Controller.Backspace();
        h.Controller.Enter();
        Assert.Equal(3, count);
    }

    // ------------------------------------------------------------ Autocorrection

    [Fact]
    public void Autocorrect_on_space()
    {
        var h = new Harness();
        h.Type("bonjuor ");
        Assert.Equal("bonjour ", h.Text);
        Assert.Equal("bonjour ", h.Controller.Context);
    }

    [Theory]
    [InlineData("Bonjuor ", "Bonjour ")]
    [InlineData("BONJUOR ", "BONJOUR ")]
    [InlineData("bonjuor ", "bonjour ")]
    public void Autocorrect_keeps_initial_case(string typed, string expected)
    {
        var h = new Harness();
        h.Type(typed);
        Assert.Equal(expected, h.Text);
    }

    [Theory]
    [InlineData(',')]
    [InlineData('.')]
    [InlineData(';')]
    [InlineData(':')]
    [InlineData('!')]
    [InlineData('?')]
    public void Autocorrect_on_punctuation(char sep)
    {
        var h = new Harness();
        h.Type("bonjuor" + sep);
        Assert.Equal("bonjour" + sep, h.Text);
    }

    [Fact]
    public void Autocorrect_disabled()
    {
        var h = new Harness();
        h.Options.AutoCorrect = false;
        h.Type("bonjuor ");
        Assert.Equal("bonjuor ", h.Text);
    }

    [Fact]
    public void Correct_word_is_left_alone_and_learned()
    {
        var h = new Harness();
        h.Type("salut ");
        Assert.Equal("salut ", h.Text);
        Assert.Equal(new[] { "salut" }, h.Engine.Learned);
    }

    [Fact]
    public void Corrected_word_is_learned_and_learning_can_be_disabled()
    {
        var h = new Harness();
        h.Type("bonjuor ");
        Assert.Equal(new[] { "bonjour" }, h.Engine.Learned);

        var h2 = new Harness();
        h2.Options.LearnFromTyping = false;
        h2.Type("salut ");
        Assert.Empty(h2.Engine.Learned);
    }

    [Theory]
    [InlineData("exmple2 ", "exmple2 ")]
    [InlineData("dossier/exmple ", "dossier/exmple ")]
    [InlineData("jean@exmple.com ", "jean@exmple.com ")]
    [InlineData("www.exmple.com ", "www.exmple.com ")]
    [InlineData("https://exmple.org ", "https://exmple.org ")]
    public void Urls_emails_and_digits_are_never_corrected(string typed, string expected)
    {
        var h = new Harness();
        h.Type(typed);
        Assert.Equal(expected, h.Text);
        Assert.DoesNotContain("exemple", h.Engine.Learned);
        Assert.DoesNotContain("exmple", h.Engine.Learned);
    }

    [Fact]
    public void Domain_name_after_dot_is_restored()
    {
        var h = new Harness();
        h.Type("exmple.com");
        Assert.Equal("exmple.com", h.Text);
    }

    // ----------------------------------------------------------------- Annulation

    [Fact]
    public void Backspace_after_autocorrection_restores_typed_word()
    {
        var h = new Harness();
        h.Type("bonjuor ");
        h.Controller.Backspace();
        Assert.Equal("bonjuor", h.Text);
        Assert.Equal("bonjuor", h.Controller.Context);
    }

    [Fact]
    public void Undone_correction_is_not_applied_again()
    {
        var h = new Harness();
        h.Type("bonjuor ");
        h.Controller.Backspace();
        h.Type(" ");
        Assert.Equal("bonjuor ", h.Text);
    }

    [Fact]
    public void Undo_works_with_punctuation_separator()
    {
        var h = new Harness();
        h.Type("Bonjuor,");
        h.Controller.Backspace();
        Assert.Equal("Bonjuor", h.Text);
    }

    [Fact]
    public void Backspace_after_other_action_is_normal()
    {
        var h = new Harness();
        h.Type("bonjuor ");
        h.Type("a");
        h.Controller.Backspace();
        Assert.Equal("bonjour ", h.Text);
    }

    // --------------------------------------------------------------- Double espace

    [Fact]
    public void Double_space_gives_period()
    {
        var h = new Harness();
        h.Type("salut  ");
        Assert.Equal("salut. ", h.Text);
    }

    [Fact]
    public void Double_space_after_digit()
    {
        var h = new Harness();
        h.Type("en 2024  ");
        Assert.Equal("en 2024. ", h.Text);
    }

    [Fact]
    public void Double_space_not_after_punctuation_or_when_disabled()
    {
        var h = new Harness();
        h.Type("oui,  ");
        Assert.Equal("oui,  ", h.Text);

        var h2 = new Harness();
        h2.Options.DoubleSpacePeriod = false;
        h2.Type("salut  ");
        Assert.Equal("salut  ", h2.Text);
    }

    [Fact]
    public void Double_space_after_autocorrection()
    {
        var h = new Harness();
        h.Type("bonjuor  ");
        Assert.Equal("bonjour. ", h.Text);
    }

    // ------------------------------------------------------------------ Swipe

    [Fact]
    public void Swipe_inserts_word_with_auto_space_and_sets_suggestions()
    {
        var h = new Harness();
        h.Type("a ");
        h.Controller.CommitSwipe(new[] { "bonjour", "bonne", "bon" });
        Assert.Equal("a bonjour ", h.Text);
        Assert.Equal(new[] { "bonne", "bon" }, h.Controller.Suggestions);
    }

    [Fact]
    public void Swipe_adds_leading_space_when_needed()
    {
        var h = new Harness();
        h.Type("a");
        h.Controller.CommitSwipe(new[] { "bonjour" });
        Assert.Equal("a bonjour ", h.Text);
    }

    [Fact]
    public void Consecutive_swipes()
    {
        var h = new Harness();
        h.Controller.CommitSwipe(new[] { "bonjour" });
        h.Controller.CommitSwipe(new[] { "monde" });
        Assert.Equal("Bonjour monde ", h.Text);
    }

    [Theory]
    [InlineData('.')]
    [InlineData(',')]
    [InlineData('!')]
    [InlineData('?')]
    public void Punctuation_after_swipe_removes_auto_space(char p)
    {
        var h = new Harness();
        h.Options.AutoCapitalize = false;
        h.Controller.CommitSwipe(new[] { "bonjour" });
        h.Type(p.ToString());
        Assert.Equal("bonjour" + p, h.Text);
    }

    [Fact]
    public void Punctuation_after_pick_removes_auto_space()
    {
        var h = new Harness();
        h.Options.AutoCapitalize = false;
        h.Type("bonj");
        h.Controller.PickSuggestion(0);
        h.Type(".");
        Assert.Equal("bonjour.", h.Text);
    }

    [Fact]
    public void Swipe_then_pick_replaces_swiped_word()
    {
        var h = new Harness();
        h.Options.AutoCapitalize = false;
        h.Type("a ");
        h.Controller.CommitSwipe(new[] { "bonjour", "bonne", "bon" });
        h.Controller.PickSuggestion(0);
        Assert.Equal("a bonne ", h.Text);
        Assert.Contains("bonne", h.Engine.Learned);
    }

    [Fact]
    public void Swipe_then_pick_keeps_automatic_capital()
    {
        var h = new Harness();
        h.Controller.CommitSwipe(new[] { "bonjour", "bonne" });
        Assert.Equal("Bonjour ", h.Text);
        h.Controller.PickSuggestion(0);
        Assert.Equal("Bonne ", h.Text);
    }

    [Fact]
    public void Backspace_after_swipe_removes_whole_word()
    {
        var h = new Harness();
        h.Type("ab ");
        h.Controller.CommitSwipe(new[] { "bonjour", "bonne" });
        h.Controller.Backspace();
        Assert.Equal("ab ", h.Text);
        Assert.Equal("ab ", h.Controller.Context);
    }

    [Fact]
    public void Backspace_after_swipe_with_leading_space_restores_previous_state()
    {
        var h = new Harness();
        h.Type("ab");
        h.Controller.CommitSwipe(new[] { "bonjour" });
        h.Controller.Backspace();
        Assert.Equal("ab", h.Text);
    }

    [Fact]
    public void Typing_while_word_in_progress_then_swipe_validates_word()
    {
        var h = new Harness();
        h.Type("bonjuor");
        h.Controller.CommitSwipe(new[] { "monde" });
        Assert.Equal("bonjour monde ", h.Text);
    }

    // ---------------------------------------------------------------- Suggestions

    [Fact]
    public void Pick_replaces_current_word_and_adds_space()
    {
        var h = new Harness();
        h.Options.AutoCapitalize = false;
        h.Type("bonj");
        h.Controller.PickSuggestion(0);
        Assert.Equal("bonjour ", h.Text);
        Assert.Contains("bonjour", h.Engine.Learned);
    }

    [Fact]
    public void Pick_capitalizes_at_start_of_text()
    {
        var h = new Harness();
        h.Type("bonj");
        h.Controller.PickSuggestion(0);
        Assert.Equal("Bonjour ", h.Text);
    }

    [Fact]
    public void Pick_keeps_typed_capital()
    {
        var h = new Harness();
        h.Type("Bonj");
        h.Controller.PickSuggestion(1);
        Assert.Equal("Bonne ", h.Text);
    }

    [Fact]
    public void Pick_next_word_prediction_when_word_is_empty()
    {
        var h = new Harness();
        h.Type("bonjour ");
        Assert.Equal(new[] { "le", "tout", "à" }, h.Controller.Suggestions);
        h.Controller.PickSuggestion(1);
        Assert.Equal("bonjour tout ", h.Text);
        Assert.Contains("tout", h.Engine.Learned);
    }

    [Fact]
    public void Pick_out_of_range_is_ignored()
    {
        var h = new Harness();
        h.Type("bonj");
        h.Controller.PickSuggestion(7);
        h.Controller.PickSuggestion(-1);
        Assert.Equal("bonj", h.Text);
    }

    [Fact]
    public void Pick_after_apostrophe_keeps_prefix()
    {
        var h = new Harness();
        h.Engine.Suggestions["l'ho"] = new[] { "homme" };
        h.Type("l'ho");
        h.Controller.PickSuggestion(0);
        Assert.Equal("l'homme ", h.Text);
    }

    // ------------------------------------------------------ Majuscule automatique

    [Fact]
    public void Capitalize_at_start_and_after_sentence_end()
    {
        var h = new Harness();
        Assert.True(h.Controller.ShouldCapitalizeNext);
        h.Type("Salut");
        Assert.False(h.Controller.ShouldCapitalizeNext);
        h.Type(".");
        Assert.False(h.Controller.ShouldCapitalizeNext);
        h.Type(" ");
        Assert.True(h.Controller.ShouldCapitalizeNext);
        h.Type("Ok, ");
        Assert.False(h.Controller.ShouldCapitalizeNext);
    }

    [Theory]
    [InlineData("Quoi? ")]
    [InlineData("Quoi! ")]
    public void Capitalize_after_question_and_exclamation(string text)
    {
        var h = new Harness();
        h.Type(text);
        Assert.True(h.Controller.ShouldCapitalizeNext);
    }

    [Fact]
    public void Capitalize_after_enter()
    {
        var h = new Harness();
        h.Type("salut");
        h.Controller.Enter();
        Assert.True(h.Controller.ShouldCapitalizeNext);
    }

    [Fact]
    public void Capitalize_can_be_disabled()
    {
        var h = new Harness();
        h.Options.AutoCapitalize = false;
        Assert.False(h.Controller.ShouldCapitalizeNext);
        h.Controller.CommitSwipe(new[] { "bonjour" });
        Assert.Equal("bonjour ", h.Text);
    }

    [Fact]
    public void Swipe_capitalizes_at_start_and_after_sentence()
    {
        var h = new Harness();
        h.Controller.CommitSwipe(new[] { "bonjour" });
        Assert.Equal("Bonjour ", h.Text);

        var h2 = new Harness();
        h2.Type("Fin. ");
        h2.Controller.CommitSwipe(new[] { "bonjour" });
        Assert.Equal("Fin. Bonjour ", h2.Text);
    }

    [Fact]
    public void Pick_capitalizes_after_sentence_end()
    {
        var h = new Harness();
        h.Type("Fin. ");
        h.Engine.Suggestions["fin. "] = new[] { "alors" };
        h.Type("a");
        h.Controller.Backspace();
        h.Controller.PickSuggestion(0);
        Assert.Equal("Fin. Alors ", h.Text);
    }

    [Fact]
    public void Swipe_state_is_true_for_text_after_swipe_period_and_space()
    {
        var h = new Harness();
        h.Controller.CommitSwipe(new[] { "bonjour" });
        h.Type(".");
        h.Type(" ");
        Assert.True(h.Controller.ShouldCapitalizeNext);
    }

    // --------------------------------------------------------------- Backspace

    [Fact]
    public void Plain_backspace_sends_one_and_updates_buffer()
    {
        var h = new Harness();
        h.Type("abc");
        h.Controller.Backspace();
        Assert.Equal("ab", h.Text);
        Assert.Equal("ab", h.Controller.Context);
        Assert.Equal(1, h.Sink.Backspaces[^1]);
    }

    [Fact]
    public void Backspace_removes_surrogate_pair_in_one_step()
    {
        var h = new Harness();
        h.Controller.InsertRaw("a😀");
        h.Controller.Backspace();
        Assert.Equal("a", h.Text);
        Assert.Equal("a", h.Controller.Context);
        Assert.Equal(1, h.Sink.Backspaces[^1]);
    }

    [Fact]
    public void Backspace_removes_composed_emoji_in_one_step()
    {
        var h = new Harness();
        const string family = "👨‍👩‍👧";
        h.Controller.InsertRaw("x" + family);
        h.Controller.Backspace();
        Assert.Equal("x", h.Controller.Context);
        Assert.Equal("x", h.Text);
        Assert.Equal(1, h.Sink.Backspaces[^1]);
    }

    [Fact]
    public void Autocorrection_after_emoji_word_counts_graphemes()
    {
        var h = new Harness();
        h.Controller.InsertRaw("😀 ");
        h.Type("bonjuor ");
        Assert.Equal("😀 bonjour ", h.Text);
    }

    [Fact]
    public void Backspace_with_empty_buffer_still_sends_key()
    {
        var h = new Harness();
        h.Controller.Backspace();
        Assert.Equal(new[] { 1 }, h.Sink.Backspaces);
    }

    // ------------------------------------------------------------ Enter, touches

    [Fact]
    public void Enter_validates_word_with_correction()
    {
        var h = new Harness();
        h.Type("bonjuor");
        h.Controller.Enter();
        Assert.Equal("bonjour\n", h.Text);
        Assert.Contains("bonjour", h.Engine.Learned);
        Assert.EndsWith("\n", h.Controller.Context);
        Assert.Equal("", h.Controller.CurrentWord);
        Assert.Equal(new[] { SpecialKey.Enter }, h.Sink.Keys);
    }

    [Fact]
    public void Special_keys_reset_context()
    {
        var h = new Harness();
        h.Type("bonj");
        h.Controller.Key(SpecialKey.Left);
        Assert.Equal("", h.Controller.Context);
        Assert.Equal(new[] { SpecialKey.Left }, h.Sink.Keys);

        h.Type("ab");
        h.Controller.Key(SpecialKey.Tab);
        Assert.Equal("", h.Controller.Context);
        h.Type("ab");
        h.Controller.Key(SpecialKey.Right);
        Assert.Equal("", h.Controller.Context);
    }

    [Fact]
    public void Key_backspace_and_enter_follow_normal_paths()
    {
        var h = new Harness();
        h.Type("ab");
        h.Controller.Key(SpecialKey.Backspace);
        h.Controller.Key(SpecialKey.Enter);
        Assert.Equal("a\n", h.Text);
    }

    // ----------------------------------------------------- InsertRaw, contexte

    [Fact]
    public void InsertRaw_is_inserted_as_is()
    {
        var h = new Harness();
        h.Controller.InsertRaw("bonjuor 😀");
        Assert.Equal("bonjuor 😀", h.Text);
        Assert.Equal("bonjuor 😀", h.Controller.Context);
        Assert.Empty(h.Engine.Learned);
    }

    [Fact]
    public void ResetContext_forgets_buffer()
    {
        var h = new Harness();
        h.Type("bonj");
        h.Controller.ResetContext();
        Assert.Equal("", h.Controller.Context);
        Assert.Equal("", h.Controller.CurrentWord);
        Assert.True(h.Controller.ShouldCapitalizeNext);
        // Plus d'annulation possible après réinitialisation.
        h.Type("bonjuor ");
        Assert.EndsWith("bonjour ", h.Text);
    }

    [Fact]
    public void Context_is_bounded()
    {
        var h = new Harness();
        h.Type(new string('a', 150));
        h.Type(" ");
        h.Type(new string('b', 150));
        Assert.True(h.Controller.Context.Length <= 200);
        Assert.EndsWith("bbbb", h.Controller.Context);
        Assert.Equal(301, h.Text.Length);
    }

    [Fact]
    public void Multi_character_text_is_processed_element_by_element()
    {
        var h = new Harness();
        h.Controller.TypeText("bonjuor, teh ");
        Assert.Equal("bonjour, the ", h.Text);
    }
}
