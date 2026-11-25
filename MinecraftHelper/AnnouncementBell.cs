using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using MinecraftHelper.Services;

namespace MinecraftHelper
{
    /// <summary>
    /// Dzwonek „Komunikaty” — WSPÓLNY dla obu programów. Mała kontrolka z licznikiem nieprzeczytanych;
    /// po kliknięciu lista komunikatów od autora w szklanym okienku. Gdy przychodzi nowy komunikat,
    /// w prawym dolnym rogu ekranu wyskakuje małe okienko (to samo, co przy ukryciu programu do zasobnika):
    /// wiadomość od autora i ostrzeżenie pokazują od razu treść, nowa wersja mówi krótko, że jest.
    /// Okienko nie przejmuje fokusu (nie wyrywa gry z pełnego ekranu), pojawia się niezależnie od tego, czy
    /// okno programu jest aktywne, znika samo po kilkunastu sekundach, a kliknięcie przywraca program
    /// i otwiera listę. Zamknięcie listy oznacza komunikaty jako przeczytane. Pokazuje też, że jest nowa
    /// wersja programu. Kontrolka jest ukryta, gdy lista jest pusta. Budowana w kodzie, ze stylami
    /// i pędzlami motywu (DynamicResource), więc działa w jasnym i ciemnym.
    /// </summary>
    internal sealed class AnnouncementBell : Grid
    {
        // Margines okienka na cień/poświatę; mniejszy ucinałby je na krawędzi okienka.
        private const double ShadowPadding = 26;
        private static readonly Geometry BellGeometry = CreateBellGeometry();
        private static readonly Brush DarkInk = CreateFrozenBrush(Color.FromRgb(0x1B, 0x1C, 0x33));
        private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

        private readonly bool _compact;
        private readonly bool _opensToTheRight;
        private readonly Button _chip = new();
        private readonly TextBlock _label = new();
        private readonly Border _badge = new();
        private readonly TextBlock _badgeText = new();
        private readonly Popup _popup = new();
        private readonly Border _card = new();
        private readonly StackPanel _list = new();
        private readonly Button _markReadButton = new();
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);
        private IReadOnlyList<AnnouncementItem> _items = Array.Empty<AnnouncementItem>();
        private AnnouncementLevel _badgeLevel = AnnouncementLevel.Info;
        private DateTime _closedAtUtc = DateTime.MinValue;

        // Okienko „jest nowa wiadomość”. _announced: o czym już powiedziało w tej sesji; przeczytane z niej wypadają,
        // więc komunikat, który znów stanie się nowy, dostanie okienko jeszcze raz.
        private readonly HashSet<string> _announced = new(StringComparer.Ordinal);
        private IReadOnlyList<AnnouncementItem> _pending = Array.Empty<AnnouncementItem>();
        private TrayToastWindow? _toast;

        /// <summary>Wołane z identyfikatorami komunikatów, które właśnie przestały być nowe.</summary>
        public event Action<IReadOnlyCollection<string>>? MarkedRead;

        /// <summary>Ikona w okienku powiadomienia (zasób aplikacji, np. „Assets/miner.ico”) — każdy program ma własną.</summary>
        public string ToastIconResource { get; set; } = "Assets/miner.ico";

        /// <summary>
        /// Przywraca okno programu (z zasobnika, z tła); wołane po kliknięciu okienka powiadomienia.
        /// Program ma własny sposób przywracania, więc podaje go sam.
        /// </summary>
        public Action? RestoreWindow { get; set; }

        /// <param name="compact">Tylko ikona i licznik (wąskie okno Auto Clickera); inaczej ikona z napisem.</param>
        /// <param name="opensToTheRight">Okienko obok kontrolki (pasek boczny Kopacza); inaczej pod nią, równo z prawą krawędzią.</param>
        public AnnouncementBell(bool compact, bool opensToTheRight)
        {
            _compact = compact;
            _opensToTheRight = opensToTheRight;
            Visibility = Visibility.Collapsed;

            BuildChip();
            BuildPopup();
            Children.Add(_chip);
            Children.Add(_popup);

            // Zdarzenie motywu jest statyczne — odpinamy je, gdy kontrolka znika z okna, żeby go nie trzymało.
            Loaded += (_, _) =>
            {
                GlassThemeMode.Changed += OnThemeChanged;
                OnThemeChanged();
            };
            Unloaded += (_, _) =>
            {
                GlassThemeMode.Changed -= OnThemeChanged;
                DismissToast();
            };
        }

