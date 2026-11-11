using System.Windows.Interop;
using WinBoard.App.Services;
using static WinBoard.App.Interop.NativeMethods;

namespace WinBoard.App.Interop;

/// <summary>Raccourci clavier global, reçu par une fenêtre invisible (message-only).</summary>
public sealed class HotKey : IDisposable
{
    private const int Id = 0x5742; // « WB »
    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    /// <summary>Enregistre le raccourci ; renvoie false s'il est déjà pris par une autre application.</summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        var p = new HwndSourceParameters("WinBoardHotKey") { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
        _registered = RegisterHotKey(_source.Handle, Id, modifiers | MOD_NOREPEAT, virtualKey);
        if (!_registered) Log.Error("RegisterHotKey a échoué : raccourci déjà utilisé ?");
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            try { Pressed?.Invoke(); }
            catch (Exception e) { Log.Error("Raccourci global.", e); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        if (_registered) UnregisterHotKey(_source.Handle, Id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
        _registered = false;
    }
}
