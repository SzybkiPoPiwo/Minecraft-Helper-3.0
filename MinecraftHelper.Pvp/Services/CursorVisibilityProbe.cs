using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace MinecraftHelper.Services;

// Read-only: never sets the cursor, clips it, hooks input or sends mouse events.
internal static class CursorVisibilityProbe
{
    private const int CursorShowing = 0x00000001;
    private const uint DiNormal = 0x0003;
    private const int SmCxCursor = 13;
    private const int SmCyCursor = 14;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<IntPtr, bool> BlankCache = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Handle;
        public Point Position;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(out CursorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon,
        int width, int height, uint step, IntPtr brush, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static bool IsGuiCursorVisible(bool failClosed)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(out info))
            return failClosed;
        return (info.Flags & CursorShowing) != 0 && !IsBlank(info.Handle);
    }

    private static bool IsBlank(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;
        lock (CacheLock)
        {
            if (BlankCache.TryGetValue(handle, out bool blank))
                return blank;

            int width = Math.Clamp(GetSystemMetrics(SmCxCursor), 1, 128);
            int height = Math.Clamp(GetSystemMetrics(SmCyCursor), 1, 128);
            bool result = !CursorChangesBackground(handle, width, height, Drawing.Color.Black)
                && !CursorChangesBackground(handle, width, height, Drawing.Color.White);
            // Serialize first-time analysis: UI and click timers must not render
            // the same uncached handle concurrently during a cursor transition.
            if (BlankCache.Count >= 24)
                BlankCache.Clear();
            BlankCache[handle] = result;
            return result;
        }
    }

    private static bool CursorChangesBackground(IntPtr cursor, int width, int height, Drawing.Color background)
    {
        using var bitmap = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format32bppArgb);
        using Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(background);
        IntPtr dc = graphics.GetHdc();
        bool drawn;
        try
        {
            drawn = DrawIconEx(dc, 0, 0, cursor, width, height, 0, IntPtr.Zero, DiNormal);
        }
        finally
        {
            graphics.ReleaseHdc(dc);
        }
        // Unknown/destroyed handles must not make an inventory look like gameplay.
        return !drawn || BitmapChangesBackground(bitmap, background);
    }

    internal static bool BitmapChangesBackground(Drawing.Bitmap bitmap, Drawing.Color background)
    {
        DrawingImaging.BitmapData data = bitmap.LockBits(
            new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            DrawingImaging.ImageLockMode.ReadOnly,
            DrawingImaging.PixelFormat.Format32bppArgb);
        try
        {
            int expected = background.ToArgb();
            int[] row = new int[bitmap.Width];
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                for (int x = 0; x < row.Length; x++)
                    if (row[x] != expected)
                        return true;
            }
            return false;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
