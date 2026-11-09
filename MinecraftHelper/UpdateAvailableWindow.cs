using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MinecraftHelper.Services;

namespace MinecraftHelper
{
    /// <summary>
    /// „Dostępna nowa wersja” (wspólne dla Kopacza i Auto Clickera): co nowego z opisu wydania
    /// na GitHubie, rozwijane wskazówki aktualizacji i trzy wyjścia — pobierz, przypomnij później,
    /// pomiń tę wersję (SkipRequested; zapis robi wywołujący, bo każdy program ma swoje ustawienia).
    /// </summary>
    internal sealed class UpdateAvailableWindow : Window
    {
        private const string ArrowUpIcon = "M12,19 V5 M6,11 L12,5 L18,11";
        private const string DownloadIcon = "M12,4 V16 M7,11 L12,16 L17,11 M5,20 H19";
        private const string ChevronRight = "M9,6 L15,12 L9,18";
        private const string ChevronDown = "M6,9 L12,15 L18,9";

#if MINER_EDITION
        private const string AppName = "Minecraft Helper Kopacz";
#else
        private const string AppName = "Minecraft Helper Auto Clicker";
#endif

        private readonly TextBlock _linkErrorText;
        private readonly StackPanel _helpPanel;
        private readonly Path _helpChevron;

        /// <summary>Użytkownik wybrał „Pomiń tę wersję” — nie przypominać o <see cref="AppUpdateInfo.LatestVersion"/>.</summary>
        public bool SkipRequested { get; private set; }

        public UpdateAvailableWindow(AppUpdateInfo update)
        {
            ArgumentNullException.ThrowIfNull(update);

            Title = "Aktualizacja — " + AppName;
            Width = 600;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            GlassChrome.Apply(this);

            var panel = new StackPanel();
            panel.Children.Add(BuildHeader(update));
            panel.Children.Add(BuildNotes(update));

            // „Jak zaktualizować?” — rozwijane, żeby nie zasłaniało listy zmian.
            _helpChevron = LineIcon(ChevronRight, 12, 2.6);
            var helpLabel = new StackPanel { Orientation = Orientation.Horizontal };
            helpLabel.Children.Add(_helpChevron);
            helpLabel.Children.Add(new TextBlock { Text = "Jak zaktualizować?", Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var helpToggle = TextButton(helpLabel, "GlassAccentInkBrush");
            helpToggle.Margin = new Thickness(0, 12, 0, 0);
            helpToggle.Click += (_, _) => ToggleHelp();
            panel.Children.Add(helpToggle);

            _helpPanel = new StackPanel { Margin = new Thickness(2, 6, 0, 0), Visibility = Visibility.Collapsed };
            foreach (string step in new[]
            {
                "1. Pobierz instalator przyciskiem poniżej.",
                "2. Zamknij Kopacza i Auto Clickera z menu ikony w zasobniku — przycisk X tylko chowa okno.",
                "3. Uruchom instalator. Gdy Windows SmartScreen go zatrzyma: Więcej informacji → Uruchom mimo to.",
                "Ustawienia, bindy, profile kont i logi zostają zachowane."
            })
            {
                _helpPanel.Children.Add(new TextBlock { Text = step, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, LineHeight = 19, Foreground = GlassPalette.Muted, Margin = new Thickness(0, 0, 0, 3) });
            }
            panel.Children.Add(_helpPanel);

            panel.Children.Add(BuildFooter(update));
            panel.Children.Add(new TextBlock
            {
                Text = update.IsDirectInstaller
                    ? "Otwiera instalator z oficjalnego wydania na GitHubie. Program niczego nie pobiera ani nie uruchamia sam."
                    : "W wydaniu nie ma instalatora — otworzy się oficjalna strona wydania na GitHubie.",
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                FontSize = 11.5,
                Foreground = GlassPalette.Muted
            });

            _linkErrorText = new TextBlock
            {
                Text = "Nie udało się otworzyć linku. Otwórz ręcznie stronę projektu na GitHubie.",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                Foreground = GlassPalette.Bad,
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_linkErrorText);

            var card = new Border
            {
                Style = GlassChrome.GetStyle("GlassCard"),
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(24, 22, 24, 20),
                Margin = new Thickness(18),
                Child = panel
            };
            Content = card;
        }

        private UIElement BuildHeader(AppUpdateInfo update)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tile = new Border
            {
                Style = GlassChrome.GetStyle("GlassIconTile"),
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(15),
                Child = LineIcon(ArrowUpIcon, 22, 2.3)
            };
            header.Children.Add(tile);

            var titles = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = $"Dostępna wersja {update.LatestVersion}", FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            string subtitle = update.PublishedAt is { } published
                ? $"{update.ReleaseName} · wydano {published.LocalDateTime.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("pl-PL"))}"
                : update.ReleaseName;
            titles.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, Foreground = GlassPalette.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(titles, 1);
            header.Children.Add(titles);

            var current = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            current.Inlines.Add(new Run("Masz ") { Foreground = GlassPalette.Muted });
            current.Inlines.Add(new Run(update.CurrentVersion) { FontWeight = FontWeights.Bold });
            var pill = new Border { Style = GlassChrome.GetStyle("GlassPill"), Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), Child = current };
            pill.SetResourceReference(Border.BorderBrushProperty, "GlassLineBrush");
            pill.BorderThickness = new Thickness(1);
            Grid.SetColumn(pill, 2);
            header.Children.Add(pill);
            return header;
        }

