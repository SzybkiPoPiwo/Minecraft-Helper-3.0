using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace MinecraftHelper
{
    internal enum ArrowSide { Up, Down, Left, Right }

    /// <summary>Jeden krok: zakładka, zrzut, podświetlenie i dymek (współrzędne w jednostkach zrzutu).</summary>
    internal sealed record TutorialStep(
        string Tab,
        string Image,
        Rect Spot,
        ArrowSide Arrow,
        Point ArrowAt,
        double TipLeft,
        double TipTop,
        double TipWidth,
        string Title,
        string Text);

    /// <summary>To, czym różnią się samouczki programów: rozmiar zrzutów, kroki i odmiana nazwy.</summary>
    internal sealed record TutorialContent(
        double StageWidth,
        double StageHeight,
        double CardWidth,
        string FullName,
        string IconName,
        string Nominative,
        string Genitive,
        string Locative,
        string FirstStepHint,
        string TabButtonHint,
        TutorialStep[] Steps);

    /// <summary>
    /// Samouczek w trybie ćwiczeń, wspólny dla Kopacza i Auto Clickera (projekt z kanwy „Przewodnik Minecraft Helper”).
    /// To makieta z prawdziwych zrzutów zakładek: użytkownik klika podświetlone miejsca, a nic tu
    /// nie zmienia ustawień, nie szuka gry, nie łapie bindów i nie łączy bota. Okno ma rozmiar
    /// i położenie okna programu; po zamknięciu w tym miejscu pokazuje się prawdziwy program.
    /// Cała scena jest w jednostkach zrzutu, wyświetlana 1:1 (Viewbox tylko na mniejszym ekranie).
    /// </summary>
    internal sealed class PracticeTutorialWindow : Window
    {
        private const double TitleBarHeight = 32;
        private static readonly string PackRoot = $"pack://application:,,,/{typeof(PracticeTutorialWindow).Assembly.GetName().Name};component/";

        private enum Screen { Intro, Step, Done }

        /// <summary>
        /// Wybór z ostatniej strony instalatora: %AppData%\Minecraft Helper\tutorial.json
        /// ({ "showOnFirstRun": true|false }). Brak pliku albo błędny plik = pokaż samouczek.
        /// </summary>
        public static bool ReadInstallerPreference(out string? error)
        {
            error = null;
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Minecraft Helper",
                    "tutorial.json");
                if (!System.IO.File.Exists(path))
                    return true;

                using var document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                return !document.RootElement.TryGetProperty("showOnFirstRun", out System.Text.Json.JsonElement value)
                    || value.ValueKind != System.Text.Json.JsonValueKind.False;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return true;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        private static readonly IEasingFunction MoveEase = Frozen(new CubicBezierEase(0.22, 1, 0.36, 1));
        private static readonly IEasingFunction FadeEase = Frozen(new CubicBezierEase(0.2, 0.8, 0.2, 1));

        private readonly TutorialContent _content;
        private readonly bool _fromSettings;
        private readonly string? _tab;
        private readonly TutorialStep[] _steps;
        private readonly double _scale;
        private readonly Dictionary<string, BitmapImage> _shots = new();
        private readonly Canvas _stage;

        private Image _shotFront = null!;
        private Image _shotBack = null!;
        private string _shownImage = "";

        private readonly RectangleGeometry _hole;
        private Path _dim = null!;
        private Border _ring = null!;
        private DropShadowEffect _ringGlow = null!;
        private Grid _ripple = null!;

        private Path _arrow = null!;
        private Border _tip = null!;
        private readonly TranslateTransform _tipShift = new();
        private TextBlock _tipLabel = null!;
        private StackPanel _tipDots = null!;
        private TextBlock _tipTitle = null!;
        private TextBlock _tipText = null!;
        private TextBlock _tipProgress = null!;
        private Button _tipNext = null!;

        private Rectangle _cardDim = null!;
        private Border _card = null!;
        private readonly TranslateTransform _cardShift = new();
        private readonly ScaleTransform _cardScale = new(1, 1);
        private TextBlock _cardTitle = null!;
        private TextBlock _cardText = null!;
        private TextBlock _cardNote = null!;
        private Button _cardSecondary = null!;
        private Button _cardPrimary = null!;

        private Screen _screen = Screen.Intro;
        private int _step;
        private int _transition;
        private bool _ringPlaced;

        private double StageWidth => _content.StageWidth;
        private double StageHeight => _content.StageHeight;
        private double CardWidth => _content.CardWidth;

        /// <param name="tab">null = pełny samouczek (wszystkie zakładki), inaczej tylko sekcje tej zakładki.</param>
        public PracticeTutorialWindow(TutorialContent content, Rect bounds, bool fromSettings, string? tab = null)
        {
            _content = content;
            _stage = new Canvas { Width = content.StageWidth, Height = content.StageHeight, ClipToBounds = true };
            _hole = new RectangleGeometry(new Rect(content.StageWidth / 2, content.StageHeight / 2, 0, 0), 18, 18);
            _fromSettings = fromSettings;
            TutorialStep[] tabSteps = tab == null ? Array.Empty<TutorialStep>() : content.Steps.Where(step => step.Tab == tab).ToArray();
            _tab = tabSteps.Length > 0 ? tab : null;
            _steps = _tab == null ? content.Steps : tabSteps;
            Title = _tab == null ? $"Samouczek — {content.FullName}" : $"Samouczek: {_tab} — {content.FullName}";
            GlassChrome.Apply(this);
            UseLightTheme();
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowInTaskbar = true;
            SnapsToDevicePixels = true;

            // Okno ma dokładnie rozmiar sceny (piksel zrzutu = piksel ekranu przy 100%), więc nic nie
            // jest przeskalowane o ułamek procenta i rozmyte. Zmniejszamy tylko, gdy ekran jest mniejszy.
            Rect workArea = SystemParameters.WorkArea;
            _scale = Math.Min(1.0, Math.Min(workArea.Width / StageWidth, workArea.Height / StageHeight));
            double width = Math.Round(StageWidth * _scale);
            double height = Math.Round(StageHeight * _scale);
            Width = width;
            Height = height;
            Left = Math.Max(workArea.Left, Math.Min(bounds.Left + (bounds.Width - width) / 2, workArea.Right - width));
            Top = Math.Max(workArea.Top, Math.Min(bounds.Top + (bounds.Height - height) / 2, workArea.Bottom - height));

            try
            {
                Icon = BitmapFrame.Create(new Uri($"{PackRoot}Assets/{content.IconName}.ico", UriKind.Absolute));
            }
            catch
            {
                // Bez ikony okno nadal działa.
            }

            BuildStage();
            Content = _scale < 1.0
                ? new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _stage }
                : _stage;

            SourceInitialized += (_, _) =>
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int preference = DwmwcpRound;
                _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            };
            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (_, _) =>
            {
                ApplyScreen(Screen.Intro, 0, animate: true);
                StartPulse();
            };
        }

        // ---------------------------------------------------------------- budowa sceny

        private void BuildStage()
        {
            _shotBack = CreateShotImage();
            _shotFront = CreateShotImage();
            _stage.Children.Add(_shotBack);
            _stage.Children.Add(_shotFront);

            // Przyciemnienie z otworem na podświetlany element.
            _dim = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0x94, 0x0E, 0x0C, 0x26)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, StageWidth, StageHeight)), _hole),
                IsHitTestVisible = false,
                Opacity = 0
            };
            _stage.Children.Add(_dim);

            BuildRing();
            BuildTooltip();
            BuildCard();
            BuildPracticeBanner();
            BuildTitleBarZones();
        }

        private Image CreateShotImage()
        {
            var image = new Image
            {
                Width = StageWidth,
                Height = StageHeight,
                Stretch = Stretch.Fill,
                Opacity = 0
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        private void BuildRing()
        {
            Color accent = AccentColor();
            _ringGlow = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 26, Opacity = 0.6 };

            var rippleRing = new Ellipse
            {
                Width = 22,
                Height = 22,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromArgb(0xE6, accent.R, accent.G, accent.B)),
                StrokeThickness = 3
            };
            _ripple = new Grid { Width = 22, Height = 22, IsHitTestVisible = false };
            _ripple.Children.Add(rippleRing);
            _ripple.Children.Add(dot);

            _ring = new Border
            {
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(0x01, 0xFF, 0xFF, 0xFF)),
                Effect = _ringGlow,
                Cursor = Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Child = _ripple,
                ToolTip = "Kliknij, aby przejść dalej"
            };
            _ring.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Next();
            };
            _stage.Children.Add(_ring);

            if (Animate)
            {
                var scale = (ScaleTransform)rippleRing.RenderTransform;
                var grow = new DoubleAnimation(0.6, 2.6, TimeSpan.FromMilliseconds(1400)) { RepeatBehavior = RepeatBehavior.Forever };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                rippleRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(1400)) { RepeatBehavior = RepeatBehavior.Forever });
            }
        }

        private void BuildTooltip()
        {
            var white = new SolidColorBrush(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF));
            _arrow = new Path { Fill = white, IsHitTestVisible = false, Opacity = 0 };
            _stage.Children.Add(_arrow);

            _tipLabel = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Light("GlassAccentInkBrush") };
            var chip = new Border
            {
                Background = Light("GlassAccentSoftBrush"),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 3, 10, 3),
                Child = _tipLabel,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _tipDots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _tipProgress = new TextBlock { FontSize = 12, Foreground = Light("GlassInk2Brush"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var labels = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            labels.Children.Add(chip);
            labels.Children.Add(_tipProgress);
            var header = new Grid();
            header.Children.Add(labels);
            header.Children.Add(_tipDots);

            _tipTitle = new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Light("GlassInkBrush"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            _tipText = new TextBlock
            {
                FontSize = 13.5,
                LineHeight = 21,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x56)),
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var skip = new Button { Content = "Pomiń samouczek", Style = GlassChrome.GetStyle("GlassTextButton"), HorizontalAlignment = HorizontalAlignment.Left };
            skip.Click += (_, _) => Close();
            var back = new Button { Content = "Wstecz", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
            back.Click += (_, _) => Back();
            _tipNext = new Button { Content = "Dalej", MinWidth = 84, Style = GlassChrome.GetStyle("GlassPrimaryButton") };
            _tipNext.Click += (_, _) => Next();
            var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            navigation.Children.Add(back);
            navigation.Children.Add(_tipNext);
            var buttons = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(skip);
            buttons.Children.Add(navigation);

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(_tipTitle);
            body.Children.Add(_tipText);
            body.Children.Add(buttons);

            _tip = new Border
            {
                Background = white,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(20, 18, 20, 16),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0x0E, 0x0C, 0x26), Opacity = 0.4, BlurRadius = 40, ShadowDepth = 14, Direction = 270 },
                RenderTransform = _tipShift,
                Child = body,
                Opacity = 0,
                Visibility = Visibility.Collapsed
            };
            _stage.Children.Add(_tip);
        }

        private void BuildCard()
        {
            _cardDim = new Rectangle
            {
                Width = StageWidth,
                Height = StageHeight,
                Fill = new SolidColorBrush(Color.FromArgb(0x94, 0x0E, 0x0C, 0x26)),
                Opacity = 0,
                // Leży nad dymkiem i podświetleniem — nie może łapać ich kliknięć.
                IsHitTestVisible = false
            };
            _stage.Children.Add(_cardDim);

            var icon = new Image { Width = 80, Height = 80, HorizontalAlignment = HorizontalAlignment.Center };
            try
            {
                icon.Source = new BitmapImage(new Uri($"{PackRoot}Assets/{_content.IconName}.png", UriKind.Absolute));
            }
            catch
            {
                icon.Visibility = Visibility.Collapsed;
            }
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            _cardTitle = new TextBlock { FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Light("GlassInkBrush"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
            _cardText = new TextBlock
            {
                FontSize = 14,
                LineHeight = 21.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x3A, 0x40, 0x56)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            };

            _cardNote = new TextBlock { FontSize = 13, LineHeight = 19, Foreground = Light("GlassInkBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var noteRow = new Grid();
            noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            noteRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var shield = ShieldIcon();
            noteRow.Children.Add(shield);
            Grid.SetColumn(_cardNote, 1);
            noteRow.Children.Add(_cardNote);
            var note = new Border
            {
                Background = Light("GlassAccentSoftBrush"),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, AccentColor().R, AccentColor().G, AccentColor().B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 11, 14, 11),
                Margin = new Thickness(0, 14, 0, 0),
                Child = noteRow
            };

            _cardSecondary = new Button { Height = 44, Margin = new Thickness(0, 0, 5, 0), FontSize = 14 };
            _cardSecondary.Click += (_, _) => CardSecondary();
            _cardPrimary = new Button { Height = 44, Margin = new Thickness(5, 0, 0, 0), FontSize = 14, Style = GlassChrome.GetStyle("GlassPrimaryButton") };
            _cardPrimary.Click += (_, _) => CardPrimary();
            var cardButtons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 18, 0, 0) };
            cardButtons.Children.Add(_cardSecondary);
            cardButtons.Children.Add(_cardPrimary);

            var body = new StackPanel();
            body.Children.Add(icon);
            body.Children.Add(_cardTitle);
            body.Children.Add(_cardText);
            body.Children.Add(note);
            body.Children.Add(cardButtons);

            var transforms = new TransformGroup();
            transforms.Children.Add(_cardScale);
            transforms.Children.Add(_cardShift);
            _card = new Border
            {
                Width = CardWidth,
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                Padding = new Thickness(28, 28, 28, 24),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0x0E, 0x0C, 0x26), Opacity = 0.45, BlurRadius = 60, ShadowDepth = 24, Direction = 270 },
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = transforms,
                Child = body,
                Opacity = 0
            };
            Canvas.SetLeft(_card, (StageWidth - CardWidth) / 2);
            Canvas.SetTop(_card, 130);
            _stage.Children.Add(_card);
        }

        private void BuildPracticeBanner()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Color.FromRgb(0xFB, 0xBF, 0x24), ShadowDepth = 0, BlurRadius = 8, Opacity = 0.8 }
            });
            row.Children.Add(new TextBlock
            {
                Text = "Tryb ćwiczeń — kliknięcia niczego nie zmieniają",
                Foreground = Brushes.White,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var banner = new Border
            {
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(Color.FromArgb(0x38, 0x16, 0x1A, 0x2B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Child = row,
                IsHitTestVisible = false
            };
            banner.Loaded += (_, _) => Canvas.SetLeft(banner, (StageWidth - banner.ActualWidth) / 2);
            Canvas.SetBottom(banner, 14);
            _stage.Children.Add(banner);
        }

        // Pasek tytułu na zrzucie zachowuje się jak prawdziwy: przeciąganie, minimalizacja i X
        // (X zamyka samouczek i otwiera program).
        private void BuildTitleBarZones()
        {
            var drag = new Rectangle
            {
                Width = StageWidth - 140,
                Height = TitleBarHeight,
                Fill = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0))
            };
            drag.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
            _stage.Children.Add(drag);

            _stage.Children.Add(TitleButton(StageWidth - 138, "Minimalizuj", hoverRed: false, () => WindowState = WindowState.Minimized));
            _stage.Children.Add(TitleButton(StageWidth - 46, "Zamknij samouczek", hoverRed: true, Close));
        }

        private static Border TitleButton(double left, string tip, bool hoverRed, Action action)
        {
            var hover = hoverRed
                ? new SolidColorBrush(Color.FromArgb(0x59, 0xE8, 0x11, 0x23))
                : new SolidColorBrush(Color.FromArgb(0x1A, 0x00, 0x00, 0x00));
            var idle = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0));
            var zone = new Border { Width = 46, Height = TitleBarHeight, Background = idle, ToolTip = tip };
            zone.MouseEnter += (_, _) => zone.Background = hover;
            zone.MouseLeave += (_, _) => zone.Background = idle;
            zone.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                action();
            };
            Canvas.SetLeft(zone, left);
            Canvas.SetTop(zone, 0);
            return zone;
        }

        // ---------------------------------------------------------------- nawigacja

        private void Next()
        {
            if (_screen == Screen.Intro)
                GoTo(Screen.Step, 0);
            else if (_screen == Screen.Step)
                GoTo(_step >= _steps.Length - 1 ? Screen.Done : Screen.Step, Math.Min(_step + 1, _steps.Length - 1));
            else
                Close();
        }

        private void Back()
        {
            if (_screen == Screen.Step)
                GoTo(_step == 0 ? Screen.Intro : Screen.Step, Math.Max(0, _step - 1));
            else if (_screen == Screen.Done)
                GoTo(Screen.Step, _steps.Length - 1);
        }

        private void CardSecondary()
        {
            if (_screen == Screen.Intro)
                Close();
            else
                GoTo(Screen.Intro, 0);
        }

        private void CardPrimary()
        {
            if (_screen == Screen.Intro)
                GoTo(Screen.Step, 0);
            else
                Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Enter:
                    Next();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Back();
                    e.Handled = true;
                    break;
            }
        }

        // Przejście: obecny dymek albo karta znika, potem zmienia się treść i wjeżdża nowa.
        private void GoTo(Screen screen, int step)
        {
            int transition = ++_transition;
            FrameworkElement leaving = _screen == Screen.Step ? _tip : _card;
            Fade(_arrow, 0, 160);
            var fadeOut = new DoubleAnimation(0, Dur(170)) { EasingFunction = FadeEase };
            fadeOut.Completed += (_, _) =>
            {
                if (transition == _transition)
                    ApplyScreen(screen, step, animate: true);
            };
            leaving.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void ApplyScreen(Screen screen, int step, bool animate)
        {
            Screen previous = _screen;
            _screen = screen;
            _step = step;

            if (screen == Screen.Step)
            {
                TutorialStep current = _steps[step];
                ShowShot(current.Image);
                _card.Visibility = Visibility.Collapsed;
                Fade(_cardDim, 0, 240);
                Fade(_dim, 1, 240);
                MoveSpot(current.Spot, fromCenter: previous != Screen.Step || !_ringPlaced);
                ShowTooltip(current);
                return;
            }

            ShowShot(_steps[0].Image);
            _ring.Visibility = Visibility.Collapsed;
            _ringPlaced = false;
            _tip.Visibility = Visibility.Collapsed;
            Fade(_dim, 0, 200);
            Fade(_cardDim, 1, 240);
            FillCard(screen);
            ShowCard();
        }

        // ---------------------------------------------------------------- elementy sceny

        private void ShowShot(string name)
        {
            if (name == _shownImage)
                return;
            _shownImage = name;

            (_shotFront, _shotBack) = (_shotBack, _shotFront);
            _shotFront.Source = LoadShot(name);
            // Zrzuty zawsze pod nakładkami (przyciemnienie, dymek, karta mają ZIndex 0).
            Panel.SetZIndex(_shotFront, -1);
            Panel.SetZIndex(_shotBack, -2);
            _shotFront.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(420)) { EasingFunction = FadeEase });
        }

        private BitmapImage LoadShot(string name)
        {
            if (_shots.TryGetValue(name, out BitmapImage? cached))
                return cached;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"{PackRoot}Assets/Tutorial/{name}.png", UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            _shots[name] = bitmap;
            return bitmap;
        }

        private void MoveSpot(Rect target, bool fromCenter)
        {
            _ring.Visibility = Visibility.Visible;
            Rect start = fromCenter
                ? new Rect(target.X + target.Width / 2 - 20, target.Y + target.Height / 2 - 20, 40, 40)
                : _hole.Rect;
            if (fromCenter)
                PlaceRing(start);

            var holeAnimation = new RectAnimation(start, target, Dur(520)) { EasingFunction = MoveEase };
            _hole.BeginAnimation(RectangleGeometry.RectProperty, holeAnimation);

            AnimateRing(Canvas.LeftProperty, target.X);
            AnimateRing(Canvas.TopProperty, target.Y);
            AnimateRing(WidthProperty, target.Width);
            AnimateRing(HeightProperty, target.Height);
            _ringPlaced = true;
        }

        private void PlaceRing(Rect rect)
        {
            foreach (DependencyProperty property in new[] { Canvas.LeftProperty, Canvas.TopProperty, WidthProperty, HeightProperty })
                _ring.BeginAnimation(property, null);
            Canvas.SetLeft(_ring, rect.X);
            Canvas.SetTop(_ring, rect.Y);
            _ring.Width = rect.Width;
            _ring.Height = rect.Height;
        }

        private void AnimateRing(DependencyProperty property, double to)
        {
            _ring.BeginAnimation(property, new DoubleAnimation(to, Dur(520)) { EasingFunction = MoveEase });
        }

        private void StartPulse()
        {
            if (!Animate)
                return;
            _ringGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(18, 34, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = FadeEase });
            _ringGlow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.45, 0.8, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = FadeEase });
        }

        private void ShowTooltip(TutorialStep step)
        {
            int index = Array.IndexOf(_steps, step);
            List<TutorialStep> tabSteps = _steps.Where(other => other.Tab == step.Tab).ToList();
            int local = tabSteps.IndexOf(step);
            _tipLabel.Text = $"{step.Tab} · {local + 1} z {tabSteps.Count}";
            _tipProgress.Text = _tab == null ? $"krok {index + 1} z {_steps.Length}" : "";
            _tipProgress.Visibility = _tab == null ? Visibility.Visible : Visibility.Collapsed;
            _tipTitle.Text = step.Title;
            _tipText.Text = step.Text;
            _tipNext.Content = index == _steps.Length - 1 ? "Zakończ" : "Dalej";

            _tipDots.Children.Clear();
            Color accent = AccentColor();
            for (int dot = 0; dot < tabSteps.Count; dot++)
            {
                _tipDots.Children.Add(new Border
                {
                    Width = dot == local ? 22 : 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(dot == 0 ? 0 : 5, 0, 0, 0),
                    Background = dot <= local
                        ? new SolidColorBrush(accent)
                        : new SolidColorBrush(Color.FromArgb(0x33, 0x16, 0x1A, 0x2B))
                });
            }

            _tip.Width = step.TipWidth;
            Canvas.SetLeft(_tip, step.TipLeft);
            if (step.Arrow == ArrowSide.Down)
            {
                _tip.ClearValue(Canvas.TopProperty);
                Canvas.SetBottom(_tip, StageHeight - step.ArrowAt.Y);
            }
            else
            {
                _tip.ClearValue(Canvas.BottomProperty);
                Canvas.SetTop(_tip, step.TipTop);
            }

            _arrow.Data = ArrowGeometry(step.Arrow);
            Canvas.SetLeft(_arrow, step.ArrowAt.X);
            Canvas.SetTop(_arrow, step.ArrowAt.Y);

            // Dymek wjeżdża od strony strzałki, z lekkim opóźnieniem za podświetleniem.
            (double fromX, double fromY) = step.Arrow switch
            {
                ArrowSide.Up => (0.0, 10.0),
                ArrowSide.Down => (0.0, -10.0),
                ArrowSide.Left => (10.0, 0.0),
                _ => (-10.0, 0.0)
            };
            // Wartości bazowe = stan sprzed wejścia, żeby dymek nie mignął przed opóźnioną animacją.
            _tip.Opacity = 0;
            _arrow.Opacity = 0;
            _tipShift.X = fromX;
            _tipShift.Y = fromY;
            _tip.Visibility = Visibility.Visible;
            var delay = TimeSpan.FromMilliseconds(Animate ? 140 : 0);
            _tip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(240)) { BeginTime = delay, EasingFunction = FadeEase });
            _arrow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(240)) { BeginTime = delay, EasingFunction = FadeEase });
            _tipShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Dur(300)) { BeginTime = delay, EasingFunction = MoveEase });
            _tipShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, Dur(300)) { BeginTime = delay, EasingFunction = MoveEase });
        }

        private void FillCard(Screen screen)
        {
            string practice = "To makieta programu do ćwiczeń: klikaj śmiało, nic tu nie zmienia ustawień i nie dotyka gry.";
            if (screen == Screen.Intro && _tab != null)
            {
                _cardTitle.Text = $"Samouczek: {_tab}";
                _cardText.Text = $"Pokażemy po kolei wszystkie sekcje tej zakładki ({StepsWord(_steps.Length)}). Klikaj podświetlone miejsca albo „Dalej”.";
                _cardNote.Text = practice;
                _cardSecondary.Content = $"Wróć do {_content.Genitive}";
                _cardPrimary.Content = "Zaczynamy";
            }
            else if (screen == Screen.Intro)
            {
                _cardTitle.Text = $"Witaj w {_content.Locative} {ReleaseNotesCatalog.CurrentVersion}";
                _cardText.Text = $"Przejdziesz po kolei przez wszystkie sekcje programu ({StepsWord(_steps.Length)}, kilka minut). Klikaj podświetlone miejsca — po samouczku otworzy się prawdziwy {_content.Nominative}.";
                _cardNote.Text = practice;
                _cardSecondary.Content = "Pomiń samouczek";
                _cardPrimary.Content = "Zaczynamy";
            }
            else if (_tab != null)
            {
                _cardTitle.Text = $"Zakładka {_tab} omówiona";
                _cardText.Text = $"Znasz już wszystkie sekcje tej zakładki. Wracamy do {_content.Genitive}.";
                _cardNote.Text = _content.TabButtonHint;
                _cardSecondary.Content = "Jeszcze raz";
                _cardPrimary.Content = $"Wróć do {_content.Genitive}";
            }
            else
            {
                _cardTitle.Text = "Samouczek zakończony";
                _cardText.Text = _fromSettings
                    ? $"Znasz już wszystkie sekcje programu. Wracamy do {_content.Genitive}."
                    : $"Znasz już wszystkie sekcje programu. Teraz otworzymy prawdziwego {_content.Genitive} — {_content.FirstStepHint}.";
                _cardNote.Text = $"Całość powtórzysz w Ustawieniach → Pokaż samouczek. {_content.TabButtonHint}";
                _cardSecondary.Content = "Jeszcze raz";
                _cardPrimary.Content = _fromSettings ? $"Wróć do {_content.Genitive}" : $"Otwórz {_content.Genitive}";
            }
        }

        private void ShowCard()
        {
            _card.Opacity = 0;
            _card.Visibility = Visibility.Visible;
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(380)) { EasingFunction = FadeEase });
            _cardShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, Dur(380)) { EasingFunction = MoveEase });
            _cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, Dur(380)) { EasingFunction = MoveEase });
            _cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, Dur(380)) { EasingFunction = MoveEase });
            _cardPrimary.Focus();
        }

        // ---------------------------------------------------------------- pomocnicze

        private static bool Animate => SystemParameters.ClientAreaAnimation;

        /// <summary>„1 krok”, „3 kroki”, „36 kroków” — polska odmiana.</summary>
        private static string StepsWord(int count)
        {
            int lastTwo = count % 100, last = count % 10;
            string word = count == 1 ? "krok"
                : last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? "kroki"
                : "kroków";
            return $"{count} {word}";
        }

        private static Duration Dur(int milliseconds) =>
            new(TimeSpan.FromMilliseconds(Animate ? milliseconds : 0));

        private static void Fade(UIElement element, double to, int milliseconds)
        {
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Dur(milliseconds)) { EasingFunction = FadeEase });
        }

        private Color AccentColor() =>
            Light("GlassAccentSolidBrush") is SolidColorBrush brush ? brush.Color : Color.FromRgb(0x54, 0x58, 0xE8);

        // Samouczek jest zawsze jasny — zrzuty zakładek są w jasnym motywie. Lokalne słowniki z jasnymi
        // wartościami mają pierwszeństwo przed nadpisaniami ciemnego motywu w zasobach aplikacji,
        // więc style przycisków, tekst i akcent w tym oknie zostają jasne także przy trybie ciemnym.
        private void UseLightTheme()
        {
            string accentFile = "GlassAccentIris.xaml";
            string? source = GlassAccents.CurrentDictionary()?.Source?.OriginalString;
            if (!string.IsNullOrEmpty(source))
                accentFile = System.IO.Path.GetFileName(source);
            string themes = PackRoot + "Themes/";
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themes + accentFile, UriKind.Absolute) });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themes + "GlassTheme.xaml", UriKind.Absolute) });
            Foreground = Light("GlassInkBrush");
        }

        private Brush Light(string key) => (Brush)FindResource(key);

        private static Geometry ArrowGeometry(ArrowSide side)
        {
            string data = side switch
            {
                ArrowSide.Up => "M0,12 L12,0 L24,12 Z",
                ArrowSide.Down => "M0,0 L24,0 L12,12 Z",
                ArrowSide.Left => "M12,0 L0,12 L12,24 Z",
                _ => "M0,0 L12,12 L0,24 Z"
            };
            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }

        private FrameworkElement CheckIcon()
        {
            var icon = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.Children.Add(new Ellipse { Fill = Light("GlassOkBrush") });
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M4.5,8.2 L6.8,10.5 L11.5,5.8"),
                Stroke = Brushes.White,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            });
            return icon;
        }

        private FrameworkElement ShieldIcon()
        {
            var icon = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.Children.Add(new Path { Data = Geometry.Parse("M9,1.5 L15,3.9 V8.3 C15,12.2 12.4,15.2 9,16.5 C5.6,15.2 3,12.2 3,8.3 V3.9 Z"), Fill = Light("GlassAccentSolidBrush") });
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse("M6,9.1 L8,11.1 L12,7.1"),
                Stroke = Brushes.White,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            });
            return icon;
        }

        private static IEasingFunction Frozen(CubicBezierEase ease)
        {
            ease.Freeze();
            return ease;
        }
    }
}