        /// <summary>Podmienia listę (po pobraniu z GitHuba). <paramref name="readIds"/> to już przeczytane.</summary>
        public void SetItems(IReadOnlyList<AnnouncementItem> items, IEnumerable<string> readIds)
        {
            _items = items;
            _read.Clear();
            foreach (string id in readIds)
                _read.Add(id);
            Refresh();
        }

        private void BuildChip()
        {
            _chip.SetResourceReference(StyleProperty, "GlassChipButton");
            _chip.HorizontalAlignment = HorizontalAlignment.Stretch;
            _chip.Padding = _compact ? CompactPadding(hasBadge: false) : new Thickness(12, 0, 6, 0);
            _chip.Click += Chip_Click;

            var icon = new System.Windows.Shapes.Path { Data = BellGeometry, StrokeThickness = 1.9, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            icon.SetResourceReference(Shape.StrokeProperty, "GlassInkBrush");
            // Dzwonek zajmuje w kwadracie 24×24 wysokości od 5 do 21 (środek 13, nie 12) — przesuwamy o 1, żeby stał pionowo pośrodku.
            icon.RenderTransform = new TranslateTransform(0, -1);
            var iconBox = new Viewbox { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Child = new Grid { Width = 24, Height = 24, Children = { icon } } };

            _label.Text = "Komunikaty";
            _label.FontWeight = FontWeights.SemiBold;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.Margin = new Thickness(8, 0, 8, 0);
            _label.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;

            _badgeText.FontSize = 11;
            _badgeText.FontWeight = FontWeights.Bold;
            _badgeText.HorizontalAlignment = HorizontalAlignment.Center;
            _badgeText.VerticalAlignment = VerticalAlignment.Center;
            _badge.CornerRadius = new CornerRadius(10);
            _badge.MinWidth = 20;
            _badge.Height = 20;
            _badge.Padding = new Thickness(6, 0, 6, 0);
            _badge.Margin = _compact ? new Thickness(6, 0, 0, 0) : new Thickness(0);
            _badge.VerticalAlignment = VerticalAlignment.Center;
            _badge.HorizontalAlignment = HorizontalAlignment.Right;
            _badge.Child = _badgeText;

            // Wersja zwarta: zawartość ma tyle, ile potrzebuje, i leży pośrodku. Rozciągnięta wypełniłaby cały
            // przycisk, a środkowa kolumna (na napis, tu ukryta) zepchnęłaby ikonę do lewej krawędzi.
            var content = new Grid { HorizontalAlignment = _compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(iconBox);
            Grid.SetColumn(_label, 1);
            content.Children.Add(_label);
            Grid.SetColumn(_badge, 2);
            content.Children.Add(_badge);
            _chip.Content = content;
        }

        // Bez licznika: równe odstępy po obu stronach, żeby sama ikona stała dokładnie pośrodku przycisku.
        // 6 + 16 + 6 + ramka po 1 px = 30, czyli okrągły przycisk o wysokości chipa.
        private static Thickness CompactPadding(bool hasBadge) => hasBadge ? new Thickness(10, 0, 5, 0) : new Thickness(6, 0, 6, 0);

        private void BuildPopup()
        {
            var title = new TextBlock { Text = "Komunikaty", FontWeight = FontWeights.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "GlassInkBrush");
            _markReadButton.Content = "Oznacz jako przeczytane";
            _markReadButton.SetResourceReference(StyleProperty, "GlassTextButton");
            _markReadButton.HorizontalAlignment = HorizontalAlignment.Right;
            _markReadButton.Click += (_, _) => MarkAllRead();

            var header = new Grid { Margin = new Thickness(8, 4, 4, 6) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(title);
            Grid.SetColumn(_markReadButton, 1);
            header.Children.Add(_markReadButton);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420,
                Content = _list
            };

            // Okienko musi odróżniać się od okna programu także w ciemnym motywie, gdzie zwykłe tło okienek
            // (GlassPopupBrush) jest prawie takie samo jak tło okna: dokładamy jaśniejszą warstwę, ramkę w kolorze
            // akcentu, linię pod nagłówkiem i cień (w ciemnym — poświata akcentu, bo czarny cień na ciemnym nie jest widać).
            var tint = new Border { CornerRadius = new CornerRadius(18.5), IsHitTestVisible = false };
            tint.SetResourceReference(Border.BackgroundProperty, "GlassRaisedBrush");
            var divider = new Border { Height = 1, Margin = new Thickness(6, 0, 6, 4) };
            divider.SetResourceReference(Border.BackgroundProperty, "GlassLineBrush");
            var content = new Border { Padding = new Thickness(8), Child = new StackPanel { Children = { header, divider, scroll } } };

            var card = _card;
            card.CornerRadius = new CornerRadius(20);
            card.BorderThickness = new Thickness(1.5);
            card.Width = _compact ? 340 : 388;
            card.Child = new Grid { Children = { tint, content } };
            card.SetResourceReference(Border.BackgroundProperty, "GlassPopupBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "GlassAccentSolidBrush");
            ApplyShadow();
            card.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    _popup.IsOpen = false;
                    e.Handled = true;
                }
            };

            _popup.AllowsTransparency = true;
            _popup.StaysOpen = false;
            _popup.PopupAnimation = PopupAnimation.Fade;
            _popup.Placement = PlacementMode.Custom;
            _popup.PlacementTarget = _chip;
            _popup.CustomPopupPlacementCallback = Place;
            _popup.Child = new Border { Padding = new Thickness(ShadowPadding), Child = card };
            // Motyw i akcent mogły się zmienić od ostatniego razu — cień zależy od obu. Lista otwarta = okienko powiadomienia zbędne.
            _popup.Opened += (_, _) =>
            {
                ApplyShadow();
                DismissToast();
            };
            _popup.Closed += (_, _) =>
            {
                _closedAtUtc = DateTime.UtcNow;
                MarkAllRead();
            };
        }

        /// <summary>
        /// Pokazuje okienko powiadomienia o nowych komunikatach: o najpoważniejszym, z licznikiem pozostałych.
        /// Niezależne od okna programu — działa też, gdy jest w tle albo w zasobniku (po to jest powiadomienie).
        /// Okienko nie przejmuje fokusu ani nie odbiera klawiszy, więc nie przeszkadza w grze.
        /// </summary>
        private void TryShowToast()
        {
            if (_pending.Count == 0 || Visibility != Visibility.Visible || _popup.IsOpen)
                return;

            AnnouncementItem headline = AnnouncementBubble.Headline(_pending);
            int count = _pending.Count;
            foreach (AnnouncementItem item in _pending)
                _announced.Add(item.Id);
            _pending = Array.Empty<AnnouncementItem>();

            Brush accent = headline.Level switch
            {
                AnnouncementLevel.Alarm => GlassPalette.Bad,
                AnnouncementLevel.Warning => GlassPalette.Warn,
                _ => GlassPalette.AccentSolid
            };

            // Nowe okienko zastępuje poprzednie (to, które jeszcze nie zniknęło).
            DismissToast();
            _toast = new TrayToastWindow(
                headline.Title,
                AnnouncementBubble.Body(headline),
                ToastIconResource,
                OnToastClicked,
                tag: AnnouncementBubble.Tag(headline).ToUpper(Polish),
                footer: count > 1 ? $"Razem nowych: {count} — kliknij, aby zobaczyć wszystkie" : null,
                visibleFor: AnnouncementBubble.VisibleFor,
                accent: accent);
            _toast.Show();
        }

        private void DismissToast()
        {
            TrayToastWindow? toast = _toast;
            _toast = null;
            toast?.Dismiss();
        }

        private void OnToastClicked()
        {
            _toast = null; // okienko samo się zamyka po kliknięciu
            RestoreWindow?.Invoke();

            // Okno mogło dopiero wracać z zasobnika (u Kopacza przez panel kont), więc listę otwieramy chwilę później, gdy jest widoczne.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (IsLoaded && IsVisible)
                    _popup.IsOpen = true;
            };
            timer.Start();
        }