        private UIElement BuildNotes(AppUpdateInfo update)
        {
            var notes = new StackPanel();
            notes.Children.Add(new TextBlock { Text = "CO NOWEGO", Style = GlassChrome.GetStyle("GlassCaption"), Margin = new Thickness(0, 0, 0, 8) });

            IReadOnlyList<UpdateReleaseNotes> releases = update.Notes ?? Array.Empty<UpdateReleaseNotes>();
            bool severalReleases = releases.Count > 1;
            foreach (UpdateReleaseNotes release in releases)
            {
                if (severalReleases)
                    notes.Children.Add(new TextBlock { Text = $"Wersja {release.Version}", FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, notes.Children.Count > 1 ? 10 : 0, 0, 4) });
                foreach (UpdateNotesSection section in release.Sections)
                {
                    if (section.Title.Length > 0)
                        notes.Children.Add(new TextBlock { Text = section.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) });
                    foreach (string line in section.Lines)
                        notes.Children.Add(Bullet(line));
                }
            }

            if (releases.Count == 0)
            {
                // Opis wydania bez sekcji „Co nowego” albo nie udało się go odczytać.
                notes.Children.Add(new TextBlock { Text = "Lista zmian jest w opisie wydania na GitHubie.", FontSize = 12.8, TextWrapping = TextWrapping.Wrap, Foreground = GlassPalette.Muted });
                if (update.ReleasePageUri != null)
                {
                    var link = TextButton(new TextBlock { Text = "Pełny opis wydania na GitHubie" }, "GlassAccentInkBrush");
                    link.Margin = new Thickness(0, 6, 0, 0);
                    link.Click += (_, _) => OpenUri(update.ReleasePageUri);
                    notes.Children.Add(link);
                }
            }

            var inset = new Border { Style = GlassChrome.GetStyle("GlassInset"), CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 14, 18, 14), Child = notes };
            return new ScrollViewer
            {
                Content = inset,
                MaxHeight = 330,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private static UIElement Bullet(string text)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 7, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            dot.SetResourceReference(Shape.FillProperty, "GlassAccentSolidBrush");
            row.Children.Add(dot);
            var line = new TextBlock { Text = text, FontSize = 12.8, LineHeight = 19, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(line, 1);
            row.Children.Add(line);
            return row;
        }

        private UIElement BuildFooter(AppUpdateInfo update)
        {
            var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var skip = TextButton(new TextBlock { Text = "Pomiń tę wersję" }, null);
            skip.Foreground = GlassPalette.Muted;
            skip.ToolTip = $"Nie przypominaj o wersji {update.LatestVersion}. O kolejnej nowej wersji program znów da znać.";
            skip.Click += (_, _) =>
            {
                SkipRequested = true;
                CloseDialog();
            };
            footer.Children.Add(skip);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var later = new Button { Content = "Przypomnij później", Height = 38, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            later.Click += (_, _) => CloseDialog();
            buttons.Children.Add(later);

            var downloadLabel = new StackPanel { Orientation = Orientation.Horizontal };
            var downloadIcon = LineIcon(DownloadIcon, 15, 2.3);
            downloadIcon.Margin = new Thickness(0, 0, 8, 0);
            downloadLabel.Children.Add(downloadIcon);
            downloadLabel.Children.Add(new TextBlock { Text = update.IsDirectInstaller ? "Pobierz instalator" : "Otwórz stronę wydania", VerticalAlignment = VerticalAlignment.Center });
            var download = new Button
            {
                Content = downloadLabel,
                Height = 38,
                Padding = new Thickness(18, 0, 18, 0),
                Style = GlassChrome.GetStyle("GlassPrimaryButton")
            };
            AutomationProperties.SetName(download, update.IsDirectInstaller ? "Pobierz instalator" : "Otwórz stronę wydania");
            download.Click += (_, _) =>
            {
                if (OpenUri(update.DownloadUri))
                    CloseDialog();
            };
            buttons.Children.Add(download);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            Loaded += (_, _) => download.Focus();
            return footer;
        }

        private void ToggleHelp()
        {
            bool show = _helpPanel.Visibility != Visibility.Visible;
            _helpPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _helpChevron.Data = Geometry.Parse(show ? ChevronDown : ChevronRight);
        }

        // Przycisk wyglądający jak link (bez tła i ramki).
        private static Button TextButton(object content, string? foregroundKey)
        {
            var button = new Button
            {
                Content = content,
                Height = 30,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
            if (foregroundKey != null)
                button.SetResourceReference(ForegroundProperty, foregroundKey);
            return button;
        }

        // Ikona liniowa 24×24 w kolorze tekstu rodzica (styl GlassIconPath).
        private static Path LineIcon(string data, double size, double thickness)
        {
            var path = new Path
            {
                Data = Geometry.Parse(data),
                Style = GlassChrome.GetStyle("GlassIconPath"),
                StrokeThickness = thickness,
                Width = 24,
                Height = 24,
                LayoutTransform = new ScaleTransform(size / 24, size / 24),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            return path;
        }

        private bool OpenUri(Uri uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }
            catch
            {
                _linkErrorText.Visibility = Visibility.Visible;
                return false;
            }
        }

        private void CloseDialog()
        {
            DialogResult = true;
            Close();
        }
    }
}
