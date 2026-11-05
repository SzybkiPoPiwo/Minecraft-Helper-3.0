using System;
using System.Windows;
using MinecraftHelper.Models;

namespace MinecraftHelper;

public partial class MainWindow
{
    private bool _idleRuntimeSuspended;
    private bool _backgroundIdleRuntimeSuspended;

    // This gate deliberately precedes target restoration, IsWindow and
    // GetForegroundWindow. A hidden, inactive helper needs only key reads.
    // Activation exits here and must pass the ordinary fresh focus/GUI guards.
    private bool TryHandleBackgroundIdleRuntimeTick(DateTime now)
    {
        _backgroundIdleRuntimeSuspended = false;
        if (IsVisible && WindowState != WindowState.Minimized)
            return false;

        if (!TryHandleIdleRuntimeTick(now, targetFocused: false, allowUnknownFocus: true))
            return false;

        _backgroundIdleRuntimeSuspended = true;
        // Unknown, not permission to send input. Never reuse pre-idle focus.
        _isTargetFocused = false;
        return true;
    }

    // A checked module is only configured, not running. While waiting for a
    // bind, do not inspect/render the game's cursor or maintain a hidden HUD.
    // Keep polling keys so activation does not acquire an idle wake-up delay.
    private bool TryHandleIdleRuntimeTick(DateTime now, bool targetFocused, bool allowUnknownFocus = false)
    {
        if ((!targetFocused && !allowUnknownFocus) || HasActiveRuntimeWork() || HasActivationBindDown())
        {
            if (_idleRuntimeSuspended)
            {
                // The first activation must use a fresh cursor observation,
                // never the cached result from before the idle period.
                _cachedCursorVisible = false;
                _nextCursorProbeUtc = DateTime.MinValue;
                _idleRuntimeSuspended = false;
            }
            return false;
        }

        if (!_idleRuntimeSuspended)
        {
            _autoClickScheduler.Stop();
            SetAutoLeftDabHold(false);
            if (_physicalMouseTracker.IsMonitoring)
                _physicalMouseTracker.Stop();
            _nextPhysicalMouseTrackerRetryUtc = DateTime.MinValue;
            _physicalMouseTrackerWarningShown = false;
            _cachedCursorVisible = false;
            _nextCursorProbeUtc = DateTime.MinValue;
            _idleRuntimeSuspended = true;
        }

        // Consume releases just as in the normal loop. Otherwise a second press
        // of the same key could be lost after returning from a GUI/idle period.
        SynchronizeBindStatesWhileGameIsInactive();
        if (IsVisible && WindowState != WindowState.Minimized)
            RefreshClickerStateUi(now, blockedByCursor: false, targetFocused);

        // An existing "BINDY completed" tile may need to expire. An empty HUD
        // needs no allocations, native window calls or repeated Hide calls.
        if (_lastOverlaySignature.Length != 0)
            RefreshOverlayHud(now);
        return true;
    }

    private bool HasActiveRuntimeWork()
    {
        return _autoLeftRuntimeEnabled
            || _autoRightRuntimeEnabled
            || IsAutoArmorRunning
            || _autoArmorCalibrationPending
            || IsAppleCutterRunning
            || IsPvpQuickActionRunning
            || _bindyStage != BindyStage.None
            || _suppressBindToggleUntilRelease
            || HasActivePvpExtrasWork();
    }

    private bool HasActivationBindDown()
    {
        if ((ChkAutoLeftEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtAutoLeftKey.Text))
            || (ChkAutoRightEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtAutoRightKey.Text))
            || (ChkAppleCutterEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtAppleCutterBind.Text))
            || (ChkAutoArmorEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtAutoArmorBind.Text))
            || IsConfiguredBindKeyDown(TxtPvpAutoBlockBind.Text)
            || IsConfiguredBindKeyDown(TxtPvpSnowballBind.Text)
            || IsConfiguredBindKeyDown(TxtPvpPearlBind.Text)
            || IsConfiguredBindKeyDown(TxtPvpRodBind.Text)
            || IsPvpExtrasBindDown())
        {
            return true;
        }

        if (ChkBindyEnabled.IsChecked == true)
        {
            foreach (BindyEntry entry in _settings.BindyEntries)
            {
                if (entry.Enabled && !string.IsNullOrWhiteSpace(entry.Command)
                    && IsConfiguredBindKeyDown(entry.Key))
                    return true;
            }
        }
        return false;
    }
}
