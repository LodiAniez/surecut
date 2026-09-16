using System.Runtime.InteropServices;
using SureCut.Interop;

namespace SureCut.Services;

/// <summary>
/// ARCH-6: while the mouse-opened (non-activated) menu is showing, catch Esc and clicks outside
/// the launcher's windows with low-level hooks. Installed only while the menu is open.
/// </summary>
public sealed class InputHooks : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardProc;
    private readonly NativeMethods.HookProc _mouseProc;
    private IntPtr _keyboardHook, _mouseHook;

    /// <summary>Returns true when the screen point (physical px) is inside one of our windows.</summary>
    public Func<int, int, bool> IsInsideLauncher { get; set; } = (_, _) => false;

    /// <summary>When true, outside clicks are ignored (a context menu or dialog owns the interaction).</summary>
    public bool Suspended { get; set; }

    /// <summary>
    /// When false, Esc passes through untouched. Set while the settings window is active, since
    /// it receives keys natively and needs Esc for cancelling a shortcut capture.
    /// </summary>
    public bool HandleEscape { get; set; } = true;

    public event Action? EscapePressed;
    public event Action? ClickedOutside;

    public bool IsInstalled => _keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero;

    public InputHooks()
    {
        // Keep the delegates alive for as long as the hooks exist.
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public bool Install()
    {
        if (IsInstalled) return true;
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            Logger.Warn($"Low-level hooks failed: {Marshal.GetLastWin32Error()}");
            Uninstall();
            return false;
        }
        return true;
    }

    public void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    private IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !Suspended && HandleEscape && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.vkCode == NativeMethods.VK_ESCAPE)
            {
                EscapePressed?.Invoke();
                return new IntPtr(1); // swallow Esc so the underlying app does not also react
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !Suspended)
        {
            var msg = (int)wParam;
            if (msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_XBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (!IsInsideLauncher(data.pt.X, data.pt.Y)) ClickedOutside?.Invoke();
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
