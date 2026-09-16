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
    }

    // ---------------------------------------------------------------- lifecycle

    public void Start()
    {
        Button = new ButtonWindow(this);
        WindowStyling.ShowNoActivate(Button);
        PlaceButtonFromConfig(save: true);

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

        _previousForeground = keyboard ? NativeMethods.GetForegroundWindow() : IntPtr.Zero;
        if (IsOurWindow(_previousForeground)) _previousForeground = IntPtr.Zero;

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
        if (SettingsOpen) { Settings?.CloseSettings(); SettingsOpen = false; }
        Menu?.CloseMenu();
        MenuOpen = false;
        Button.SetOpenState(false);

        // ARCH-5 / ARCH-7: hand focus back to whoever had it.
        if (_previousForeground != IntPtr.Zero && NativeMethods.IsWindow(_previousForeground))
            NativeMethods.SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    /// <summary>Keyboard mode only: both launcher windows lost activation, so the user went elsewhere.</summary>
    public void OnLauncherWindowDeactivated()
    {
        if (!MenuOpen || _contextMenuOpen) return;
        if (!KeyboardMode && !SettingsOpen) return; // mouse mode relies on the hooks

        Button.Dispatcher.BeginInvoke(() =>
        {
            if (!MenuOpen || _contextMenuOpen) return;
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
        if (_previousForeground == IntPtr.Zero)
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (!IsOurWindow(fg)) _previousForeground = fg;
        }
        Settings ??= new SettingsWindow(this);
        Settings.Open();
        SettingsOpen = true;
    }

    public void CloseSettings()
    {
        if (!SettingsOpen) return;
        Settings?.CloseSettings();
        SettingsOpen = false;
        if (Menu is { KeyboardMode: true }) Menu.Activate();
    }

    /// <summary>Opens a WPF context menu from a window that may not be active (MENU-9, SET-8).</summary>
    public void ShowContextMenu(ContextMenu menu, Window owner)
    {
        var prev = NativeMethods.GetForegroundWindow();
        var weWereForeground = IsOurWindow(prev);
        _contextMenuOpen = true;
        _hooks.Suspended = true;

        menu.PlacementTarget = owner;
        menu.Closed += (_, _) =>
        {
            _contextMenuOpen = false;
            _hooks.Suspended = false;
            Button.Dispatcher.BeginInvoke(() =>
            {
                if (SettingsOpen || KeyboardMode || _quitting) return;
                if (!weWereForeground && prev != IntPtr.Zero && NativeMethods.IsWindow(prev)) NativeMethods.SetForegroundWindow(prev);
            }, DispatcherPriority.Background);
        };

        // A popup needs mouse capture, which only the foreground thread gets.
        if (!weWereForeground) NativeMethods.SetForegroundWindow(WindowStyling.Handle(owner));
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- launching (MENU-7, MENU-8, SET-3)

    public void Launch(Favorite f, Action<string> onError)
    {
        var foregroundBefore = KeyboardMode || SettingsOpen ? _previousForeground : NativeMethods.GetForegroundWindow();
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
        Controls.ErrorBubble.Show(Button.Fab, text, toLeft: !Config.Position.AnchorLeft);

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

    public void RenameFavorite(Favorite f, string name)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name) || name == f.Name) return;
        f.Name = name;
        Save();
        RaiseFavoritesChanged();
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
            if (expandIfNeeded && Config.Favorites.Count > 5) Menu.SetExpanded(true);
        }
        FavoritesChanged?.Invoke();
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
        if (size is not ("small" or "medium" or "large") || size == Config.ButtonSize) return;
        Config.ButtonSize = size;
        Button.ApplySize();
        Button.Dispatcher.BeginInvoke(() =>
        {
            PlaceButtonFromConfig(save: false);
            if (MenuOpen && Menu is not null) { Menu.Rebuild(); Menu.Reposition(); Settings?.Reposition(); }
        }, DispatcherPriority.Loaded);
        Save();
    }

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

        Config.Hotkey = combo.Text;
        Save();
        if (Hotkey.Apply(combo.Text)) return HotkeyApplyResult.Bound;
        return Hotkey.LastAttemptConflicted ? HotkeyApplyResult.Conflict : HotkeyApplyResult.Invalid;
    }

    // ---------------------------------------------------------------- position (FAB-1, FAB-6, SET-6, DATA-4)

    private void PlaceButtonFromConfig(bool save)
    {
        var firstRun = string.IsNullOrEmpty(Config.Position.Monitor);
        var (monitor, pos) = Monitors.Resolve(Config.Position, Config.ButtonSizePx);
        if (!ReferenceEquals(pos, Config.Position))
        {
            if (!firstRun) Logger.Info($"Stored position unusable; using default on {monitor.DeviceName}.");
            Config.Position = pos;
            if (save) Save();
        }
        var rect = Monitors.ButtonRect(Config.Position, monitor, Config.ButtonSizePx);
        Button.PlaceFab(rect, monitor.Scale);
    }

    public void OnButtonDragEnded()
    {
        Config.Position = Monitors.FromButtonRect(Button.FabRect);
        PlaceButtonFromConfig(save: false); // snap to the exact stored offsets
        Save();
        Logger.Info($"Button moved: {Config.Position.Anchor} ({Config.Position.OffsetX},{Config.Position.OffsetY}) on {Config.Position.Monitor}");
    }

    public void ResetPosition()
    {
        var primary = Monitors.Primary();
        Config.Position = ButtonPosition.Default();
        Config.Position.Monitor = primary.DeviceName;
        PlaceButtonFromConfig(save: false);
        Save();
        Logger.Info("Button position reset.");
        if (MenuOpen && Menu is not null) { Menu.Rebuild(); Menu.Reposition(); Settings?.Reposition(); }
    }

    public void OnDisplayChanged()
    {
        PlaceButtonFromConfig(save: true);
        if (MenuOpen && Menu is not null) { Menu.Reposition(); Settings?.Reposition(); }
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

    private bool IsOurWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        return NativeMethods.ProcessIdOf(hwnd) == (uint)Environment.ProcessId;
    }
}
