using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Funkcje przeniesione z ChlebeekClicker: podwójne kliknięcie, jitter, pauza
// przy kucaniu, Auto Guard, spam spacji, zamrożenie okna i lag switch.
// Do tego ustawienia overlay/akcentu z zakładki Ustawienia.
public partial class MainWindow
{
    // Jitter, spam spacji i lag switch są schowane w GUI i wyłączone (decyzja z 29.10.2026).
    // Ich zapisane ustawienia zostają w pliku — true przywraca sekcje bez utraty konfiguracji.
    internal static readonly bool ShowHiddenPvpFeatures = false;

    private const int VK_SPACE = 0x20;

    private readonly HoldKeyRepeater _spaceRepeater = new(VK_SPACE);
    private readonly GameThreadFreezer _gameFreezer = new();
    private DoubleClickRunner? _leftDoubleClick;
    private DoubleClickRunner? _rightDoubleClick;
    private LagSwitchClient? _lagSwitch;
    private volatile int _extrasTargetProcessId;
    private volatile int _leftSneakPauseKey;
    private bool _autoGuardRuntimeEnabled;
    private bool _autoGuardBindWasDown;
    private bool _freezeBindWasDown;
    private bool _freezeActive;
    private bool _lagBindWasDown;
    private bool _lagRequested;
    private DateTime _lagRequestedAtUtc;
    private bool _syncingExtrasUi;
    private DateTime _nextExtrasUiRefreshUtc = DateTime.MinValue;

    private void InitializePvpExtras()
    {
        _leftDoubleClick = new DoubleClickRunner(
            leftButton: true,
            () => _physicalMouseTracker.IsButtonDown(VkLeftButton),
            () => CanSendDoubleClick(isLeft: true));
        _rightDoubleClick = new DoubleClickRunner(
            leftButton: false,
            () => _physicalMouseTracker.IsButtonDown(VkRightButton),
            () => CanSendDoubleClick(isLeft: false));

        // A frozen game thread or a held network queue must never outlive the
        // helper, also after a crash in the UI thread.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseHeldGameResources();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseHeldGameResources();
        Dispatcher.UnhandledException += (_, _) => ReleaseHeldGameResources();

        ApplyHiddenPvpFeatures();
        RefreshPvpExtrasUi(animate: false);
    }

    // Chowa sekcje jitter, spam spacji i lag switch (razem z ich separatorami) — patrz ShowHiddenPvpFeatures.
    private void ApplyHiddenPvpFeatures()
    {
        Visibility visibility = ShowHiddenPvpFeatures ? Visibility.Visible : Visibility.Collapsed;
        foreach (UIElement element in new UIElement[]
        {
            ChkAutoLeftJitter, PanelAutoLeftJitter, DividerAfterJitter, RowSpaceSpam, PanelSpaceSpamContent, DividerAfterSpaceSpam,
            DividerBeforeLagSwitch, RowLagSwitch, PanelLagSwitchContent
        })
        {
            element.Visibility = visibility;
        }
    }

    // ===== Ustawienia =====

    private void NormalizePvpExtrasSettings()
    {
        if (_settings.AutoLeftDoubleClickMode)
        {
            _settings.AutoLeftComboMode = false;
            _settings.AutoLeftHoldBindMode = false;
            _settings.AutoLeftPhysicalToggleMode = false;
        }
        if (_settings.AutoRightDoubleClickMode)
        {
            _settings.AutoRightComboMode = false;
            _settings.AutoRightHoldBindMode = false;
            _settings.AutoRightPhysicalToggleMode = false;
        }
        _settings.AutoLeftDoubleClickDelayMs = Math.Clamp(_settings.AutoLeftDoubleClickDelayMs, 0, 200);
        _settings.AutoRightDoubleClickDelayMs = Math.Clamp(_settings.AutoRightDoubleClickDelayMs, 0, 200);
        _settings.AutoLeftJitterChance = Math.Clamp(_settings.AutoLeftJitterChance, 0, 100);
        _settings.AutoLeftJitterDispersion = Math.Clamp(_settings.AutoLeftJitterDispersion, 1, 10);
        _settings.AutoLeftSneakKey = NormalizeControlKey(_settings.AutoLeftSneakKey, "LeftShift");
        _settings.AutoGuardBind ??= string.Empty;
        _settings.AutoGuardIntervalMs = Math.Clamp(_settings.AutoGuardIntervalMs, 50, 500);
        _settings.SpaceSpamBind ??= string.Empty;
        _settings.SpaceSpamIntervalMs = Math.Clamp(_settings.SpaceSpamIntervalMs, 20, 300);
        _settings.FreezeWindowBind ??= string.Empty;
        _settings.FreezeWindowMaxSeconds = Math.Clamp(_settings.FreezeWindowMaxSeconds, 1, 30);
        _settings.LagSwitchBind ??= string.Empty;
        _settings.LagSwitchMaxSeconds = Math.Clamp(_settings.LagSwitchMaxSeconds, 1, 30);
        _settings.OverlayCorner = Enum.TryParse(_settings.OverlayCorner, true, out OverlayCorner corner)
            ? corner.ToString()
            : nameof(OverlayCorner.BottomRight);
        _settings.GlassAccent = string.IsNullOrWhiteSpace(_settings.GlassAccent)
            ? "koral"
            : GlassAccents.Normalize(_settings.GlassAccent);
    }

