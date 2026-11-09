using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MinecraftHelper;

// Nowości po aktualizacji: panel „Nowe w …” w rogu okna (nie blokuje programu), plakietki NOWE
// w pasku bocznym i podświetlenie — „Pokaż” przechodzi do zakładki, przewija do karty i ściemnia
// resztę okna. Lista nowości z miejscami docelowymi: MinerWhatsNew.
public partial class MainWindow
{
    private IReadOnlyList<MinerWhatsNewItem> _whatsNewItems = Array.Empty<MinerWhatsNewItem>();
    private readonly Dictionary<string, Border> _whatsNewNavPills = new();
    private int _spotlightIndex = -1;
    private FrameworkElement? _spotlightTarget;
    private bool _whatsNewPanelBeforeSpotlight;
    private readonly HashSet<string> _spotlightVisitedTabs = new();
    // „Pokaż” przy nowości pokazuje tylko ją; pokaz po kolei daje przycisk pod listą.
    private bool _spotlightSingle;
    private readonly HashSet<string> _whatsNewSeen = new(StringComparer.Ordinal);

    private bool IsSpotlightActive => SpotlightLayer.Visibility == Visibility.Visible;

    /// <summary>
    /// Po zmianie wersji (nie przy pierwszym uruchomieniu): panel z nowościami od ostatnio
    /// potwierdzonej wersji. Wersja jest potwierdzana od razu — panel niczego nie wymusza.
    /// </summary>
    private void ShowWhatsNewAfterUpdate()
    {
        _whatsNewItems = MinerWhatsNew.Since(_settings.LastAcknowledgedVersion, _currentAppVersion);
        AcknowledgeCurrentVersion();
        if (_whatsNewItems.Count == 0)
            return;

        BuildWhatsNewPanel();
        AddWhatsNewNavPills();
        PanelWhatsNew.Visibility = Visibility.Visible;
    }

    private void AcknowledgeCurrentVersion()
    {
        try
        {
            _settings.LastAcknowledgedVersion = _currentAppVersion;
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            UpdateStatusBar("Nie udało się zapisać potwierdzenia wersji: " + ex.Message, "Red");
        }
    }

    // „Co nowego w tej wersji” z Ustawień: nowości bieżącej wersji, nawet gdy panel już zamknięto.
    private void BtnWhatsNewAbout_Click(object sender, RoutedEventArgs e)
    {
        if (_whatsNewItems.Count == 0)
            _whatsNewItems = MinerWhatsNew.Since(null, _currentAppVersion);
        if (_whatsNewItems.Count == 0)
        {
            UpdateTemporaryStatusBar($"Wersja {_currentAppVersion} nie ma opisanych nowości do pokazania.", "Orange", 5);
            return;
        }

        BuildWhatsNewPanel();
        BtnWhatsNewReopen.Visibility = Visibility.Collapsed;
        PanelWhatsNew.Visibility = Visibility.Visible;
    }

    private void BuildWhatsNewPanel()
    {
        string versions = string.Join(", ", _whatsNewItems.Select(item => item.Version).Distinct());
        TxtWhatsNewTitle.Text = $"Nowe w {versions}";
        int total = _whatsNewItems.Count;
        int seen = _whatsNewItems.Count(item => _whatsNewSeen.Contains(item.Title));
        TxtWhatsNewSub.Text = seen == 0
            ? $"{FormatChangeCount(total)} · ustawienia zachowane"
            : seen == total ? "Wszystko obejrzane" : $"Obejrzane {seen} z {total}";
        TxtWhatsNewReopen.Text = seen == total ? "Co nowego" : $"Co nowego · {total - seen}";

        PanelWhatsNewItems.Children.Clear();
        for (int index = 0; index < _whatsNewItems.Count; index++)
            PanelWhatsNewItems.Children.Add(BuildWhatsNewRow(_whatsNewItems[index], index));
    }

    private static string FormatChangeCount(int count) => count switch
    {
        1 => "1 zmiana",
        >= 2 and <= 4 => $"{count} zmiany",
        _ => $"{count} zmian"
    };

