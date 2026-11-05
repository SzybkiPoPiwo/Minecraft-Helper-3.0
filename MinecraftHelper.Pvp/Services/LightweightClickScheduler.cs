using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MinecraftHelper.Services;

internal sealed class LightweightClickScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly Random _random = new();
    private readonly Channel _left = new(leftButton: true);
    private readonly Channel _right = new(leftButton: false);
    private readonly Func<int, bool> _isPhysicalPointerDown;
    private readonly Func<bool> _shouldSuppressClick;
    private readonly Timer _guardTimer;
    private bool _disposed;
    private bool _timerResolutionActive;
    private int _jitterChance;
    private int _jitterDispersion;
    private int _leftPauseKey;
    private bool _guardEnabled;
    private int _guardIntervalMs = 160;
    private int _guardGeneration;
    private long _guardPauseUntilTimestamp;

    // Auto Guard counts AUTO LPM as "clicking" only if it really clicked recently.
    private static readonly long LeftClickRecencyTicks = Stopwatch.Frequency * 300 / 1000;
    private static readonly long GuardThrowPauseTicks = Stopwatch.Frequency * 2;
    private const int GuardIdlePollMs = 15;
    private const int VkRightButton = 0x02;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint periodMilliseconds);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint periodMilliseconds);

    public LightweightClickScheduler(
        Func<int, bool> isPhysicalPointerDown,
        Func<bool> shouldSuppressClick)
    {
        _isPhysicalPointerDown = isPhysicalPointerDown ?? throw new ArgumentNullException(nameof(isPhysicalPointerDown));
        _shouldSuppressClick = shouldSuppressClick ?? throw new ArgumentNullException(nameof(shouldSuppressClick));
        _left.Timer = new Timer(OnTimer, _left, Timeout.Infinite, Timeout.Infinite);
        _right.Timer = new Timer(OnTimer, _right, Timeout.Infinite, Timeout.Infinite);
        _guardTimer = new Timer(OnGuardTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    // Jitter and the sneak pause apply to AUTO LPM only, as in the original tool.
    public void ConfigureLeftExtras(int jitterChancePercent, int jitterDispersion, int pauseVirtualKey)
    {
        lock (_sync)
        {
            _jitterChance = Math.Clamp(jitterChancePercent, 0, 100);
            _jitterDispersion = Math.Clamp(jitterDispersion, 0, PvpInputPolicies.JitterMaxDispersion);
            _leftPauseKey = pauseVirtualKey;
        }
    }

    public void UpdateAutoGuard(bool enabled, int intervalMs)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            intervalMs = Math.Clamp(intervalMs, 20, 2000);
            if (_guardEnabled == enabled && _guardIntervalMs == intervalMs)
                return;

            _guardIntervalMs = intervalMs;
            if (_guardEnabled == enabled)
                return;

            _guardEnabled = enabled;
            _guardGeneration++;
            _guardTimer.Change(enabled ? GuardIdlePollMs : Timeout.Infinite, Timeout.Infinite);
            UpdateTimerResolution();
        }
    }

    // A pearl or snowball was just thrown: Auto Guard waits 2 s once, so the
    // throw is not cancelled by a block.
    public void PauseAutoGuardAfterThrow()
    {
        lock (_sync)
            _guardPauseUntilTimestamp = Stopwatch.GetTimestamp() + GuardThrowPauseTicks;
    }

    public void Update(
        bool leftEnabled,
        int leftMinCps,
        int leftMaxCps,
        bool rightEnabled,
        int rightMinCps,
        int rightMaxCps,
        int leftRequiredHoldKey,
        int leftRequiredPointerKey,
        int rightRequiredHoldKey,
        int rightRequiredPointerKey,
        IntPtr targetWindow)
    {
        bool startLeft;
        bool startRight;
        lock (_sync)
        {
            if (_disposed)
                return;

            startLeft = UpdateChannel(
                _left,
                leftEnabled,
                leftMinCps,
                leftMaxCps,
                leftRequiredHoldKey,
                leftRequiredPointerKey,
                targetWindow);
            startRight = UpdateChannel(
                _right,
                rightEnabled,
                rightMinCps,
                rightMaxCps,
                rightRequiredHoldKey,
                rightRequiredPointerKey,
                targetWindow);
            UpdateTimerResolution();
        }

        if (startLeft)
            OnTimer(_left);
        if (startRight)
            OnTimer(_right);
    }

    public void UpdateLeft(
        bool enabled,
        int minCps,
        int maxCps,
        int requiredHoldKey,
        int requiredPointerKey,
        IntPtr targetWindow)
    {
        bool startLeft;
        lock (_sync)
        {
            if (_disposed)
                return;

            startLeft = UpdateChannel(
                _left,
                enabled,
                minCps,
                maxCps,
                requiredHoldKey,
                requiredPointerKey,
                targetWindow);
            UpdateTimerResolution();
        }

        if (startLeft)
            OnTimer(_left);
    }

    public void Stop()
    {
        Update(false, 1, 1, false, 1, 1, 0, 0, 0, 0, IntPtr.Zero);
        UpdateAutoGuard(false, _guardIntervalMs);
    }

    public void StopLeftImmediately() => StopImmediately(_left);

    public void StopRightImmediately() => StopImmediately(_right);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            Disable(_left);
            Disable(_right);
            _guardEnabled = false;
            _guardGeneration++;
            UpdateTimerResolution();
            _left.Timer?.Dispose();
            _right.Timer?.Dispose();
            _guardTimer.Dispose();
        }
    }

    private bool UpdateChannel(
        Channel channel,
        bool enabled,
        int minCps,
        int maxCps,
        int requiredHoldKey,
        int requiredPointerKey,
        IntPtr targetWindow)
    {
        enabled = enabled && targetWindow != IntPtr.Zero;
        // Compare the game process, not the window: LWJGL recreates its window on
        // F11/fullscreen, which must neither reset the rhythm nor block clicks.
        int targetProcessId = GameWindowFinder.GetWindowProcessId(targetWindow);
        minCps = Math.Clamp(minCps, 1, 100);
        maxCps = Math.Clamp(maxCps, 1, 100);
        if (maxCps < minCps)
            (minCps, maxCps) = (maxCps, minCps);

        if (channel.Enabled == enabled
            && channel.MinCps == minCps
            && channel.MaxCps == maxCps
            && channel.RequiredHoldKey == requiredHoldKey
            && channel.RequiredPointerKey == requiredPointerKey
            && channel.TargetProcessId == targetProcessId)
        {
            return false;
        }

        channel.Generation++;
        channel.Enabled = enabled;
        channel.MinCps = minCps;
        channel.MaxCps = maxCps;
        channel.RequiredHoldKey = requiredHoldKey;
        channel.RequiredPointerKey = requiredPointerKey;
        channel.TargetProcessId = targetProcessId;
        channel.NextTimestamp = enabled ? Stopwatch.GetTimestamp() : 0;
        channel.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return enabled;
    }

    private void OnTimer(object? state)
    {
        if (state is not Channel channel)
            return;

        int generation;
        long plannedTimestamp;
        long intervalTicks;
        int requiredHoldKey;
        int requiredPointerKey;
        int targetProcessId;
        bool leftButton;
        int pauseKey;
        lock (_sync)
        {
            if (_disposed || !channel.Enabled || channel.Timer == null)
                return;

            generation = channel.Generation;
            long now = Stopwatch.GetTimestamp();
            if (channel.NextTimestamp > now)
            {
                Schedule(channel, channel.NextTimestamp, now);
                return;
            }

            int cps = channel.MinCps == channel.MaxCps
                ? channel.MinCps
                : _random.Next(channel.MinCps, channel.MaxCps + 1);
            intervalTicks = Math.Max(1, (long)Math.Round(Stopwatch.Frequency / (double)cps));
            plannedTimestamp = channel.NextTimestamp > 0 ? channel.NextTimestamp : now;
            requiredHoldKey = channel.RequiredHoldKey;
            requiredPointerKey = channel.RequiredPointerKey;
            targetProcessId = channel.TargetProcessId;
            leftButton = channel.LeftButton;
            pauseKey = leftButton ? _leftPauseKey : 0;
        }

        if ((requiredHoldKey != 0 && !IsVirtualKeyDown(requiredHoldKey))
            || (requiredPointerKey != 0 && !_isPhysicalPointerDown(requiredPointerKey)))
        {
            StopImmediately(channel);
            return;
        }

        // The UI cursor cache is intentionally not trusted here. At high CPS a
        // click can otherwise fit between opening Minecraft's GUI and the next
        // dispatcher tick. Recheck the real cursor immediately before every
        // injected click and fail closed if the guard itself cannot be read.
        // Sneak pause: the schedule keeps running, only the click is skipped.
        bool pausedByKey = pauseKey != 0 && IsVirtualKeyDown(pauseKey);
        if (!pausedByKey && GameWindowFinder.IsProcessForeground(targetProcessId))
        {
            bool suppressClick;
            try
            {
                suppressClick = _shouldSuppressClick();
            }
            catch
            {
                suppressClick = true;
            }

            if (!suppressClick)
            {
                // Stop() must be a hard boundary for quick PVP actions.  Recheck
                // the generation while holding the scheduler lock so a callback
                // that was already in flight cannot inject a late click after
                // Stop() has returned.
                lock (_sync)
                {
                    if (_disposed || !channel.Enabled || channel.Generation != generation || channel.Timer == null)
                        return;

                    if (NativeInput.SendMouseClick(leftButton, holdPulseMode: false) && leftButton)
                    {
                        channel.LastClickTimestamp = Stopwatch.GetTimestamp();
                        if (PvpInputPolicies.TryRollJitter(_random, _jitterChance, _jitterDispersion, out int deltaX, out int deltaY))
                            _ = NativeInput.SendMouseMoveRelative(deltaX, deltaY);
                    }
                }
            }
        }

        lock (_sync)
        {
            if (_disposed || !channel.Enabled || channel.Generation != generation || channel.Timer == null)
                return;

            long next = plannedTimestamp + intervalTicks;
            long now = Stopwatch.GetTimestamp();
            if (next <= now)
                next += (((now - next) / intervalTicks) + 1) * intervalTicks;
            channel.NextTimestamp = next;
            Schedule(channel, next, now);
        }
    }

    private void OnGuardTimer(object? state)
    {
        int generation;
        int intervalMs;
        bool leftClickingNow;
        long pauseUntil;
        int targetProcessId;
        lock (_sync)
        {
            if (_disposed || !_guardEnabled)
                return;

            generation = _guardGeneration;
            intervalMs = _guardIntervalMs;
            long now = Stopwatch.GetTimestamp();
            // AUTO PPM owns the right button while it runs.
            leftClickingNow = _left.Enabled
                && !_right.Enabled
                && _left.LastClickTimestamp != 0
                && now - _left.LastClickTimestamp <= LeftClickRecencyTicks;
            pauseUntil = _guardPauseUntilTimestamp;
            targetProcessId = _left.TargetProcessId;
        }

        long timestamp = Stopwatch.GetTimestamp();
        AutoGuardDecision decision = PvpInputPolicies.DecideAutoGuard(
            leftClickingNow && GameWindowFinder.IsProcessForeground(targetProcessId),
            IsVirtualKeyDown(VkRightButton),
            timestamp,
            pauseUntil);

        int nextMs = GuardIdlePollMs;
        if (decision == AutoGuardDecision.WaitAfterThrow)
        {
            nextMs = Math.Max(1, (int)Math.Ceiling((pauseUntil - timestamp) * 1000.0 / Stopwatch.Frequency));
        }
        else if (decision == AutoGuardDecision.Tap)
        {
            bool suppress;
            try
            {
                suppress = _shouldSuppressClick();
            }
            catch
            {
                suppress = true;
            }

            lock (_sync)
            {
                if (_disposed || !_guardEnabled || _guardGeneration != generation)
                    return;
                if (!suppress)
                {
                    _ = NativeInput.SendMouseClick(leftButton: false, holdPulseMode: false);
                    nextMs = intervalMs;
                }
            }
        }

        lock (_sync)
        {
            if (!_disposed && _guardEnabled && _guardGeneration == generation)
                _guardTimer.Change(nextMs, Timeout.Infinite);
        }
    }

    private void StopImmediately(Channel channel)
    {
        lock (_sync)
        {
            if (_disposed || !channel.Enabled)
                return;
            Disable(channel);
            UpdateTimerResolution();
        }
    }

    private static void Disable(Channel channel)
    {
        channel.Generation++;
        channel.Enabled = false;
        channel.RequiredHoldKey = 0;
        channel.RequiredPointerKey = 0;
        channel.TargetProcessId = 0;
        channel.NextTimestamp = 0;
        channel.LastClickTimestamp = 0;
        channel.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private static void Schedule(Channel channel, long target, long now)
    {
        int delay = Math.Max(1, (int)Math.Ceiling((target - now) * 1000.0 / Stopwatch.Frequency));
        channel.Timer?.Change(delay, Timeout.Infinite);
    }

    private void UpdateTimerResolution()
    {
        bool shouldRun = !_disposed && (_left.Enabled || _right.Enabled || _guardEnabled);
        if (shouldRun == _timerResolutionActive)
            return;

        if (shouldRun)
            _timerResolutionActive = timeBeginPeriod(1) == 0;
        else
        {
            if (_timerResolutionActive)
                _ = timeEndPeriod(1);
            _timerResolutionActive = false;
        }
    }

    private static bool IsVirtualKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private sealed class Channel
    {
        public Channel(bool leftButton) => LeftButton = leftButton;
        public bool LeftButton { get; }
        public Timer? Timer { get; set; }
        public bool Enabled { get; set; }
        public int MinCps { get; set; } = 1;
        public int MaxCps { get; set; } = 1;
        public int RequiredHoldKey { get; set; }
        public int RequiredPointerKey { get; set; }
        public int TargetProcessId { get; set; }
        public int Generation { get; set; }
        public long NextTimestamp { get; set; }
        public long LastClickTimestamp { get; set; }
    }
}

