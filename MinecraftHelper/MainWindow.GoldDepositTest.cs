using System;
using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper;

public partial class MainWindow
{
    private GoldDepositManualTestSession? _goldDepositManualTest;
    private bool _goldDepositReadTestActive;
    private int _goldDepositReadTestGeneration;

    private bool HasOtherGoldTestAutomation() => _kopacz533RuntimeEnabled || _kopacz633RuntimeEnabled
        || _autoReconnectStage != AutoReconnectStage.None || _inventoryCleanupScanInProgress
        || _testAutoFishingRuntimeEnabled;

    private void UpdateGoldDepositTestButtons()
    {
        if (BtnGoldDepositRunTest == null) return;
        bool busy = IsGoldDepositActive || _goldDepositReadTestActive || _goldDepositBusy;
        BtnGoldDepositRunTest.IsEnabled = !busy;
        BtnGoldDepositReadTest.IsEnabled = !busy;
        BtnGoldDepositStopTest.IsEnabled = _goldDepositManualTest != null || _goldDepositReadTestActive;
        PanelGoldDepositConfiguration.IsEnabled = !busy;
    }

    private void BtnGoldDepositRunTest_Click(object sender, RoutedEventArgs e)
    {
        if (HasOtherGoldTestAutomation() || _inventoryCleanupStage != InventoryCleanupStage.None
            || _goldDepositBusy || _goldDepositReadTestActive || _goldDepositManualTest != null)
        {
            SetGoldStatus("Najpierw zatrzymaj kopanie i pozostałe testy lub automatyzacje.", true);
            return;
        }
        GoldDepositOptions options = GoldOptions();
        string error = options.Validate();
        if (error.Length > 0) { SetGoldStatus(error, true); return; }
        if (_targetGameWindowHandle == IntPtr.Zero || !IsWindow(_targetGameWindowHandle))
        {
            SetGoldStatus("Najpierw wybierz i zapisz działające okno Minecrafta.", true);
            return;
        }

        DateTime now = DateTime.UtcNow;
        ResetInventoryCleanupState(scheduleNext: false, now);
        _goldDepositManualTest = new GoldDepositManualTestSession(options, _targetGameWindowHandle, now);
        _goldDepositFlow = _goldDepositManualTest.Flow;
        _inventoryCleanupOwner = InventoryCleanupOwner.None;
        _inventoryCleanupStage = InventoryCleanupStage.DepositGold;
        _nextInventoryCleanupStageAtUtc = _goldDepositManualTest.ReadyAt;
        _kopacz533BindWasDown = IsConfiguredBindKeyDown(TxtKopacz533Key.Text);
        _kopacz633BindWasDown = IsConfiguredBindKeyDown(TxtKopacz633Key.Text);
        LogGoldDeposit(MiningLogStatuses.Completed, "Uruchomiono ręczny test rzeczywistej wpłaty; komenda: " + options.Command);
        SetGoldStatus($"Test rzeczywistej wpłaty za 3 s — {GoldTestWhereHint()} Kopanie pozostaje wyłączone.");
        UpdateStatusBar($"Test wpłaty złota za 3 s — {GoldTestWhereHint()}", "Orange");
        UpdateGoldDepositTestButtons();
        RefreshOverlayHud(now);
    }

    private bool TryRunGoldDepositManualTest(DateTime now)
    {
        GoldDepositManualTestSession? session = _goldDepositManualTest;
        if (session == null) return false;
        // Ta sama sesja w tle co przy kopaniu: z włączonym „Kopanie w tle” test nie wymaga
        // gry na wierzchu, a wejście trafia prosto do okna gry tego konta.
        bool gameFocused = GetForegroundWindow() == session.TargetWindow;
        bool backgroundInput = UpdateBackgroundMiningSession(gameFocused) && BackgroundGameWindow.Window == session.TargetWindow;
        // Bindy STOP działają tylko z grą na wierzchu — w tle klawiatura należy do innego programu.
        bool stop533 = !backgroundInput && IsMiningStopBindPressed(TxtKopacz533Key.Text, ref _kopacz533BindWasDown);
        bool stop633 = !backgroundInput && IsMiningStopBindPressed(TxtKopacz633Key.Text, ref _kopacz633BindWasDown);
        GoldDepositTestDecision decision = session.Tick(now, _targetGameWindowHandle,
            gameFocused || backgroundInput, stop533 || stop633 || _isExitRequested, HasOtherGoldTestAutomation());
        if (decision == GoldDepositTestDecision.Cancelled)
        {
            FinishGoldDepositManualTest(session.StopReason, success: false);
            return true;
        }
        if (decision == GoldDepositTestDecision.Countdown)
            SetGoldStatus($"Test rzeczywistej wpłaty za {session.RemainingSeconds(now)} s — {GoldTestWhereHint()} STOP anuluje test.");
        else if (now >= _nextInventoryCleanupStageAtUtc)
            ProcessGoldDeposit(now);
        RefreshOverlayHud(now);
        return true;
    }

    private string GoldTestWhereHint() => IsBackgroundMiningSelected()
        ? "kopanie w tle włączone, gra może zostać pod spodem."
        : "przejdź do Minecrafta.";

    private void FinishGoldDepositManualTest(string result, bool success)
    {
        if (_goldDepositManualTest == null) return;
        LogGoldDeposit(success ? MiningLogStatuses.Completed : MiningLogStatuses.Aborted, result);
        ResetInventoryCleanupState(scheduleNext: false);
        string message = (success ? "Test wpłaty zakończony: " : "Test wpłaty przerwany: ") + result;
        SetGoldStatus(message, !success);
        UpdateInventoryCleanupStatus(message, success ? "Green" : "Orange");
        UpdateStatusBar(message, success ? "Green" : "Orange");
        UpdateGoldDepositTestButtons();
        if (!_isExitRequested) RefreshOverlayHud(DateTime.UtcNow);
    }

    private void BtnGoldDepositStopTest_Click(object sender, RoutedEventArgs e)
    {
        if (_goldDepositManualTest != null)
            FinishGoldDepositManualTest("Zatrzymano przyciskiem STOP. Sprawdź przedmiot na kursorze.", success: false);
        else if (_goldDepositReadTestActive)
        {
            CancelGoldDepositReadTest();
            SetGoldStatus("Test odczytu menu anulowany.");
        }
    }

    private void CancelGoldDepositReadTest()
    {
        _goldDepositReadTestGeneration++;
        _goldDepositReadTestActive = false;
        UpdateGoldDepositTestButtons();
    }
}
