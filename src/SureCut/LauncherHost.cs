using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SureCut.Interop;
using SureCut.Models;
using SureCut.Services;
using SureCut.Windows;

namespace SureCut;

public enum HotkeyApplyResult { Bound, Conflict, Invalid, Unbound }

/// <summary>
/// Owns the three windows and all services and implements the behaviors of PRD §5.
/// Everything runs on the UI thread.
/// </summary>
public sealed class LauncherHost : IDisposable
{
    private static readonly string[] AllowedExtensions = { ".exe", ".lnk", ".bat", ".cmd" };

    private readonly ConfigStore _store;
    private readonly LaunchTracker _tracker = new();
    private readonly InputHooks _hooks = new();

    /// <summary>
    /// The foreign window that had focus before the launcher took it (keyboard mode, settings,
    /// or a context menu). Zero when the launcher never took focus. Restored when we let go.
    /// </summary>
    private IntPtr _previousForeground;
    private bool _contextMenuOpen;
    private bool _quitting;

    public AppConfig Config { get; }
    public ThemeService Theme { get; }
    public IconCache Icons { get; }
    public HotkeyService Hotkey { get; } = new();
    public InputHooks Hooks => _hooks;

    public ButtonWindow Button { get; private set; } = null!;
    public MenuWindow? Menu { get; private set; }
    public SettingsWindow? Settings { get; private set; }

    /// <summary>
    /// Where the button actually is right now. Equals <see cref="AppConfig.Position"/> unless the
    /// stored monitor is missing or the stored spot is off-screen, in which case this is the
    /// temporary default and the stored position is left untouched (DATA-4).
    /// </summary>
    public ButtonPosition EffectivePosition { get; private set; }

    public bool MenuOpen { get; private set; }
    public bool SettingsOpen { get; private set; }
    public bool KeyboardMode => Menu?.KeyboardMode ?? false;

    public event Action? FavoritesChanged;

    public LauncherHost(ConfigStore store, AppConfig config, ThemeService theme)
    {
        _store = store;
        Config = config;
        Theme = theme;
        Icons = new IconCache(store.Folder);
        EffectivePosition = config.Position;
    }

    // ---------------------------------------------------------------- lifecycle

    public void Start()
    {
        Button = new ButtonWindow(this);
        WindowStyling.ShowNoActivate(Button);
        PlaceButton();

        Hotkey.Attach(Button.Hwnd);
        Hotkey.Apply(Config.Hotkey);

        StartupService.Apply(Config.StartWithWindows);

        _tracker.WindowAppeared += () => { if (Config.HideOnLaunch) HideButton(); };
        _tracker.WindowClosed += ShowButton;

        _hooks.IsInsideLauncher = IsPointInsideLauncher;
        _hooks.EscapePressed += () => Button.Dispatcher.BeginInvoke(CloseMenu);
        _hooks.ClickedOutside += () => Button.Dispatcher.BeginInvoke(CloseMenu);

        // Preload icons off the critical path so the first menu open is instant.
        Button.Dispatcher.BeginInvoke(() =>
        {
            var dirty = false;
            foreach (var f in Config.Favorites)
            {
                var hadCache = !string.IsNullOrEmpty(f.IconCache);
                Icons.Get(f);
                if (!hadCache && !string.IsNullOrEmpty(f.IconCache)) dirty = true;
            }
            if (dirty) Save();
        }, DispatcherPriority.ApplicationIdle);

        Logger.Info($"Started with {Config.Favorites.Count} favorites; position {Config.Position.Anchor} ({Config.Position.OffsetX},{Config.Position.OffsetY}) on {Config.Position.Monitor}.");
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        Logger.Info("Quit requested.");
        CloseMenu();
        _store.Flush();
        Application.Current.Shutdown();
    }

    /// <summary>Called on sign-out/shutdown so the debounced save is not lost.</summary>
    public void FlushNow() => _store.Flush();

    public void Dispose()
    {
        _hooks.Dispose();
        _tracker.Dispose();
        Hotkey.Dispose();
    }

    private void Save() => _store.Save(Config);

    // ---------------------------------------------------------------- menu (5.0, 5.2)

    public void ToggleMenu(bool keyboard)
    {
        if (MenuOpen) CloseMenu();
        else OpenMenu(keyboard);
    }

    public void OpenMenu(bool keyboard)
    {
        if (_quitting) return;
        if (Button.HiddenBySetting) ShowButton();
        if (MenuOpen && Menu is not null)
        {
            if (keyboard && !Menu.KeyboardMode) { CloseMenu(); }
            else return;
        }

        if (keyboard) RememberForeground();

        Menu ??= new MenuWindow(this);
        Menu.Open(keyboard);
        MenuOpen = true;
        Button.SetOpenState(true);

        if (!keyboard && !_hooks.Install())
            Logger.Warn("Running without low-level hooks: Esc will not close a mouse-opened menu.");
    }

