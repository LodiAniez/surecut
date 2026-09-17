using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SureCut.Controls;
using SureCut.Interop;
using SureCut.Models;
using SureCut.Services;

namespace SureCut.Windows;

/// <summary>The favorites menu (PRD §5.2): a non-layered tool window with a DWM backdrop.</summary>
public partial class MenuWindow : Window
{
    public const int VisibleCount = 5;
    private const int GapDip = 10;

    private readonly LauncherHost _host;
    private bool _expanded;
    private bool _sourceReady;

    public bool KeyboardMode { get; private set; }
    public IntPtr Hwnd => WindowStyling.Handle(this);

    public MenuWindow(LauncherHost host)
    {
        _host = host;
        InitializeComponent();

        SourceInitialized += (_, _) =>
        {
            WindowStyling.MakeToolWindow(this);
            WindowStyling.AllowTinyWindow(this);
            WindowStyling.DeliverClicksWithoutActivation(this, () => !KeyboardMode);
            _sourceReady = true;
            ApplySurface();
        };
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => { if (IsVisible) Reposition(); };
        Deactivated += (_, _) => _host.OnLauncherWindowDeactivated();
        PreviewKeyDown += OnPreviewKeyDown;
        _host.Theme.Changed += ApplySurface;
        _host.Updates.Changed += () => Dispatcher.BeginInvoke(RenderUpdateDot);
        RenderUpdateDot();
    }

    private void RenderUpdateDot()
    {
        UpdateDot.Visibility = _host.Updates.IsUpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
        GearButton.ToolTip = _host.Updates.IsUpdateAvailable ? "Settings · update available" : "Settings";
    }

    // ---------------------------------------------------------------- surface (MENU-11)

    private void ApplySurface()
    {
        if (_sourceReady) WindowStyling.ApplySurface(this, Surface, _host.Theme);
    }

    // ---------------------------------------------------------------- open / close

    public void Open(bool keyboard)
    {
        KeyboardMode = keyboard;
        WindowStyling.SetNoActivate(this, !keyboard);
        _expanded = false;
        Rebuild();

        if (!IsVisible)
        {
            ShowActivated = keyboard;
            Show();
        }
        Reposition();
        WindowStyling.SetTopmost(this, true);

        if (keyboard)
        {
            Activate();
            Dispatcher.BeginInvoke(() => FocusFirst(), System.Windows.Threading.DispatcherPriority.Input);
        }

        Animate();
    }

    public void CloseMenu()
    {
        ErrorBubble.Hide();
        _expanded = false;
        if (IsVisible) Hide();
    }