        private void ApplyShadow()
        {
            Color accent = TryFindResource("GlassAccentSolidBrush") is SolidColorBrush brush ? brush.Color : Color.FromRgb(0x54, 0x58, 0xE8);
            _card.Effect = GlassThemeMode.IsDark
                ? new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Color = accent, Opacity = 0.45 }
                : new DropShadowEffect { BlurRadius = 20, ShadowDepth = 6, Direction = 270, Color = Color.FromRgb(0x1E, 0x24, 0x50), Opacity = 0.26 };
        }

        private CustomPopupPlacement[] Place(Size popupSize, Size targetSize, Point offset)
        {
            // Rozmiar okienka zawiera margines na cień — odejmujemy go, żeby krawędzie karty były równo z kontrolką.
            Point point = _opensToTheRight
                ? new Point(targetSize.Width - ShadowPadding + 6, -ShadowPadding)
                : new Point(targetSize.Width - popupSize.Width + ShadowPadding, targetSize.Height - ShadowPadding + 6);
            return new[] { new CustomPopupPlacement(point, PopupPrimaryAxis.None) };
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            // Kliknięcie w kontrolkę przy otwartym okienku najpierw je zamyka (StaysOpen=false);
            // bez tej bramki ten sam klik otworzyłby je od razu z powrotem.
            if ((DateTime.UtcNow - _closedAtUtc).TotalMilliseconds < 250)
                return;
            DismissToast();
            _popup.IsOpen = !_popup.IsOpen;
        }

        private void MarkAllRead()
        {
            List<string> unread = _items.Where(item => !_read.Contains(item.Id)).Select(item => item.Id).ToList();
            if (unread.Count == 0)
                return;
            foreach (string id in unread)
                _read.Add(id);
            Refresh();
            MarkedRead?.Invoke(unread);
        }

        private void Refresh()
        {
            bool show = _items.Count > 0;
            Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show && _popup.IsOpen)
                _popup.IsOpen = false;

            List<AnnouncementItem> unread = _items.Where(item => !_read.Contains(item.Id)).ToList();
            _badge.Visibility = unread.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_compact)
                _chip.Padding = CompactPadding(hasBadge: unread.Count > 0);
            _badgeText.Text = unread.Count.ToString(CultureInfo.InvariantCulture);
            ApplyBadgeLevel(unread.Count == 0 ? AnnouncementLevel.Info : unread.Max(item => item.Level));
            _markReadButton.Visibility = unread.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _chip.ToolTip = unread.Count > 0 ? $"Komunikaty od autora — nowe: {unread.Count}" : "Komunikaty od autora";
            AutomationProperties.SetName(_chip, unread.Count > 0 ? $"Komunikaty, nowe: {unread.Count}" : "Komunikaty");

            _list.Children.Clear();
            for (int index = 0; index < _items.Count; index++)
            {
                // Cienka linia między komunikatami — bez niej przeczytane (bez podświetlenia) zlewają się w jedną plamę.
                if (index > 0)
                {
                    var line = new Border { Height = 1, Margin = new Thickness(8, 2, 8, 2) };
                    line.SetResourceReference(Border.BackgroundProperty, "GlassLineBrush");
                    _list.Children.Add(line);
                }
                _list.Children.Add(BuildItem(_items[index], !_read.Contains(_items[index].Id)));
            }

            // Okienko mówi tylko o tym, co nowe i czego jeszcze nie ogłosiło; wszystko przeczytane = okienko znika.
            _announced.RemoveWhere(id => _read.Contains(id));
            _pending = AnnouncementBubble.Pending(_items, _read, _announced);
            if (!show || unread.Count == 0)
                DismissToast();
            else
                TryShowToast();
        }

        private void ApplyBadgeLevel(AnnouncementLevel level)
        {
            _badgeLevel = level;
            string key = level switch
            {
                AnnouncementLevel.Alarm => "GlassBadBrush",
                AnnouncementLevel.Warning => "GlassWarnBrush",
                _ => "GlassAccentBrush"
            };
            _badge.SetResourceReference(Border.BackgroundProperty, key);
            // Bursztyn i czerwień są ciemne w jasnym motywie (biały tekst), a jasne w ciemnym (ciemny tekst);
            // akcent jest ciemny w obu. Kolor tekstu nie jest zasobem, więc odświeżamy go przy zmianie motywu.
            bool lightFill = level != AnnouncementLevel.Info && GlassThemeMode.IsDark;
            _badgeText.Foreground = lightFill ? DarkInk : Brushes.White;
        }

        private void OnThemeChanged()
        {
            ApplyBadgeLevel(_badgeLevel);
            ApplyShadow();
        }

        private static UIElement BuildItem(AnnouncementItem item, bool unread)
        {
            string levelBrush = item.Level switch
            {
                AnnouncementLevel.Alarm => "GlassBadBrush",
                AnnouncementLevel.Warning => "GlassWarnBrush",
                _ => "GlassAccentInkBrush"
            };

            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 6, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(Shape.FillProperty, levelBrush);

            var title = new TextBlock { Text = item.Title, FontWeight = FontWeights.Bold, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "GlassInkBrush");
            var text = new TextBlock { Text = item.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, "GlassInk2Brush");

            var stack = new StackPanel();
            stack.Children.Add(title);
            stack.Children.Add(text);

            string date = FormatDate(item.PublishedUtc);
            if (date.Length > 0 || item.Link != null)
            {
                var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                if (date.Length > 0)
                {
                    var time = new TextBlock { Text = date, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                    time.SetResourceReference(TextBlock.ForegroundProperty, "GlassInk2Brush");
                    meta.Children.Add(time);
                }
                if (item.Link is Uri link)
                {
                    var open = new Button { Content = item.LinkText, Margin = new Thickness(date.Length > 0 ? 10 : 0, 0, 0, 0), FontSize = 11.5 };
                    open.SetResourceReference(StyleProperty, "GlassTextButton");
                    open.Click += (_, _) => OpenLink(link);
                    meta.Children.Add(open);
                }
                stack.Children.Add(meta);
            }

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(dot);
            Grid.SetColumn(stack, 1);
            row.Children.Add(stack);

            var container = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(8, 10, 8, 10), Margin = new Thickness(0, 0, 0, 2), Child = row };
            if (unread)
                container.SetResourceReference(Border.BackgroundProperty, "GlassAccentSoftBrush");
            return container;
        }

        private static string FormatDate(DateTime? utc)
        {
            if (utc == null)
                return string.Empty;
            DateTime local = utc.Value.ToLocalTime();
            bool today = local.Date == DateTime.Today;
            string day = today ? "dziś" : local.Year == DateTime.Today.Year ? local.ToString("d MMM", Polish) : local.ToString("d MMM yyyy", Polish);
            // Data bez godziny w pliku (północ UTC) — pokazujemy sam dzień.
            return utc.Value.TimeOfDay == TimeSpan.Zero ? day : $"{day}, {local.ToString("HH:mm", Polish)}";
        }

        private static void OpenLink(Uri uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Brak domyślnej przeglądarki — nic do zrobienia.
            }
        }

        private static Geometry CreateBellGeometry()
        {
            Geometry geometry = Geometry.Parse("M6,16.5 V11 A6,6 0 0 1 18,11 V16.5 L19.5,18 H4.5 Z M10,21 H14");
            geometry.Freeze();
            return geometry;
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