    private void LoadPvpExtrasToUi()
    {
        _syncingExtrasUi = true;
        try
        {
            SliderAutoLeftDoubleClickMs.Value = _settings.AutoLeftDoubleClickDelayMs;
            SliderAutoRightDoubleClickMs.Value = _settings.AutoRightDoubleClickDelayMs;
            ChkAutoLeftJitter.IsChecked = ShowHiddenPvpFeatures && _settings.AutoLeftJitterEnabled;
            SliderAutoLeftJitterChance.Value = _settings.AutoLeftJitterChance;
            SliderAutoLeftJitterDispersion.Value = _settings.AutoLeftJitterDispersion;
            ChkAutoLeftSneakPause.IsChecked = _settings.AutoLeftSneakPauseEnabled;
            TxtAutoLeftSneakKey.Text = _settings.AutoLeftSneakKey;

            ChkAutoGuardEnabled.IsChecked = _settings.AutoGuardEnabled;
            TxtAutoGuardBind.Text = _settings.AutoGuardBind;
            SliderAutoGuardMs.Value = _settings.AutoGuardIntervalMs;
            ChkSpaceSpamEnabled.IsChecked = ShowHiddenPvpFeatures && _settings.SpaceSpamEnabled;
            TxtSpaceSpamBind.Text = _settings.SpaceSpamBind;
            SliderSpaceSpamMs.Value = _settings.SpaceSpamIntervalMs;
            ChkFreezeWindowEnabled.IsChecked = _settings.FreezeWindowEnabled;
            TxtFreezeWindowBind.Text = _settings.FreezeWindowBind;
            SliderFreezeWindowMaxSeconds.Value = _settings.FreezeWindowMaxSeconds;
            ChkLagSwitchEnabled.IsChecked = ShowHiddenPvpFeatures && _settings.LagSwitchEnabled;
            TxtLagSwitchBind.Text = _settings.LagSwitchBind;
            SliderLagSwitchMaxSeconds.Value = _settings.LagSwitchMaxSeconds;

            RadioButton cornerButton = _settings.OverlayCorner switch
            {
                nameof(OverlayCorner.TopLeft) => RbOverlayTopLeft,
                nameof(OverlayCorner.TopRight) => RbOverlayTopRight,
                nameof(OverlayCorner.BottomLeft) => RbOverlayBottomLeft,
                _ => RbOverlayBottomRight
            };
            cornerButton.IsChecked = true;

            RadioButton accentButton = _settings.GlassAccent switch
            {
                "iris" => RbAccentIris,
                "ocean" => RbAccentOcean,
                _ => RbAccentKoral
            };
            accentButton.IsChecked = true;
            GlassAccents.Apply(_settings.GlassAccent);
        }
        finally
        {
            _syncingExtrasUi = false;
        }
    }

