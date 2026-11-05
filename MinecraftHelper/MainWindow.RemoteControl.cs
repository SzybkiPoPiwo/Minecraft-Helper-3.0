using System;
using System.Linq;
using System.Windows.Controls;
using MinecraftHelper.Services;

namespace MinecraftHelper;

public partial class MainWindow
{
    string IMinerRemoteSession.ProfileId => _accountProfileId;

    string IMinerRemoteSession.DisplayName => _accountDisplayName;

    MinerRemoteStatus IMinerRemoteSession.GetRemoteStatus() => OnUiThread(BuildRemoteStatus);

    bool IMinerRemoteSession.TryCaptureRemoteFrame(MinerPreviewRegion region, int maximumWidth, out MinerLiveFrame? frame)
    {
        // PrintWindow does not require access to WPF controls. Keeping capture on the
        // pipe worker prevents the optional Live view from stalling the miner UI.
        IntPtr windowHandle = _targetGameWindowHandle;
        if (windowHandle == IntPtr.Zero)
        {
            frame = null;
            return false;
        }

        return MinerLivePreviewService.TryCapture(windowHandle, region, maximumWidth, out frame);
    }

    void IMinerRemoteSession.StopRemoteMining() => OnUiThread(StopMiningFromRemoteCommand);

    bool IMinerRemoteSession.TryStartRemoteMining(string? mode, out string message)
    {
        InventoryCleanupOwner requested = mode switch
        {
            "533" => InventoryCleanupOwner.Kopacz533,
            "633" => InventoryCleanupOwner.Kopacz633,
            _ => InventoryCleanupOwner.None
        };
        (bool started, string text) = OnUiThread(() => StartMiningFromRemoteCommand(requested));
        message = text;
        return started;
    }

    bool IMinerRemoteSession.TryAssignGameWindow(int processId, out string message)
    {
        (bool assigned, string text) = OnUiThread(() => AssignGameWindowFromRemoteCommand(processId));
        message = text;
        return assigned;
    }

    bool IMinerRemoteSession.TryArrangeGameWindow(out string message)
    {
        (bool arranged, string text) = OnUiThread(ArrangeGameWindowFromRemoteCommand);
        message = text;
        return arranged;
    }

    void IMinerRemoteSession.ShowFromRemote() => Dispatcher.BeginInvoke(new Action(async () =>
    {
        try
        {
            MinerWorkspaceResponse result = await MinerWorkspaceClient.SelectAsync(_accountProfileId);
            if (!result.Success)
                UpdateStatusBar(result.Message, "Red");
        }
        catch (Exception ex)
        {
            UpdateStatusBar("Nie udało się wybrać konta: " + ex.GetBaseException().Message, "Red");
        }
    }));

    void IMinerRemoteSession.ReloadProfile() => OnUiThread(ReloadAccountProfile);

    // Panel Logi w innym procesie prosi o wyczyszczenie — tylko ten proces trzyma dziennik
    // w pamięci, więc zmiana pliku z zewnątrz zostałaby nadpisana przy następnym zapisie.
    bool IMinerRemoteSession.TryClearMiningLogs(out string message)
    {
        (bool cleared, string text) = OnUiThread(() =>
        {
            if (!_miningLogService.Clear(out string error))
                return (false, "nie udało się wyczyścić historii: " + error);
            RefreshMiningLogsSummary();
            AppLog.Info("Logi", "Wyczyszczono historię kopania na prośbę panelu Logi.");
            return (true, "wyczyszczono historię kopania.");
        });
        message = text;
        return cleared;
    }

    private T OnUiThread<T>(Func<T> action) => Dispatcher.CheckAccess() ? action() : Dispatcher.Invoke(action);

    private void OnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    private MinerRemoteStatus BuildRemoteStatus()
    {
        bool running533 = HasMiningWork(InventoryCleanupOwner.Kopacz533);
        bool running633 = HasMiningWork(InventoryCleanupOwner.Kopacz633);
        string mode = running533 ? "5/3/3" : running633 ? "6/3/3" : "—";
        string windowTitle = string.IsNullOrWhiteSpace(_settings.TargetWindowTitle)
            ? BuildTargetProcessDisplayText()
            : _settings.TargetWindowTitle;
        DateTime now = DateTime.UtcNow;
        DateTime startedAt = running533 ? _kopacz533RuntimeStartedAtUtc : _kopacz633RuntimeStartedAtUtc;
        int runtimeSeconds = running533 || running633
            ? (int)Math.Clamp((now - startedAt).TotalSeconds, 0, int.MaxValue)
            : 0;
        IntPtr window = _targetGameWindowHandle;

        return new MinerRemoteStatus(
            _accountProfileId,
            _accountDisplayName,
            running533 || running633,
            mode,
            windowTitle,
            _latestMiningLogSummary.MiningRuns,
            _latestMiningLogSummary.InventorySessions,
            _latestMiningLogSummary.CobbleXCreated,
            _latestMiningLogSummary.DiscardedItems)
        {
            GameProcessId = _gameWindowClaim.ProcessId,
            GameWindowHandle = window.ToInt64(),
            WindowAvailable = window != IntPtr.Zero && IsWindow(window),
            WindowOwnedByAnotherProfile = _gameWindowOwnedByAnotherProfile,
            GameFocused = _isMinecraftFocused,
            BackgroundActive = BackgroundGameWindow.IsActive,
            BackgroundMiningEnabled = IsBackgroundMiningSelected(),
            RuntimeSeconds = runtimeSeconds
        };
    }