    public void CloseMenu()
    {
        if (!MenuOpen) return;
        _hooks.Uninstall();
        _hooks.HandleEscape = true;
        if (SettingsOpen) { SettingsOpen = false; Settings?.CloseSettings(); }
        Menu?.CloseMenu();
        MenuOpen = false;
        Button.SetOpenState(false);
        RestoreForeground();
    }

    /// <summary>Records the user's window before the launcher activates one of its own (ARCH-5 / ARCH-7).</summary>
    private void RememberForeground()
    {
        if (_previousForeground != IntPtr.Zero) return;
        var fg = NativeMethods.GetForegroundWindow();
        if (fg != IntPtr.Zero && !IsOurWindow(fg)) _previousForeground = fg;
    }

    private void RestoreForeground()
    {
        var prev = _previousForeground;
        _previousForeground = IntPtr.Zero;
        if (prev != IntPtr.Zero && NativeMethods.IsWindow(prev)) NativeMethods.SetForegroundWindow(prev);
    }

    /// <summary>An activated launcher window lost activation, so the user may have gone elsewhere.</summary>
    public void OnLauncherWindowDeactivated()
    {
        if (!MenuOpen || _contextMenuOpen) return;
        if (!KeyboardMode && !SettingsOpen) return; // mouse mode relies on the hooks

        Button.Dispatcher.BeginInvoke(() =>
        {
            if (!MenuOpen || _contextMenuOpen) return;
            if (!KeyboardMode && !SettingsOpen) return; // settings were closed on purpose meanwhile
            var menuActive = Menu?.IsActive == true;
            var settingsActive = Settings?.IsActive == true;
            if (!menuActive && !settingsActive && !IsOurWindow(NativeMethods.GetForegroundWindow()))
            {
                _previousForeground = IntPtr.Zero; // the user already chose a new foreground window
                CloseMenu();
            }
        }, DispatcherPriority.Background);
    }

    public void ToggleSettings()
    {
        if (SettingsOpen) CloseSettings();
        else OpenSettings();
    }

    public void OpenSettings()
    {
        if (!MenuOpen) OpenMenu(keyboard: false);
        RememberForeground();
        Settings ??= new SettingsWindow(this);
        SettingsOpen = true;
        _hooks.HandleEscape = false; // the settings window handles Esc itself while it has focus
        Settings.Open();
    }

    public void CloseSettings()
    {
        if (!SettingsOpen) return;
        SettingsOpen = false;            // before Hide(): Deactivated fires synchronously
        _hooks.HandleEscape = true;
        Settings?.CloseSettings();
        if (Menu is { KeyboardMode: true }) Menu.Activate();
        else if (_previousForeground != IntPtr.Zero && NativeMethods.IsWindow(_previousForeground))
            NativeMethods.SetForegroundWindow(_previousForeground); // menu stays open, user gets focus back
    }

    /// <summary>Opens a WPF context menu from a window that may not be active (MENU-9, SET-8).</summary>
    public void ShowContextMenu(ContextMenu menu, Window owner)
    {
        var weWereForeground = IsOurWindow(NativeMethods.GetForegroundWindow());
        RememberForeground();
        _contextMenuOpen = true;
        _hooks.Suspended = true;

        menu.PlacementTarget = owner;
        menu.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            _hooks.Suspended = false;
            Button.Dispatcher.BeginInvoke(() =>
            {
                // Give focus back only if we still hold it: a launch or an opened settings
                // window has already decided who owns the foreground.
                if (_quitting || SettingsOpen || KeyboardMode || !MenuOpen) return;
                var prev = _previousForeground;
                if (prev != IntPtr.Zero && NativeMethods.IsWindow(prev) && IsOurWindow(NativeMethods.GetForegroundWindow()))
                    NativeMethods.SetForegroundWindow(prev);
            }, DispatcherPriority.Background);
        };

        // A popup needs mouse capture, which only the foreground thread gets.
        if (!weWereForeground) NativeMethods.SetForegroundWindow(WindowStyling.Handle(owner));
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- launching (MENU-7, MENU-8, SET-3)

    public void Launch(Favorite f, Action<string> onError)
    {
        var foregroundBefore = _previousForeground != IntPtr.Zero ? _previousForeground : NativeMethods.GetForegroundWindow();
        if (IsOurWindow(foregroundBefore)) foregroundBefore = IntPtr.Zero;

        var result = ProgramLauncher.Launch(f);
        if (!result.Success)
        {
            onError(result.ErrorMessage ?? "Can't open");
            return;
        }

        _previousForeground = IntPtr.Zero; // let the launched program take the foreground
        CloseMenu();

        if (Config.HideOnLaunch) _tracker.Begin(result.ProcessId, foregroundBefore);

        // FAB-4: some programs briefly make themselves topmost on startup.
        ReassertTopmostAfter(TimeSpan.FromMilliseconds(500));
        ReassertTopmostAfter(TimeSpan.FromSeconds(2));
    }

