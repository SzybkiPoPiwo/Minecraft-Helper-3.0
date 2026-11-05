using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace MinecraftHelper.Services;

// Peer HWND properties identify HUDs without cross-process ownership/input queues.
internal sealed class MinerHudLayout : IDisposable
{
    private const string Marker = "MinecraftHelper.Miner.HudLayout.v1";
    private IntPtr _window;
    private Rectangle _lastWorkArea;
    private Size _lastSize;
    private bool _lastLeft, _lastTop;
    private int _lastMargin, _lastGap;
    private DateTime _nextRefreshUtc;
    public bool NoSpace { get; private set; }

    public void Invalidate() => _nextRefreshUtc = DateTime.MinValue;

    public bool TryPosition(IntPtr window, Rectangle workArea, bool fromLeft, bool fromTop, int margin, int gap)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out NativeRect own))
            return false;
        if (_window != window)
        {
            Dispose();
            if (!SetProp(window, Marker, (IntPtr)1))
                return false;
            _window = window;
        }
        var size = new Size(own.Right - own.Left, own.Bottom - own.Top);
        if (!NoSpace && DateTime.UtcNow < _nextRefreshUtc && workArea == _lastWorkArea
            && size == _lastSize && fromLeft == _lastLeft && fromTop == _lastTop
            && margin == _lastMargin && gap == _lastGap)
            return true;

        using var gate = new Mutex(false, @"Local\MinecraftHelper.Miner.HudLayout.v1");
        bool locked = false;
        try
        {
            try { locked = gate.WaitOne(30); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked)
                return false;

            var peers = new List<Rectangle>();
            EnumWindows((peer, _) =>
            {
                if (peer != window && IsWindowVisible(peer) && GetProp(peer, Marker) != IntPtr.Zero
                    && GetWindowRect(peer, out NativeRect bounds))
                {
                    Rectangle rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
                    if (rectangle.IntersectsWith(workArea)) peers.Add(rectangle);
                }
                return true;
            }, IntPtr.Zero);
            Rectangle? target = MinerWindowLayout.FindOverlayBounds(workArea, size, fromLeft, fromTop, margin, gap, peers);
            NoSpace = target == null;
            if (target is not { } position)
                return false;
            // Move only our own HUD, preserving focus, z-order and independent ownership.
            if (!SetWindowPos(window, IntPtr.Zero, position.X, position.Y, 0, 0, 0x0001 | 0x0004 | 0x0010 | 0x0200))
                return false;
            _lastWorkArea = workArea;
            _lastSize = size;
            _lastLeft = fromLeft;
            _lastTop = fromTop;
            _lastMargin = margin;
            _lastGap = gap;
            _nextRefreshUtc = DateTime.UtcNow.AddSeconds(1);
            return true;
        }
        finally
        {
            if (locked) gate.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        if (_window != IntPtr.Zero)
            _ = RemoveProp(_window, Marker);
        _window = IntPtr.Zero;
        Invalidate();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr window, string name);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