    // Same path as the STOP bind: releases held keys, cancels Auto EQ and reconnect,
    // clears a pending resume and closes the mining log entry. It never sends ESC,
    // because the user may be working in another application right now.
    private void StopMiningFromRemoteCommand()
    {
        bool stopped = false;
        foreach (InventoryCleanupOwner owner in new[] { InventoryCleanupOwner.Kopacz533, InventoryCleanupOwner.Kopacz633 })
        {
            if (!HasMiningWork(owner))
                continue;

            StopMiningFromBind(owner, remote: true);
            stopped = true;
        }

        if (!stopped)
            UpdateStatusBar("Zdalny STOP: Kopacz nie pracował.", "Orange");

        RefreshOverlayHud(DateTime.UtcNow);
    }

    // Both channels may stay enabled for their independent binds. The panel's
    // START must prefer the movement channel instead of silently choosing 5/3/3.
    private InventoryCleanupOwner GetMiningStartOwner()
    {
        if (ChkKopacz633Enabled.IsChecked == true)
            return InventoryCleanupOwner.Kopacz633;
        return ChkKopacz533Enabled.IsChecked == true
            ? InventoryCleanupOwner.Kopacz533
            : InventoryCleanupOwner.None;
    }

    // Check before the countdown as well as immediately before the actual start.
    // requested: tryb wskazany wprost (np. !start 5/3/3) — wtedy jego kanał nie musi być jeszcze zaznaczony.
    private string? GetMiningStartBlocker(InventoryCleanupOwner requested = InventoryCleanupOwner.None)
    {
        if (HasMiningWork(InventoryCleanupOwner.Kopacz533) || HasMiningWork(InventoryCleanupOwner.Kopacz633))
            return "Kopacz już pracuje.";
        InventoryCleanupOwner owner = requested != InventoryCleanupOwner.None ? requested : GetMiningStartOwner();
        if (owner == InventoryCleanupOwner.None)
            return "zaznacz kanał kopania (5/3/3 albo 6/3/3) w tym profilu.";
        if (owner == InventoryCleanupOwner.Kopacz633 && !IsKopacz633DirectionSelected())
            return "wybierz kierunek Kopacza 6/3/3 („Na wprost” albo „Do góry”).";
        if (_targetGameWindowHandle == IntPtr.Zero)
        {
            return _gameWindowOwnedByAnotherProfile
                ? "wybrane okno Minecrafta należy do innego konta."
                : "najpierw wybierz okno Minecrafta.";
        }
        return null;
    }

    private (bool Started, string Message) StartMiningFromRemoteCommand() => StartMiningFromRemoteCommand(InventoryCleanupOwner.None);

    private (bool Started, string Message) StartMiningFromRemoteCommand(InventoryCleanupOwner requested)
    {
        if (GetMiningStartBlocker(requested) is { } blocker)
            return (false, blocker);

        InventoryCleanupOwner owner = requested != InventoryCleanupOwner.None ? requested : GetMiningStartOwner();
        // Tryb wybrany z Discorda/panelu, którego kanał jest odznaczony — zaznaczamy go,
        // bo bez tego Toggle…Runtime odmówi startu.
        CheckBox channel = owner == InventoryCleanupOwner.Kopacz533 ? ChkKopacz533Enabled : ChkKopacz633Enabled;
        string channelNote = string.Empty;
        if (channel.IsChecked != true)
        {
            channel.IsChecked = true;
            channelNote = $" (włączono kanał {GetInventoryCleanupOwnerLabel(owner)} w profilu)";
        }
        _isMinecraftFocused = CheckGameFocus();
        IntPtr window = _targetGameWindowHandle;

        // Starting outside the game must never press keys in the user's current
        // application, so it is allowed only through the background session.
        bool foreground = GetForegroundWindow() == window;
        bool beganBackground = false;
        if (!foreground)
        {
            if (!IsBackgroundMiningSelected())
                return (false, "włącz „Kopanie w tle” w tym profilu albo przejdź do okna gry i użyj bindu.");
            if (HasForegroundOnlyAutomationRunning())
                return (false, "najpierw wyłącz auto łowienie — działa tylko z grą na wierzchu.");
            if (!BackgroundGameWindow.CanUse(window))
                return (false, "okno gry jest niedostępne dla kopania w tle.");

            if (BackgroundGameWindow.IsActive && BackgroundGameWindow.Window != window)
                EndBackgroundMiningSession(restoreHeldInputsInForeground: false);
            if (!BackgroundGameWindow.IsActive)
            {
                if (!BeginBackgroundMiningSession(window))
                    return (false, BackgroundGameWindow.LastPrepareError ?? "Nie udało się przygotować kopania w tle. Spróbuj ponownie.");
                beganBackground = true;
            }
        }

        if (owner == InventoryCleanupOwner.Kopacz533)
            ToggleKopacz533Runtime();
        else
            ToggleKopacz633Runtime();

        bool started = owner == InventoryCleanupOwner.Kopacz533 ? _kopacz533RuntimeEnabled : _kopacz633RuntimeEnabled;
        string label = GetInventoryCleanupOwnerLabel(owner);
        if (!started)
        {
            if (beganBackground)
                EndBackgroundMiningSession(restoreHeldInputsInForeground: false);
            return (false, $"nie udało się uruchomić: {label}. Sprawdź okno Kopacza.");
        }

        return (true, (foreground ? $"uruchomiono: {label}" : $"uruchomiono w tle: {label}") + channelNote + ".");
    }

