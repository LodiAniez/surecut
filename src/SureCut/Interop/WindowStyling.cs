using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SureCut.Interop;

/// <summary>Window-level Win32 tweaks from PRD §5.0 and §7 (tool window, no-activate, topmost, backdrop).</summary>
public static class WindowStyling
{
    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    /// <summary>ARCH-1: no taskbar button, no Alt+Tab entry.</summary>
    public static void MakeToolWindow(Window w) => NativeMethods.AddExStyle(Handle(w), NativeMethods.WS_EX_TOOLWINDOW);

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_NOACTIVATE = 3;

    /// <summary>
    /// A no-activate window still receives WM_MOUSEACTIVATE on the first click; the default
    /// reply (MA_ACTIVATE) makes the system try to activate it and the click is lost. Reply
    /// MA_NOACTIVATE while <paramref name="whenNoActivate"/> is true so the click is delivered.
    /// </summary>
    public static void DeliverClicksWithoutActivation(Window w, Func<bool> whenNoActivate)
    {
        var source = HwndSource.FromHwnd(Handle(w));
        source?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (msg == WM_MOUSEACTIVATE && whenNoActivate())
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>
    /// Windows enforces a minimum tracking width (~136 px) on overlapped windows, which would
    /// force the 56 px menu wider. Lift that limit for this window.
    /// </summary>
    public static void AllowTinyWindow(Window w)
    {
        // Drop every frame style that makes Windows enforce SM_CXMINTRACK.
        var h = Handle(w);
        const int GWL_STYLE = -16;
        const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000,
                   WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
        var style = (long)NativeMethods.GetWindowLongPtr(h, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        NativeMethods.SetWindowLongPtr(h, GWL_STYLE, new IntPtr(style));

        var source = HwndSource.FromHwnd(h);
        source?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize = new POINT { X = 1, Y = 1 };
                System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>
    /// Sizes a window to its content explicitly. WPF's SizeToContent keeps rewriting external
    /// size changes back to the hwnd's creation width, so the launcher windows size themselves.
    /// </summary>
    public static void SizeToContent(Window w, FrameworkElement content, double fixedWidth = double.NaN)
    {
        var widthConstraint = double.IsNaN(fixedWidth) ? double.PositiveInfinity : fixedWidth;
        content.Measure(new Size(widthConstraint, double.PositiveInfinity));
        var size = content.DesiredSize;
        var width = double.IsNaN(fixedWidth) ? Math.Ceiling(size.Width) : fixedWidth;
        var height = Math.Ceiling(size.Height);
        if (Math.Abs(w.Width - width) > 0.5) w.Width = width;
        if (Math.Abs(w.Height - height) > 0.5) w.Height = height;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    public static void SetNoActivate(Window w, bool on)
    {
        var h = Handle(w);
        if (on) NativeMethods.AddExStyle(h, NativeMethods.WS_EX_NOACTIVATE);
        else NativeMethods.RemoveExStyle(h, NativeMethods.WS_EX_NOACTIVATE);
    }

    public static void SetTopmost(Window w, bool on) => NativeMethods.SetTopmost(Handle(w), on);

    public static void ShowNoActivate(Window w)
    {
        if (!w.IsVisible)
        {
            w.ShowActivated = false;
            w.Show();
        }
        NativeMethods.ShowWindow(Handle(w), NativeMethods.SW_SHOWNOACTIVATE);
    }

    public static bool CompositionEnabled()
    {
        try { return NativeMethods.DwmIsCompositionEnabled(out var on) == 0 && on; }
        catch { return false; }
    }

    /// <summary>Windows 11 22H2 (build 22621) introduced DWMWA_SYSTEMBACKDROP_TYPE.</summary>
    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// MENU-11: extend the frame over the whole client area, ask DWM for an Acrylic
    /// (transient-window) backdrop and rounded corners. Returns false when the backdrop is not
    /// available so the caller can paint a solid surface instead.
    /// </summary>
    public static bool TryApplyAcrylic(Window w, bool darkMode)
    {
        var h = Handle(w);
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        int dark = darkMode ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (!BackdropSupported || !CompositionEnabled()) return false;

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (NativeMethods.DwmExtendFrameIntoClientArea(h, ref margins) != 0) return false;

        var source = HwndSource.FromHwnd(h);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;

        int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
        return NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    /// <summary>
    /// MENU-11 for the menu and settings windows: Acrylic when the OS and the user's
    /// Transparency-effects setting allow it, otherwise a solid themed surface with a 1 px border.
    /// </summary>
    public static bool ApplySurface(Window w, System.Windows.Controls.Border surface, Services.ThemeService theme)
    {
        var acrylic = theme.TransparencyEnabled && TryApplyAcrylic(w, theme.IsDark);
        if (acrylic)
        {
            surface.Background = System.Windows.Media.Brushes.Transparent;
            surface.BorderThickness = new Thickness(0);
        }
        else
        {
            RemoveBackdrop(w);
            SetImmersiveDarkMode(w, theme.IsDark);
            surface.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SurfaceSolidBrush");
            surface.BorderThickness = new Thickness(1);
        }
        return acrylic;
    }

    public static void SetImmersiveDarkMode(Window w, bool darkMode)
    {
        int dark = darkMode ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(Handle(w), NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    public static void RemoveBackdrop(Window w)
    {
        var h = Handle(w);
        int none = NativeMethods.DWMSBT_NONE;
        NativeMethods.DwmSetWindowAttribute(h, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        var source = HwndSource.FromHwnd(h);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
    }

    /// <summary>Moves/sizes a window in physical pixels without activating it.</summary>
    public static void Place(Window w, RECT r, bool keepSize = false)
    {
        var flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | (keepSize ? NativeMethods.SWP_NOSIZE : 0);
        NativeMethods.SetWindowPos(Handle(w), IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags);
    }

    public static RECT GetRect(Window w)
    {
        NativeMethods.GetWindowRect(Handle(w), out var r);
        return r;
    }

    /// <summary>DPI scale of the monitor the window is currently on.</summary>
    public static double ScaleOf(Window w)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        return dpi.DpiScaleX;
    }
}
