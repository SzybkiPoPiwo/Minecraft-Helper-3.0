using System;
using System.Threading.Tasks;
using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Karta „Testy ochrony” w zakładce Ochrona: każde flow osobno (wyjście, wejście, home, kilof)
// oraz pełny alarm — symulowany albo wywołany prawdziwym dźwiękiem uderzenia. Testy używają
// tego samego kodu co prawdziwe zdarzenia, więc wysyłają prawdziwe klawisze i komendy w grze.
public partial class MainWindow
{
    private const int LiveSoundTestSeconds = 30;

    private void UpdateProtectionTestStatus(string message, string colorName)
    {
        AppLog.Info("Ochrona", "Test: " + message);
        if (TxtProtectionTestStatus != null)
        {
            TxtProtectionTestStatus.Text = message;
            TxtProtectionTestStatus.Foreground = GlassPalette.Readable(colorName switch
            {
                "Red" => System.Windows.Media.Brushes.OrangeRed,
                "Green" => System.Windows.Media.Brushes.MediumSpringGreen,
                "Orange" => System.Windows.Media.Brushes.Orange,
                _ => System.Windows.Media.Brushes.DeepSkyBlue
            });
        }
    }

    // Wspólne warunki: wybrane okno gry i brak trwającej procedury.
    private bool CanStartProtectionTest()
    {
        if (_targetGameWindowHandle == IntPtr.Zero)
        {
            UpdateProtectionTestStatus("Najpierw wybierz okno Minecrafta w zakładce Okno gry.", "Red");
            return false;
        }
        if (_emergencyDamageSoundHandlingAlarm || _emergencyReconnectActive || _autoReconnectStage != AutoReconnectStage.None)
        {
            UpdateProtectionTestStatus("Trwa inna procedura ochrony albo reconnectu — zatrzymaj ją przyciskiem STOP.", "Orange");
            return false;
        }
        return true;
    }

    // Trwa test, który sam steruje grą — sesja w tle ma wtedy pozostać włączona (UpdateBackgroundMiningSession).
    private bool _protectionTestInputActive;

    // Odliczanie 5 s bez okna modalnego: zamknięcie ShowDialog oddawało fokus Kopaczowi,
    // więc test nie mógł kliknąć w grze. Przy kopaniu w tle fokus nie jest potrzebny.
    private async Task<bool> CountdownToGameAsync(string title)
    {
        bool background = IsBackgroundMiningSelected() && BackgroundGameWindow.CanUse(_targetGameWindowHandle);
        var countdown = new StartCountdownWindow(title, background
            ? "Kopanie w tle jest włączone — test zadziała w tle, możesz zostać tutaj."
            : "Kliknij w okno gry — test ruszy automatycznie.")
        {
            Owner = this
        };
        if (await countdown.RunModelessAsync())
            return true;
        UpdateProtectionTestStatus("Test anulowany.", "Orange");
        return false;
    }

    // Gra aktywna albo sesja w tle (gdy zaznaczone „Kopanie w tle”) — inaczej ESC i kliknięcie nie dojdą.
    private bool EnsureGameInputForTest()
    {
        IntPtr window = _targetGameWindowHandle;
        UpdateBackgroundMiningSession(GetForegroundWindow() == window);
        if (IsGameInputAvailableFor(window))
            return true;
        UpdateProtectionTestStatus(IsBackgroundMiningSelected()
            ? "Okno gry jest niedostępne dla sterowania w tle (np. zminimalizowane). Przywróć je i spróbuj ponownie."
            : "Minecraft nie był aktywny po odliczaniu — kliknij w okno gry w trakcie odliczania albo włącz „Kopanie w tle” w zakładce Okno gry.", "Red");
        return false;
    }

