using System;
using System.Threading;

namespace MinecraftHelper.Services;

// DoubleClick mode: while armed, every physical press of the button gets one
// extra injected press after a short delay. The injected button stays down for
// as long as the player holds the physical one and is always released.
internal sealed class DoubleClickRunner : IDisposable
{
    private readonly object _sync = new();
    private readonly bool _leftButton;
    private readonly Func<bool> _isPhysicalDown;
    private readonly Func<bool> _canSend;
    private readonly Func<bool, bool> _sendButton;
    private Thread? _thread;
    private volatile bool _armed;
    private volatile int _delayMs = 50;
    private bool _disposed;

    public DoubleClickRunner(bool leftButton, Func<bool> isPhysicalDown, Func<bool> canSend)
        : this(leftButton, isPhysicalDown, canSend, down => NativeInput.SendMouseButton(leftButton, down))
    {
    }

    internal DoubleClickRunner(bool leftButton, Func<bool> isPhysicalDown, Func<bool> canSend, Func<bool, bool> sendButton)
    {
        _leftButton = leftButton;
        _isPhysicalDown = isPhysicalDown ?? throw new ArgumentNullException(nameof(isPhysicalDown));
        _canSend = canSend ?? throw new ArgumentNullException(nameof(canSend));
        _sendButton = sendButton ?? throw new ArgumentNullException(nameof(sendButton));
    }

    public bool IsArmed => _armed;

    public void Update(bool armed, int delayMs)
    {
        _delayMs = Math.Clamp(delayMs, 0, 1000);
        Thread? stopping = null;
        lock (_sync)
        {
            if (_disposed)
                armed = false;
            if (armed == _armed)
                return;

            _armed = armed;
            if (armed)
            {
                _thread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = _leftButton ? "Auto Clicker double click LPM" : "Auto Clicker double click PPM"
                };
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
        Update(false, _delayMs);
    }

    // One extra click for one physical press: wait, skip if input is not
    // allowed (GUI open, no focus), press, follow the physical hold, release.
    internal static bool RunExtraClick(
        int delayMs,
        Func<bool> isPhysicalDown,
        Func<bool> canSend,
        Func<bool, bool> sendButton,
        Action<int> sleep,
        Func<bool> cancelled)
    {
        if (delayMs > 0)
            sleep(delayMs);
        if (cancelled() || !canSend())
            return false;
        if (!sendButton(true))
        {
            _ = sendButton(false);
            return false;
        }

        try
        {
            while (!cancelled() && isPhysicalDown())
                sleep(1);
        }
        finally
        {
            _ = sendButton(false);
        }
        return true;
    }

    private void WorkerLoop()
    {
        // 1 ms polling and an accurate delay need the 1 ms system timer.
        bool timerResolution = timeBeginPeriod(1) == 0;
        try
        {
            bool wasDown = _isPhysicalDown();
            while (_armed)
            {
                Thread.Sleep(1);
                bool down = _isPhysicalDown();
                if (down && !wasDown)
                {
                    _ = RunExtraClick(_delayMs, _isPhysicalDown, _canSend, _sendButton, Thread.Sleep, () => !_armed);
                    down = _isPhysicalDown();
                }
                wasDown = down;
            }
        }
        finally
        {
            if (timerResolution)
                _ = timeEndPeriod(1);
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint periodMilliseconds);

    [System.Runtime.InteropServices.DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint periodMilliseconds);
}
