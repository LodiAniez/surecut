using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SureCut.Interop;
using SureCut.Models;
using SureCut.Services;

namespace SureCut.Windows;

/// <summary>Settings panel (PRD §5.4). A separate window beside the menu; it does take focus (ARCH-7).</summary>
public partial class SettingsWindow : Window
{
    private const int GapDip = 12;

    private readonly LauncherHost _host;
    private bool _sourceReady;
    private bool _listeningForHotkey;
    private bool _suppressEvents;

    /// <summary>Feedback from the last capture attempt; null means "derive from the bound state".</summary>
    private string? _hotkeyMessage;

    private UpdateState? _renderedUpdateState;

    public IntPtr Hwnd => WindowStyling.Handle(this);

    public SettingsWindow(LauncherHost host)
    {
        _host = host;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            WindowStyling.MakeToolWindow(this);
            WindowStyling.AllowTinyWindow(this);
            _sourceReady = true;
            ApplySurface();
        };
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => { if (IsVisible) Reposition(); };
        Deactivated += (_, _) => _host.OnLauncherWindowDeactivated();
        PreviewKeyDown += OnPreviewKeyDown;
        _host.Theme.Changed += ApplySurface;
        _host.FavoritesChanged += () => { if (IsVisible) { RebuildFavorites(); AutoSize(); } };
        _host.Updates.Changed += () => Dispatcher.BeginInvoke(RenderUpdate);
    }

    private void ApplySurface()
    {
        if (_sourceReady) WindowStyling.ApplySurface(this, Surface, _host.Theme);
    }

    // ---------------------------------------------------------------- open / close / placement

    public void Open()
    {
        Rebuild();
        if (!IsVisible) { ShowActivated = true; Show(); }
        Reposition();
        WindowStyling.SetTopmost(this, true);
        Activate();
        Dispatcher.BeginInvoke(() => HideSwitch.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void CloseSettings()
    {
        _listeningForHotkey = false;
        if (IsVisible) Hide();
    }

    public void Reposition()
    {
        if (!_sourceReady || _host.Menu is null) return;
        var fab = _host.Button.FabRect;
        var menuRect = WindowStyling.GetRect(_host.Menu);
        var m = Monitors.FromRect(fab);
        var s = m.Scale;
        var me = WindowStyling.GetRect(this);
        var w = me.Width; var h = me.Height;
        if (w <= 0 || h <= 0) return;

        var pos = _host.EffectivePosition;
        var gap = (int)Math.Round(GapDip * s);
        var left = pos.AnchorLeft ? menuRect.Right + gap : menuRect.Left - gap - w;
        var top = pos.AnchorTop ? fab.Top : fab.Bottom - h;
        var r = Monitors.ClampToWorkArea(left, top, w, h, m);
        WindowStyling.Place(this, r, keepSize: true);
    }

    // ---------------------------------------------------------------- content

    public void Rebuild()
    {
        _suppressEvents = true;
        try
        {
            var cfg = _host.Config;
            HideSwitch.IsChecked = cfg.HideOnLaunch;
            StartSwitch.IsChecked = cfg.StartWithWindows;
            SizeSmall.IsChecked = cfg.ButtonSize == "small";
            SizeMedium.IsChecked = cfg.ButtonSize == "medium";
            SizeLarge.IsChecked = cfg.ButtonSize == "large";
            UpdateSwitch.IsChecked = cfg.CheckForUpdates;
            RenderHotkey();
            RenderUpdate();
            RebuildFavorites();
        }
        finally { _suppressEvents = false; }
        AutoSize();
    }

    private void RenderUpdate()
    {
        var u = _host.Updates;

        // Progress ticks only change the percentage text. Do not re-measure or resize the
        // window for them: DWM suspends the Acrylic backdrop while a window is being resized,
        // and a resize on every tick left the panel flat grey for the whole download.
        if (_renderedUpdateState == u.State && u.State == UpdateState.Downloading)
        {
            UpdateHint.Text = DownloadingText(u);
            return;
        }
        _renderedUpdateState = u.State;

        VersionLabel.Text = $"SureCut {u.Installed}";
        var available = u.IsUpdateAvailable;
        NewBadge.Visibility = available && u.State is not (UpdateState.Downloading or UpdateState.ReadyToRestart) ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Style = (Style)FindResource(available ? "AccentButtonStyle" : "FlatButtonStyle");
        UpdateButton.IsEnabled = u.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.ReadyToRestart);
        UpdateHint.SetResourceReference(ForegroundProperty, u.State == UpdateState.Failed ? "DangerBrush" : "MutedBrush");

        (UpdateHint.Text, UpdateButton.Content) = u.State switch
        {
            UpdateState.Idle => ("", "Check for updates"),
            UpdateState.Checking => ("Checking for updates…", "Checking…"),
            UpdateState.UpToDate => ("You're up to date.", "Check for updates"),
            UpdateState.Available => ($"Version {u.Latest} is available.", "Update"),
            UpdateState.Downloading => (DownloadingText(u), "Updating…"),
            UpdateState.ReadyToRestart => ("Restarting to finish the update…", "Updating…"),
            UpdateState.Failed => (u.Error ?? "Something went wrong.", available ? "Try again" : "Check for updates"),
            _ => ("", "Check for updates"),
        };
        if (IsVisible) AutoSize();
    }

    /// <summary>Fixed-width percentage so the hint keeps the same line count for the whole download.</summary>
    private static string DownloadingText(UpdateService u) => $"Downloading version {u.Latest}… {u.Progress,3}%";

    private void AutoSize()
    {
        // Cap before the first measure too: the panel is sized before it is ever shown, and
        // without the cap a long favorites list produced a window taller than the screen.
        var m = Monitors.FromRect(_host.Button.FabRect);
        Scroller.MaxHeight = Math.Max(200, m.WorkArea.Height / m.Scale - 40 - 26); // 26 = surface padding
        WindowStyling.SizeToContent(this, Surface, fixedWidth: 300);
    }

    private const string ConflictMessage = "This shortcut is in use by another program.";

    private void RenderHotkey()
    {
        if (_listeningForHotkey) { HotkeyButton.Content = "Press a shortcut…"; return; }
        var text = _host.Config.Hotkey;
        var bound = _host.Hotkey.IsBound;
        HotkeyButton.Content = string.IsNullOrEmpty(text) ? "Not set" : text.Replace("+", " + ");
        HotkeyButton.FontStyle = bound ? FontStyles.Normal : FontStyles.Italic;
        HotkeyButton.SetResourceReference(ForegroundProperty, bound ? "TextBrush" : "MutedBrush");

        var message = _hotkeyMessage ?? (!bound && !string.IsNullOrEmpty(text) ? ConflictMessage : null);
        HotkeyWarning.Text = message ?? "";
        HotkeyWarning.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RebuildFavorites()
    {
        FavRows.Children.Clear();
        var favorites = _host.Config.Favorites;
        FavEmpty.Visibility = favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var fav in favorites) FavRows.Children.Add(CreateRow(fav));
    }

    private FrameworkElement CreateRow(Favorite fav)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, AllowDrop = true, Tag = fav };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(2, 4, 4, 4), Background = Brushes.Transparent, Child = grid, Tag = fav, AllowDrop = true };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        // grip (drag to reorder, FAV-3)
        var grip = new System.Windows.Shapes.Path
        {
            Width = 8, Height = 14, Stretch = Stretch.Uniform, Cursor = Cursors.SizeAll,
            Fill = (Brush)FindResource("MutedBrush"),
            Data = Geometry.Parse("M2,2 m-1.3,0 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 M6,2 m-1.3,0 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 M2,7 m-1.3,0 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 M6,7 m-1.3,0 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 M2,12 m-1.3,0 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 M6,12 m-1.3,0 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0"),
        };
        var gripHost = new Border { Background = Brushes.Transparent, Child = grip, Cursor = Cursors.SizeAll, VerticalAlignment = VerticalAlignment.Center };
        gripHost.PreviewMouseLeftButtonDown += (_, _) =>
        {
            _host.Hooks.Suspended = true;
            try { DragDrop.DoDragDrop(row, new DataObject("SureCut.Favorite", fav.Id), DragDropEffects.Move); }
            finally { _host.Hooks.Suspended = false; }
        };
        Grid.SetColumn(gripHost, 0);
        grid.Children.Add(gripHost);

        // icon
        var img = _host.Icons.Get(fav);
        FrameworkElement icon = img is not null
            ? new Image { Source = img, Width = 22, Height = 22 }
            : new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = (Brush)FindResource("MutedBrush") };
        icon.Opacity = fav.IsMissing ? 0.5 : 1;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        // editable name (FAV-4)
        var name = new TextBox { Text = fav.Name, Style = (Style)FindResource("NameBoxStyle"), Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(name, "Name for " + fav.Name);
        name.LostKeyboardFocus += (_, _) => _host.RenameFavorite(fav, name.Text);
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _host.RenameFavorite(fav, name.Text); HideSwitch.Focus(); e.Handled = true; } };
        Grid.SetColumn(name, 2);
        grid.Children.Add(name);

        if (fav.IsMissing)
        {
            var tag = new TextBlock { Text = "not found", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Target file not found" };
            tag.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            Grid.SetColumn(tag, 3);
            grid.Children.Add(tag);
        }

        var remove = new Button { Style = (Style)FindResource("RemoveButtonStyle"), Content = "✕", ToolTip = "Remove from favorites" };
        System.Windows.Automation.AutomationProperties.SetName(remove, "Remove " + fav.Name + " from favorites");
        remove.Click += (_, _) => _host.RemoveFavorite(fav);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(remove);

        // drop target
        row.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent("SureCut.Favorite") ? DragDropEffects.Move : DragDropEffects.None;
            if (e.Effects == DragDropEffects.Move)
            {
                var above = e.GetPosition(row).Y < row.ActualHeight / 2;
                row.BorderThickness = above ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
                row.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            }
            e.Handled = true;
        };
        row.DragLeave += (_, _) => row.BorderThickness = new Thickness(0);
        row.Drop += (_, e) =>
        {
            row.BorderThickness = new Thickness(0);
            if (e.Data.GetData("SureCut.Favorite") is not string draggedId) return;
            var above = e.GetPosition(row).Y < row.ActualHeight / 2;
            _host.MoveFavoriteRelativeTo(draggedId, fav, above);
            e.Handled = true;
        };

        return row;
    }

    // ---------------------------------------------------------------- handlers

    private void OnHideSwitch(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _host.SetHideOnLaunch(HideSwitch.IsChecked == true);
    }

    private void OnStartSwitch(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _host.SetStartWithWindows(StartSwitch.IsChecked == true);
    }

    private void OnSizeChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is RadioButton { Tag: string size }) _host.SetButtonSize(size);
    }

    private void OnResetPosition(object sender, RoutedEventArgs e) => _host.ResetPosition();

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        try { await _host.ApplyUpdateAsync(); }
        catch (Exception ex) { Logger.Error("Update click failed", ex); }
    }

    private void OnUpdateSwitch(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        _host.SetCheckForUpdates(UpdateSwitch.IsChecked == true);
    }
    private void OnAddProgram(object sender, RoutedEventArgs e) => _host.PickAndAddProgram();

    private void OnHotkeyClick(object sender, RoutedEventArgs e)
    {
        _listeningForHotkey = true;
        _hotkeyMessage = null;
        HotkeyWarning.Visibility = Visibility.Collapsed;
        HotkeyButton.SetResourceReference(ForegroundProperty, "AccentBrush");
        HotkeyButton.FontStyle = FontStyles.Normal;
        HotkeyButton.Content = "Press a shortcut…";
        HotkeyButton.Focus();
    }

    private void OnHotkeyLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_listeningForHotkey) { _listeningForHotkey = false; RenderHotkey(); }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listeningForHotkey)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { _listeningForHotkey = false; RenderHotkey(); return; }
            if (HotkeyCombo.IsModifierKey(key) || !HotkeyCombo.IsBindableKey(key)) return;

            var mods = HotkeyCombo.ModifiersFrom(Keyboard.Modifiers);
            _listeningForHotkey = false;
            if (mods == 0)
            {
                _hotkeyMessage = "Include Ctrl, Alt or Shift in the shortcut.";
                RenderHotkey();
                return;
            }

            _hotkeyMessage = _host.SetHotkey(HotkeyCombo.Format(mods, key)) switch
            {
                HotkeyApplyResult.Bound => null,
                HotkeyApplyResult.Conflict => ConflictMessage,
                HotkeyApplyResult.Invalid => "That key can't be used in a shortcut.",
                _ => null,
            };
            RenderHotkey();
            return;
        }

        if (e.Key == Key.Escape) { _host.CloseMenu(); e.Handled = true; }
    }
}
