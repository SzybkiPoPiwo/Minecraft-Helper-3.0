using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MinecraftHelper;

// Odliczanie przed startem kopania: 5 s na przełączenie
// się na inny pulpit (albo do okna gry), potem kopanie rusza samo. Esc anuluje.
internal sealed class StartCountdownWindow : Window
{
    private const double RingSize = 132;
    private const double RingThickness = 7;

    private readonly TimeSpan _duration;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly DispatcherTimer _timer;
    private readonly Ellipse _progress;
    private readonly TextBlock _digit;
    private int _shownSeconds = -1;

    public StartCountdownWindow(string title, string message, int seconds = 5)
    {
        _duration = TimeSpan.FromSeconds(Math.Max(1, seconds));
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current?.TryFindResource("GlassFont") is FontFamily font)
            FontFamily = font;

        var track = new Ellipse
        {
            Width = RingSize,
            Height = RingSize,
            Stroke = GlassPalette.NeutralSoft,
            StrokeThickness = RingThickness
        };
        _progress = new Ellipse
        {
            Width = RingSize,
            Height = RingSize,
            StrokeThickness = RingThickness,
            StrokeDashCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-90)
        };
        _digit = new TextBlock
        {
            FontSize = 46,
            FontWeight = FontWeights.Bold,
            Foreground = GlassPalette.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var ring = new Grid { Width = RingSize, Height = RingSize, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(track);
        ring.Children.Add(_progress);
        ring.Children.Add(_digit);

        var cancel = new Button
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(14, 0, 10, 0),
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsCancel = true,
            Content = BuildCancelContent()
        };
        cancel.Click += (_, _) => Finish(started: false);

        var panel = new StackPanel { Margin = new Thickness(28, 26, 28, 22) };
        panel.Children.Add(ring);
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = GlassPalette.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = GlassPalette.Muted,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260
        });
        panel.Children.Add(cancel);

        Content = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 5, Direction = 270, Opacity = 0.2, Color = Color.FromRgb(0x1E, 0x24, 0x50) },
            Child = panel
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(started: false);
                e.Handled = true;
            }
        };
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) =>
        {
            Tick();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private static FrameworkElement BuildCancelContent()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = "Anuluj", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new Border
        {
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(5, 1, 5, 1),
            CornerRadius = new CornerRadius(5),
            Background = GlassPalette.NeutralSoft,
            Child = new TextBlock { Text = "Esc", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = GlassPalette.Muted }
        });
        return content;
    }

    private void Tick()
    {
        double elapsed = (DateTime.UtcNow - _startedAtUtc).TotalSeconds;
        double remaining = Math.Max(0, _duration.TotalSeconds - elapsed);
        if (remaining <= 0)
        {
            Finish(started: true);
            return;
        }

        int seconds = (int)Math.Ceiling(remaining);
        if (seconds != _shownSeconds)
        {
            _shownSeconds = seconds;
            _digit.Text = seconds.ToString();
            // Kolor odliczania: akcent, przy 2 s pomarańcz, przy 1 s czerwień.
            _progress.Stroke = seconds <= 1
                ? GlassPalette.Bad
                : seconds == 2
                    ? GlassPalette.Warn
                    : GlassPalette.AccentSolid;
        }

        double circumference = Math.PI * (RingSize - RingThickness);
        double visible = circumference * (remaining / _duration.TotalSeconds) / RingThickness;
        _progress.StrokeDashArray = new DoubleCollection { Math.Max(0.01, visible), 1000 };
    }

    private TaskCompletionSource<bool>? _modeless;

    // Odliczanie bez okna modalnego: nie zabiera fokusu i po zamknięciu go nie oddaje Kopaczowi
    // (ShowDialog po zamknięciu aktywuje okno właściciela). Gra, do której przeszedł użytkownik,
    // zostaje aktywna — testy klikające od razu w Minecrafcie mogą działać.
    public Task<bool> RunModelessAsync()
    {
        _modeless = new TaskCompletionSource<bool>();
        ShowActivated = false;
        Topmost = true;
        Closed += (_, _) => _modeless.TrySetResult(false);
        Show();
        return _modeless.Task;
    }

    private void Finish(bool started)
    {
        if (!IsVisible)
            return;
        _timer.Stop();
        if (_modeless != null)
        {
            _modeless.TrySetResult(started);
            Close();
            return;
        }
        DialogResult = started;
    }
}
