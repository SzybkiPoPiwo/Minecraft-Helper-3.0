using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MinecraftHelper.Services;

internal enum MinerPreviewRegion
{
    Full,
    Hotbar,
    Inventory
}

internal sealed record MinerLiveFrame(
    byte[] Jpeg,
    int Width,
    int Height,
    DateTimeOffset CapturedAt,
    string? Note = null);

internal static class MinerLivePreviewService
{
    // Minecraft 1.8 GUI sizes in GUI pixels (multiplied by the GUI scale on screen).
    private const int HotbarWidth = 182;
    private const int HotbarWithStatusBarsHeight = 50;
    private const int InventoryWidth = 193;
    private const int InventoryHeight = 196;
    private const int InventoryMargin = 8;

    public static bool TryCapture(
        IntPtr window,
        MinerPreviewRegion region,
        int maximumWidth,
        out MinerLiveFrame? frame)
    {
        frame = null;
        if (!BackgroundGameWindow.TryCaptureClient(window, out Bitmap? captured) || captured == null)
            return false;

        using (captured)
        {
            var full = new Rectangle(0, 0, captured.Width, captured.Height);
            if (region == MinerPreviewRegion.Inventory)
            {
                if (InventoryMarkerDetector.TryDetect(captured, null, null, out InventoryMarkerDetection detection)
                    && TryGetInventoryRectangle(full, detection.Layout, out Rectangle inventory))
                {
                    frame = Encode(captured, inventory, maximumWidth, note: null);
                    MinerInventorySnapshot.Current.Remember(frame);
                    return true;
                }

                if (MinerInventorySnapshot.Current.TryGetFallback(DateTimeOffset.UtcNow, out frame))
                    return true;

                frame = Encode(captured, full, maximumWidth, "Ekwipunek jest zamknięty i nie ma jeszcze skanu EQ — wysłano cały ekran.");
                return true;
            }

            Rectangle rectangle = region == MinerPreviewRegion.Hotbar ? GetHotbarRectangle(full) : full;
            frame = Encode(captured, rectangle, maximumWidth, note: null);
            return true;
        }
    }

    // Called by the Auto EQ scan with the frame it already captured, so the preview
    // always has the most recent inventory without an extra capture.
    public static void RememberInventory(Bitmap source, InventoryMarkerLayout layout, int maximumWidth)
    {
        var full = new Rectangle(0, 0, source.Width, source.Height);
        if (!TryGetInventoryRectangle(full, layout, out Rectangle inventory))
            return;

        try
        {
            MinerInventorySnapshot.Current.Remember(Encode(source, inventory, maximumWidth, note: null));
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException)
        {
            // The preview is optional; a failed encode must never interrupt Auto EQ.
        }
    }

    private static MinerLiveFrame Encode(Bitmap source, Rectangle rectangle, int maximumWidth, string? note)
    {
        using Bitmap cropped = source.Clone(rectangle, PixelFormat.Format32bppArgb);
        using Bitmap scaled = Scale(cropped, Math.Clamp(maximumWidth, 160, 1920));
        using var stream = new MemoryStream();
        SaveJpeg(scaled, stream, quality: 82L);
        return new MinerLiveFrame(stream.ToArray(), scaled.Width, scaled.Height, DateTimeOffset.UtcNow, note);
    }

    internal static Rectangle GetHotbarRectangle(Rectangle full)
    {
        // Hotbar with health/food bars above it, centred at the bottom edge.
        int scale = GetAutoGuiScale(full.Width, full.Height);
        int width = (HotbarWidth + 2 * InventoryMargin) * scale;
        int height = HotbarWithStatusBarsHeight * scale;
        return Rectangle.Intersect(
            full,
            new Rectangle((full.Width - width) / 2, full.Height - height, width, height));
    }

    internal static bool TryGetInventoryRectangle(Rectangle full, InventoryMarkerLayout layout, out Rectangle inventory)
    {
        inventory = Rectangle.Intersect(
            full,
            new Rectangle(
                layout.Left - InventoryMargin * layout.Scale,
                layout.Top - InventoryMargin * layout.Scale,
                (InventoryWidth + 2 * InventoryMargin) * layout.Scale,
                (InventoryHeight + 2 * InventoryMargin) * layout.Scale));
        return inventory.Width > 0 && inventory.Height > 0;
    }

    // "Auto" GUI scale of Minecraft 1.8: the largest scale that keeps 320x240 GUI pixels.
    private static int GetAutoGuiScale(int width, int height)
    {
        return Math.Clamp(Math.Min(width / 320, height / 240), 1, 8);
    }

    private static Bitmap Scale(Bitmap source, int maximumWidth)
    {
        if (source.Width <= maximumWidth)
            return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format24bppRgb);

        int height = Math.Max(1, (int)Math.Round(source.Height * (maximumWidth / (double)source.Width)));
        var result = new Bitmap(maximumWidth, height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height));
        return result;
    }

    private static void SaveJpeg(Bitmap bitmap, Stream destination, long quality)
    {
        ImageCodecInfo? codec = Array.Find(
            ImageCodecInfo.GetImageEncoders(),
            item => item.FormatID == ImageFormat.Jpeg.Guid);
        if (codec == null)
        {
            bitmap.Save(destination, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bitmap.Save(destination, codec, parameters);
    }
}
