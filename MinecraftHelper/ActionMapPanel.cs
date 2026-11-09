using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MinecraftHelper
{
    /// <summary>
    /// Mapa działania (ActionMapWindow): kafelki z bieżącymi ustawieniami konta, podświetlony
    /// krok, który Kopacz właśnie wykonuje, i opis wybranego kafelka. Niczego nie zmienia —
    /// „Przejdź do ustawienia” otwiera właściwą kartę (GoToRequested).
    /// </summary>
    internal sealed class ActionMapPanel : UserControl
    {
        private sealed record TileView(Button Button, TextBlock Title, TextBlock Value);

        private readonly Dictionary<string, TileView> _views = new();
        private readonly TextBlock _detailTitle = new() { FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _detailNow = new() { Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed };
        private readonly TextBlock _detailText = new() { FontSize = 12.5, LineHeight = 19, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private readonly TextBlock _detailWarning = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        private readonly TextBlock _detailWhere = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _detailRequires = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        private readonly Button _goTo = new() { Content = "Przejdź do ustawienia", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };

        private readonly StackPanel _settingsColumn = new();
        private readonly StackPanel _settingsRows = new();
        private readonly ColumnDefinition _settingsColumnWidth = new() { Width = new GridLength(0) };
        private string _settingsSignature = "";

        private IReadOnlyList<ActionMapTile> _tiles = Array.Empty<ActionMapTile>();
        private string? _current;
        private string _selected = "mine";

        public event Action<ActionMapTile>? GoToRequested;

        public ActionMapPanel()
        {
            var canvas = new Canvas { Width = ActionMapCatalog.Width, Height = ActionMapCatalog.Height };
            AddBand(canvas, 150, "GlassNeutralSoftBrush", 1);
            AddBand(canvas, 300, "GlassBadSoftBrush", 0.55);
            foreach (ActionMapSegment segment in ActionMapCatalog.Wires)
                AddWire(canvas, segment);
            AddLabel(canvas, 90, 158, "CO USTAWIONY CZAS · AUTO EQ", "GlassInk2Brush");
            AddLabel(canvas, 90, 308, "GDY USŁYSZY OBRAŻENIA · OCHRONA", "GlassBadBrush");
            AddLabel(canvas, 16, 454, "ZAWSZE OBOK", "GlassInk2Brush");
            foreach ((string id, (int X, int Y, int W, int H) box) in ActionMapCatalog.Layout)
                _views[id] = AddTile(canvas, id, box);

            _detailText.SetResourceReference(TextBlock.ForegroundProperty, "GlassInk2Brush");
            _detailWarning.SetResourceReference(TextBlock.ForegroundProperty, "GlassWarnBrush");
            _detailRequires.SetResourceReference(TextBlock.ForegroundProperty, "GlassInk2Brush");
            _goTo.Click += (_, _) =>
            {
                ActionMapTile? tile = _tiles.FirstOrDefault(item => item.Id == _selected);
                if (tile?.Tab != null)
                    GoToRequested?.Invoke(tile);
            };

            var root = new StackPanel();
            root.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
            root.Children.Add(BuildDetail());
            Content = root;
        }

        public void Update(IReadOnlyList<ActionMapTile> tiles, string? current)
        {
            _tiles = tiles;
            _current = current;
            foreach (ActionMapTile tile in tiles)
            {
                if (_views.TryGetValue(tile.Id, out TileView? view))
                    PaintTile(tile, view);
            }
            ShowDetail();
        }

        private void PaintTile(ActionMapTile tile, TileView view)
        {
            bool active = tile.Id == _current;
            bool picked = tile.Id == _selected;
            string accent = tile.Alarm ? "GlassBadBrush" : "GlassAccentSolidBrush";

            view.Button.SetResourceReference(BackgroundProperty, active ? (tile.Alarm ? "GlassBadBrush" : "GlassAccentBrush") : "GlassRaisedBrush");
            view.Button.SetResourceReference(BorderBrushProperty, picked ? accent : tile.Warning.Length > 0 ? "GlassWarnBrush" : "GlassLineBrush");
            view.Button.BorderThickness = new Thickness(active ? 0 : picked ? 2 : tile.Warning.Length > 0 ? 1.5 : 1);
            view.Button.Opacity = tile.Enabled ? 1 : 0.42;

            view.Title.Text = tile.Title;
            view.Value.Text = tile.Value;
            AutomationProperties.SetName(view.Button, tile.Title);
            view.Button.ToolTip = tile.Warning.Length > 0 ? tile.Warning : tile.Value;
            if (active)
            {
                view.Title.Foreground = Brushes.White;
                view.Value.Foreground = Brushes.White;
            }
            else
            {
                view.Title.SetResourceReference(TextBlock.ForegroundProperty, "GlassInkBrush");
                view.Value.SetResourceReference(TextBlock.ForegroundProperty, tile.Warning.Length > 0 ? "GlassWarnBrush" : "GlassInk2Brush");
            }
        }

        private void ShowDetail()
        {
            ActionMapTile? tile = _tiles.FirstOrDefault(item => item.Id == _selected);
            if (tile == null)
                return;

            _detailTitle.Text = tile.Title;
            _detailNow.Visibility = tile.Id == _current ? Visibility.Visible : Visibility.Collapsed;
            _detailText.Text = tile.Enabled ? tile.Description : "Ta opcja jest wyłączona, więc Kopacz pominie ten krok.";
            _detailWarning.Text = tile.Warning;
            _detailWarning.Visibility = tile.Warning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _detailWhere.Text = tile.Where;
            _detailRequires.Text = tile.Requires == null ? string.Empty : "Wymaga: " + tile.Requires;
            _detailRequires.Visibility = tile.Requires == null ? Visibility.Collapsed : Visibility.Visible;
            _goTo.Visibility = tile.Tab == null ? Visibility.Collapsed : Visibility.Visible;
            ShowSettings(tile);
        }

        // Odświeżanie co sekundę: wiersze przebudowujemy tylko po faktycznej zmianie.
        private void ShowSettings(ActionMapTile tile)
        {
            string signature = tile.Id + "|" + string.Join("|", tile.Details.Select(detail => detail.Label + "=" + detail.Value));
            if (signature == _settingsSignature)
                return;
            _settingsSignature = signature;

            // Kafelek bez ustawień (np. Logi) oddaje miejsce opisowi.
            _settingsColumn.Visibility = tile.Details.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _settingsColumnWidth.Width = tile.Details.Count > 0 ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
            _settingsRows.Children.Clear();
            foreach (ActionMapDetail detail in tile.Details)
            {
                var label = new TextBlock { Text = detail.Label, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                label.SetResourceReference(TextBlock.ForegroundProperty, "GlassInk2Brush");
                var value = new TextBlock { Text = detail.Value, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(value, 1);

                var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(label);
                row.Children.Add(value);
                _settingsRows.Children.Add(row);
            }
        }

        private UIElement BuildDetail()
        {
            var nowText = new TextBlock { Text = "TERAZ", FontSize = 10.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            nowText.SetResourceReference(TextBlock.ForegroundProperty, "GlassAccentInkBrush");
            _detailNow.SetResourceReference(StyleProperty, "GlassPill");
            _detailNow.SetResourceReference(Border.BackgroundProperty, "GlassAccentSoftBrush");
            _detailNow.Child = nowText;

            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(_detailTitle);
            heading.Children.Add(_detailNow);

            var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            left.Children.Add(heading);
            left.Children.Add(_detailText);
            left.Children.Add(_detailWarning);

            var settingsCaption = new TextBlock { Text = "OBECNE USTAWIENIA", Margin = new Thickness(0, 0, 0, 5) };
            settingsCaption.SetResourceReference(StyleProperty, "GlassCaption");
            _settingsColumn.Margin = new Thickness(0, 0, 18, 0);
            _settingsColumn.Children.Add(settingsCaption);
            _settingsColumn.Children.Add(_settingsRows);
            Grid.SetColumn(_settingsColumn, 1);

            var caption = new TextBlock { Text = "GDZIE TO USTAWIĆ", Margin = new Thickness(0, 0, 0, 3) };
            caption.SetResourceReference(StyleProperty, "GlassCaption");
            var right = new StackPanel();
            right.Children.Add(caption);
            right.Children.Add(_detailWhere);
            right.Children.Add(_detailRequires);
            right.Children.Add(_goTo);
            Grid.SetColumn(right, 2);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(_settingsColumnWidth);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.Children.Add(left);
            grid.Children.Add(_settingsColumn);
            grid.Children.Add(right);

            var detail = new Border { Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(16, 12, 16, 12), Child = grid };
            detail.SetResourceReference(StyleProperty, "GlassInset");
            return detail;
        }

        private TileView AddTile(Canvas canvas, string id, (int X, int Y, int W, int H) box)
        {
            var title = new TextBlock { FontSize = id == "mine" ? 16 : 12.5, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var value = new TextBlock { FontSize = 10.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(title);
            stack.Children.Add(value);

            var button = new Button
            {
                Content = stack,
                Width = box.W,
                Height = box.H,
                MinHeight = 0,
                Padding = new Thickness(5, 2, 5, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Click += (_, _) =>
            {
                _selected = id;
                Update(_tiles, _current);
            };
            Canvas.SetLeft(button, box.X);
            Canvas.SetTop(button, box.Y);
            canvas.Children.Add(button);
            return new TileView(button, title, value);
        }

        private static void AddBand(Canvas canvas, double top, string brush, double opacity)
        {
            var band = new Border { Width = ActionMapCatalog.Width, Height = 120, CornerRadius = new CornerRadius(16), Opacity = opacity };
            band.SetResourceReference(Border.BackgroundProperty, brush);
            Canvas.SetTop(band, top);
            canvas.Children.Add(band);
        }

        private static void AddLabel(Canvas canvas, double x, double y, string text, string brush)
        {
            var label = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold };
            label.SetResourceReference(TextBlock.ForegroundProperty, brush);
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
        }

        private static void AddWire(Canvas canvas, ActionMapSegment s)
        {
            string brush = s.Wire switch
            {
                ActionMapWire.AutoEq => "GlassAccentSolidBrush",
                ActionMapWire.Alarm => "GlassBadBrush",
                _ => "GlassInk2Brush"
            };
            var line = new Line { X1 = s.X1, Y1 = s.Y1, X2 = s.X2, Y2 = s.Y2, StrokeThickness = 2 };
            if (s.Dashed)
                line.StrokeDashArray = new DoubleCollection { 3, 2 };
            line.SetResourceReference(Shape.StrokeProperty, brush);
            canvas.Children.Add(line);
            if (!s.Arrow)
                return;

            // Grot kończy się na końcu odcinka, skierowany wzdłuż linii.
            double dx = Math.Sign(s.X2 - s.X1), dy = Math.Sign(s.Y2 - s.Y1);
            var head = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(s.X2, s.Y2),
                    new Point(s.X2 - dx * 7 - dy * 5, s.Y2 - dy * 7 - dx * 5),
                    new Point(s.X2 - dx * 7 + dy * 5, s.Y2 - dy * 7 + dx * 5)
                }
            };
            head.SetResourceReference(Shape.FillProperty, brush);
            canvas.Children.Add(head);
        }
    }
}