    private void ReadPvpExtrasFromUi()
    {
        _settings.AutoLeftDoubleClickMode = GetSelectedMode(CbAutoLeftMode) == "DoubleClick";
        _settings.AutoRightDoubleClickMode = GetSelectedMode(CbAutoRightMode) == "DoubleClick";
        _settings.AutoLeftDoubleClickDelayMs = ReadSlider(SliderAutoLeftDoubleClickMs);
        _settings.AutoRightDoubleClickDelayMs = ReadSlider(SliderAutoRightDoubleClickMs);
        if (ShowHiddenPvpFeatures)
            _settings.AutoLeftJitterEnabled = ChkAutoLeftJitter.IsChecked == true;
        _settings.AutoLeftJitterChance = ReadSlider(SliderAutoLeftJitterChance);
        _settings.AutoLeftJitterDispersion = ReadSlider(SliderAutoLeftJitterDispersion);
        _settings.AutoLeftSneakPauseEnabled = ChkAutoLeftSneakPause.IsChecked == true;
        _settings.AutoLeftSneakKey = NormalizeControlKey(TxtAutoLeftSneakKey.Text, "LeftShift");
        _settings.AutoGuardEnabled = ChkAutoGuardEnabled.IsChecked == true;
        _settings.AutoGuardBind = TxtAutoGuardBind.Text.Trim();
        _settings.AutoGuardIntervalMs = ReadSlider(SliderAutoGuardMs);
        if (ShowHiddenPvpFeatures)
            _settings.SpaceSpamEnabled = ChkSpaceSpamEnabled.IsChecked == true;
        _settings.SpaceSpamBind = TxtSpaceSpamBind.Text.Trim();
        _settings.SpaceSpamIntervalMs = ReadSlider(SliderSpaceSpamMs);
        _settings.FreezeWindowEnabled = ChkFreezeWindowEnabled.IsChecked == true;
        _settings.FreezeWindowBind = TxtFreezeWindowBind.Text.Trim();
        _settings.FreezeWindowMaxSeconds = ReadSlider(SliderFreezeWindowMaxSeconds);
        if (ShowHiddenPvpFeatures)
            _settings.LagSwitchEnabled = ChkLagSwitchEnabled.IsChecked == true;
        _settings.LagSwitchBind = TxtLagSwitchBind.Text.Trim();
        _settings.LagSwitchMaxSeconds = ReadSlider(SliderLagSwitchMaxSeconds);
    }

    private static int ReadSlider(Slider slider) => (int)Math.Round(slider.Value);

    // ===== Zdarzenia UI =====

