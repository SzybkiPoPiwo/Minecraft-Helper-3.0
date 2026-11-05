using System;
using System.Windows;
using MinecraftHelper.Services;
using Drawing = System.Drawing;

namespace MinecraftHelper;

// Kopanie w tle: Minecraft pracuje na innym pulpicie wirtualnym Windows, a użytkownik
// korzysta z komputera na swoim. Gdy gra traci fokus w trakcie pracy Kopacza, wejście
// przechodzi z globalnego SendInput na wiadomości wysyłane do okna gry, a obraz jest
// pobierany przez PrintWindow. Powrót do okna gry przywraca zwykły tryb.
public partial class MainWindow
{
    // Repeat fake focus in case WM_KILLFOCUS arrived late during the switch.
    private static readonly TimeSpan BackgroundFakeFocusRepeatDelay = TimeSpan.FromMilliseconds(500);

    private DateTime _nextBackgroundFakeFocusAtUtc = DateTime.MaxValue;
    private bool _backgroundMiningMinimizedReported;
    private IntPtr _backgroundWindowPreparedFor;
    private GameWindowShield? _gameShield;
    private DateTime _nextBackgroundPrepareAttemptUtc;
    private int _backgroundFakeFocusesLeft;
    private IntPtr _lastUserForegroundWindow;
    private int _backgroundFocusStealsReverted;
    private bool _backgroundWaitingForUserReported;
    private bool _autoReconnectJoinCheckByScreen;

    private readonly record struct HeldMinerInputs(
        bool Mining533,
        bool Attack633,
        Kopacz633StrafeDirection Strafe633,
        bool Eating,
        bool DropControl,
        bool DropKey,
        bool ReturnLeft,
        bool ReturnBackward);

    private bool IsBackgroundMiningSelected() => ChkBackgroundMiningEnabled.IsChecked == true;

    // Returns true when this tick should keep running the miner without focus.
    private bool UpdateBackgroundMiningSession(bool gameFocused)
    {
        bool background = UpdateBackgroundMiningSessionCore(gameFocused);
        UpdateGameWindowShield(background);
        return background;
    }

    private bool UpdateBackgroundMiningSessionCore(bool gameFocused)
    {
        IntPtr window = _targetGameWindowHandle;
        // Gra na tym samym pulpicie po fałszywym fokusie przejmuje pierwszy plan, gdy tylko
        // dostanie prawdziwe wejście (np. kursor przejedzie nad jej oknem). Jeśli użytkownik
        // nie kliknął, nie użył Alt+Tab ani klawisza Windows — oddajemy fokus i kopiemy dalej.
        IntPtr foregroundNow = GetForegroundWindow();
        if (foregroundNow != IntPtr.Zero && foregroundNow != window)
            _lastUserForegroundWindow = foregroundNow;
        bool returnForeground = BackgroundGameWindow.ShouldReturnForeground(gameFocused);
        if (gameFocused
            && BackgroundGameWindow.IsActive
            && BackgroundGameWindow.Window == window
            && !BackgroundGameWindow.IsUserSwitchRequested)
        {
            // A failed native focus return is not permission to resume SendInput.
            // Also keep PostMessage while awaiting a queued physical click event.
            gameFocused = false;
            if (returnForeground)
            {
                HeldMinerInputs held = ReleaseHeldMinerInputs();
                bool returned = BackgroundGameWindow.ReturnForegroundTo(_lastUserForegroundWindow, window);
                BackgroundGameWindow.ProtectUserCursor();
                RestoreHeldMinerInputs(held);
                if (returned)
                {
                    _backgroundFocusStealsReverted++;
                    // Returning real focus sends WM_KILLFOCUS to LWJGL. Re-prime
                    // its internal input state or it stops responding in the background.
                    _backgroundFakeFocusesLeft = 2;
                    _nextBackgroundFakeFocusAtUtc = DateTime.UtcNow.AddMilliseconds(50);
                    UpdateStatusBar($"Minecraft próbował przejąć fokus — oddano go ({_backgroundFocusStealsReverted}×). Wejdź do gry kliknięciem, by sterować ręcznie.", "Orange");
                }
            }
        }

        bool minerWorkActive = _kopacz533RuntimeEnabled
            || _kopacz633RuntimeEnabled
            || _inventoryCleanupStage != InventoryCleanupStage.None
            || _autoReconnectStage != AutoReconnectStage.None
            || _emergencyReconnectActive
            || _emergencyDamageSoundHandlingAlarm
            || _protectionTestInputActive;
        bool wanted = !gameFocused
            && IsBackgroundMiningSelected()
            && minerWorkActive
            && !HasForegroundOnlyAutomationRunning()
            && BackgroundGameWindow.CanUse(window);

        // Okno przygotowujemy raz na każde uruchomienie kopania.
        if (!minerWorkActive)
            _backgroundWindowPreparedFor = IntPtr.Zero;

        if (BackgroundGameWindow.IsActive && (!wanted || BackgroundGameWindow.Window != window))
            EndBackgroundMiningSession(restoreHeldInputsInForeground: gameFocused);

        if (wanted && !BackgroundGameWindow.IsActive)
            BeginBackgroundMiningSession(window);

        if (!BackgroundGameWindow.IsActive)
            return false;

        ReportMinimizedGameWindow(BackgroundGameWindow.IsMinimized(window));

        DateTime now = DateTime.UtcNow;
        if (_backgroundFakeFocusesLeft > 0 && now >= _nextBackgroundFakeFocusAtUtc)
            SendPendingBackgroundFakeFocus(now);

        BackgroundGameWindow.ProtectUserCursor();
        return true;
    }

