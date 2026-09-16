using System.Runtime.InteropServices;

namespace SureCut.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }

[StructLayout(LayoutKind.Sequential)]
public struct MARGINS { public int Left, Right, Top, Bottom; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SHELLEXECUTEINFO
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public string? lpVerb;
    public string lpFile;
    public string? lpParameters;
    public string? lpDirectory;
    public int nShow;
    public IntPtr hInstApp;
    public IntPtr lpIDList;
    public string? lpClass;
    public IntPtr hkeyClass;
    public uint dwHotKey;
    public IntPtr hIcon;
    public IntPtr hProcess;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct PROCESSENTRY32
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public IntPtr th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
}

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct APPBARDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(SIZE size, uint flags, out IntPtr phbm);
}

public static class NativeMethods
{
    // ----- window styles -----
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr HWND_BROADCAST = new(0xffff);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOWNA = 8;

    // ----- messages -----
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    public const int WM_WTSSESSION_CHANGE = 0x02B1;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int WTS_SESSION_UNLOCK = 0x8;
    public const int NOTIFY_FOR_THIS_SESSION = 0;

    public const int VK_ESCAPE = 0x1B;

    // ----- hotkeys -----
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // ----- hooks -----
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    // ----- WinEvents -----
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;

    // ----- ShellExecuteEx -----
    public const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    public const uint SEE_MASK_NOASYNC = 0x00000100;
    public const uint ASFW_ANY = 0xFFFFFFFF;

    // ----- DWM -----
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_DEFAULT = 0;
    public const int DWMWCP_DONOTROUND = 1;
    public const int DWMWCP_ROUND = 2;
    public const int DWMWCP_ROUNDSMALL = 3;
    public const int DWMSBT_AUTO = 0;
    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    public const int DWMSBT_TABBEDWINDOW = 4;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    // ----- monitors -----
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;

    // ----- Toolhelp -----
    public const uint TH32CS_SNAPPROCESS = 0x00000002;

    // ----- SIIGBF -----
    public const uint SIIGBF_RESIZETOFIT = 0x00;
    public const uint SIIGBF_BIGGERSIZEOK = 0x01;
    public const uint SIIGBF_ICONONLY = 0x04;

    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    // ----- user32 -----
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint dwProcessId);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    public const int VK_LBUTTON = 0x01;
    public static bool IsLeftButtonDown() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    // ----- shcore -----
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ----- dwmapi -----
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
    [DllImport("dwmapi.dll")] public static extern int DwmIsCompositionEnabled(out bool enabled);
    [DllImport("dwmapi.dll")] public static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

    // ----- app bars (taskbar) -----
    public const uint ABM_GETSTATE = 4;
    public const uint ABM_GETTASKBARPOS = 5;
    public const uint ABM_SETSTATE = 10;
    public const uint ABM_GETAUTOHIDEBAREX = 11;
    public const uint ABS_AUTOHIDE = 1;
    public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    [DllImport("shell32.dll")] public static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ----- shell32 -----
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface, IidParameterIndex = 2)] out object ppv);

    // ----- kernel32 -----
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern uint GetProcessId(IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ----- gdi32 -----
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);

    // ----- wtsapi32 -----
    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);
    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    // ----- helpers -----
    public static int GetExStyle(IntPtr hwnd) => (int)(long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hwnd, int style) => SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));

    public static void AddExStyle(IntPtr hwnd, int flags) => SetExStyle(hwnd, GetExStyle(hwnd) | flags);

    public static void RemoveExStyle(IntPtr hwnd, int flags) => SetExStyle(hwnd, GetExStyle(hwnd) & ~flags);

    public static void SetTopmost(IntPtr hwnd, bool topmost) =>
        SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static uint ProcessIdOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static bool ClientAreaAnimationEnabled()
    {
        bool on = true;
        return SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, ref on, 0) ? on : true;
    }
}
