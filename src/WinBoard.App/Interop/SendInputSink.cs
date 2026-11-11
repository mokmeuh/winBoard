using System.Runtime.InteropServices;
using WinBoard.App.Services;
using WinBoard.Input;
using static WinBoard.App.Interop.NativeMethods;

namespace WinBoard.App.Interop;

/// <summary>Injecte la saisie dans la fenêtre active via SendInput (Unicode pour le texte).</summary>
public sealed class SendInputSink : ITextSink
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public SendInputSink()
    {
        // Sur Windows 64 bits, la structure INPUT doit mesurer 40 octets.
        if (IntPtr.Size == 8 && InputSize != 40)
            Log.Error($"Taille de INPUT inattendue : {InputSize} octets (40 attendus).");
    }

    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var list = new List<INPUT>(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n') { AddVirtualKey(list, VK_RETURN, false); continue; }
            if (c == '\t') { AddVirtualKey(list, VK_TAB, false); continue; }
            // Les paires de substitution sont envoyées unité par unité : le système les recompose.
            list.Add(Key(0, c, KEYEVENTF_UNICODE));
            list.Add(Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }
        Send(list);
    }

    public void SendBackspace(int count)
    {
        if (count <= 0) return;
        var list = new List<INPUT>(count * 2);
        for (int i = 0; i < count; i++) AddVirtualKey(list, VK_BACK, false);
        Send(list);
    }

    public void SendKey(SpecialKey key)
    {
        var list = new List<INPUT>(2);
        switch (key)
        {
            case SpecialKey.Backspace: AddVirtualKey(list, VK_BACK, false); break;
            case SpecialKey.Enter: AddVirtualKey(list, VK_RETURN, false); break;
            case SpecialKey.Tab: AddVirtualKey(list, VK_TAB, false); break;
            case SpecialKey.Left: AddVirtualKey(list, VK_LEFT, true); break;
            case SpecialKey.Right: AddVirtualKey(list, VK_RIGHT, true); break;
            default: return;
        }
        Send(list);
    }

    private static void AddVirtualKey(List<INPUT> list, ushort vk, bool extended)
    {
        uint ext = extended ? KEYEVENTF_EXTENDEDKEY : 0;
        list.Add(Key(vk, 0, ext));
        list.Add(Key(vk, 0, ext | KEYEVENTF_KEYUP));
    }

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } }
    };

    private static void Send(List<INPUT> list)
    {
        if (list.Count == 0) return;
        try
        {
            var arr = list.ToArray();
            uint sent = SendInput((uint)arr.Length, arr, InputSize);
            if (sent != arr.Length)
                Log.Error($"SendInput : {sent}/{arr.Length} événements injectés (erreur Win32 {Marshal.GetLastWin32Error()}).");
        }
        catch (Exception e)
        {
            Log.Error("SendInput a échoué.", e);
        }
    }
}
