using System.Runtime.InteropServices;
using System.Windows.Input;
using SureCut.Interop;

namespace SureCut.Services;

/// <summary>A parsed "Ctrl+Alt+Space" style combination.</summary>
public readonly record struct HotkeyCombo(uint Modifiers, uint VirtualKey, string Text)
{
    public static bool TryParse(string? text, out HotkeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint mods = 0;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; break;
                case "alt": mods |= NativeMethods.MOD_ALT; break;
                case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                case "win": case "windows": mods |= NativeMethods.MOD_WIN; break;
                default:
                    if (!TryParseKey(raw, out key)) return false;
                    break;
            }
        }
        if (key == Key.None || mods == 0) return false;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        combo = new HotkeyCombo(mods, vk, Format(mods, key));
        return true;
    }

    public static string Format(uint mods, Key key)
    {
        var parts = new List<string>(4);
        if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    public static uint ModifiersFrom(ModifierKeys m)
    {
        uint mods = 0;
        if (m.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    public static string KeyName(Key k) => k switch
    {
        Key.Space => "Space",
        Key.Escape => "Esc",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        _ when k >= Key.D0 && k <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        _ when k >= Key.NumPad0 && k <= Key.NumPad9 => "Num" + (k - Key.NumPad0),
        _ => k.ToString(),
    };

    private static bool TryParseKey(string raw, out Key key)
    {
        key = Key.None;
        switch (raw.ToLowerInvariant())
        {
            case "space": key = Key.Space; return true;
            case "esc": case "escape": key = Key.Escape; return true;
            case "enter": case "return": key = Key.Return; return true;
            case "backspace": key = Key.Back; return true;
            case "tab": key = Key.Tab; return true;
            case "`": key = Key.OemTilde; return true;
            case "-": key = Key.OemMinus; return true;
            case "=": key = Key.OemPlus; return true;
            case ",": key = Key.OemComma; return true;
            case ".": key = Key.OemPeriod; return true;
            case "/": key = Key.OemQuestion; return true;
            case ";": key = Key.OemSemicolon; return true;
            case "'": key = Key.OemQuotes; return true;
            case "[": key = Key.OemOpenBrackets; return true;
            case "]": key = Key.OemCloseBrackets; return true;
            case "\\": key = Key.OemPipe; return true;
        }
        if (raw.Length == 1 && char.IsDigit(raw[0])) { key = Key.D0 + (raw[0] - '0'); return true; }
        if (raw.Length == 1 && char.IsLetter(raw[0])) { key = Key.A + (char.ToUpperInvariant(raw[0]) - 'A'); return true; }
        if (raw.StartsWith("num", StringComparison.OrdinalIgnoreCase) && raw.Length == 4 && char.IsDigit(raw[3])) { key = Key.NumPad0 + (raw[3] - '0'); return true; }
        return Enum.TryParse(raw, ignoreCase: true, out key) && key != Key.None;
    }
}

/// <summary>SET-3a: RegisterHotKey on the button window; WM_HOTKEY is routed by the window's hook.</summary>
public sealed class HotkeyService : IDisposable
{
    public const int HotkeyId = 0x5C01;

    private IntPtr _hwnd;
    private HotkeyCombo? _registered;

    public bool IsBound => _registered is not null;
    public string? BoundText => _registered?.Text;

    /// <summary>Set when the last registration attempt failed because the combination is in use.</summary>
    public bool LastAttemptConflicted { get; private set; }

    public void Attach(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Returns true when the hotkey is now registered. Null/invalid text unbinds.</summary>
    public bool Apply(string? text)
    {
        Unregister();
        LastAttemptConflicted = false;
        if (_hwnd == IntPtr.Zero || !HotkeyCombo.TryParse(text, out var combo)) return false;

        if (NativeMethods.RegisterHotKey(_hwnd, HotkeyId, combo.Modifiers | NativeMethods.MOD_NOREPEAT, combo.VirtualKey))
        {
            _registered = combo;
            Logger.Info($"Hotkey registered: {combo.Text}");
            return true;
        }

        var err = Marshal.GetLastWin32Error();
        LastAttemptConflicted = err == 1409; // ERROR_HOTKEY_ALREADY_REGISTERED
        Logger.Warn($"RegisterHotKey failed for {combo.Text}: error {err}");
        return false;
    }

    /// <summary>Re-registers after sleep / unlock, when some shells drop hotkeys.</summary>
    public void Reassert()
    {
        if (_registered is { } c && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
            NativeMethods.RegisterHotKey(_hwnd, HotkeyId, c.Modifiers | NativeMethods.MOD_NOREPEAT, c.VirtualKey);
        }
    }

    public void Unregister()
    {
        if (_registered is not null && _hwnd != IntPtr.Zero) NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        _registered = null;
    }

    public void Dispose() => Unregister();
}
