using System;
using System.Threading;

namespace MinecraftHelper.Services;

// Owns one "held" external resource (a suspended game thread, a WinDivert
// handle) and guarantees exactly one release: on Release(), on Dispose(), or
// when the safety deadline expires. The deadline runs on a thread-pool timer,
// so it fires even if the UI thread is blocked.
internal sealed class TimedResourceHold : IDisposable
{
    private readonly object _sync = new();
    private readonly Timer _deadline;
    private Action? _release;
    private bool _disposed;

    public TimedResourceHold()
    {
        _deadline = new Timer(_ => ExpireByDeadline(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsHeld
    {
        get
        {
            lock (_sync)
                return _release != null;
        }
    }

    public bool ExpiredByDeadline { get; private set; }

    public Exception? LastReleaseError { get; private set; }

    // acquire returns the matching release action, or null when nothing was
    // acquired. An exception from acquire leaves nothing held.
    public bool TryAcquire(Func<Action?> acquire, TimeSpan maxDuration)
    {
        ArgumentNullException.ThrowIfNull(acquire);
        lock (_sync)
        {
            if (_disposed || _release != null)
                return false;

            Action? release = acquire();
            if (release == null)
                return false;

            _release = release;
            ExpiredByDeadline = false;
            long dueMilliseconds = (long)Math.Clamp(maxDuration.TotalMilliseconds, 1, int.MaxValue);
            _deadline.Change(dueMilliseconds, Timeout.Infinite);
            return true;
        }
    }

    public void Release()
    {
        Action? release;
        lock (_sync)
        {
            release = _release;
            _release = null;
            _deadline.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (release == null)
            return;

        try
        {
            release();
            LastReleaseError = null;
        }
        catch (Exception exception)
        {
            LastReleaseError = exception;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Release();
        _deadline.Dispose();
    }

    private void ExpireByDeadline()
    {
        lock (_sync)
        {
            if (_release == null)
                return;
            ExpiredByDeadline = true;
        }
        Release();
    }
}
