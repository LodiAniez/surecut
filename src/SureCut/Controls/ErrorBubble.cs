using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace SureCut.Controls;

/// <summary>MENU-8 / FAV-1: a transient inline error next to an element, never a modal dialog.</summary>
public static class ErrorBubble
{
    private static Popup? _popup;
    private static DispatcherTimer? _timer;

    public static void Show(FrameworkElement target, string text, bool toLeft, TimeSpan? duration = null)
    {
        Hide();

        var border = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.Resources["TooltipBrush"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["DangerBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.18 },
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["UiFont"],
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["DangerBrush"],
            },
        };

        _popup = new Popup
        {
            Child = border,
            PlacementTarget = target,
            Placement = toLeft ? PlacementMode.Left : PlacementMode.Right,
            VerticalOffset = 0,
            HorizontalOffset = toLeft ? -2 : 2,
            AllowsTransparency = true,
            StaysOpen = true,
            IsOpen = true,
        };

        _timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Hide();
        _timer.Start();
    }

    public static void Hide()
    {
        _timer?.Stop();
        _timer = null;
        if (_popup is not null) { _popup.IsOpen = false; _popup = null; }
    }
}
