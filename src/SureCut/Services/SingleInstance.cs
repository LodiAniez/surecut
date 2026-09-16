using SureCut.Interop;

namespace SureCut.Services;

/// <summary>
/// LIFE-1: named mutex; a second copy broadcasts a registered message and exits, the running
/// instance shows and flashes its button.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SureCut.SingleInstance";
    private const string MessageName = "SureCut.ShowMe";

    private Mutex? _mutex;

    public static uint ShowMeMessage { get; } = NativeMethods.RegisterWindowMessage(MessageName);

    /// <summary>Returns true when this process owns the instance.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) return true;
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static void SignalRunningInstance()
    {
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowMeMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        _mutex?.Dispose();
        _mutex = null;
    }
}