    private bool BeginBackgroundMiningSession(IntPtr window)
    {
        if (DateTime.UtcNow < _nextBackgroundPrepareAttemptUtc)
            return false;
        IntPtr foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != window)
            _lastUserForegroundWindow = foreground;
        // Buttons pressed by SendInput are global. Release them before the user
        // starts clicking on their own desktop, then press them again in the game.
        HeldMinerInputs held = ReleaseHeldMinerInputs();
        Drawing.Size previousClientSize = BackgroundGameWindow.GetClientSize(window);

        if (_backgroundWindowPreparedFor != window)
        {
            if (BackgroundGameWindow.PrepareWindow(window))
            {
                Drawing.Size size = BackgroundGameWindow.GetClientSize(window);
                UpdateStatusBar($"Okno gry przygotowane ({size.Width}x{size.Height}).", "Green");
            }
            else if (BackgroundGameWindow.LastPrepareError is { } error)
            {
                _nextBackgroundPrepareAttemptUtc = DateTime.UtcNow.AddSeconds(1);
                UpdateStatusBar(error, "Orange");
                return false;
            }
            _backgroundWindowPreparedFor = window;
        }

        BackgroundGameWindow.Activate(window, previousClientSize);
        // Remember the user's cursor before LWJGL may centre it on WM_SETFOCUS.
        BackgroundGameWindow.ProtectUserCursor();
        // Dwa fałszywe fokusy (drugi po 0,5 s), z ochroną pierwszego planu Kopacza.
        _backgroundFakeFocusesLeft = 2;
        _backgroundWaitingForUserReported = false;
        _nextBackgroundFakeFocusAtUtc = DateTime.UtcNow;
        _backgroundMiningMinimizedReported = false;

