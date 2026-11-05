using System;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace MinecraftHelper.Services;

// Pagefile-backed shared memory disappears when the last engine closes it.
// No cursor movement or settings/file IO happens in this class.
internal sealed class BackgroundCursorStateStore : IDisposable
{
    private const string DefaultName = @"Local\MinecraftHelper.Miner.Cursor.v1";
    private const int Capacity = 64;
    private const int Magic = 0x4D484331;
    private Mutex? _gate;
    private MemoryMappedFile? _memory;
    private MemoryMappedViewAccessor? _view;
    private bool _disposed;

    public bool IsAvailable => !_disposed && _view != null && _gate != null;

    public BackgroundCursorStateStore(string? name = null)
    {
        try
        {
            string sharedName = name ?? DefaultName;
            _gate = new Mutex(false, sharedName + ".Mutex");
            _memory = MemoryMappedFile.CreateOrOpen(sharedName, Capacity, MemoryMappedFileAccess.ReadWrite);
            _view = _memory.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            Dispose();
        }
    }

    public void Arm(Point fallback, int timestamp) => WithLock(view =>
    {
        WriteSnapshot(view, BackgroundCursorPolicy.Arm(ReadSnapshot(view), fallback, timestamp));
        return true;
    }, false);

    public Point? ObserveMouse(Point packetPosition, int rawX, int rawY, bool absolute, int timestamp, bool userSelectedGame = false) =>
        WithLock<Point?>(view =>
        {
            BackgroundCursorSnapshot before = ReadSnapshot(view);
            BackgroundCursorSnapshot after = BackgroundCursorPolicy.ObserveMouse(before, packetPosition, rawX, rawY, absolute, timestamp, userSelectedGame);
            WriteSnapshot(view, after);
            return after.LastCorrectedTimestamp == timestamp && before.LastCorrectedTimestamp != timestamp
                && after.Position != packetPosition ? after.Position : null;
        }, null);

    public Point? RestoreTarget(Point current, Point center, bool userSelectedGame, int timestamp) =>
        WithLock(view => BackgroundCursorPolicy.RestoreTarget(ReadSnapshot(view), current, center, userSelectedGame, timestamp), (Point?)null);

    public void SelectUserWindow(IntPtr hwnd) => WithLock(view =>
    {
        EnsureHeader(view);
        view.Write(32, hwnd.ToInt64());
        return true;
    }, false);

    public IntPtr UserWindow => WithLock(view => view.ReadInt32(0) == Magic
        ? new IntPtr(view.ReadInt64(32)) : IntPtr.Zero, IntPtr.Zero);

    private T WithLock<T>(Func<MemoryMappedViewAccessor, T> action, T fallback)
    {
        Mutex? gate = _gate;
        MemoryMappedViewAccessor? view = _view;
        if (_disposed || gate == null || view == null)
            return fallback;
        bool locked = false;
        try
        {
            try { locked = gate.WaitOne(5); }
            catch (AbandonedMutexException) { locked = true; }
            if (!locked || _disposed)
                return fallback;
            return action(view);
        }
        catch (Exception ex) when (IsUnavailable(ex)) { return fallback; }
        finally
        {
            if (locked)
            {
                try { gate.ReleaseMutex(); }
                catch (ObjectDisposedException) { }
                catch (ApplicationException) { }
            }
        }
    }

    private static BackgroundCursorSnapshot ReadSnapshot(MemoryMappedViewAccessor view) =>
        view.ReadInt32(0) != Magic ? default : new BackgroundCursorSnapshot(view.ReadInt32(4) != 0,
            new Point(view.ReadInt32(8), view.ReadInt32(12)), view.ReadInt32(16), view.ReadInt32(20), view.ReadInt32(24));

    private static void WriteSnapshot(MemoryMappedViewAccessor view, BackgroundCursorSnapshot snapshot)
    {
        EnsureHeader(view);
        view.Write(4, snapshot.Initialized ? 1 : 0);
        view.Write(8, snapshot.Position.X);
        view.Write(12, snapshot.Position.Y);
        view.Write(16, snapshot.BaselineTimestamp);
        view.Write(20, snapshot.GuardUntil);
        view.Write(24, snapshot.LastCorrectedTimestamp);
    }

    private static void EnsureHeader(MemoryMappedViewAccessor view)
    {
        if (view.ReadInt32(0) == Magic)
            return;
        view.WriteArray(0, new byte[Capacity], 0, Capacity);
        view.Write(0, Magic);
    }

    private static bool IsUnavailable(Exception error) => error is IOException
        or UnauthorizedAccessException or ObjectDisposedException or ArgumentException or InvalidOperationException
        or WaitHandleCannotBeOpenedException or NotSupportedException;

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Exchange(ref _view, null)?.Dispose();
        Interlocked.Exchange(ref _memory, null)?.Dispose();
        Interlocked.Exchange(ref _gate, null)?.Dispose();
    }
}
