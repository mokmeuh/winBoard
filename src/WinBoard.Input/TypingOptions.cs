namespace WinBoard.Input;

/// <summary>Réglages de la saisie, modifiables à chaud.</summary>
public sealed class TypingOptions
{
    public bool AutoCorrect { get; set; } = true;
    public bool AutoCapitalize { get; set; } = true;
    public bool DoubleSpacePeriod { get; set; } = true;
    public bool LearnFromTyping { get; set; } = true;
    public bool ShowSuggestions { get; set; } = true;
    public int MaxSuggestions { get; set; } = 3;
}