    private void PvpExtrasCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi || _syncingExtrasUi)
            return;

        if (ReferenceEquals(sender, ChkAutoGuardEnabled) && ChkAutoGuardEnabled.IsChecked != true)
            _autoGuardRuntimeEnabled = false;
        if (ReferenceEquals(sender, ChkSpaceSpamEnabled) && ChkSpaceSpamEnabled.IsChecked != true)
            _spaceRepeater.Update(false, 0);
        if (ReferenceEquals(sender, ChkFreezeWindowEnabled) && ChkFreezeWindowEnabled.IsChecked != true)
            ResumeFrozenGame(string.Empty);
        if (ReferenceEquals(sender, ChkLagSwitchEnabled) && ChkLagSwitchEnabled.IsChecked != true)
            ReleaseLagSwitch(string.Empty);

        RefreshPvpExtrasUi(animate: true);
        MarkDirty();
    }

    private void PvpExtrasSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingUi || _syncingExtrasUi || !IsInitialized)
            return;

        RefreshPvpExtrasUi(animate: false);
        MarkDirty();
    }

    private void OverlayCorner_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string corner })
            return;

        _settings.OverlayCorner = corner;
        UpdateOverlayPreview();
        _lastOverlayLayoutRefreshUtc = DateTime.MinValue;
        if (!_isLoadingUi && !_syncingExtrasUi)
        {
            MarkDirty();
            RefreshOverlayHud(DateTime.UtcNow);
        }
    }

    private void AccentSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingExtrasUi || sender is not RadioButton { Tag: string accent })
            return;

        GlassAccents.Apply(accent);
        _settings.GlassAccent = GlassAccents.Normalize(accent);
        RefreshGlassHotbar();
        _nextRuntimeUiRefreshUtc = DateTime.MinValue;
        if (!_isLoadingUi)
            MarkDirty();
    }

    private void BtnWhatsNew_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PvpStartupNoticeWindow(
            isFirstRun: false,
            _currentAppVersion,
            string.Empty,
            requireConfirmation: false)
        {
            Owner = this
        };
        _ = dialog.ShowDialog();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            UpdateStatusBar($"Nie otwarto strony: {exception.Message}", "Red");
        }
        e.Handled = true;
    }

    // ===== Widok =====

    private void RefreshPvpExtrasUi(bool animate)
    {
        if (TxtAutoGuardMs == null)
            return;

        animate = animate && IsLoaded;
        TxtAutoLeftDoubleClickMs.Text = $"{ReadSlider(SliderAutoLeftDoubleClickMs)} ms";
        TxtAutoRightDoubleClickMs.Text = $"{ReadSlider(SliderAutoRightDoubleClickMs)} ms";
        TxtAutoLeftDoubleClickFlow.Text = $"Twój LPM → {ReadSlider(SliderAutoLeftDoubleClickMs)} ms → drugi LPM, trzymany razem z Twoim.";
        TxtAutoRightDoubleClickFlow.Text = $"Twój PPM → {ReadSlider(SliderAutoRightDoubleClickMs)} ms → drugi PPM, trzymany razem z Twoim.";
        TxtAutoLeftJitterChance.Text = $"{ReadSlider(SliderAutoLeftJitterChance)}%";
        TxtAutoLeftJitterDispersion.Text = $"±{ReadSlider(SliderAutoLeftJitterDispersion)} px";
        TxtAutoGuardMs.Text = $"{ReadSlider(SliderAutoGuardMs)} ms";
        TxtSpaceSpamMs.Text = $"{ReadSlider(SliderSpaceSpamMs)} ms";
        TxtSpaceSpamSub.Text = $"Spacja co {ReadSlider(SliderSpaceSpamMs)} ms.";
        TxtFreezeWindowMaxSeconds.Text = $"{ReadSlider(SliderFreezeWindowMaxSeconds)} s";
        TxtLagSwitchMaxSeconds.Text = $"{ReadSlider(SliderLagSwitchMaxSeconds)} s";

        bool leftDouble = GetSelectedMode(CbAutoLeftMode) == "DoubleClick";
        bool rightDouble = GetSelectedMode(CbAutoRightMode) == "DoubleClick";
        GlassMotion.SetExpanded(PanelAutoLeftDoubleClick, leftDouble, animate);
        GlassMotion.SetExpanded(PanelAutoLeftCps, !leftDouble, animate);
        GlassMotion.SetExpanded(PanelAutoRightDoubleClick, rightDouble, animate);
        GlassMotion.SetExpanded(PanelAutoRightCps, !rightDouble, animate);
        GlassMotion.SetExpanded(PanelAutoLeftJitter, ChkAutoLeftJitter.IsChecked == true, animate);
        GlassMotion.SetExpanded(PanelAutoLeftSneak, ChkAutoLeftSneakPause.IsChecked == true, animate);
        GlassMotion.SetExpanded(PanelAutoGuardContent, ChkAutoGuardEnabled.IsChecked == true, animate);
        GlassMotion.SetExpanded(PanelSpaceSpamContent, ChkSpaceSpamEnabled.IsChecked == true, animate);
        GlassMotion.SetExpanded(PanelFreezeWindowContent, ChkFreezeWindowEnabled.IsChecked == true, animate);
        GlassMotion.SetExpanded(PanelLagSwitchContent, ChkLagSwitchEnabled.IsChecked == true, animate);
        GlassMotion.SetExpanded(PanelOverlayPosition, ChkOverlayEnabled.IsChecked == true, animate);
        UpdateOverlayPreview();
        RefreshPvpExtrasStatus();
    }

    private void UpdateOverlayPreview()
    {
        if (OverlayPreviewChip == null)
            return;

        OverlayCorner corner = GetOverlayCorner();
        OverlayPreviewChip.HorizontalAlignment = corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        OverlayPreviewChip.VerticalAlignment = corner is OverlayCorner.TopLeft or OverlayCorner.TopRight
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
    }

    private OverlayCorner GetOverlayCorner() =>
        Enum.TryParse(_settings.OverlayCorner, true, out OverlayCorner corner) ? corner : OverlayCorner.BottomRight;

    private void RefreshPvpExtrasStatus()
    {
        string guardState = ChkAutoGuardEnabled.IsChecked != true || !_autoGuardRuntimeEnabled ? "OFF" : "ON";
        TxtAutoGuardState.Text = guardState;
        ApplyStatePill(PillAutoGuardState, TxtAutoGuardState, guardState);

        if (!LagSwitchClient.AreDriverFilesPresent(out string missing))
        {
            TxtLagSwitchPill.Text = "Brak WinDivert";
            TxtLagSwitchStatus.Text = missing + " Antywirus mógł je usunąć.";
            ApplyStatePill(PillLagSwitch, TxtLagSwitchPill, "PAUZA");
            return;
        }

        LagSwitchState state = _lagSwitch?.State ?? LagSwitchState.Idle;
        (string pill, string pillState) = state switch
        {
            LagSwitchState.Holding => ("Wstrzymuje pakiety", "ON"),
            LagSwitchState.Ready => ("Pomocnik gotowy", "GOTOWY"),
            LagSwitchState.Starting => ("Czeka na UAC", "PAUZA"),
            LagSwitchState.Failed or LagSwitchState.Unavailable => ("Błąd", "PAUZA"),
            _ => ("Wymaga zgody administratora", "PAUZA")
        };
        TxtLagSwitchPill.Text = pill;
        ApplyStatePill(PillLagSwitch, TxtLagSwitchPill, pillState);
        string message = _lagSwitch?.LastMessage ?? string.Empty;
        TxtLagSwitchStatus.Text = string.IsNullOrWhiteSpace(message) ? "okno UAC przy pierwszym użyciu" : message;
    }

    // ===== Działanie w pętli =====

    private bool IsDoubleClickArmed() =>
        _leftDoubleClick?.IsArmed == true || _rightDoubleClick?.IsArmed == true;

    private bool CanSendDoubleClick(bool isLeft)
    {
        int targetProcessId = _extrasTargetProcessId;
        if (!GameWindowFinder.IsProcessForeground(targetProcessId))
            return false;
        int sneakKey = _leftSneakPauseKey;
        if (isLeft && sneakKey != 0 && IsVirtualKeyDown(sneakKey))
            return false;
        return !ShouldSuppressScheduledClick();
    }

    private bool HasActivePvpExtrasWork()
    {
        return _autoGuardRuntimeEnabled
            || _spaceRepeater.IsRunning
            || _gameFreezer.IsFrozen
            || _freezeActive
            || _lagRequested
            || IsDoubleClickArmed();
    }

    private bool IsPvpExtrasBindDown()
    {
        return (ChkAutoGuardEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtAutoGuardBind.Text))
            || (ChkSpaceSpamEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtSpaceSpamBind.Text))
            || (ChkFreezeWindowEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtFreezeWindowBind.Text))
            || (ChkLagSwitchEnabled.IsChecked == true && IsConfiguredBindKeyDown(TxtLagSwitchBind.Text));
    }

    private bool PvpExtrasNeedCursorProbe()
    {
        return _autoGuardRuntimeEnabled
            || _spaceRepeater.IsRunning
            || IsDoubleClickArmed()
            || IsPvpExtrasBindDown();
    }

    private void HandlePvpExtras(DateTime now, bool targetFocused, bool cursorVisible, bool blockedByCursor)
    {
        _extrasTargetProcessId = targetFocused ? _targetGameProcessId : 0;
        bool inputAllowed = targetFocused && !cursorVisible;

        // Jitter i pauza przy kucaniu (tylko AUTO LPM).
        int sneakKey = ChkAutoLeftSneakPause.IsChecked == true && TryGetVirtualKey(TxtAutoLeftSneakKey.Text, out int key) ? key : 0;
        _leftSneakPauseKey = sneakKey;
        _autoClickScheduler.ConfigureLeftExtras(
            ChkAutoLeftJitter.IsChecked == true ? ReadSlider(SliderAutoLeftJitterChance) : 0,
            ReadSlider(SliderAutoLeftJitterDispersion),
            sneakKey);

        // Podwójne kliknięcie: bind clickera uzbraja tryb, działa na fizyczne kliknięcia.
        bool otherWorkflow = IsAutoArmorRunning || IsAppleCutterRunning || IsPvpQuickActionRunning;
        _leftDoubleClick?.Update(
            targetFocused && !blockedByCursor && !otherWorkflow
                && GetSelectedMode(CbAutoLeftMode) == "DoubleClick"
                && _autoLeftRuntimeEnabled && ChkAutoLeftEnabled.IsChecked == true,
            ReadSlider(SliderAutoLeftDoubleClickMs));
        _rightDoubleClick?.Update(
            targetFocused && !blockedByCursor && !otherWorkflow
                && GetSelectedMode(CbAutoRightMode) == "DoubleClick"
                && _autoRightRuntimeEnabled && ChkAutoRightEnabled.IsChecked == true,
            ReadSlider(SliderAutoRightDoubleClickMs));

        // Auto Guard: bind przełącza, harmonogram tapuje tylko przy klikającym AUTO LPM.
        bool guardModule = ChkAutoGuardEnabled.IsChecked == true;
        if (IsBindPressed(TxtAutoGuardBind.Text, ref _autoGuardBindWasDown) && guardModule && inputAllowed)
        {
            _autoGuardRuntimeEnabled = !_autoGuardRuntimeEnabled;
            UpdateStatusBar(
                _autoGuardRuntimeEnabled ? "AUTO GUARD: włączony — tapuje PPM, gdy AUTO LPM klika." : "AUTO GUARD: wyłączony.",
                _autoGuardRuntimeEnabled ? "Green" : "Orange");
        }
        if (!guardModule)
            _autoGuardRuntimeEnabled = false;
        _autoClickScheduler.UpdateAutoGuard(
            _autoGuardRuntimeEnabled && targetFocused && !blockedByCursor && !otherWorkflow
                && GetSelectedMode(CbAutoLeftMode) != "DoubleClick",
            ReadSlider(SliderAutoGuardMs));

        // Spam spacji: tylko podczas trzymania binda.
        _spaceRepeater.Update(
            ChkSpaceSpamEnabled.IsChecked == true && inputAllowed && IsConfiguredBindKeyDown(TxtSpaceSpamBind.Text),
            ReadSlider(SliderSpaceSpamMs));

        HandleFreezeWindow(targetFocused, inputAllowed);
        HandleLagSwitch(now, targetFocused, inputAllowed);

        if (IsVisible && now >= _nextExtrasUiRefreshUtc)
        {
            _nextExtrasUiRefreshUtc = now.AddMilliseconds(250);
            RefreshPvpExtrasStatus();
        }
    }

    private void HandleFreezeWindow(bool targetFocused, bool inputAllowed)
    {
        bool down = IsConfiguredBindKeyDown(TxtFreezeWindowBind.Text);
        bool pressed = down && !_freezeBindWasDown;
        _freezeBindWasDown = down;

        if (_freezeActive && !_gameFreezer.IsFrozen)
        {
            // Wątek wznowił limit czasu (osobny timer, niezależny od UI).
            _freezeActive = false;
            UpdateStatusBar($"ZAMROŻENIE: limit {ReadSlider(SliderFreezeWindowMaxSeconds)} s — gra wznowiona.", "Orange");
        }

        if (_gameFreezer.IsFrozen)
        {
            if (!down || !targetFocused || ChkFreezeWindowEnabled.IsChecked != true)
                ResumeFrozenGame("ZAMROŻENIE: gra wznowiona.");
            return;
        }

        if (pressed && inputAllowed && ChkFreezeWindowEnabled.IsChecked == true)
        {
            TimeSpan limit = TimeSpan.FromSeconds(ReadSlider(SliderFreezeWindowMaxSeconds));
            if (_gameFreezer.Freeze(_targetGameWindowHandle, limit))
            {
                _freezeActive = true;
                UpdateStatusBar("ZAMROŻENIE: wątek gry wstrzymany — puść bind, aby wznowić.", "Orange");
            }
            else
            {
                UpdateStatusBar("ZAMROŻENIE: nie udało się wstrzymać wątku gry.", "Red");
            }
        }
    }

    private void HandleLagSwitch(DateTime now, bool targetFocused, bool inputAllowed)
    {
        bool down = IsConfiguredBindKeyDown(TxtLagSwitchBind.Text);
        bool pressed = down && !_lagBindWasDown;
        _lagBindWasDown = down;
        TimeSpan limit = TimeSpan.FromSeconds(ReadSlider(SliderLagSwitchMaxSeconds));

        if (_lagRequested)
        {
            if (!down || !targetFocused || ChkLagSwitchEnabled.IsChecked != true)
                ReleaseLagSwitch("LAG SWITCH: pakiety puszczone.");
            else if (now - _lagRequestedAtUtc >= limit)
                ReleaseLagSwitch($"LAG SWITCH: limit {ReadSlider(SliderLagSwitchMaxSeconds)} s — pakiety puszczone.");
            return;
        }

        if (!pressed || !inputAllowed || ChkLagSwitchEnabled.IsChecked != true)
            return;

        if (!LagSwitchClient.AreDriverFilesPresent(out string missing))
        {
            UpdateStatusBar("LAG SWITCH: " + missing, "Red");
            return;
        }

        uint processId = (uint)_targetGameProcessId;
        if (processId == 0)
            return;

        _lagSwitch ??= new LagSwitchClient();
        _lagSwitch.SetHeld(true, (int)processId, limit);
        _lagRequested = true;
        _lagRequestedAtUtc = now;
        UpdateStatusBar(
            _lagSwitch.IsHelperConnected
                ? "LAG SWITCH: wstrzymuję pakiety — puść bind, aby je wysłać."
                : "LAG SWITCH: uruchamiam pomocnika — potwierdź okno UAC, potem użyj binda ponownie.",
            "Orange");
    }

    private void ResumeFrozenGame(string message)
    {
        bool wasFrozen = _gameFreezer.IsFrozen;
        _gameFreezer.Resume();
        _freezeActive = false;
        if (wasFrozen && !string.IsNullOrEmpty(message))
            UpdateStatusBar(message, "Green");
    }

    private void ReleaseLagSwitch(string message)
    {
        if (!_lagRequested)
            return;
        _lagRequested = false;
        _lagSwitch?.SetHeld(false, 0, TimeSpan.Zero);
        if (!string.IsNullOrEmpty(message))
            UpdateStatusBar(message, "Green");
    }

    // Wszystko, co trzyma grę lub sieć, puszczamy przy utracie fokusu i wyjściu.
    private void StopPvpExtras()
    {
        _leftDoubleClick?.Update(false, 0);
        _rightDoubleClick?.Update(false, 0);
        _autoClickScheduler.UpdateAutoGuard(false, ReadSlider(SliderAutoGuardMs));
        _spaceRepeater.Update(false, 0);
        ResumeFrozenGame(string.Empty);
        ReleaseLagSwitch(string.Empty);
    }

    private void ReleaseHeldGameResources()
    {
        // Może być wołane z dowolnego wątku: tylko operacje bezpieczne wątkowo.
        _gameFreezer.Resume();
        _spaceRepeater.Update(false, 0);
        _leftDoubleClick?.Update(false, 0);
        _rightDoubleClick?.Update(false, 0);
        _lagSwitch?.SetHeld(false, 0, TimeSpan.Zero);
    }

    private void DisposePvpExtras()
    {
        _gameFreezer.Dispose();
        _spaceRepeater.Dispose();
        _leftDoubleClick?.Dispose();
        _rightDoubleClick?.Dispose();
        _lagSwitch?.Dispose();
        _lagSwitch = null;
    }

    private void AddPvpExtrasOverlayEntries(List<OverlayHudEntry> entries)
    {
        if (_leftDoubleClick?.IsArmed == true)
            entries.Add(new OverlayHudEntry("AUTO LPM", $"PODWÓJNE • +{ReadSlider(SliderAutoLeftDoubleClickMs)} ms", OverlayHudTone.Active));
        if (_rightDoubleClick?.IsArmed == true)
            entries.Add(new OverlayHudEntry("AUTO PPM", $"PODWÓJNE • +{ReadSlider(SliderAutoRightDoubleClickMs)} ms", OverlayHudTone.Active));
        if (_autoGuardRuntimeEnabled)
            entries.Add(new OverlayHudEntry("AUTO GUARD", $"ON • PPM co {ReadSlider(SliderAutoGuardMs)} ms", OverlayHudTone.Active));
        if (_spaceRepeater.IsRunning)
            entries.Add(new OverlayHudEntry("SPACJA", $"co {ReadSlider(SliderSpaceSpamMs)} ms", OverlayHudTone.Active));
        if (_gameFreezer.IsFrozen)
            entries.Add(new OverlayHudEntry("ZAMROŻENIE", "gra wstrzymana", OverlayHudTone.Warning));
        if (_lagRequested)
            entries.Add(new OverlayHudEntry("LAG SWITCH", _lagSwitch?.State == LagSwitchState.Holding ? "pakiety wstrzymane" : "uruchamianie…", OverlayHudTone.Warning));
    }
}
