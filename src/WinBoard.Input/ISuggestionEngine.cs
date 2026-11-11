namespace WinBoard.Input;

/// <summary>Moteur de suggestions et de correction utilisé par le contrôleur de saisie.</summary>
public interface ISuggestionEngine
{
    IReadOnlyList<string> GetSuggestions(string textBeforeCursor, int max);

    /// <summary>Retourne la version corrigée du mot, ou null s'il n'y a rien à corriger.</summary>
    string? GetAutoCorrection(string word);

    void Learn(string text);
}