        RestoreHeldMinerInputs(held);
        UpdateStatusBar("Kopanie w tle — Kopacz steruje Minecraftem bez fokusu. Wróć do okna gry, aby użyć bindów.", "Green");
        RefreshOverlayHud(DateTime.UtcNow);
        return true;
    }

    // LWJGL can request foreground in response to fake focus. If Kopacz is the
    // foreground process, lock programmatic activation before posting the messages.
    private void SendPendingBackgroundFakeFocus(DateTime now)
    {
        if (IsOwnProcessForeground() && !BackgroundGameWindow.TryProtectOwnForeground())
        {
            if (!_backgroundWaitingForUserReported)
            {
                _backgroundWaitingForUserReported = true;
                UpdateStatusBar("Kopanie w tle czeka — przełącz się z Kopacza na inny program albo pulpit.", "Orange");
            }
            return;
        }

        // Fokus nie może trafić w trzymany LPM/Shift: puść, wyślij, wciśnij ponownie.
        // An open inventory/chest owns its cursor coordinates; don't rebase it.
        bool resetMouseBaseline = BackgroundGameWindow.NeedsInitialMouseBaseline && !IsGameGuiOpen();
        HeldMinerInputs held = ReleaseHeldMinerInputs();
        bool sent = BackgroundGameWindow.SendFakeFocus(resetMouseBaseline);
        RestoreHeldMinerInputs(held);
        if (!sent)
        {
            _nextBackgroundFakeFocusAtUtc = now.AddMilliseconds(100);
            return;
        }
        _backgroundFakeFocusesLeft--;
        _nextBackgroundFakeFocusAtUtc = _backgroundFakeFocusesLeft > 0
            ? now.Add(BackgroundFakeFocusRepeatDelay)
            : DateTime.MaxValue;
        if (_backgroundWaitingForUserReported && _backgroundFakeFocusesLeft == 0)
            UpdateStatusBar("Kopanie w tle — Kopacz steruje Minecraftem bez fokusu.", "Green");
    }

    private static bool IsOwnProcessForeground() => IsOwnProcessForegroundWindow(GetForegroundWindow());

    private static bool IsOwnProcessForegroundWindow(IntPtr window)
    {
        _ = GetWindowThreadProcessId(window, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    private void EndBackgroundMiningSession(bool restoreHeldInputsInForeground)
    {
        AppLog.Info("Tło", restoreHeldInputsInForeground ? "Koniec sesji w tle — gra znów na wierzchu." : "Koniec sesji w tle.");
        HeldMinerInputs held = ReleaseHeldMinerInputs();
        BackgroundGameWindow.Deactivate();
        _nextBackgroundFakeFocusAtUtc = DateTime.MaxValue;
        _backgroundFakeFocusesLeft = 0;

        if (restoreHeldInputsInForeground)
        {
            RestoreHeldMinerInputs(held);
            UpdateStatusBar("Minecraft jest znów aktywnym oknem — Kopacz wrócił do zwykłego sterowania.", "Orange");
        }

        RefreshOverlayHud(DateTime.UtcNow);
    }

    private void ReportMinimizedGameWindow(bool minimized)
    {
        if (!minimized)
        {
            _backgroundMiningMinimizedReported = false;
            return;
        }

        if (_backgroundMiningMinimizedReported)
            return;

        _backgroundMiningMinimizedReported = true;
        UpdateStatusBar("Minecraft jest zminimalizowany — Kopacz kopie na ślepo. Funkcje czytające obraz (EQ, drop, CobbleX, reconnect) ruszą po przywróceniu okna.", "Orange");
    }

    // Modules that read the real cursor, move the camera or work on screen
    // regions are not adapted to the background mode.
    private bool HasForegroundOnlyAutomationRunning()
    {
        return _testAutoFishingRuntimeEnabled;
    }

    private HeldMinerInputs ReleaseHeldMinerInputs()
    {
        var held = new HeldMinerInputs(
            _kopacz533Holding,
            _kopacz633HoldingAttack,
            _kopacz633StrafeDirection,
            _inventoryCleanupEatingRightButtonDown,
            _inventoryCleanupControlDown,
            _inventoryCleanupDropKeyDown,
            _inventoryCleanupReturnLeftDown,
            _inventoryCleanupReturnBackwardDown);

        SetKopacz533MiningHold(false);
        SetKopacz633AttackHold(false);
        SetKopacz633StrafeDirection(Kopacz633StrafeDirection.None);
        SetInventoryCleanupEatingHold(false);
        ReleaseInventoryCleanupDropKeys();
        ReleaseInventoryCleanupReturnKeys();
        return held;
    }

    private void RestoreHeldMinerInputs(HeldMinerInputs held)
    {
        if (held.Mining533)
            SetKopacz533MiningHold(true);
        if (held.Attack633)
            SetKopacz633AttackHold(true);
        if (held.Strafe633 != Kopacz633StrafeDirection.None)
            SetKopacz633StrafeDirection(held.Strafe633);
        if (held.Eating)
            SetInventoryCleanupEatingHold(true);
        if (held.DropControl)
            SetInventoryCleanupControl(true);
        if (held.DropKey)
            SetInventoryCleanupDropKey(true);
        if (held.ReturnLeft)
            SetInventoryCleanupReturnLeft(true);
        if (held.ReturnBackward)
            SetInventoryCleanupReturnBackward(true);
    }

    private bool IsGameInputAvailableFor(IntPtr window)
    {
        return window != IntPtr.Zero
            && (GetForegroundWindow() == window || BackgroundGameWindow.Window == window);
    }

    // In the foreground a visible Windows cursor means an open GUI. In the
    // background that cursor belongs to the user's desktop, so the open
    // inventory/chest is recognised by the texture-pack markers instead.
    private bool IsGameGuiOpen(int chestColumns = 9)
    {
        if (!BackgroundGameWindow.IsActive)
            return IsInventoryCursorVisible();

        if (!TryCaptureTargetClient(out Drawing.Bitmap? bitmap, out _))
            return false;

        using (bitmap)
        {
            return InventoryMarkerDetector.TryDetect(bitmap!, null, null, out _)
                || InventoryMarkerDetector.TryDetectChestMenu(bitmap!, Math.Clamp(chestColumns, 1, 9), 1, out _);
        }
    }

    // The game's own pointer in background mode; the user's cursor otherwise.
    private static bool TryGetGameCursorPosition(out POINT point)
    {
        if (!BackgroundGameWindow.IsActive)
            return GetCursorPos(out point);

        bool known = BackgroundGameWindow.TryGetCursorScreenPosition(out int x, out int y);
        point = new POINT { X = x, Y = y };
        return known;
    }

    private void ContinueAfterServerJoin(DateTime now)
    {
        if (_emergencyReconnectActive)
        {
            RecordAutomationLogEvent(
                MiningLogEventTypes.EmergencyReconnectJoined,
                MiningLogStatuses.Completed,
                $"Dołączono do serwera po {_autoReconnectAttempt} próbach. Otwieram EQ i rozpoczynam kontrolę diamentowego kilofa.");
            _autoReconnectStage = AutoReconnectStage.EmergencyOpenInventory;
            _nextAutoReconnectActionAtUtc = now.AddMilliseconds(350);
            UpdateAutoReconnectStatus("Dołączono do gry. Otwieram EQ i sprawdzam diamentowy kilof...", "Orange");
        }
        else
        {
            // Zaraz po wejściu serwer jeszcze się ładuje (powitanie, pluginy) i nie otwiera menu home.
            _autoReconnectStage = AutoReconnectStage.OpenHomeChat;
            _nextAutoReconnectActionAtUtc = now.AddSeconds(AutoReconnectSettleAfterJoinSeconds);
            UpdateAutoReconnectStatus($"Wszedłem na serwer. Czekam {AutoReconnectSettleAfterJoinSeconds} s, aż serwer się załaduje, potem {_autoReconnectActiveHomeCommand}...", "Orange");
        }
    }

    private void ChkBackgroundMiningEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi)
            return;

        MarkDirty();
    }

    private void ChkGameShieldEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUi)
            return;

        MarkDirty();
        UpdateGameWindowShield(BackgroundGameWindow.IsActive);
    }

    // Osłona istnieje tylko podczas sesji w tle; tworzymy ją leniwie przy pierwszej potrzebie.
    private void UpdateGameWindowShield(bool backgroundActive)
    {
        bool wanted = backgroundActive && ChkGameShieldEnabled?.IsChecked == true && !_isExitRequested;
        if (!wanted && _gameShield == null)
            return;

        if (_gameShield == null)
        {
            _gameShield = new GameWindowShield();
            _gameShield.TakeOverRequested += (_, __) => TakeOverGameFromShield();
            Closed += (_, __) => _gameShield?.Close();
        }

        _gameShield.Track(BackgroundGameWindow.IsActive ? BackgroundGameWindow.Window : _targetGameWindowHandle, wanted);
    }

    // „Zatrzymaj i przejmij grę” z osłony: STOP jak z panelu, potem fokus do okna gry.
    private void TakeOverGameFromShield()
    {
        IntPtr game = BackgroundGameWindow.IsActive ? BackgroundGameWindow.Window : _targetGameWindowHandle;
        AppLog.Info("Osłona", "Przejęcie gry z osłony — zatrzymuję kopanie.");
        StopMiningFromRemoteCommand();
        if (BackgroundGameWindow.IsActive)
            EndBackgroundMiningSession(restoreHeldInputsInForeground: false);
        UpdateGameWindowShield(backgroundActive: false);

        if (game != IntPtr.Zero && !BackgroundGameWindow.ReturnForegroundTo(game, GetForegroundWindow()))
            UpdateStatusBar("Kopanie zatrzymane. Kliknij okno gry, aby w nim grać.", "Orange");
        else
            UpdateStatusBar("Kopanie zatrzymane — możesz grać ręcznie.", "Green");
    }
}
