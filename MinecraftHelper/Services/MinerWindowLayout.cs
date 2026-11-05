using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MinecraftHelper.Services;

internal readonly record struct MinerGameTile(Rectangle Bounds, Size ClientSize);

internal static class MinerWindowLayout
{
    // Coordinates are physical desktop pixels, including negative monitor origins.
    public static IReadOnlyList<MinerGameTile> CreateGameTiles(
        IEnumerable<Rectangle> workAreas, Size desiredClient, Size framePadding)
    {
        var result = new List<MinerGameTile>();
        foreach (Rectangle area in workAreas)
        {
            const int minWidth = 320, minHeight = 240;
            // Slot origins must be identical in every process even if Minecraft
            // clients have different frame styles/DPI. Only the client size is
            // fitted to the actual frame, within this shared cell geometry.
            const int frameAllowanceWidth = 64, frameAllowanceHeight = 96;
            int reservedColumns = area.Width >= 2 * (minWidth + frameAllowanceWidth) ? 2 : 1;
            int reservedRows = area.Height >= 2 * (minHeight + frameAllowanceHeight) ? 2 : 1;
            int cellWidth = Math.Min(desiredClient.Width + frameAllowanceWidth, area.Width / reservedColumns);
            int cellHeight = Math.Min(desiredClient.Height + frameAllowanceHeight, area.Height / reservedRows);
            int maxWidth = cellWidth - framePadding.Width;
            int maxHeight = cellHeight - framePadding.Height;
            if (maxWidth < minWidth || maxHeight < minHeight)
                continue;

            double scale = Math.Min(1, Math.Min((double)maxWidth / desiredClient.Width, (double)maxHeight / desiredClient.Height));
            var client = new Size(Math.Max(minWidth, (int)Math.Floor(desiredClient.Width * scale)),
                Math.Max(minHeight, (int)Math.Floor(desiredClient.Height * scale)));
            int width = client.Width + framePadding.Width;
            int height = client.Height + framePadding.Height;
            int columns = area.Width / cellWidth;
            int rows = area.Height / cellHeight;
            int gapX = columns > 1 ? Math.Min(8, (area.Width - columns * cellWidth) / (columns - 1)) : 0;
            int gapY = rows > 1 ? Math.Min(8, (area.Height - rows * cellHeight) / (rows - 1)) : 0;
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                    result.Add(new MinerGameTile(new Rectangle(area.Left + column * (cellWidth + gapX),
                        area.Top + row * (cellHeight + gapY), width, height), client));
        }
        return result;
    }

    public static Rectangle? FindOverlayBounds(Rectangle workArea, Size size, bool fromLeft, bool fromTop,
        int margin, int gap, IReadOnlyList<Rectangle> occupied)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return null;
        margin = Math.Max(0, margin);
        gap = Math.Max(0, gap);
        Rectangle area = Rectangle.Inflate(workArea, -margin, -margin);
        if (area.Width < size.Width || area.Height < size.Height)
            return null;

        var xs = new HashSet<int> { area.Left, area.Right - size.Width };
        var ys = new HashSet<int> { area.Top, area.Bottom - size.Height };
        foreach (Rectangle peer in occupied)
        {
            xs.Add(peer.Left - gap - size.Width);
            xs.Add(peer.Right + gap);
            ys.Add(peer.Top - gap - size.Height);
            ys.Add(peer.Bottom + gap);
        }
        int[] orderedX = (fromLeft ? xs.OrderBy(x => x) : xs.OrderByDescending(x => x)).ToArray();
        int[] orderedY = (fromTop ? ys.OrderBy(y => y) : ys.OrderByDescending(y => y)).ToArray();
        // Fill a horizontal row before moving away from the requested corner.
        foreach (int y in orderedY)
            foreach (int x in orderedX)
            {
                var candidate = new Rectangle(x, y, size.Width, size.Height);
                if (area.Contains(candidate) && !occupied.Any(peer => Rectangle.Inflate(peer, gap, gap).IntersectsWith(candidate)))
                    return candidate;
            }
        return null;
    }
}