    private async void BtnProtectionTestDisconnect_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest() || !await CountdownToGameAsync("Test: wyjście z serwera"))
            return;

        _protectionTestInputActive = true;
        try
        {
            if (!EnsureGameInputForTest())
                return;
            (bool disconnected, string result) = await TryDisconnectMinecraftAfterEmergencyAsync(
                _targetGameWindowHandle,
                IsMinecraftGuiLikelyOpenForEmergency());
            UpdateProtectionTestStatus(disconnected
                    ? "Wyjście z serwera: kliknięto Disconnect. Sprawdź, czy gra pokazuje listę serwerów lub menu."
                    : "Wyjście z serwera nie powiodło się: " + result + ".",
                disconnected ? "Green" : "Red");
        }
        finally
        {
            _protectionTestInputActive = false;
            UpdateBackgroundMiningSession(GetForegroundWindow() == _targetGameWindowHandle);
        }
    }

    private void BtnProtectionTestJoin_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest())
            return;
        UpdateProtectionTestStatus("Wejście na serwer: pełny reconnect uruchomiony — przejdź do gry. Postęp w sekcji Profil powrotu i w HUD.", "Orange");
        BtnAutoReconnectStart_Click(sender, e);
    }

    private void BtnProtectionTestHome_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest())
            return;
        UpdateProtectionTestStatus("Powrót na home uruchomiony — przejdź do gry.", "Orange");
        BtnAutoReconnectTestHome_Click(sender, e);
    }

    private void BtnProtectionTestPickaxe_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest())
            return;
        UpdateProtectionTestStatus("Kontrola kilofa w EQ uruchomiona — przejdź do gry. Wynik w sekcji Profil powrotu.", "Orange");
        BtnAutoReconnectCheckInventory_Click(sender, e);
    }

    // Jak po nieudanej kontroli kilofa podczas kopania: home (awaryjny, jeśli ustawiony) i koniec pracy.
    private void BtnProtectionTestMissingPickaxe_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest())
            return;
        if (!CanRunAutoReconnectTest(out string error))
        {
            UpdateProtectionTestStatus(error, "Red");
            return;
        }
        InventoryCleanupOwner owner = _kopacz533RuntimeEnabled
            ? InventoryCleanupOwner.Kopacz533
            : _kopacz633RuntimeEnabled ? InventoryCleanupOwner.Kopacz633 : InventoryCleanupOwner.None;
        UpdateProtectionTestStatus("Brak kilofa → powrót na home uruchomiony — przejdź do gry. Kopanie (jeśli trwało) zostanie zakończone.", "Orange");
        BeginMissingPickaxeHomeRecovery(owner, DateTime.UtcNow);
    }

    private void BtnProtectionTestSound_Click(object sender, RoutedEventArgs e)
    {
        UpdateProtectionTestStatus("Test nasłuchu bez reakcji (15 s) — daj się uderzyć w grze. Wynik w sekcji Nasłuch obrażeń.", "Orange");
        BtnEmergencyDamageSoundTest_Click(sender, e);
    }

    // Prawdziwy dźwięk uderzenia uruchamia pełną reakcję — także bez kopania i mimo trybu testowego.
    private void BtnProtectionTestLiveSound_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest())
            return;
        if (_settings.EmergencyDamageSoundTemplates.Count == 0)
        {
            UpdateProtectionTestStatus("Nie wczytano wzorców dźwięku hit1–hit4 — kliknij „Przeładuj wzorce” w sekcji Nasłuch obrażeń.", "Red");
            return;
        }

        _emergencyDamageSoundLiveTestActive = true;
        _emergencyDamageSoundManualTestActive = true;
        _emergencyDamageSoundManualTestUntilUtc = DateTime.UtcNow.AddSeconds(LiveSoundTestSeconds);
        StartEmergencyDamageSoundMonitoring(manualTest: true);
        UpdateProtectionTestStatus($"Nasłuch z reakcją przez {LiveSoundTestSeconds} s — przejdź do gry i daj się uderzyć. Po wykryciu Kopacz wykona pełną reakcję według trybu po alarmie.", "Orange");
        UpdateEnabledStates();
    }

    private async void BtnProtectionTestAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest() || !await CountdownToGameAsync("Test: pełny alarm (symulacja)"))
            return;

        _protectionTestInputActive = true;
        try
        {
            if (!EnsureGameInputForTest())
                return;
            UpdateProtectionTestStatus(IsEmergencyReconnectEnabled()
                ? "Symulowany alarm: wyjście z serwera, potem awaryjny reconnect, kontrola kilofa i home. Postęp w sekcji Po alarmie."
                : "Symulowany alarm: wyjście z serwera (tryb tylko Disconnect — bez powrotu).", "Orange");
            await RunEmergencyProtectionAsync("symulowany alarm z przycisku", "symulacja alarmu (przycisk testowy)");
        }
        finally
        {
            _protectionTestInputActive = false;
        }
    }

    // Pełny cykl: kanał, który ma ruszyć po udanym reconnekcie (None = brak testu w toku).
    private InventoryCleanupOwner _fullCycleTestStartOwner = InventoryCleanupOwner.None;
    private int _fullCycleTestGeneration;

    private InventoryCleanupOwner TakeFullCycleTestOwner()
    {
        InventoryCleanupOwner owner = _fullCycleTestStartOwner;
        _fullCycleTestStartOwner = InventoryCleanupOwner.None;
        return owner;
    }

    // Wyjście z serwera → odczekanie (jak po alarmie) → pełny reconnect (lista serwerów, Direct Connect,
    // wejście, /home, kontrola EQ) → start kopania tym samym kanałem, który kopał albo ruszyłby START.
    private async void BtnProtectionTestFullCycle_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartProtectionTest())
            return;

        bool wasMining = _kopacz533RuntimeEnabled || _kopacz633RuntimeEnabled;
        InventoryCleanupOwner owner = _kopacz533RuntimeEnabled
            ? InventoryCleanupOwner.Kopacz533
            : _kopacz633RuntimeEnabled ? InventoryCleanupOwner.Kopacz633 : GetMiningStartOwner();
        if (owner == InventoryCleanupOwner.None)
        {
            UpdateProtectionTestStatus("Zaznacz kanał kopania (5/3/3 albo 6/3/3) w zakładce Kopanie — test na końcu uruchamia kopanie.", "Red");
            return;
        }
        if (!wasMining && GetMiningStartBlocker(owner) is { } blocker)
        {
            UpdateProtectionTestStatus("Kopanie nie mogłoby ruszyć: " + blocker, "Red");
            return;
        }
        if (GetSelectedAutoReconnectServerProfile() == null)
        {
            UpdateProtectionTestStatus("Wybierz profil powrotu (serwer i home) w sekcji Profil powrotu.", "Red");
            return;
        }
        if (!await CountdownToGameAsync("Test: pełny cykl z kopaniem"))
            return;

        int generation = ++_fullCycleTestGeneration;
        _protectionTestInputActive = true;
        try
        {
            if (!EnsureGameInputForTest())
                return;
            if (wasMining)
                StopMiningFromRemoteCommand();

            UpdateProtectionTestStatus("1/4 Wychodzę z serwera (ESC → Disconnect)…", "Orange");
            (bool disconnected, string result) = await TryDisconnectMinecraftAfterEmergencyAsync(
                _targetGameWindowHandle,
                IsMinecraftGuiLikelyOpenForEmergency());
            if (!disconnected)
            {
                UpdateProtectionTestStatus("Pełny cykl przerwany — wyjście z serwera nie powiodło się: " + result + ".", "Red");
                return;
            }

            // Ten sam odstęp co po prawdziwym alarmie — serwer i proxy muszą zwolnić sesję.
            int delay = Math.Clamp(_settings.EmergencyReconnectDelaySeconds, 1, 600);
            for (int left = delay; left > 0; left--)
            {
                if (generation != _fullCycleTestGeneration)
                    return;
                UpdateProtectionTestStatus($"2/4 Wyszedłem z serwera. Reconnect za {left} s…", "Orange");
                await Task.Delay(1000);
            }
            if (generation != _fullCycleTestGeneration)
                return;

            _fullCycleTestStartOwner = owner;
            _fullCycleStartFromDiscord = false;
            UpdateProtectionTestStatus($"3/4 Reconnect: lista serwerów, wejście, /home i kontrola EQ — potem ruszy {GetInventoryCleanupOwnerLabel(owner)}. Postęp w „Wynik testów reconnectu”.", "Orange");
            BeginFullAutoReconnect(manual: true);
            if (_autoReconnectStage == AutoReconnectStage.None)
            {
                _fullCycleTestStartOwner = InventoryCleanupOwner.None;
                UpdateProtectionTestStatus("Pełny cykl przerwany — reconnect się nie uruchomił. Sprawdź profil powrotu.", "Red");
            }
        }
        finally
        {
            _protectionTestInputActive = false;
        }
    }

    // 4/4: po potwierdzonym powrocie na serwer — start kopania tą samą drogą co !start z Discorda
    // (w tle, gdy gra nie jest na wierzchu).
    private void StartMiningAfterFullCycleTest(InventoryCleanupOwner owner)
    {
        (bool started, string message) = StartMiningFromRemoteCommand(owner);
        if (_fullCycleStartFromDiscord)
        {
            ReportDiscordJoinResult(started, message);
            return;
        }
        UpdateProtectionTestStatus(started
                ? "4/4 Pełny cykl zakończony — " + message
                : "Reconnect udany, ale kopanie nie ruszyło: " + message,
            started ? "Green" : "Red");
    }

    // Zatrzymuje każdy test i procedurę naraz: nasłuch, awaryjny powrót i reconnect.
    private void BtnProtectionTestStop_Click(object sender, RoutedEventArgs e)
    {
        _fullCycleTestGeneration++;
        BtnEmergencyDamageSoundStop_Click(sender, e);
        BtnEmergencyReconnectStop_Click(sender, e);
        // StopAutoReconnect czyści też start kopania z testu pełnego cyklu i z !join (z powiadomieniem).
        BtnAutoReconnectStop_Click(sender, e);
        UpdateProtectionTestStatus("Zatrzymano testy, nasłuch i procedury powrotu.", "Orange");
    }
}
