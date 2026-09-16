using System.Runtime.InteropServices;
using SureCut.Interop;
using SureCut.Models;

namespace SureCut.Services;

/// <summary>One physical monitor, in physical pixels, plus its effective DPI.</summary>
public sealed record MonitorData(IntPtr Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary, uint Dpi)
{
    public double Scale => Dpi / 96.0;
}

/// <summary>Monitor enumeration and the DATA-4 position math.</summary>
public static class Monitors
{
    public static List<MonitorData> All()
    {
        var list = new List<MonitorData>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var m = FromHandle(h);
            if (m is not null) list.Add(m);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Used when enumeration returns nothing (remote-session disconnect, topology transitions):
    /// a stand-in so callers keep working until WM_DISPLAYCHANGE brings the real monitors back.
    /// </summary>
    private static readonly MonitorData Fallback = new(IntPtr.Zero, "", new RECT { Right = 1920, Bottom = 1080 }, new RECT { Right = 1920, Bottom = 1040 }, true, 96);

    public static MonitorData Primary()
    {
        var all = All();
        if (all.Count == 0)
        {
            Logger.Warn("No monitors enumerated; using a stand-in until the display topology settles.");
            return Fallback;
        }
        return all.FirstOrDefault(m => m.IsPrimary) ?? all[0];
    }

    public static MonitorData? ByName(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;
        return All().FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static MonitorData FromPoint(int x, int y)
    {
        var h = NativeMethods.MonitorFromPoint(new POINT { X = x, Y = y }, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return FromHandle(h) ?? Primary();
    }

    public static MonitorData FromRect(RECT r)
    {
        var h = NativeMethods.MonitorFromRect(ref r, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return FromHandle(h) ?? Primary();
    }

    private static MonitorData? FromHandle(IntPtr h)
    {
        if (h == IntPtr.Zero) return null;
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(h, ref info)) return null;
        uint dpi = 96;
        if (NativeMethods.GetDpiForMonitor(h, NativeMethods.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0) dpi = dx;
        return new MonitorData(h, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0, dpi);
    }

    /// <summary>
    /// Resolves a stored position to the monitor it refers to. Falls back to the primary monitor
    /// (and the default position) when the monitor is gone or the button would be off-screen.
    /// </summary>
    public static (MonitorData Monitor, ButtonPosition Position) Resolve(ButtonPosition pos, int buttonDip)
    {
        var monitor = ByName(pos.Monitor);
        if (monitor is null)
        {
            var primary = Primary();
            var def = ButtonPosition.Default();
            def.Monitor = primary.DeviceName;
            return (primary, def);
        }

        var rect = ButtonRect(pos, monitor, buttonDip);
        var work = monitor.WorkArea;
        var fits = rect.Left >= work.Left && rect.Top >= work.Top && rect.Right <= work.Right && rect.Bottom <= work.Bottom;
        if (!fits)
        {
            var def = ButtonPosition.Default();
            def.Monitor = monitor.DeviceName;
            return (monitor, def);
        }
        return (monitor, pos);
    }

    /// <summary>Physical rectangle of the button (not including any shadow padding).</summary>
    public static RECT ButtonRect(ButtonPosition pos, MonitorData m, int buttonDip)
    {
        var s = m.Scale;
        var size = (int)Math.Round(buttonDip * s);
        var ox = (int)Math.Round(pos.OffsetX * s);
        var oy = (int)Math.Round(pos.OffsetY * s);
        var work = m.WorkArea;

        var left = pos.AnchorLeft ? work.Left + ox : work.Right - ox - size;
        var top = pos.AnchorTop ? work.Top + oy : work.Bottom - oy - size;
        return new RECT { Left = left, Top = top, Right = left + size, Bottom = top + size };
    }

    /// <summary>Derives anchor + offsets from where the button currently sits (FAB-6).</summary>
    public static ButtonPosition FromButtonRect(RECT button)
    {
        var m = FromRect(button);
        var work = m.WorkArea;
        var s = m.Scale;

        var top = button.CenterY < work.Top + work.Height / 2;
        var left = button.CenterX < work.Left + work.Width / 2;

        var ox = left ? button.Left - work.Left : work.Right - button.Right;
        var oy = top ? button.Top - work.Top : work.Bottom - button.Bottom;

        return new ButtonPosition
        {
            Monitor = m.DeviceName,
            Anchor = (top ? "top" : "bottom") + "-" + (left ? "left" : "right"),
            OffsetX = Math.Max(0, Math.Round(ox / s)),
            OffsetY = Math.Max(0, Math.Round(oy / s)),
        };
    }

    /// <summary>Clamps a rectangle of the given size into the work area, preferring the given origin.</summary>
    public static RECT ClampToWorkArea(int left, int top, int width, int height, MonitorData m)
    {
        var work = m.WorkArea;
        left = Math.Max(work.Left, Math.Min(left, work.Right - width));
        top = Math.Max(work.Top, Math.Min(top, work.Bottom - height));
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }
}
