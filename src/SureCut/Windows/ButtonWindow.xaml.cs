using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SureCut.Interop;
using SureCut.Services;

namespace SureCut.Windows;

/// <summary>
/// The floating button (PRD §5.1). A layered, always-no-activate tool window that never takes
/// focus. Hosts the global hotkey, the single-instance message and system notifications.
/// </summary>
public partial class ButtonWindow : Window
{
    private const int PadDip = 16;
    private const double HoldMs = 200;
    private const double DragThresholdDip = 4;

    private readonly LauncherHost _host;
    private readonly DispatcherTimer _holdTimer;
    private readonly DispatcherTimer _dragTimer;
    private HwndSource? _source;
    private IntPtr _hwnd;

    private bool _pressed, _dragging, _open;
    private POINT _pressPoint;
    private int _dragStartFabLeft, _dragStartFabTop;

    public IntPtr Hwnd => new WindowInteropHelper(this).EnsureHandle();

    public bool HiddenBySetting { get; private set; }

    public ButtonWindow(LauncherHost host)
    {
        _host = host;
        InitializeComponent();

        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoldMs) };
        _holdTimer.Tick += (_, _) => BeginDrag();
        _dragTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _dragTimer.Tick += (_, _) => DragTick();

        SourceInitialized += OnSourceInitialized;
        PreviewMouseLeftButtonDown += OnLeftDown;
        PreviewMouseMove += OnMove;
        PreviewMouseLeftButtonUp += OnLeftUp;
        MouseRightButtonUp += OnRightUp;
        DragOver += OnDragOver;
        DragLeave += (_, _) => DropRing.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
        // Use the cached handle: EnsureHandle() throws once the window has been destroyed.
        Closed += (_, _) => { _source?.RemoveHook(WndProc); if (_hwnd != IntPtr.Zero) NativeMethods.WTSUnRegisterSessionNotification(_hwnd); };

        ApplySize();
        _host.Theme.Changed += ApplyTheme;
        ApplyTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var h = Hwnd;
        _hwnd = h;
        NativeMethods.AddExStyle(h, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST);
        WindowStyling.AllowTinyWindow(this); // the 80 px window must not be clamped to the OS minimum width
        WindowStyling.DeliverClicksWithoutActivation(this, () => true);
        _source = HwndSource.FromHwnd(h);
        _source?.AddHook(WndProc);
        NativeMethods.WTSRegisterSessionNotification(h, NativeMethods.NOTIFY_FOR_THIS_SESSION);
    }

    // ---------------------------------------------------------------- geometry

    public int SizeDip => _host.Config.ButtonSizePx;

    /// <summary>Physical rectangle of the visible circle (the window is padded for the shadow).</summary>
    public RECT FabRect
    {
        get
        {
            var r = WindowStyling.GetRect(this);
            var pad = (int)Math.Round(PadDip * WindowStyling.ScaleOf(this));
            return new RECT { Left = r.Left + pad, Top = r.Top + pad, Right = r.Right - pad, Bottom = r.Bottom - pad };
        }
    }

    /// <summary>Places the circle at the given physical rectangle (moves the padded window).</summary>
    public void PlaceFab(RECT fab, double scale)
    {
        var pad = (int)Math.Round(PadDip * scale);
        var r = new RECT { Left = fab.Left - pad, Top = fab.Top - pad, Right = fab.Right + pad, Bottom = fab.Bottom + pad };
        NativeMethods.SetWindowPos(Hwnd, NativeMethods.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, NativeMethods.SWP_NOACTIVATE);
    }

    public void ApplySize()
    {
        var size = SizeDip;
        Fab.Width = size;
        Fab.Height = size;
        Width = size + 2 * PadDip;
        Height = size + 2 * PadDip;
        var glyphScale = size / 48.0;
        GlyphScale.ScaleX = glyphScale;
        GlyphScale.ScaleY = glyphScale;
    }

    private void ApplyTheme()
    {
        Shadow.Opacity = _host.Theme.IsDark ? 0.5 : 0.18;
    }

    // ---------------------------------------------------------------- open/closed visual (FAB-2)

    public void SetOpenState(bool open)
    {
        if (_open == open) return;
        _open = open;

        var animate = _host.Theme.AnimationsEnabled;
        var dur = animate ? TimeSpan.FromMilliseconds(250) : TimeSpan.Zero;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        L1Move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(open ? 4.5 : 0, dur) { EasingFunction = ease });
        L1Rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 45 : 0, dur) { EasingFunction = ease });
        L3Move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(open ? -4.5 : 0, dur) { EasingFunction = ease });
        L3Rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? -45 : 0, dur) { EasingFunction = ease });
        L2Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(open ? 0 : 1, dur) { EasingFunction = ease });
        L2.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, animate ? TimeSpan.FromMilliseconds(150) : TimeSpan.Zero));

        var fill = open ? "AccentBrush" : "SurfaceBrush";
        var ink = open ? "AccentInkBrush" : "TextBrush";
        Circle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, fill);
        if (open) Circle.Stroke = Brushes.Transparent;
        else Circle.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "StrokeBrush");
        L1.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ink);
        L2.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ink);
        L3.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ink);
    }

    /// <summary>LIFE-1: a second copy was started.</summary>
    public void Flash()
    {
        if (!_host.Theme.AnimationsEnabled) return;
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(450) };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.12, KeyTime.FromPercent(0.4), new CubicEase()));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new CubicEase()));
        FabScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        FabScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    public void SetHiddenBySetting(bool hidden)
    {
        HiddenBySetting = hidden;
        if (hidden) Hide();
        else WindowStyling.ShowNoActivate(this);
    }

    // ---------------------------------------------------------------- click / hold-to-drag (FAB-3, FAB-6)

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragging = false;
        NativeMethods.GetCursorPos(out _pressPoint);
        _holdTimer.Stop();
        _holdTimer.Start();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || _dragging) return;
        NativeMethods.GetCursorPos(out var p);
        var threshold = DragThresholdDip * WindowStyling.ScaleOf(this);
        if (Math.Abs(p.X - _pressPoint.X) > threshold || Math.Abs(p.Y - _pressPoint.Y) > threshold)
            _holdTimer.Stop(); // moved too early: this is a click, not a drag
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        _holdTimer.Stop();
        if (_dragging) { EndDrag(); }
        else if (_pressed) { _pressed = false; _host.ToggleMenu(keyboard: false); }
        e.Handled = true;
    }

    private void BeginDrag()
    {
        _holdTimer.Stop();
        if (!_pressed || !NativeMethods.IsLeftButtonDown()) { _pressed = false; return; }
        _dragging = true;
        _host.CloseMenu();
        var startFab = FabRect; // screen coordinates stay valid even if the DPI changes mid-drag
        _dragStartFabLeft = startFab.Left;
        _dragStartFabTop = startFab.Top;
        Cursor = Cursors.SizeAll;
        _dragTimer.Start();
    }

    private void DragTick()
    {
        if (!NativeMethods.IsLeftButtonDown()) { EndDrag(); return; }

        NativeMethods.GetCursorPos(out var p);
        var dx = p.X - _pressPoint.X;
        var dy = p.Y - _pressPoint.Y;

        // Size and padding come from the window's *current* DPI (PerMonitorV2 may have resized it).
        var current = WindowStyling.GetRect(this);
        var pad = (int)Math.Round(PadDip * WindowStyling.ScaleOf(this));
        var fabW = current.Width - 2 * pad;
        var fabH = current.Height - 2 * pad;
        var fabLeft = _dragStartFabLeft + dx;
        var fabTop = _dragStartFabTop + dy;

        // Confine the circle to the work area of the monitor under the cursor.
        var m = Monitors.FromPoint(p.X, p.Y);
        var fab = Monitors.ClampToWorkArea(fabLeft, fabTop, fabW, fabH, m);
        NativeMethods.SetWindowPos(Hwnd, IntPtr.Zero, fab.Left - pad, fab.Top - pad, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
    }

    private void EndDrag()
    {
        _dragTimer.Stop();
        _dragging = false;
        _pressed = false;
        Cursor = Cursors.Hand;
        _host.OnButtonDragEnded();
    }

    // ---------------------------------------------------------------- right-click (SET-8)

    private void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("FluentContextMenuStyle"), Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(MenuItem("Settings", () => { _host.OpenMenu(keyboard: false); _host.OpenSettings(); }));
        menu.Items.Add(MenuItem("Reset position", _host.ResetPosition));
        menu.Items.Add(new Separator { Style = (Style)FindResource("FluentSeparatorStyle") });
        menu.Items.Add(MenuItem("Quit", _host.Quit));
        _host.ShowContextMenu(menu, this);
        e.Handled = true;
    }

    private MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header, Style = (Style)FindResource("FluentMenuItemStyle") };
        item.Click += (_, _) => action();
        return item;
    }

    // ---------------------------------------------------------------- drop to add (FAB-8)

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        DropRing.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropRing.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files) _host.AddFavorite(f, out _);
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- WndProc

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && (int)wParam == HotkeyService.HotkeyId)
        {
            _host.OnHotkeyPressed();
            handled = true;
        }
        else if (msg == (int)SingleInstance.ShowMeMessage)
        {
            _host.OnShowMeMessage();
            handled = true;
        }
        else if (msg == NativeMethods.WM_SETTINGCHANGE || msg == NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED)
        {
            Dispatcher.BeginInvoke(_host.Theme.Refresh);
        }
        else if (msg == NativeMethods.WM_DISPLAYCHANGE)
        {
            Dispatcher.BeginInvoke(_host.OnDisplayChanged);
        }
        else if (msg == NativeMethods.WM_WTSSESSION_CHANGE && (int)wParam == NativeMethods.WTS_SESSION_UNLOCK)
        {
            _host.Hotkey.Reassert();
        }
        else if (msg == NativeMethods.WM_POWERBROADCAST && (int)wParam == NativeMethods.PBT_APMRESUMEAUTOMATIC)
        {
            _host.Hotkey.Reassert();
        }
        return IntPtr.Zero;
    }
}
