using System.Runtime.InteropServices;
using System.Windows.Threading;
using SureCut.Interop;

namespace SureCut.Services;

/// <summary>
/// SET-3: after a launch, find the program's window (even when the launched process is a stub
/// that hands off to a child or an existing instance), then report when that window is destroyed.
/// </summary>
public sealed class LaunchTracker : IDisposable
{
    private static readonly TimeSpan AppearTimeout = TimeSpan.FromSeconds(10);

    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private NativeMethods.WinEventDelegate? _foregroundProc;
    private NativeMethods.WinEventDelegate? _destroyProc;
    private IntPtr _foregroundHook, _destroyHook;
    private DispatcherTimer? _timeout, _poll;

    private uint _launchedPid, _previousForegroundPid;
    private IntPtr _trackedWindow;

    /// <summary>The launched program's window appeared (hide the button now).</summary>
    public event Action? WindowAppeared;

    /// <summary>The tracked window is gone (show the button again).</summary>
    public event Action? WindowClosed;

    /// <summary>No qualifying window showed up within the timeout (leave the button visible).</summary>
    public event Action? GaveUp;

    public bool IsTracking => _trackedWindow != IntPtr.Zero;

    public void Begin(uint launchedPid, IntPtr previousForeground)
    {
        Cancel();
        _launchedPid = launchedPid;
        _previousForegroundPid = previousForeground == IntPtr.Zero ? 0 : NativeMethods.ProcessIdOf(previousForeground);

        _foregroundProc = OnForeground;
        _foregroundHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _timeout = new DispatcherTimer { Interval = AppearTimeout };
        _timeout.Tick += (_, _) =>
        {
            StopWaitingForWindow();
            Logger.Info("Launch tracker: no window appeared within timeout.");
            GaveUp?.Invoke();
        };
        _timeout.Start();
    }

    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd)) return;
        var pid = NativeMethods.ProcessIdOf(hwnd);
        if (pid == 0 || pid == _ownPid) return;

        var qualifies = pid == _launchedPid
                        || (_launchedPid != 0 && IsDescendant(pid, _launchedPid))
                        || (pid != _previousForegroundPid);
        if (!qualifies) return;

        StopWaitingForWindow();
        _trackedWindow = hwnd;
        Logger.Info($"Launch tracker: tracking window 0x{hwnd.ToInt64():X} (pid {pid}).");
        WindowAppeared?.Invoke();

        _destroyProc = OnDestroy;
        _destroyHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _destroyProc, pid, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _poll.Tick += (_, _) => { if (!NativeMethods.IsWindow(_trackedWindow)) Closed(); };
        _poll.Start();
    }

    private void OnDestroy(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == _trackedWindow && idObject == NativeMethods.OBJID_WINDOW && idChild == NativeMethods.CHILDID_SELF) Closed();
    }

    private void Closed()
    {
        if (_trackedWindow == IntPtr.Zero) return;
        Logger.Info("Launch tracker: tracked window closed.");
        Cancel();
        WindowClosed?.Invoke();
    }

    private void StopWaitingForWindow()
    {
        _timeout?.Stop(); _timeout = null;
        if (_foregroundHook != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        _foregroundProc = null;
    }

    public void Cancel()
    {
        StopWaitingForWindow();
        _poll?.Stop(); _poll = null;
        if (_destroyHook != IntPtr.Zero) { NativeMethods.UnhookWinEvent(_destroyHook); _destroyHook = IntPtr.Zero; }
        _destroyProc = null;
        _trackedWindow = IntPtr.Zero;
    }

    /// <summary>Walks parent PIDs via Toolhelp (PRD SET-3 step 3).</summary>
    private static bool IsDescendant(uint pid, uint ancestor)
    {
        var parents = new Dictionary<uint, uint>();
        var snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return false;
        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (NativeMethods.Process32First(snap, ref entry))
            {
                do { parents[entry.th32ProcessID] = entry.th32ParentProcessID; }
                while (NativeMethods.Process32Next(snap, ref entry));
            }
        }
        finally { NativeMethods.CloseHandle(snap); }

        var current = pid;
        for (var depth = 0; depth < 10 && parents.TryGetValue(current, out var parent) && parent != 0; depth++)
        {
            if (parent == ancestor) return true;
            current = parent;
        }
        return false;
    }

    public void Dispose() => Cancel();
}