    private void Animate()
    {
        var pos = _host.EffectivePosition;
        Surface.RenderTransformOrigin = new Point(pos.AnchorLeft ? 0 : 1, pos.AnchorTop ? 0 : 1);

        if (!_host.Theme.AnimationsEnabled)
        {
            Surface.Opacity = 1; OpenScale.ScaleX = OpenScale.ScaleY = 1;
            return;
        }
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scale = new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } };
        Surface.BeginAnimation(OpacityProperty, fade);
        OpenScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        OpenScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    // ---------------------------------------------------------------- placement (MENU-1a)

    public void Reposition()
    {
        if (!_sourceReady) return;
        var fab = _host.Button.FabRect;
        var m = Monitors.FromRect(fab);
        var s = m.Scale;
        var me = WindowStyling.GetRect(this);
        var w = me.Width; var h = me.Height;
        if (w <= 0 || h <= 0) return;

        var pos = _host.EffectivePosition;
        var gap = (int)Math.Round(GapDip * s);
        var left = pos.AnchorLeft ? fab.Left : fab.Right - w;
        var top = pos.AnchorTop ? fab.Bottom + gap : fab.Top - gap - h;
        var r = Monitors.ClampToWorkArea(left, top, w, h, m);
        WindowStyling.Place(this, r, keepSize: true);
    }

    // ---------------------------------------------------------------- content

    public void Rebuild()
    {
        var favorites = _host.Config.Favorites;
        var iconPx = _host.Config.MenuIconPx;
        var toLeft = !_host.EffectivePosition.AnchorLeft;

        Items.Children.Clear();
        for (var i = 0; i < favorites.Count; i++)
        {
            var fav = favorites[i];
            var btn = CreateItem(fav, iconPx, toLeft);
            if (i >= VisibleCount && !_expanded) btn.Visibility = Visibility.Collapsed;
            Items.Children.Add(btn);
        }

        EmptyState.Visibility = favorites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Visibility = favorites.Count > VisibleCount ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Content = _expanded ? "Show less" : "Show more";
        Scroller.Visibility = favorites.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Surface.MinWidth = iconPx + 24;
        AutoSize();
    }

    private void AutoSize()
    {
        var m = Monitors.FromRect(_host.Button.FabRect);
        Scroller.MaxHeight = Math.Max(120, m.WorkArea.Height / m.Scale - 140);
        WindowStyling.SizeToContent(this, Surface);
    }

    private Button CreateItem(Favorite fav, int iconPx, bool tooltipLeft)
    {
        var missing = fav.IsMissing; // one file-system probe per item per rebuild
        var btn = new Button
        {
            Style = (Style)FindResource("MenuItemButtonStyle"),
            Tag = fav,
            Content = CreateIcon(fav, iconPx),
            Opacity = missing ? 0.5 : 1.0,
        };
        System.Windows.Automation.AutomationProperties.SetName(btn, fav.Name + (missing ? " (file not found)" : ""));

        // MENU-6: 400 ms hover tooltip on the side facing the screen center; immediate on keyboard focus.
        var tip = new ToolTip
        {
            Content = missing ? fav.Name + " — file not found" : fav.Name,
            Style = (Style)FindResource("BubbleToolTipStyle"),
            Placement = tooltipLeft ? PlacementMode.Left : PlacementMode.Right,
            PlacementTarget = btn,
        };
        btn.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(btn, 400);
        ToolTipService.SetShowDuration(btn, 8000);
        btn.GotKeyboardFocus += (_, _) => { if (KeyboardMode) tip.IsOpen = true; };
        btn.LostKeyboardFocus += (_, _) => tip.IsOpen = false;

        btn.Click += (_, _) => _host.Launch(fav, msg => ErrorBubble.Show(btn, msg, tooltipLeft));
        btn.MouseRightButtonUp += (_, e) => { ShowItemContextMenu(btn, fav); e.Handled = true; };
        return btn;
    }

    private FrameworkElement CreateIcon(Favorite fav, int px)
    {
        var img = _host.Icons.Get(fav);
        if (img is not null)
        {
            var image = new Image { Source = img, Width = px, Height = px, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        // FAV-6 fallback: generic application glyph.
        var border = new Border
        {
            Width = px, Height = px, CornerRadius = new CornerRadius(7),
            Background = (Brush)FindResource("MutedBrush"),
            Child = new System.Windows.Shapes.Path
            {
                Stroke = Brushes.White, StrokeThickness = 1.5, Stretch = Stretch.Uniform,
                Width = px * 0.56, Height = px * 0.56,
                Data = Geometry.Parse("M3,2.5 H13 A1.5,1.5 0 0 1 14.5,4 V12 A1.5,1.5 0 0 1 13,13.5 H3 A1.5,1.5 0 0 1 1.5,12 V4 A1.5,1.5 0 0 1 3,2.5 Z M5.5,6 H10.5 M5.5,8.5 H8.5"),
            },
        };
        return border;
    }

    private void ShowItemContextMenu(Button anchor, Favorite fav)
    {
        var favorites = _host.Config.Favorites;
        var index = favorites.IndexOf(fav);
        var toLeft = !_host.EffectivePosition.AnchorLeft;

        var menu = new ContextMenu { Style = (Style)FindResource("FluentContextMenuStyle"), Placement = PlacementMode.MousePoint };
        menu.Items.Add(Item("Open", () => _host.Launch(fav, msg => ErrorBubble.Show(anchor, msg, toLeft))));
        menu.Items.Add(Item("Open file location", () => _host.OpenFileLocation(fav)));
        menu.Items.Add(new Separator { Style = (Style)FindResource("FluentSeparatorStyle") });
        menu.Items.Add(Item("Move up", () => _host.MoveFavorite(fav, -1), enabled: index > 0));
        menu.Items.Add(Item("Move down", () => _host.MoveFavorite(fav, +1), enabled: index < favorites.Count - 1));
        menu.Items.Add(new Separator { Style = (Style)FindResource("FluentSeparatorStyle") });
        menu.Items.Add(Item("Remove from favorites", () => _host.RemoveFavorite(fav)));
        _host.ShowContextMenu(menu, this);
    }

    private MenuItem Item(string header, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, Style = (Style)FindResource("FluentMenuItemStyle"), IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        Rebuild();
    }

    private void OnToggleMore(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);
    private void OnGear(object sender, RoutedEventArgs e) => _host.ToggleSettings();
    private void OnAddProgram(object sender, RoutedEventArgs e) => _host.PickAndAddProgram();

    // ---------------------------------------------------------------- keyboard mode (ARCH-5)

    private IEnumerable<UIElement> Focusables()
    {
        foreach (UIElement child in Items.Children) if (child.Visibility == Visibility.Visible) yield return child;
        if (EmptyState.Visibility == Visibility.Visible) yield return EmptyAddButton;
        if (MoreButton.Visibility == Visibility.Visible) yield return MoreButton;
        yield return GearButton;
    }

    private void FocusFirst() => Focusables().FirstOrDefault()?.Focus();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _host.CloseMenu(); e.Handled = true; return; }
        if (!KeyboardMode) return;

        var list = Focusables().ToList();
        if (list.Count == 0) return;
        var current = Keyboard.FocusedElement as UIElement;
        var i = current is null ? -1 : list.IndexOf(current);

        switch (e.Key)
        {
            case Key.Down:
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
                list[(i + 1 + list.Count) % list.Count].Focus(); e.Handled = true; break;
            case Key.Up:
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.Shift:
                list[(i - 1 + list.Count) % list.Count].Focus(); e.Handled = true; break;
            case Key.Home: list[0].Focus(); e.Handled = true; break;
            case Key.End: list[^1].Focus(); e.Handled = true; break;
            case Key.Right when MoreButton.Visibility == Visibility.Visible: SetExpanded(true); e.Handled = true; break;
            case Key.Left when MoreButton.Visibility == Visibility.Visible: SetExpanded(false); e.Handled = true; break;
        }
    }
}
