using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MinecraftHelper;

// Zasobnik w stylu glass, wspólny dla Auto Clickera i Kopacza: własne
// powiadomienie zamiast dymka Windows i menu WPF zamiast ContextMenuStrip.
// Systemowy dymek ma szary wygląd, techniczną nazwę procesu i ikonę
// z pamięci podręcznej Windows.
internal sealed class TrayToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(4.5);

    private readonly Action _onClick;
    private readonly DispatcherTimer _hideTimer;
    private readonly TranslateTransform _slide = new(0, 14);
    private bool _closing;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    // Okno nie przejmuje fokusu, więc nie wyrywa gry z pełnego ekranu.
    public TrayToastWindow(string title, string message, string iconResource, Action onClick)
    {
        _onClick = onClick;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Cursor = Cursors.Hand;
        Opacity = 0;
        if (Application.Current?.TryFindResource("GlassFont") is FontFamily font)
            FontFamily = font;

        var icon = new Image
        {
            Source = new BitmapImage(new Uri($"pack://application:,,,/{iconResource}", UriKind.Absolute)),
            Width = 34,
            Height = 34,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = GlassPalette.Ink
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GlassPalette.Muted,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);

        Content = new Border
        {
            Width = 340,
            Margin = new Thickness(14),
            Padding = new Thickness(14, 12, 16, 13),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0xC7, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Direction = 270,
                Opacity = 0.16,
                Color = Color.FromRgb(0x1E, 0x24, 0x50)
            },
            Child = row,
            RenderTransform = _slide
        };

        _hideTimer = new DispatcherTimer { Interval = VisibleDuration };
        _hideTimer.Tick += (_, _) => FadeOutAndClose();
        MouseLeftButtonUp += (_, _) =>
        {
            _onClick();
            FadeOutAndClose();
        };
        Loaded += (_, _) => ShowAnimated();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow | WsExNoActivate);
    }

    private void ShowAnimated()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth;
        Top = workArea.Bottom - ActualHeight;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
        _hideTimer.Start();
    }

    private void FadeOutAndClose()
    {
        if (_closing)
            return;
        _closing = true;
        _hideTimer.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}

internal static class GlassTrayMenu
{
    public const string OpenIcon = "M4,5 H20 V19 H4 Z M4,9 H20";
    public const string ExitIcon = "M12,3 V11 M7,6.5 A7,7 0 1 0 17,6.5";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    // items: (nagłówek, ikona liniowa, akcja); null w tablicy = separator.
    public static ContextMenu Create(params (string Header, string Icon, Action Action)?[] items)
    {
        var menu = new ContextMenu
        {
            Style = (Style)Application.Current.FindResource("GlassTrayMenu"),
            Placement = PlacementMode.MousePoint
        };
        foreach ((string Header, string Icon, Action Action)? item in items)
        {
            if (item is not { } entry)
            {
                menu.Items.Add(new Separator { Style = (Style)Application.Current.FindResource("GlassTrayMenuSeparator") });
                continue;
            }

            var menuItem = new MenuItem
            {
                Header = entry.Header,
                Icon = CreateLineIcon(entry.Icon),
                Style = (Style)Application.Current.FindResource("GlassTrayMenuItem")
            };
            menuItem.Click += (_, _) => entry.Action();
            menu.Items.Add(menuItem);
        }

        // Menu z zasobnika musi być na pierwszym planie, inaczej nie zamknie się
        // po kliknięciu obok (okno programu bywa wtedy ukryte).
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source)
                _ = SetForegroundWindow(source.Handle);
        };
        return menu;
    }

    public static void Show(ContextMenu? menu)
    {
        if (menu == null)
            return;
        menu.IsOpen = false;
        menu.IsOpen = true;
    }

    private static FrameworkElement CreateLineIcon(string data)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Style = (Style)Application.Current.FindResource("GlassIconPath")
        };
        var box = new Grid { Width = 24, Height = 24 };
        box.Children.Add(path);
        return new Viewbox { Width = 16, Height = 16, Child = box };
    }
}
