using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MinecraftHelper.Services;

// Spam spacji: while running, presses the key, waits, releases it, waits.
// Stop() always leaves the key released, even mid-press.
internal sealed class HoldKeyRepeater : IDisposable
{
    private readonly object _sync = new();
    private readonly int _virtualKey;
    private readonly Func<int, bool, bool> _sendKey;
    private Thread? _thread;
    private volatile bool _running;
    private volatile int _intervalMs = 67;
    private bool _disposed;

    public HoldKeyRepeater(int virtualKey)
        : this(virtualKey, NativeInput.SendKey)
    {
    }

    internal HoldKeyRepeater(int virtualKey, Func<int, bool, bool> sendKey)
    {
        _virtualKey = virtualKey;
        _sendKey = sendKey ?? throw new ArgumentNullException(nameof(sendKey));
    }

    public bool IsRunning => _running;

    public void Update(bool running, int intervalMs)
    {
        _intervalMs = Math.Clamp(intervalMs, 10, 1000);
        Thread? stopping = null;
        lock (_sync)
        {
            if (_disposed)
                running = false;
            if (running == _running)
                return;

            _running = running;
            if (running)
            {
                _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "Auto Clicker key repeater" };
                _thread.Start();
            }
            else
            {
                stopping = _thread;
                _thread = null;
            }
        }

        if (stopping != null && stopping != Thread.CurrentThread)
            _ = stopping.Join(TimeSpan.FromMilliseconds(500));
    }

    public void Dispose()
    {
        lock (_sync)
            _disposed = true;
        Update(false, _intervalMs);
    }

    private void WorkerLoop()
    {
        bool timerResolution = timeBeginPeriod(1) == 0;
        bool down = false;
        try
        {
            while (_running)
            {
                down = _sendKey(_virtualKey, true);
                SleepWhileRunning(_intervalMs);
                _ = _sendKey(_virtualKey, false);
                down = false;
                SleepWhileRunning(_intervalMs);
            }
        }
        finally
        {
            if (down)
                _ = _sendKey(_virtualKey, false);
            if (timerResolution)
                _ = timeEndPeriod(1);
        }
    }

    // Short slices keep Stop() responsive even with long intervals.
    private void SleepWhileRunning(int milliseconds)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (_running)
        {
            long left = milliseconds - elapsed.ElapsedMilliseconds;
            if (left <= 0)
                return;
            Thread.Sleep((int)Math.Min(left, 10));
        }
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint periodMilliseconds);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint periodMilliseconds);
}
