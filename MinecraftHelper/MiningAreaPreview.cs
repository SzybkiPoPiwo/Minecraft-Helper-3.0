using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MinecraftHelper
{
    /// <summary>
    /// Podgląd obszaru kopania Kopacza 6/3/3 (tryb Do góry) z góry: siatka bloków, obwód,
    /// po którym chodzi Kopacz, przerywana ścieżka ze strzałkami kierunku i znacznik startu.
    /// Oś pozioma to lewo/prawo (A/D), pionowa to przód/tył (W/S); start z tyłu po lewej, ruch W → D → S → A.
    /// </summary>
    internal sealed class MiningAreaPreview : FrameworkElement
    {
        private const double MaxTile = 30;
        private const double MaxGridHeight = 190;
        private const double Gap = 3;

        private int _across = 2;
        private int _depth = 4;

        /// <summary>Bloki w poziomie (A/D) i w pionie (W/S).</summary>
        public void SetArea(int across, int depth)
        {
            across = Math.Clamp(across, 1, 999);
            depth = Math.Clamp(depth, 1, 999);
            if (across == _across && depth == _depth)
                return;
            _across = across;
            _depth = depth;
            InvalidateMeasure();
            InvalidateVisual();
        }

        // Jedna kratka na blok: długość 4 i szerokość 2 to siatka 4 × 2.
        private (int Columns, int Rows, double Tile) Layout(double width)
        {
            int columns = _across;
            int rows = _depth;
            double byWidth = (width - Gap) / columns - Gap;
            double byHeight = (MaxGridHeight - Gap) / rows - Gap;
            double tile = Math.Max(2, Math.Min(MaxTile, Math.Min(byWidth, byHeight)));
            return (columns, rows, tile);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            bool unbounded = double.IsInfinity(availableSize.Width);
            double width = unbounded ? 320 : availableSize.Width;
            (int columns, int rows, double tile) = Layout(width);
            return new Size(unbounded ? columns * (tile + Gap) + Gap : width, rows * (tile + Gap) + Gap);
        }

        protected override void OnRender(DrawingContext dc)
        {
            (int columns, int rows, double tile) = Layout(ActualWidth);
            double step = tile + Gap;
            double gridWidth = columns * step + Gap;
            double left = Math.Max(0, (ActualWidth - gridWidth) / 2);
            double radius = Math.Min(6, tile / 4);

            Brush accent = GlassPalette.AccentInk;
            Brush pathFill = GlassPalette.AccentSoft;
            Brush innerFill = GlassPalette.NeutralSoft;
            var tileEdge = new Pen(GlassPalette.Line, 1);

            Rect Cell(int column, int row) => new(left + Gap + column * step, Gap + row * step, tile, tile);
            Point Center(int column, int row) => new(left + Gap + column * step + tile / 2, Gap + row * step + tile / 2);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    bool onPath = row == 0 || row == rows - 1 || column == 0 || column == columns - 1;
                    dc.DrawRoundedRectangle(onPath ? pathFill : innerFill, tileEdge, Cell(column, row), radius, radius);
                }
            }

            // Narożniki w kolejności ruchu: start, potem kolejne odcinki.
            int last = columns - 1;
            int bottom = rows - 1;
            (int Column, int Row)[] corners = { (0, bottom), (0, 0), (last, 0), (last, bottom) };

            var dashed = new Pen(accent, Math.Max(1.2, Math.Min(2, tile / 12))) { DashStyle = new DashStyle(new double[] { 3, 2.5 }, 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            // Przy jednym rzędzie albo kolumnie droga to tam i z powrotem po tej samej linii —
            // strzałki tylko na pierwszych odcinkach, żeby się nie nakładały.
            bool singleLine = columns == 1 || rows == 1;
            for (int index = 0; index < 4; index++)
            {
                Point from = Center(corners[index].Column, corners[index].Row);
                Point to = Center(corners[(index + 1) % 4].Column, corners[(index + 1) % 4].Row);
                if (from == to)
                    continue;
                dc.DrawLine(dashed, from, to);
                // Krótki ostatni odcinek kończy się pod znacznikiem startu — strzałka by na nim wisiała.
                bool hiddenByStart = index == 3 && (to - from).Length < step * 1.5;
                if (tile >= 8 && !(singleLine && index >= 2) && !hiddenByStart)
                    DrawArrow(dc, accent, from, to, Math.Max(4, tile / 4));
            }

            Rect start = Cell(corners[0].Column, corners[0].Row);
            start.Inflate(Math.Min(2, tile / 8), Math.Min(2, tile / 8));
            dc.DrawRoundedRectangle(GlassPalette.Ok, new Pen(Brushes.White, Math.Max(1, tile / 14)), start, radius + 1, radius + 1);
            if (tile >= 18)
            {
                var label = new FormattedText("S", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    tile * 0.45, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point(start.X + (start.Width - label.Width) / 2, start.Y + (start.Height - label.Height) / 2));
            }
        }

        // Mały trójkąt w połowie odcinka, skierowany w stronę ruchu.
        private static void DrawArrow(DrawingContext dc, Brush brush, Point from, Point to, double size)
        {
            Vector direction = to - from;
            if (direction.Length < size * 3)
                return;
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            Point tip = from + (to - from) / 2 + direction * size;
            Point back = tip - direction * size * 1.6;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(tip, isFilled: true, isClosed: true);
                context.LineTo(back + normal * size * 0.8, isStroked: false, isSmoothJoin: false);
                context.LineTo(back - normal * size * 0.8, isStroked: false, isSmoothJoin: false);
            }
            geometry.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }
    }
}
