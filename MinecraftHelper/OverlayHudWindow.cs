using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MinecraftHelper
{
    internal enum OverlayCorner
    {
        BottomRight,
        BottomLeft,
        TopRight,
        TopLeft
    }

    internal enum OverlayHudTone
    {
        Active,
        Warning,
        Alarm
    }

    // Ikona kafelka HUD (rysowana kreską na kwadracie w kolorze akcentu albo ostrzeżenia).
    internal enum OverlayHudIcon
    {
        None,
        Pickaxe,
        Commands,
        Inventory,
        Gold,
        Shield,
        Reconnect,
        Fishing,
        Mouse,
        Keyboard,
        Armor,
        Bolt,
        Snowflake,
        Network,
        Apple
    }

    // Kolor plakietki, dużej wartości albo paska. Auto: dobierany z treści (ON → zielony, PAUZA → bursztyn…).
    internal enum OverlayHudColor
    {
        Auto,
        Accent,
        Ok,
        Warn,
        Bad,
        Info,
        Ink,
        Muted
    }

    // Pasek odliczania. Gdy FixedFraction jest puste, pasek sam płynnie dochodzi do końca w EndUtc —
    // dzięki temu animacja nie zależy od tego, jak często aplikacja odświeża HUD.
    internal readonly record struct OverlayHudProgress(DateTime StartUtc, DateTime EndUtc, double? FixedFraction = null)
    {
        public static OverlayHudProgress Fixed(double fraction) => new(DateTime.MinValue, DateTime.MinValue, Math.Clamp(fraction, 0, 1));

        public bool IsRunning => FixedFraction == null && EndUtc > StartUtc;

        public double FractionAt(DateTime nowUtc)
        {
            if (FixedFraction is double fixedFraction)
                return fixedFraction;
            double total = (EndUtc - StartUtc).TotalMilliseconds;
            if (total <= 0)
                return 1;
            return Math.Clamp((nowUtc - StartUtc).TotalMilliseconds / total, 0, 1);
        }
    }

    // Title, Body i Tone wystarczą do prostego kafelka. Ikona, plakietka, duża wartość i pasek
    // tworzą pełny kafelek: [ikona] TYTUŁ ··· [PLAKIETKA] / duża wartość / linie Body / pasek.
    internal readonly record struct OverlayHudEntry(string Title, string Body, OverlayHudTone Tone, bool Emphasize = false)
    {
        public OverlayHudIcon Icon { get; init; }
        public string? Badge { get; init; }
        public OverlayHudColor BadgeColor { get; init; }
        public string? Headline { get; init; }
        public OverlayHudColor HeadlineColor { get; init; }
        public OverlayHudProgress? Progress { get; init; }
        public OverlayHudColor ProgressColor { get; init; }
    }

    internal sealed class OverlayHudWindow : Window
    {
        private const int GwlExStyle = -20;
        private const int GwlHwndParent = -8;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        // Pasek animuje się płynnie, ale okno HUD jest warstwowe i każda klatka to przerysowanie
        // całego okna — 30 klatek na sekundę wystarcza dla paska kilku-kilkudziesięciu sekund.
        private const int ProgressFrameRate = 30;
        private const double TileCornerRadius = 14;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr newLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        // Kolory z palety glass — jasne albo ciemne zależnie od motywu (pędzle zmieniają się w miejscu).
        private static readonly Brush LabelBrush = GlassPalette.Muted;
        private static readonly Brush OnBrush = GlassPalette.Ok;
        private static readonly Brush OffBrush = GlassPalette.Bad;
        private static readonly Brush PauseBrush = GlassPalette.Warn;
        private static readonly Brush TimeBrush = GlassPalette.Info;
        private static readonly Brush TileBackground = GlassPalette.HudTile;
        private static readonly Brush TileBorder = GlassPalette.HudTileBorder;

        private static Brush AccentBrush => GlassPalette.AccentInk;

        // Ikony 24×24 rysowane kreską — te same kształty co w oknach programów.
        private static readonly Dictionary<OverlayHudIcon, Geometry> IconGeometries = new()
        {
            [OverlayHudIcon.Pickaxe] = IconGeometry("M7,4.5 C12,3 18.5,6 19.5,17 M14.5,9.5 L4,20"),
            [OverlayHudIcon.Commands] = IconGeometry("M5,4 H19 A2,2 0 0 1 21,6 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 V6 A2,2 0 0 1 5,4 Z M7,9 L10,12 L7,15 M13,15 H17"),
            [OverlayHudIcon.Inventory] = IconGeometry("M12,3 L20,7.5 V16.5 L12,21 L4,16.5 V7.5 Z M4,7.5 L12,12 L20,7.5 M12,12 V21"),
            [OverlayHudIcon.Gold] = IconGeometry("M4,15 H11 L13,19 H2 Z M13,15 H20 L22,19 H11 Z M8.5,10 H15.5 L17.5,14 H6.5 Z"),
            [OverlayHudIcon.Shield] = IconGeometry("M12,3 L4.5,6 V11.5 C4.5,16.1 7.7,19.7 12,21 C16.3,19.7 19.5,16.1 19.5,11.5 V6 Z M9,12 L11,14 L15,10"),
            [OverlayHudIcon.Reconnect] = IconGeometry("M20,11 A8,8 0 1 0 17.7,16.7 M20,5 V11 H14"),
            [OverlayHudIcon.Fishing] = IconGeometry("M3,12 C6,7 12,6 16,9 L21,6 V18 L16,15 C12,18 6,17 3,12 Z M8,11 H8.01"),
            [OverlayHudIcon.Mouse] = IconGeometry("M12,3 A6,6 0 0 1 18,9 V15 A6,6 0 0 1 6,15 V9 A6,6 0 0 1 12,3 Z M12,7 V11"),
            [OverlayHudIcon.Keyboard] = IconGeometry("M4.5,6 H19.5 A2,2 0 0 1 21.5,8 V16 A2,2 0 0 1 19.5,18 H4.5 A2,2 0 0 1 2.5,16 V8 A2,2 0 0 1 4.5,6 Z M7,14.5 H17 M7,10 H7.01 M10.5,10 H10.51 M14,10 H14.01 M17.5,10 H17.51"),
            [OverlayHudIcon.Armor] = IconGeometry("M8,3 L3.5,6 V11 L7,12 V21 H17 V12 L20.5,11 V6 L16,3 C15,5.5 9,5.5 8,3 Z"),
            [OverlayHudIcon.Bolt] = IconGeometry("M13,2.5 L4.5,13.5 H11 L10,21.5 L19.5,9.5 H13 Z"),
            [OverlayHudIcon.Snowflake] = IconGeometry("M12,2.5 V21.5 M3.8,7.25 L20.2,16.75 M3.8,16.75 L20.2,7.25"),
            [OverlayHudIcon.Network] = IconGeometry("M4.5,12.5 A8,8 0 0 1 19.5,12.5 M8,12.5 A4,4 0 0 1 16,12.5 M12,13 V20"),
            [OverlayHudIcon.Apple] = IconGeometry("M12,7.5 C9,5.5 4.5,6.5 4.5,11.5 C4.5,16.5 8,21.5 12,19.5 C16,21.5 19.5,16.5 19.5,11.5 C19.5,6.5 15,5.5 12,7.5 Z M12,7.5 C12,5.5 13,3.5 15,2.5")
        };

        private static Geometry IconGeometry(string data)
        {
            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            return geometry;
        }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        // Elementy jednego kafelka, aby liczniki zmieniały tylko tekst i pasek, bez przebudowy okna.
        private sealed class HudTileView
        {
            public required Border Root { get; init; }
            public required OverlayHudEntry Entry { get; set; }
            public TextBlock? BadgeText { get; init; }
            public Border? Badge { get; init; }
            public TextBlock? Headline { get; init; }
            public required StackPanel Body { get; init; }
            public Border? ProgressFill { get; init; }
            public ScaleTransform? ProgressScale { get; init; }
        }

        private readonly StackPanel _itemsPanel;
        private readonly List<HudTileView> _tiles = new();
        private string _lastSignature = string.Empty;
        private string _lastStructureSignature = string.Empty;
        private IReadOnlyList<OverlayHudEntry>? _lastEntries;
        private Rect _workArea = SystemParameters.WorkArea;
        private OverlayCorner _corner = OverlayCorner.BottomRight;
        private double _stackOffset = 16;
        private bool _animationsEnabled = true;
        private bool _isStructureAnimating;
        private List<OverlayHudEntry>? _queuedStructureEntries;
        private IntPtr _ownerWindowHandle;
#if MINER_EDITION
        private readonly Services.MinerHudLayout _minerLayout = new();
        private System.Drawing.Rectangle? _minerWorkArea;
        private int _minerMargin = 16;
        public bool IsLayoutSuppressed => _minerLayout.NoSpace;

        public void SetMinerLayout(System.Drawing.Rectangle workArea, OverlayCorner corner, int margin)
        {
            _minerWorkArea = workArea;
            _corner = corner;
            _minerMargin = Math.Max(0, margin);
            if (IsVisible)
                RepositionToAnchor();
        }
#endif

        public OverlayHudWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _itemsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            _itemsPanel.RenderTransform = new TranslateTransform();

            Content = _itemsPanel;

            // Zmiana motywu albo akcentu: napisy przełączają się same (pędzle palety), ale tło ikon
            // i cień są wpisywane przy budowie kafelków — przy tej samej treści trzeba je zbudować od nowa.
            GlassThemeMode.Changed += OnThemeChanged;
            Closed += (_, _) => GlassThemeMode.Changed -= OnThemeChanged;
        }

        private void OnThemeChanged()
        {
            if (_lastEntries == null || _lastEntries.Count == 0 || !IsVisible || _isStructureAnimating)
                return;
            RebuildTiles(_lastEntries);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyNativeWindowBehavior();
            ApplyOwnerWindowHandle();
            EnsureNativeTopmost();
        }

        public bool IsHudVisible => IsVisible;

        // Okno właściciela (gra) może zniszczyć ten HUD razem ze sobą, np. gdy
        // Minecraft tworzy nowe okno po F11. Zamkniętego okna nie da się pokazać.
        public bool IsClosed { get; private set; }

        protected override void OnClosed(EventArgs e)
        {
#if MINER_EDITION
            _minerLayout.Dispose();
#endif
            IsClosed = true;
            base.OnClosed(e);
        }

        public double CurrentHeight => ActualHeight > 0 ? ActualHeight : RenderSize.Height;

        public void SetOwnerWindowHandle(IntPtr ownerWindowHandle)
        {
#if MINER_EDITION
            // A foreign owner implicitly attaches both UI input queues. The HUD
            // runs on Kopacz's main thread, so owning it by Minecraft also joins
            // the main window's focus/capture state to LWJGL's fake focus.
            // Keep this HUD independent even when it is hidden. Z-order and
            // positioning are already maintained explicitly with NOACTIVATE.
            ownerWindowHandle = IntPtr.Zero;
#endif
            if (_ownerWindowHandle == ownerWindowHandle)
                return;

            _ownerWindowHandle = ownerWindowHandle;
            ApplyOwnerWindowHandle();
            EnsureNativeTopmost();
        }

        public void SetAnimationsEnabled(bool enabled)
        {
            if (_animationsEnabled == enabled)
                return;
            _animationsEnabled = enabled;
            // Paski w toku przechodzą między płynną animacją a skokami przy każdym odświeżeniu.
            foreach (HudTileView tile in _tiles)
                ApplyProgress(tile, tile.Entry.Progress);
        }

        public void SetLayout(Rect workArea, OverlayCorner corner, double stackOffset)
        {
            _workArea = workArea;
            _corner = corner;
            _stackOffset = Math.Max(0, stackOffset);
            if (IsVisible)
                RepositionToAnchor();
        }

        public void UpdateEntries(IReadOnlyList<OverlayHudEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                _lastSignature = string.Empty;
                _lastStructureSignature = string.Empty;
                _isStructureAnimating = false;
                _queuedStructureEntries = null;
                CancelItemsAnimations();
                HideWindow();
                return;
            }

            _lastEntries = entries;
            string signature = BuildSignature(entries);
            string structureSignature = BuildStructureSignature(entries);
            bool signatureChanged = !string.Equals(signature, _lastSignature, StringComparison.Ordinal);
            bool structureChanged = !string.Equals(structureSignature, _lastStructureSignature, StringComparison.Ordinal);
            if (signatureChanged || structureChanged)
            {
                _lastSignature = signature;
                _lastStructureSignature = structureSignature;
                if (IsVisible && structureChanged)
                    AnimateStructureRefresh(entries);
                else if (structureChanged || !TryUpdateTilesInPlace(entries))
                    RebuildTiles(entries);
            }

            bool wasVisible = IsVisible;
            ShowWindow();
            // Bez animacji pasek przesuwa się skokowo przy każdym odświeżeniu HUD-a. Po ponownym
            // pokazaniu okna paski zatrzymane przy ukryciu ruszają od bieżącego miejsca.
            if (!_animationsEnabled || !wasVisible)
            {
                foreach (HudTileView tile in _tiles)
                    ApplyProgress(tile, tile.Entry.Progress);
            }
            if (!_isStructureAnimating)
                RepositionToAnchor();
            EnsureNativeTopmost();
        }

        private void ApplyNativeWindowBehavior()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            int extendedStyle = GetWindowLong(handle, GwlExStyle);
            extendedStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            _ = SetWindowLong(handle, GwlExStyle, extendedStyle);
        }

        private void ApplyOwnerWindowHandle()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            if (IntPtr.Size == 8)
                _ = SetWindowLongPtr64(handle, GwlHwndParent, _ownerWindowHandle);
            else
                _ = SetWindowLong(handle, GwlHwndParent, _ownerWindowHandle.ToInt32());
        }

        private void EnsureNativeTopmost()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            _ = SetWindowPos(
                handle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }

        // Liczniki (czas, „za 9 s”) zmieniają treść kilka razy na sekundę. Przebudowa całego
        // HUD-a za każdym razem dawała w przezroczystym oknie widoczne migotanie, więc przy
        // tych samych kafelkach podmieniamy tylko tekst, plakietkę i pasek tych, które się zmieniły.
        private bool TryUpdateTilesInPlace(IReadOnlyList<OverlayHudEntry> entries)
        {
            if (_tiles.Count != entries.Count || _itemsPanel.Children.Count != entries.Count)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.Equals(StructureKey(_tiles[i].Entry), StructureKey(entries[i]), StringComparison.Ordinal))
                    return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                HudTileView tile = _tiles[i];
                OverlayHudEntry previous = tile.Entry;
                OverlayHudEntry entry = entries[i];
                if (previous == entry)
                    continue;

                bool warning = entry.Tone != OverlayHudTone.Active;
                if (tile.BadgeText != null && tile.Badge != null
                    && (previous.Badge != entry.Badge || previous.BadgeColor != entry.BadgeColor))
                {
                    Brush badgeBrush = ResolveColor(entry.BadgeColor, entry.Badge ?? string.Empty, warning);
                    tile.BadgeText.Text = entry.Badge;
                    tile.BadgeText.Foreground = badgeBrush;
                    tile.Badge.Background = GlassPalette.SoftFor(badgeBrush);
                }

                if (tile.Headline != null
                    && (previous.Headline != entry.Headline || previous.HeadlineColor != entry.HeadlineColor))
                {
                    tile.Headline.Text = entry.Headline;
                    tile.Headline.Foreground = ResolveColor(entry.HeadlineColor, entry.Headline ?? string.Empty, warning);
                }

                if (previous.Body != entry.Body)
                {
                    bool rich = entry.Headline != null || entry.Icon != OverlayHudIcon.None;
                    StackPanel fresh = rich ? BuildBodyLines(entry, warning) : BuildBodyPanel(entry.Body, warning, entry.Emphasize);
                    CopyChangedLines(tile.Body, fresh);
                }

                tile.Entry = entry;
                if (previous.Progress != entry.Progress || previous.ProgressColor != entry.ProgressColor)
                    ApplyProgress(tile, entry.Progress);
            }

            return true;
        }

        // Ta sama liczba linii: przenosimy tylko nowe fragmenty tekstu do istniejących linii.
        // Podmiana całego bloku na jedną klatkę zmieniała wysokość okna i kafelek mrugał.
        private static void CopyChangedLines(StackPanel current, StackPanel fresh)
        {
            if (current.Children.Count != fresh.Children.Count)
            {
                var moved = fresh.Children.Cast<UIElement>().ToList();
                fresh.Children.Clear();
                current.Children.Clear();
                foreach (UIElement line in moved)
                    current.Children.Add(line);
                current.Visibility = current.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                return;
            }

            for (int i = 0; i < current.Children.Count; i++)
            {
                if (current.Children[i] is not TextBlock target || fresh.Children[i] is not TextBlock source)
                    continue;
                if (Equals(target.Tag, source.Tag))
                    continue;

                var inlines = new List<Inline>(source.Inlines);
                source.Inlines.Clear();
                target.Inlines.Clear();
                target.Inlines.AddRange(inlines);
                target.Tag = source.Tag;
            }
        }

        private void RebuildTiles(IReadOnlyList<OverlayHudEntry> entries)
        {
            foreach (HudTileView tile in _tiles)
                tile.ProgressScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _itemsPanel.Children.Clear();
            _tiles.Clear();

            for (int i = 0; i < entries.Count; i++)
            {
                HudTileView tile = BuildTile(entries[i], i);
                _tiles.Add(tile);
                _itemsPanel.Children.Add(tile.Root);
            }
        }

        // Kafelek: [ikona] TYTUŁ ··· [PLAKIETKA] / duża wartość / linie szczegółów / pasek odliczania.
        private HudTileView BuildTile(OverlayHudEntry entry, int index)
        {
            bool warning = entry.Tone != OverlayHudTone.Active;
            bool rich = entry.Headline != null || entry.Icon != OverlayHudIcon.None;

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (entry.Icon == OverlayHudIcon.None)
            {
                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = ToneBrush(entry.Tone),
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                header.Children.Add(dot);
            }

            var title = new TextBlock
            {
                Text = entry.Title,
                FontSize = rich ? 11 : entry.Emphasize ? 14 : 12,
                FontWeight = FontWeights.Bold,
                Foreground = rich ? LabelBrush : entry.Emphasize ? AccentBrush : GlassPalette.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 1);
            header.Children.Add(title);

            Border? badge = null;
            TextBlock? badgeText = null;
            if (entry.Badge != null)
            {
                Brush badgeBrush = ResolveColor(entry.BadgeColor, entry.Badge, warning);
                badgeText = new TextBlock
                {
                    Text = entry.Badge,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = badgeBrush
                };
                badge = new Border
                {
                    Background = GlassPalette.SoftFor(badgeBrush),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(7, 0, 7, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = badgeText
                };
                Grid.SetColumn(badge, 2);
                header.Children.Add(badge);
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(header);

            TextBlock? headline = null;
            if (entry.Headline != null)
            {
                headline = new TextBlock
                {
                    Text = entry.Headline,
                    FontSize = entry.Emphasize ? 22 : 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = ResolveColor(entry.HeadlineColor, entry.Headline, warning),
                    // Dłuższy etap („Wyrzucanie pełnych stosów”) przechodzi do drugiej linii zamiast urywać się.
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = entry.Emphasize ? 26 : 22,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                text.Children.Add(headline);
            }

            StackPanel body = rich ? BuildBodyLines(entry, warning) : BuildBodyPanel(entry.Body, warning, entry.Emphasize);
            text.Children.Add(body);

            var content = new Grid
            {
                Margin = entry.Emphasize ? new Thickness(14, 12, 14, 12) : new Thickness(12, 9, 12, 10)
            };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (entry.Icon != OverlayHudIcon.None)
                content.Children.Add(BuildIcon(entry.Icon, entry.Tone, entry.Emphasize));
            Grid.SetColumn(text, 1);
            content.Children.Add(text);

            var layers = new Grid();
            layers.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layers.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layers.Children.Add(content);

            Border? progressFill = null;
            ScaleTransform? progressScale = null;
            if (entry.Progress != null)
            {
                progressScale = new ScaleTransform(0, 1);
                progressFill = new Border
                {
                    RenderTransformOrigin = new Point(0, 0.5),
                    RenderTransform = progressScale
                };
                var track = new Border
                {
                    Height = 3,
                    Background = GlassPalette.Line,
                    Child = progressFill
                };
                Grid.SetRow(track, 1);
                layers.Children.Add(track);
            }

            // Pasek przy dolnej krawędzi musi kończyć się na zaokrągleniu kafelka, więc warstwy są przycięte.
            double innerRadius = TileCornerRadius - 1;
            layers.SizeChanged += (_, e) =>
                layers.Clip = new RectangleGeometry(new Rect(e.NewSize), innerRadius, innerRadius);

            var root = new Border
            {
                Width = entry.Emphasize ? 420 : 300,
                Background = TileBackground,
                BorderBrush = TileBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(TileCornerRadius),
                Margin = new Thickness(6, index == 0 ? 6 : 2, 6, 6),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 14,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = GlassPalette.IsDark ? 0.35 : 0.10,
                    Color = GlassPalette.ShadowColor
                },
                Child = layers,
                IsHitTestVisible = false
            };
            if (Application.Current?.TryFindResource("GlassFont") is FontFamily font)
                TextElement.SetFontFamily(root, font);

            var view = new HudTileView
            {
                Root = root,
                Entry = entry,
                Badge = badge,
                BadgeText = badgeText,
                Headline = headline,
                Body = body,
                ProgressFill = progressFill,
                ProgressScale = progressScale
            };
            root.Tag = view;
            ApplyProgress(view, entry.Progress);
            return view;
        }

        // Kwadrat w kolorze akcentu (bursztyn przy ostrzeżeniu, czerwień przy alarmie) z białą ikoną.
        private static Border BuildIcon(OverlayHudIcon icon, OverlayHudTone tone, bool emphasize)
        {
            Color background = tone switch
            {
                OverlayHudTone.Warning => GlassPalette.Warn.Color,
                OverlayHudTone.Alarm => GlassPalette.Bad.Color,
                _ => RawAccentColor()
            };
            // Jasny bursztyn i czerwień ciemnego motywu potrzebują ciemnej ikony, aby była czytelna.
            Color glyph = GlassPalette.IsDark && tone != OverlayHudTone.Active
                ? Color.FromRgb(0x16, 0x18, 0x2C)
                : Colors.White;
            double size = emphasize ? 42 : 36;
            var path = new Path
            {
                Data = IconGeometries.TryGetValue(icon, out Geometry? geometry) ? geometry : Geometry.Empty,
                Stroke = Freeze(new SolidColorBrush(glyph)),
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 24,
                Height = 24,
                Stretch = Stretch.None
            };
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(emphasize ? 12 : 10),
                Background = Freeze(new SolidColorBrush(background)),
                Margin = new Thickness(0, 0, 11, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Viewbox { Width = size * 0.56, Height = size * 0.56, Child = path }
            };
        }

        private static Color RawAccentColor() =>
            Application.Current?.TryFindResource("GlassAccentColor") is Color color
                ? color
                : AccentBrush is SolidColorBrush brush ? brush.Color : Color.FromRgb(0x54, 0x58, 0xE8);

        private static Brush ToneBrush(OverlayHudTone tone) => tone switch
        {
            OverlayHudTone.Warning => PauseBrush,
            OverlayHudTone.Alarm => OffBrush,
            _ => OnBrush
        };

        private static Brush ResolveColor(OverlayHudColor color, string text, bool warningTile) => color switch
        {
            OverlayHudColor.Accent => AccentBrush,
            OverlayHudColor.Ok => OnBrush,
            OverlayHudColor.Warn => PauseBrush,
            OverlayHudColor.Bad => OffBrush,
            OverlayHudColor.Info => TimeBrush,
            OverlayHudColor.Ink => GlassPalette.Ink,
            OverlayHudColor.Muted => LabelBrush,
            _ => ResolveValueBrush(text, warningTile)
        };

        // Pasek odliczania: w toku dochodzi płynnie do końca sam, bez czekania na kolejne odświeżenia.
        private void ApplyProgress(HudTileView tile, OverlayHudProgress? progress)
        {
            if (tile.ProgressScale is not ScaleTransform scale || tile.ProgressFill is not Border fill)
                return;

            OverlayHudEntry entry = tile.Entry;
            if (progress is not OverlayHudProgress value)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.ScaleX = 0;
                return;
            }

            OverlayHudColor color = entry.ProgressColor == OverlayHudColor.Auto
                ? value.IsRunning ? OverlayHudColor.Info : OverlayHudColor.Warn
                : entry.ProgressColor;
            fill.Background = ResolveColor(color, string.Empty, entry.Tone != OverlayHudTone.Active);

            DateTime now = DateTime.UtcNow;
            double fraction = value.FractionAt(now);
            TimeSpan remaining = value.EndUtc - now;
            if (!value.IsRunning || !_animationsEnabled || remaining <= TimeSpan.Zero)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.ScaleX = fraction;
                return;
            }

            var animation = new DoubleAnimation(fraction, 1, new Duration(remaining))
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            Timeline.SetDesiredFrameRate(animation, ProgressFrameRate);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        }

        // Linie szczegółów pełnego kafelka: stonowany tekst, a w „Etykieta: wartość” wartość wyraźniej.
        // Kolor niosą plakietka, duża wartość i pasek, więc szczegóły nie konkurują z nimi.
        private static StackPanel BuildBodyLines(OverlayHudEntry entry, bool warningTile)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 1, 0, 0) };
            foreach (string rawLine in (entry.Body ?? string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                var text = new TextBlock
                {
                    FontSize = entry.Emphasize ? 12.5 : 11.5,
                    LineHeight = entry.Emphasize ? 18 : 16,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = LabelBrush,
                    Tag = line
                };
                int separator = line.IndexOf(": ", StringComparison.Ordinal);
                if (separator > 0)
                {
                    text.Inlines.Add(new Run(line.Substring(0, separator + 2)));
                    string value = line.Substring(separator + 2);
                    Brush valueBrush = ResolveValueBrush(value, warningTile);
                    bool status = ReferenceEquals(valueBrush, OnBrush) || ReferenceEquals(valueBrush, OffBrush) || ReferenceEquals(valueBrush, PauseBrush);
                    text.Inlines.Add(new Run(value)
                    {
                        Foreground = status && value.Length <= 14 ? valueBrush : GlassPalette.Ink,
                        FontWeight = FontWeights.SemiBold
                    });
                }
                else
                {
                    text.Inlines.Add(new Run(line));
                }
                panel.Children.Add(text);
            }
            panel.Visibility = panel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            return panel;
        }

        // Krótkie słowo stanu (ON, OFF, PAUZA, STOP…) jako kolorowa pigułka zamiast zwykłego tekstu.
        private static bool TryAppendStatusPill(TextBlock block, string segment, bool warningTile)
        {
            if (segment.Length == 0 || segment.Length > 14 || segment.Any(char.IsDigit))
                return false;
            Brush color = ResolveValueBrush(segment, warningTile);
            if (!ReferenceEquals(color, OnBrush) && !ReferenceEquals(color, OffBrush) && !ReferenceEquals(color, PauseBrush))
                return false;

            var pill = new Border
            {
                Background = GlassPalette.SoftFor(color),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 0, 6, 1),
                Child = new TextBlock { Text = segment, Foreground = color, FontWeight = FontWeights.Bold, FontSize = block.FontSize }
            };
            block.Inlines.Add(new InlineUIContainer(pill) { BaselineAlignment = BaselineAlignment.Center });
            return true;
        }

        // Prosty kafelek (bez ikony i dużej wartości): linie „Etykieta: wartość” z kolorowymi wartościami.
        private static StackPanel BuildBodyPanel(string body, bool warningTile, bool emphasize)
        {
            var bodyPanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            string[] lines = (body ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (emphasize)
                {
                    TextBlock emphasized = BuildEmphasizedLine(line, warningTile);
                    emphasized.Tag = line;
                    bodyPanel.Children.Add(emphasized);
                    continue;
                }

                var text = new TextBlock
                {
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Tag = line
                };

                int separator = line.IndexOf(':');
                if (separator > 0)
                {
                    string label = line.Substring(0, separator + 1);
                    string value = line.Substring(separator + 1).TrimStart();

                    text.Inlines.Add(new Run(label + " ")
                    {
                        Foreground = LabelBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                    AppendStyledValue(text, value, warningTile);
                }
                else
                {
                    AppendStyledValue(text, line, warningTile);
                }

                bodyPanel.Children.Add(text);
            }

            return bodyPanel;
        }

        private static TextBlock BuildEmphasizedLine(string line, bool warningTile)
        {
            var text = new TextBlock
            {
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap
            };

            int separator = line.IndexOf(':');
            if (separator > 0)
            {
                string label = line.Substring(0, separator + 1);
                string value = line.Substring(separator + 1).TrimStart();
                bool isEntityCountLine = label.StartsWith("Encje", StringComparison.OrdinalIgnoreCase);

                text.Inlines.Add(new Run(label + " ")
                {
                    Foreground = LabelBrush,
                    FontWeight = FontWeights.Bold,
                    FontSize = isEntityCountLine ? 16 : 14
                });
                text.Inlines.Add(new Run(value)
                {
                    Foreground = isEntityCountLine ? ResolveEntityCountBrush(value, warningTile) : ResolveValueBrush(value, warningTile),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = isEntityCountLine ? 30 : 18
                });
                return text;
            }

            text.Inlines.Add(new Run(line)
            {
                Foreground = ResolveValueBrush(line, warningTile),
                FontWeight = FontWeights.Bold,
                FontSize = 18
            });
            return text;
        }

        private static void AppendStyledValue(TextBlock block, string value, bool warningTile)
        {
            string[] parts = value.Split(new[] { " | " }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                // „ON • 10–12 CPS”: stan jako pigułka, reszta w swoim kolorze.
                string[] segments = part.Split(new[] { " • " }, StringSplitOptions.None);
                for (int j = 0; j < segments.Length; j++)
                {
                    string segment = segments[j].Trim();
                    if (j > 0)
                        block.Inlines.Add(new Run("  "));
                    if (TryAppendStatusPill(block, segment, warningTile))
                        continue;
                    block.Inlines.Add(new Run(segment)
                    {
                        Foreground = ResolveValueBrush(segment, warningTile),
                        FontWeight = FontWeights.SemiBold
                    });
                }

                if (i < parts.Length - 1)
                {
                    block.Inlines.Add(new Run(" | ")
                    {
                        Foreground = LabelBrush
                    });
                }
            }
        }

        private static Brush ResolveValueBrush(string valuePart, bool warningTile)
        {
            string lower = valuePart.ToLowerInvariant();

            if (lower.Contains("alarm"))
                return OffBrush;

            if (lower.Contains("pauza") || lower.Contains("wstrzym") || lower.Contains("test"))
                return PauseBrush;

            if (lower.Contains("off") || lower.Contains("wyłącz") || lower.Contains("brak") || lower == "stop")
                return OffBrush;

            if (lower.Contains("on") || lower.Contains("kopanie") || lower.Contains("aktywn") || lower.Contains("teraz") || lower.Contains("wznawianie") || lower.Contains("wykonane"))
                return OnBrush;

            if (lower.Contains("cps", StringComparison.Ordinal))
                return AccentBrush;

            if (lower.Contains("za ") || lower.EndsWith("ms", StringComparison.Ordinal) || lower.EndsWith("s", StringComparison.Ordinal))
                return TimeBrush;

            if (warningTile)
                return PauseBrush;

            return AccentBrush;
        }

        private static Brush ResolveEntityCountBrush(string valuePart, bool warningTile)
        {
            string lower = valuePart.ToLowerInvariant();
            if (lower.Contains("brak", StringComparison.Ordinal))
                return warningTile ? PauseBrush : OffBrush;

            int index = 0;
            while (index < valuePart.Length && !char.IsDigit(valuePart[index]))
                index++;

            int start = index;
            while (index < valuePart.Length && char.IsDigit(valuePart[index]))
                index++;

            if (start < index && int.TryParse(valuePart.Substring(start, index - start), out int parsedPlayers))
                return parsedPlayers > 0 ? OnBrush : PauseBrush;

            return warningTile ? PauseBrush : AccentBrush;
        }

        private static string BuildSignature(IReadOnlyList<OverlayHudEntry> entries)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                OverlayHudEntry entry = entries[i];
                sb.Append(StructureKey(entry));
                sb.Append('|');
                sb.Append(entry.Body);
                sb.Append('|');
                sb.Append(entry.Badge);
                sb.Append('|');
                sb.Append((int)entry.BadgeColor);
                sb.Append('|');
                sb.Append(entry.Headline);
                sb.Append('|');
                sb.Append((int)entry.HeadlineColor);
                sb.Append('|');
                if (entry.Progress is OverlayHudProgress progress)
                {
                    sb.Append(progress.StartUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(progress.EndUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(progress.FixedFraction?.ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append('|');
                sb.Append((int)entry.ProgressColor);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        // Zmiana tego klucza wymaga zbudowania kafelka od nowa (inne elementy albo kolor ikony).
        private static string StructureKey(OverlayHudEntry entry) =>
            string.Join('\u001f',
                entry.Title,
                ((int)entry.Tone).ToString(CultureInfo.InvariantCulture),
                entry.Emphasize ? "1" : "0",
                ((int)entry.Icon).ToString(CultureInfo.InvariantCulture),
                entry.Badge != null ? "b" : "-",
                entry.Headline != null ? "h" : "-",
                entry.Progress != null ? "p" : "-");

        private static string BuildStructureSignature(IReadOnlyList<OverlayHudEntry> entries)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append(StructureKey(entries[i]));
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private void AnimateStructureRefresh(IReadOnlyList<OverlayHudEntry> entries)
        {
            if (!_animationsEnabled)
            {
                RebuildTiles(entries);
                RepositionToAnchor();
                return;
            }

            if (_isStructureAnimating)
            {
                _queuedStructureEntries = new List<OverlayHudEntry>(entries);
                return;
            }

            _isStructureAnimating = true;
            _queuedStructureEntries = null;
            CancelItemsAnimations();

            TranslateTransform translate = EnsureItemsTranslateTransform();
            var hideEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            var showEase = new CubicEase { EasingMode = EasingMode.EaseOut };

            double hideY = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? -6 : 6;
            var hideOpacity = new DoubleAnimation
            {
                From = _itemsPanel.Opacity <= 0 ? 1 : _itemsPanel.Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(90),
                EasingFunction = hideEase
            };

            hideOpacity.Completed += (_, __) =>
            {
                if (!IsVisible)
                {
                    _isStructureAnimating = false;
                    _queuedStructureEntries = null;
                    return;
                }

                // Treść mogła się zmienić w czasie wygaszania — budujemy z najnowszych wpisów.
                RebuildTiles(_lastEntries ?? entries);
                RepositionToAnchor();

                double showFromY = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? -12 : 12;
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = showFromY;

                _itemsPanel.BeginAnimation(UIElement.OpacityProperty, null);
                _itemsPanel.Opacity = 0;

                var showOpacity = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = showEase
                };
                showOpacity.Completed += (_, __2) =>
                {
                    _isStructureAnimating = false;
                    if (_queuedStructureEntries is { Count: > 0 } queued)
                    {
                        _queuedStructureEntries = null;
                        AnimateStructureRefresh(queued);
                    }
                };
                _itemsPanel.BeginAnimation(UIElement.OpacityProperty, showOpacity);

                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
                {
                    From = showFromY,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(210),
                    EasingFunction = showEase
                });
            };

            _itemsPanel.BeginAnimation(UIElement.OpacityProperty, hideOpacity);

            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
            {
                From = 0,
                To = hideY,
                Duration = TimeSpan.FromMilliseconds(90),
                EasingFunction = hideEase
            });
        }

        private void ShowWindow()
        {
            BeginAnimation(OpacityProperty, null);

            if (!IsVisible)
            {
                if (_animationsEnabled)
                {
                    Opacity = 0;
                    Show();
                    _itemsPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    _itemsPanel.Opacity = 1;
                    BeginAnimation(OpacityProperty, new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(140),
                    });
                }
                else
                {
                    Opacity = 1;
                    Show();
                }
            }
            else
            {
                Opacity = 1;
            }
        }

        private void HideWindow()
        {
#if MINER_EDITION
            _minerLayout.Invalidate();
#endif
            // Ukryty HUD nie potrzebuje płynących pasków — zatrzymujemy je, aby nie zużywały CPU.
            foreach (HudTileView tile in _tiles)
                tile.ProgressScale?.BeginAnimation(ScaleTransform.ScaleXProperty, null);

            if (!IsVisible)
                return;

            CancelItemsAnimations();
            _isStructureAnimating = false;
            _queuedStructureEntries = null;
            BeginAnimation(OpacityProperty, null);
            if (!_animationsEnabled)
            {
                Opacity = 1;
                Hide();
                return;
            }

            var fade = new DoubleAnimation
            {
                From = Opacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(180),
            };
            fade.Completed += (_, __) =>
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        }

        private TranslateTransform EnsureItemsTranslateTransform()
        {
            if (_itemsPanel.RenderTransform is TranslateTransform translate)
                return translate;

            translate = new TranslateTransform();
            _itemsPanel.RenderTransform = translate;
            return translate;
        }

        private void CancelItemsAnimations()
        {
            _itemsPanel.BeginAnimation(UIElement.OpacityProperty, null);
            TranslateTransform translate = EnsureItemsTranslateTransform();
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            _itemsPanel.Opacity = 1;
            translate.Y = 0;
        }

        private void RepositionToAnchor()
        {
            UpdateLayout();
#if MINER_EDITION
            if (_minerWorkArea is { } area)
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (!_minerLayout.TryPosition(handle, area,
                    _corner is OverlayCorner.BottomLeft or OverlayCorner.TopLeft,
                    _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight, _minerMargin, 8))
                {
                    // A fade can be cancelled by the next status tick. Hide now
                    // so a full monitor never leaves a HUD on top of its peers.
                    CancelItemsAnimations();
                    _isStructureAnimating = false;
                    _queuedStructureEntries = null;
                    BeginAnimation(OpacityProperty, null);
                    _minerLayout.Invalidate();
                    Hide();
                }
                return;
            }
#endif

            double left = _corner is OverlayCorner.BottomLeft or OverlayCorner.TopLeft
                ? _workArea.Left + 16
                : _workArea.Right - ActualWidth - 16;

            double top = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight
                ? _workArea.Top + _stackOffset
                : _workArea.Bottom - ActualHeight - _stackOffset;

            Left = left;
            Top = top;
        }
    }
}