    private Grid BuildWhatsNewRow(MinerWhatsNewItem item, int index)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Obejrzana nowość: zielony znacznik zamiast ikony i przygaszony tytuł.
        bool seen = _whatsNewSeen.Contains(item.Title);
        var iconPath = new Path { Data = TryFindResource(seen ? "IcoCheck" : item.Icon) as Geometry ?? TryFindResource("IcoSparkle") as Geometry };
        iconPath.SetResourceReference(StyleProperty, "GlassIconPath");
        iconPath.SetResourceReference(Shape.StrokeProperty, seen ? "GlassOkBrush" : "GlassAccentInkBrush");
        var tile = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Viewbox { Width = 15, Height = 15, Child = new Grid { Width = 24, Height = 24, Children = { iconPath } } }
        };
        tile.SetResourceReference(Border.BackgroundProperty, seen ? "GlassOkSoftBrush" : "GlassAccentSoftBrush");
        row.Children.Add(tile);

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0)
        };
        if (seen)
            title.SetResourceReference(TextBlock.ForegroundProperty, "GlassInk2Brush");
        Grid.SetColumn(title, 1);
        row.Children.Add(title);

        var show = new Button
        {
            Content = "Pokaż",
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Przejdź do tego miejsca w programie"
        };
        show.SetResourceReference(ForegroundProperty, "GlassAccentInkBrush");
        AutomationProperties.SetName(show, "Pokaż: " + item.Title);
        show.Click += (_, _) => StartSpotlight(index, single: true);
        Grid.SetColumn(show, 2);
        row.Children.Add(show);
        return row;
    }

    private void BtnWhatsNewClose_Click(object sender, RoutedEventArgs e)
    {
        PanelWhatsNew.Visibility = Visibility.Collapsed;
        BtnWhatsNewReopen.Visibility = Visibility.Visible;
    }

    private void BtnWhatsNewReopen_Click(object sender, RoutedEventArgs e)
    {
        BtnWhatsNewReopen.Visibility = Visibility.Collapsed;
        PanelWhatsNew.Visibility = Visibility.Visible;
    }

    private void BtnWhatsNewTour_Click(object sender, RoutedEventArgs e) => StartSpotlight(0, single: false);

    // ===== Plakietki NOWE w pasku bocznym (do pierwszego wejścia w zakładkę) =====

    private void AddWhatsNewNavPills()
    {
        foreach (string tabName in _whatsNewItems.Select(item => item.Tab).Distinct())
        {
            if (_whatsNewNavPills.ContainsKey(tabName))
                continue;
            RadioButton? nav = FindNavItem(tabName);
            if (nav?.Content is not DockPanel dock)
                continue;

            var text = new TextBlock { Text = "NOWE", FontSize = 9.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "GlassAccentInkBrush");
            var pill = new Border { Height = 18, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = text };
            pill.SetResourceReference(StyleProperty, "GlassPill");
            pill.SetResourceReference(Border.BackgroundProperty, "GlassAccentSoftBrush");
            DockPanel.SetDock(pill, Dock.Right);
            // Ostatnie dziecko DockPanelu wypełnia resztę — plakietka musi stać przed tekstem.
            dock.Children.Insert(Math.Max(0, dock.Children.Count - 1), pill);
            _whatsNewNavPills[tabName] = pill;
        }
    }

    private RadioButton? FindNavItem(string tabName) =>
        new[] { NavAccounts, NavDiscord, NavKopacz, NavInventory, NavGoldDeposit, NavEnderchest, NavProtection, NavBackground, NavTools, NavSettings }
            .FirstOrDefault(item => item.Tag as string == tabName);

    // Wołane z NavItem_Checked. Podczas pokazu plakietki zostają — znikają po zwykłym wejściu.
    private void OnWhatsNewTabVisited(string tabName)
    {
        // _spotlightIndex, nie IsSpotlightActive: warstwa pojawia się dopiero po przejściu do zakładki.
        if (_spotlightIndex >= 0 || !_whatsNewNavPills.Remove(tabName, out Border? pill))
            return;
        (pill.Parent as Panel)?.Children.Remove(pill);
    }

    // ===== Podświetlenie (ściemnienie poza kartą) =====

    private void StartSpotlight(int index, bool single)
    {
        if (_whatsNewItems.Count == 0)
            return;
        _spotlightSingle = single;
        if (!IsSpotlightActive)
        {
            _whatsNewPanelBeforeSpotlight = PanelWhatsNew.Visibility == Visibility.Visible;
            PreviewKeyDown += Spotlight_PreviewKeyDown;
        }
        PanelWhatsNew.Visibility = Visibility.Collapsed;
        BtnWhatsNewReopen.Visibility = Visibility.Collapsed;
        ShowSpotlightStep(Math.Clamp(index, 0, _whatsNewItems.Count - 1));
    }

    private void ShowSpotlightStep(int index)
    {
        _spotlightIndex = index;
        MinerWhatsNewItem item = _whatsNewItems[index];
        TxtSpotlightStep.Text = _spotlightSingle ? "NOWE" : $"NOWE · {index + 1} Z {_whatsNewItems.Count}";
        TxtSpotlightTitle.Text = item.Title;
        TxtSpotlightText.Text = item.Text;
        BtnSpotlightPrev.Visibility = !_spotlightSingle && index > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnSpotlightEnd.Visibility = _spotlightSingle ? Visibility.Collapsed : Visibility.Visible;
        BtnSpotlightNext.Content = _spotlightSingle || index == _whatsNewItems.Count - 1 ? "Gotowe" : "Dalej";
        PanelSpotlightDots.Visibility = _spotlightSingle ? Visibility.Collapsed : Visibility.Visible;
        BuildSpotlightDots();
        _whatsNewSeen.Add(item.Title);
        BuildWhatsNewPanel();

        _spotlightVisitedTabs.Add(item.Tab);
        NavigateToSection(item.Tab);
        // Zakładka buduje się przy najbliższym układzie; dopiero potem karta ma rozmiar i położenie.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (_spotlightIndex != index)
                return;
            _spotlightTarget = item.Targets
                .Select(name => FindName(name) as FrameworkElement)
                .FirstOrDefault(element => element is { IsVisible: true, ActualHeight: > 0 });
            ScrollSpotlightTargetIntoView();
            SpotlightLayer.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionSpotlight));
        }));
    }

    private void BuildSpotlightDots()
    {
        PanelSpotlightDots.Children.Clear();
        for (int i = 0; i < _whatsNewItems.Count; i++)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0) };
            dot.SetResourceReference(Shape.FillProperty, i == _spotlightIndex ? "GlassAccentSolidBrush" : "GlassToggleOffBrush");
            PanelSpotlightDots.Children.Add(dot);
        }
    }

    // Karta tuż pod górną krawędzią strony, żeby pod nią zmieścił się dymek.
    private void ScrollSpotlightTargetIntoView()
    {
        if (_spotlightTarget == null || FindAncestor<ScrollViewer>(_spotlightTarget) is not { } scroller)
            return;
        double top = _spotlightTarget.TranslatePoint(new Point(0, 0), scroller).Y;
        scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + top - 16));
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(element); node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match)
                return match;
        }
        return null;
    }

    private void PositionSpotlight()
    {
        if (!IsSpotlightActive)
            return;

        Size area = SpotlightLayer.RenderSize;
        if (area.Width <= 0 || area.Height <= 0)
            return;

        SpotlightDim.Fill = new SolidColorBrush(GlassThemeMode.IsDark ? Color.FromArgb(0xA0, 0x04, 0x05, 0x0C) : Color.FromArgb(0x66, 0x16, 0x1A, 0x2B));
        var full = new RectangleGeometry(new Rect(area));

        // Warstwa leży obok treści okna, nie nad nią w drzewie — TransformToAncestor rzuciłby wyjątek.
        Rect hole = Rect.Empty;
        if (_spotlightTarget is { IsVisible: true })
        {
            hole = _spotlightTarget.TransformToVisual(SpotlightLayer).TransformBounds(new Rect(_spotlightTarget.RenderSize));
            // Część karty przewinięta poza stronę nie jest widoczna — nie obrysowujemy jej.
            if (FindAncestor<ScrollViewer>(_spotlightTarget) is { } scroller)
                hole.Intersect(scroller.TransformToVisual(SpotlightLayer).TransformBounds(new Rect(scroller.RenderSize)));
            if (!hole.IsEmpty)
                hole.Inflate(4, 4);
        }

        if (hole.IsEmpty)
        {
            // Brak karty (np. ukryta): samo ściemnienie i dymek na środku.
            SpotlightDim.Data = full;
            SpotlightRing.Visibility = Visibility.Collapsed;
            SpotlightArrow.Visibility = Visibility.Collapsed;
            SpotlightBubble.Measure(new Size(SpotlightBubble.Width, double.PositiveInfinity));
            Canvas.SetLeft(SpotlightBubble, (area.Width - SpotlightBubble.Width) / 2);
            Canvas.SetTop(SpotlightBubble, Math.Max(12, (area.Height - SpotlightBubble.DesiredSize.Height) / 2));
            return;
        }

        SpotlightDim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole, 26, 26));
        SpotlightRing.Visibility = Visibility.Visible;
        Canvas.SetLeft(SpotlightRing, hole.X);
        Canvas.SetTop(SpotlightRing, hole.Y);
        SpotlightRing.Width = hole.Width;
        SpotlightRing.Height = hole.Height;

        SpotlightBubble.Measure(new Size(SpotlightBubble.Width, double.PositiveInfinity));
        double bubbleHeight = SpotlightBubble.DesiredSize.Height;
        double x = Math.Clamp(hole.Left + 24, 12, Math.Max(12, area.Width - SpotlightBubble.Width - 12));
        double below = hole.Bottom + 16;
        double above = hole.Top - 16 - bubbleHeight;
        double y;
        bool arrowUp;
        if (below + bubbleHeight <= area.Height - 12)
        {
            y = below;
            arrowUp = true;
        }
        else if (above >= 12)
        {
            y = above;
            arrowUp = false;
        }
        else
        {
            // Karta zajmuje prawie całą wysokość: dymek przy dolnej krawędzi, bez strzałki.
            y = area.Height - bubbleHeight - 16;
            arrowUp = false;
            SpotlightArrow.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(SpotlightBubble, x);
            Canvas.SetTop(SpotlightBubble, y);
            return;
        }

        Canvas.SetLeft(SpotlightBubble, x);
        Canvas.SetTop(SpotlightBubble, y);
        SpotlightArrow.Visibility = Visibility.Visible;
        SpotlightArrow.BorderThickness = arrowUp ? new Thickness(1, 1, 0, 0) : new Thickness(0, 0, 1, 1);
        Canvas.SetLeft(SpotlightArrow, x + 34);
        Canvas.SetTop(SpotlightArrow, arrowUp ? y - 7 : y + bubbleHeight - 7);
    }

    private void SpotlightLayer_SizeChanged(object sender, SizeChangedEventArgs e) => PositionSpotlight();

    // Wołane z obsługi przewijania strony: obrys idzie za kartą.
    private void OnSpotlightPageScrolled()
    {
        if (IsSpotlightActive)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PositionSpotlight));
    }

    // Klik w ściemnione tło niczego nie robi — tylko karta i dymek są aktywne.
    private void SpotlightDim_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void BtnSpotlightNext_Click(object sender, RoutedEventArgs e)
    {
        if (_spotlightSingle || _spotlightIndex >= _whatsNewItems.Count - 1)
            EndSpotlight();
        else
            ShowSpotlightStep(_spotlightIndex + 1);
    }

    private void BtnSpotlightPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_spotlightIndex > 0)
            ShowSpotlightStep(_spotlightIndex - 1);
    }

    private void BtnSpotlightEnd_Click(object sender, RoutedEventArgs e) => EndSpotlight();

    private void Spotlight_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        EndSpotlight();
    }

    private void EndSpotlight()
    {
        if (!IsSpotlightActive && _spotlightIndex < 0)
            return;
        PreviewKeyDown -= Spotlight_PreviewKeyDown;
        SpotlightLayer.Visibility = Visibility.Collapsed;
        _spotlightTarget = null;
        _spotlightIndex = -1;
        // Panel wraca, jeśli był otwarty; inaczej zostaje przycisk „Co nowego” w pasku bocznym.
        PanelWhatsNew.Visibility = _whatsNewPanelBeforeSpotlight ? Visibility.Visible : Visibility.Collapsed;
        BtnWhatsNewReopen.Visibility = _whatsNewPanelBeforeSpotlight ? Visibility.Collapsed : Visibility.Visible;
        // Plakietka NOWE znika dopiero, gdy obejrzano wszystkie nowości danej zakładki.
        foreach (string tabName in _spotlightVisitedTabs.ToList())
        {
            if (_whatsNewItems.Where(item => item.Tab == tabName).All(item => _whatsNewSeen.Contains(item.Title)))
                OnWhatsNewTabVisited(tabName);
        }
        _spotlightVisitedTabs.Clear();
    }
}
