namespace WinBoard.Input;

/// <summary>Destination du texte saisi (implémentée côté Windows avec SendInput).</summary>
public interface ITextSink
{
    void SendText(string text);

    /// <summary>Efface <paramref name="count"/> éléments de texte (graphèmes).</summary>
    void SendBackspace(int count);

    void SendKey(SpecialKey key);
}