    private (bool Assigned, string Message) AssignGameWindowFromRemoteCommand(int processId)
    {
        if (HasMiningWork(InventoryCleanupOwner.Kopacz533) || HasMiningWork(InventoryCleanupOwner.Kopacz633))
            return (false, "zatrzymaj kopanie przed zmianą okna gry.");
        if (_gameWindowClaim.IsClaimedByAnotherProfile(processId))
            return (false, "to okno Minecrafta należy już do innego konta.");

        ProcessTargetOption? option = GetRunningWindowProcesses().FirstOrDefault(item => item.ProcessId == processId);
        if (option == null)
            return (false, "nie znaleziono okna tego procesu.");

        ReadFromUi(includeWindowTitle: false);
        _settings.TargetProcessId = option.ProcessId;
        _settings.TargetProcessName = option.ProcessName;
        _settings.TargetWindowTitle = option.WindowTitle;
        TxtTargetWindowTitle.Text = option.WindowTitle;
        TxtCurrentWindowTitle.Text = BuildTargetProcessDisplayText();

        // The combo keeps its own selection first; clear it so it follows the new target.
        bool previousLoading = _isLoadingUi;
        _isLoadingUi = true;
        CbTargetProcessList.SelectedIndex = -1;
        _isLoadingUi = previousLoading;
        RefreshTargetProcessChoices();

        _targetGameWindowHandle = IntPtr.Zero;
        _ = TryResolveTargetWindow(allowPendingSelection: false, out _targetGameWindowHandle);
        UpdateEmergencyDamageSoundSourcePreview();
        _settingsService.Save(_settings);
        _isMinecraftFocused = CheckGameFocus();

        if (_gameWindowOwnedByAnotherProfile)
            return (false, "to okno Minecrafta należy już do innego konta.");

        UpdateStatusBar($"Przypisano okno gry z panelu kont: {option.WindowTitle}", "Green");
        return (true, $"przypisano okno „{option.WindowTitle}”.");
    }

    // Ten sam układ co przy starcie kopania w tle, ale na żądanie — bez czekania na START.
    private (bool Arranged, string Message) ArrangeGameWindowFromRemoteCommand()
    {
        // Zmiana rozmiaru w trakcie pracy przesunęłaby środek ekranu, w który celuje kopanie.
        if (HasMiningWork(InventoryCleanupOwner.Kopacz533) || HasMiningWork(InventoryCleanupOwner.Kopacz633))
            return (false, "zatrzymaj kopanie przed kalibracją okna gry.");

        IntPtr window = _targetGameWindowHandle;
        if (window == IntPtr.Zero && !TryResolveTargetWindow(allowPendingSelection: false, out window))
            return (false, "najpierw wybierz okno Minecrafta.");
        if (_gameWindowOwnedByAnotherProfile)
            return (false, "to okno Minecrafta należy już do innego konta.");

        if (!BackgroundGameWindow.PrepareWindow(window))
        {
            string error = BackgroundGameWindow.LastPrepareError
                ?? "gra jest na pełnym ekranie — wyłącz F11 i spróbuj ponownie.";
            UpdateStatusBar(error, "Orange");
            return (false, error);
        }

        System.Drawing.Size size = BackgroundGameWindow.GetClientSize(window);
        UpdateStatusBar($"Okno gry skalibrowane ({size.Width}x{size.Height}) obok pozostałych kont.", "Green");
        return (true, $"okno gry ustawione obok pozostałych kont ({size.Width}x{size.Height}).");
    }

    private void ReloadAccountProfile()
    {
        string? name = new MinerAccountStore()
            .Load()
            .FirstOrDefault(profile => string.Equals(profile.Id, _accountProfileId, StringComparison.OrdinalIgnoreCase))
            ?.Name;
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, _accountDisplayName, StringComparison.Ordinal))
            return;

        _accountDisplayName = name;
        RefreshGlassAccountInfo();
        if (_accountProfileId != MinerProfileId.Default)
            Title = $"Minecraft Helper Kopacz {_currentAppVersion} — {_accountDisplayName}";
    }
}
