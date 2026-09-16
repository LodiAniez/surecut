using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SureCut.Interop;

namespace SureCut.Services;

/// <summary>
/// Follows Windows light/dark mode, accent color, transparency and animation settings live
/// (PRD §6 Theme, §10). Publishes brushes into Application.Resources so DynamicResource
/// bindings update without a restart.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    public bool IsDark { get; private set; }
    public bool TransparencyEnabled { get; private set; } = true;
    public bool AnimationsEnabled { get; private set; } = true;
    public Color Accent { get; private set; } = Color.FromRgb(0x00, 0x67, 0xC0);

    public event Action? Changed;

    public ThemeService()
    {
        Refresh();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Window or UserPreferenceCategory.Accessibility)
            Application.Current?.Dispatcher.BeginInvoke(Refresh);
    }

    /// <summary>Also called from the button window's WndProc on WM_SETTINGCHANGE / colorization changes.</summary>
    public void Refresh()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            IsDark = (personalize?.GetValue("AppsUseLightTheme") as int? ?? 1) == 0;
            TransparencyEnabled = (personalize?.GetValue("EnableTransparency") as int? ?? 1) != 0;
        }
        catch { IsDark = false; TransparencyEnabled = true; }

        AnimationsEnabled = NativeMethods.ClientAreaAnimationEnabled();
        Accent = ReadAccent();

        Publish();
        Changed?.Invoke();
    }

    private static Color ReadAccent()
    {
        try
        {
            using var dwm = Registry.CurrentUser.OpenSubKey(DwmKey);
            if (dwm?.GetValue("AccentColor") is int abgr)
            {
                var v = unchecked((uint)abgr);
                return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
            }
        }
        catch { /* fall through */ }

        try
        {
            if (NativeMethods.DwmGetColorizationColor(out var argb, out _) == 0)
                return Color.FromRgb((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
        }
        catch { /* fall through */ }

        return Color.FromRgb(0x00, 0x67, 0xC0);
    }

    /// <summary>Section 10 values.</summary>
    private void Publish()
    {
        var res = Application.Current?.Resources;
        if (res is null) return;

        Color surface, surfaceSolid, text, muted, hover, press, stroke, danger, tooltipBg;
        if (IsDark)
        {
            surface = Color.FromArgb(0xE0, 0x2C, 0x2C, 0x2C);       // rgba(44,44,44,.88)
            surfaceSolid = Color.FromRgb(0x2C, 0x2C, 0x2C);
            text = Color.FromRgb(0xF2, 0xF2, 0xF2);
            muted = Color.FromRgb(0xA8, 0xA8, 0xA8);
            hover = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);         // 6 % white
            press = Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF);         // 11 % white
            stroke = Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF);        // 9 % white
            danger = Color.FromRgb(0xFF, 0x99, 0xA4);
            tooltipBg = Color.FromRgb(0x2C, 0x2C, 0x2C);
        }
        else
        {
            surface = Color.FromArgb(0xDB, 0xF9, 0xF9, 0xF9);       // rgba(249,249,249,.86)
            surfaceSolid = Color.FromRgb(0xF9, 0xF9, 0xF9);
            text = Color.FromRgb(0x1B, 0x1B, 0x1B);
            muted = Color.FromRgb(0x5C, 0x5C, 0x5C);
            hover = Color.FromArgb(0x0B, 0x00, 0x00, 0x00);         // 4.5 % black
            press = Color.FromArgb(0x17, 0x00, 0x00, 0x00);         // 9 % black
            stroke = Color.FromArgb(0x14, 0x00, 0x00, 0x00);        // 8 % black
            danger = Color.FromRgb(0xC4, 0x2B, 0x1C);
            tooltipBg = Color.FromRgb(0xF9, 0xF9, 0xF9);
        }

        var accent = Accent;
        // Readable ink on the accent: dark ink for light accents (Windows dark-mode accents are light).
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        var accentInk = luminance > 0.6 ? Color.FromRgb(0x0D, 0x1A, 0x24) : Colors.White;

        Set(res, "SurfaceBrush", surface);
        Set(res, "SurfaceSolidBrush", surfaceSolid);
        Set(res, "TooltipBrush", tooltipBg);
        Set(res, "TextBrush", text);
        Set(res, "MutedBrush", muted);
        Set(res, "HoverBrush", hover);
        Set(res, "PressBrush", press);
        Set(res, "StrokeBrush", stroke);
        Set(res, "DangerBrush", danger);
        Set(res, "AccentBrush", accent);
        Set(res, "AccentInkBrush", accentInk);
        Set(res, "FieldBrush", IsDark ? Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        res["ShadowColor"] = Colors.Black;
        res["ShadowOpacity"] = IsDark ? 0.5 : 0.18;
    }

    private static void Set(ResourceDictionary res, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        res[key] = b;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
