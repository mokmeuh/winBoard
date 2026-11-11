using WinBoard.App.Services;
using static WinBoard.App.Interop.NativeMethods;

namespace WinBoard.App.Interop;

/// <summary>Hook WinEvent : signale chaque changement de fenêtre au premier plan (à créer sur le fil d'interface).</summary>
public sealed class WinEventHook : IDisposable
{
    // Le délégué est conservé dans un champ pour ne pas être collecté tant que le hook existe.
    private readonly WinEventDelegate _proc;
    private IntPtr _handle;

    public event Action<IntPtr>? ForegroundChanged;

    public WinEventHook()
    {
        _proc = OnEvent;
        _handle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_handle == IntPtr.Zero) Log.Error("SetWinEventHook a échoué.");
    }

    private void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { ForegroundChanged?.Invoke(hwnd); }
        catch (Exception e) { Log.Error("Rappel du hook de premier plan.", e); }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            UnhookWinEvent(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
