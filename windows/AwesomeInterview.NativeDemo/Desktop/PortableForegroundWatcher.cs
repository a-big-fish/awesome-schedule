using System;
using System.Runtime.InteropServices;

namespace AwesomeInterview.NativeDemo;

internal sealed class PortableForegroundWatcher : IDisposable
{
    const uint ForegroundChanged = 3, SkipOwnProcess = 2;
    readonly WinEvent callback;
    nint hook;

    internal PortableForegroundWatcher(Action changed)
    {
        callback = (_, _, window, _, _, _, _) => { if (window != 0 && hook != 0) changed(); };
        hook = SetWinEventHook(ForegroundChanged, ForegroundChanged, 0, callback, 0, 0, SkipOwnProcess);
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWinEvent(hook);
        hook = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate void WinEvent(nint hook, uint type, nint window, int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(nint hook);
}