    public void OpenFileLocation(Favorite f)
    {
        _previousForeground = IntPtr.Zero;
        CloseMenu();
        ProgramLauncher.OpenFileLocation(f);
    }

    private void ReassertTopmostAfter(TimeSpan delay)
    {
        var t = new DispatcherTimer { Interval = delay };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (Button.IsVisible) WindowStyling.SetTopmost(Button, true);
        };
        t.Start();
    }

    // ---------------------------------------------------------------- favorites (5.3)

    public bool AddFavorite(string path, out string? error)
    {
        error = null;
        var ext = Path.GetExtension(path);
        if (!File.Exists(path) || !AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            error = "Only programs and shortcuts can be added";
            ErrorBubbleOnButton(error);
            return false;
        }
        if (Config.Favorites.Any(f => string.Equals(f.Target, path, StringComparison.OrdinalIgnoreCase)))
        {
            error = "Already in favorites";
            ErrorBubbleOnButton(error);
            return false;
        }

        var fav = new Favorite { Name = IconCache.DefaultName(path), Target = path };
        Icons.Extract(fav);
        Config.Favorites.Add(fav);
        Save();
        Logger.Info($"Added favorite '{fav.Name}' -> {path}");
        RaiseFavoritesChanged(expandIfNeeded: true);
        return true;
    }

    private void ErrorBubbleOnButton(string text) =>
        Controls.ErrorBubble.Show(Button.Fab, text, toLeft: !EffectivePosition.AnchorLeft);

    public void RemoveFavorite(Favorite f)
    {
        if (!Config.Favorites.Remove(f)) return;
        Icons.Forget(f);
        Save();
        RaiseFavoritesChanged();
    }

    public void MoveFavorite(Favorite f, int delta)
    {
        var i = Config.Favorites.IndexOf(f);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Config.Favorites.Count) return;
        (Config.Favorites[i], Config.Favorites[j]) = (Config.Favorites[j], Config.Favorites[i]);
        Save();
        RaiseFavoritesChanged();
    }

    public void MoveFavoriteRelativeTo(string draggedId, Favorite target, bool before)
    {
        var dragged = Config.Favorites.FirstOrDefault(x => x.Id == draggedId);
        if (dragged is null || dragged == target) return;
        Config.Favorites.Remove(dragged);
        var idx = Config.Favorites.IndexOf(target);
        Config.Favorites.Insert(before ? idx : idx + 1, dragged);
        Save();
        RaiseFavoritesChanged();
    }

    /// <summary>
    /// A rename only changes a label: the menu is rebuilt for its tooltips, but the settings rows
    /// are left alone. The row's own text box already shows the new name, and rebuilding here
    /// would detach whatever control the user clicked to commit the edit.
    /// </summary>
    public void RenameFavorite(Favorite f, string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name) || name == f.Name) return;
        f.Name = name;
        Save();
        if (Menu is not null && MenuOpen) Menu.Rebuild();
    }

    public void PickAndAddProgram()
    {
        _hooks.Suspended = true;
        _contextMenuOpen = true; // keep deactivation logic quiet while the dialog is up
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Add a program",
                Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*",
                Multiselect = true,
                DereferenceLinks = false,
                CheckFileExists = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            };
            var owner = SettingsOpen ? (Window?)Settings : Menu;
            var ok = owner is { IsVisible: true } ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (ok == true)
                foreach (var file in dlg.FileNames) AddFavorite(file, out _);
        }
        finally
        {
            _hooks.Suspended = false;
            _contextMenuOpen = false;
            if (Settings is { IsVisible: true }) Settings.Activate();
        }
    }

    private void RaiseFavoritesChanged(bool expandIfNeeded = false)
    {
        if (Menu is not null && MenuOpen)
        {
            Menu.Rebuild();
            if (expandIfNeeded && Config.Favorites.Count > MenuWindow.VisibleCount) Menu.SetExpanded(true);
        }
        FavoritesChanged?.Invoke();
    }

    /// <summary>Re-lays out whatever is open after the button moved or resized.</summary>
    private void RefreshOpenWindows()
    {
        if (!MenuOpen || Menu is null) return;
        Menu.Rebuild();
        Menu.Reposition();
        Settings?.Reposition();
    }

    // ---------------------------------------------------------------- settings (5.4)

    public void SetHideOnLaunch(bool on)
    {
        Config.HideOnLaunch = on;
        if (!on) { _tracker.Cancel(); ShowButton(); }
        Save();
    }

    public void SetStartWithWindows(bool on)
    {
        Config.StartWithWindows = on;
        StartupService.Apply(on);
        Save();
    }

    public void SetButtonSize(string size)
    {
        size = AppConfig.NormalizeSize(size);
        if (size == Config.ButtonSize) return;
        Config.ButtonSize = size;
        Button.ApplySize();
        Button.Dispatcher.BeginInvoke(() =>
        {
            PlaceButton();
            RefreshOpenWindows();
        }, DispatcherPriority.Loaded);
        Save();
    }

    /// <summary>
    /// Tries the new combination first; the previous one is kept (and re-registered) if the new
    /// one cannot be bound, so a conflict never leaves the user without a shortcut (SET-3a).
    /// </summary>
    public HotkeyApplyResult SetHotkey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Hotkey.Unregister();
            Config.Hotkey = null;
            Save();
            return HotkeyApplyResult.Unbound;
        }
        if (!HotkeyCombo.TryParse(text, out var combo)) return HotkeyApplyResult.Invalid;

        var previous = Config.Hotkey;
        if (Hotkey.Apply(combo.Text))
        {
            Config.Hotkey = combo.Text;
            Save();
            return HotkeyApplyResult.Bound;
        }

        var result = Hotkey.LastAttemptConflicted ? HotkeyApplyResult.Conflict : HotkeyApplyResult.Invalid;
        if (!string.IsNullOrEmpty(previous)) Hotkey.Apply(previous);
        return result;
    }

    // ---------------------------------------------------------------- position (FAB-1, FAB-6, SET-6, DATA-4)

    /// <summary>
    /// Places the button at the stored position, or at a temporary default when the stored
    /// monitor is missing or the spot is off-screen. Only the very first run persists the
    /// default; later fallbacks are transient so the user's spot survives dock/undock.
    /// </summary>
    private void PlaceButton()
    {
        var firstRun = string.IsNullOrEmpty(Config.Position.Monitor);
        var (monitor, pos) = Monitors.Resolve(Config.Position, Config.ButtonSizePx);
        if (!ReferenceEquals(pos, Config.Position))
        {
            if (firstRun)
            {
                Config.Position = pos;
                Save();
            }
            else if (!ReferenceEquals(EffectivePosition, pos))
            {
                Logger.Info($"Stored position on {Config.Position.Monitor} is unavailable; showing the button at the default on {monitor.DeviceName} until it comes back.");
            }
        }
        EffectivePosition = ReferenceEquals(pos, Config.Position) ? Config.Position : pos;

        var rect = Monitors.ButtonRect(EffectivePosition, monitor, Config.ButtonSizePx);
        Button.PlaceFab(rect, monitor.Scale);
    }

    public void OnButtonDragEnded()
    {
        Config.Position = Monitors.FromButtonRect(Button.FabRect);
        PlaceButton(); // snap to the exact stored offsets
        Save();
        Logger.Info($"Button moved: {Config.Position.Anchor} ({Config.Position.OffsetX},{Config.Position.OffsetY}) on {Config.Position.Monitor}");
    }

    public void ResetPosition()
    {
        var primary = Monitors.Primary();
        Config.Position = ButtonPosition.Default();
        Config.Position.Monitor = primary.DeviceName;
        PlaceButton();
        Save();
        Logger.Info("Button position reset.");
        RefreshOpenWindows();
    }

    public void OnDisplayChanged()
    {
        PlaceButton();
        RefreshOpenWindows();
    }

    // ---------------------------------------------------------------- visibility (SET-3, hotkey, single instance)

    public void ShowButton()
    {
        if (_quitting) return;
        Button.SetHiddenBySetting(false);
        WindowStyling.SetTopmost(Button, true);
    }

    public void HideButton()
    {
        CloseMenu();
        Button.SetHiddenBySetting(true);
    }

    public void OnHotkeyPressed()
    {
        if (Button.HiddenBySetting)
        {
            _tracker.Cancel();
            ShowButton();
            return;
        }
        if (MenuOpen) CloseMenu();
        else OpenMenu(keyboard: true);
    }

    public void OnShowMeMessage()
    {
        _tracker.Cancel();
        ShowButton();
        Button.Flash();
    }

    // ---------------------------------------------------------------- helpers

    public bool IsPointInsideLauncher(int x, int y)
    {
        if (Button.IsVisible && Button.FabRect.Contains(x, y)) return true;
        if (MenuOpen && Menu is { IsVisible: true } && WindowStyling.GetRect(Menu).Contains(x, y)) return true;
        if (SettingsOpen && Settings is { IsVisible: true } && WindowStyling.GetRect(Settings).Contains(x, y)) return true;
        return false;
    }

    private static bool IsOurWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return NativeMethods.ProcessIdOf(hwnd) == (uint)Environment.ProcessId;
    }
}
