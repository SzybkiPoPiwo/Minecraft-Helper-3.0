using System;
using System.Threading.Tasks;
using MinecraftHelper.Models;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Komendy !join i !leave z Discorda. Korzystają z tych samych procedur co Auto Reconnect
// i test pełnego cyklu: wyjście to ESC → Disconnect, wejście to pełny reconnect (lista
// serwerów, Direct Connect, /home, kontrola EQ), po którym rusza kopanie.
public partial class MainWindow
{
    // Start kopania po reconnekcie zlecił Discord (!join), a nie przycisk testu.
    private bool _fullCycleStartFromDiscord;

    bool IMinerRemoteSession.TryJoinServer(string? mode, out string message)
    {
        InventoryCleanupOwner requested = mode switch
        {
            "533" => InventoryCleanupOwner.Kopacz533,
            "633" => InventoryCleanupOwner.Kopacz633,
            _ => InventoryCleanupOwner.None
        };
        (bool joining, string text) = OnUiThread(() => JoinServerFromRemoteCommand(requested));
        message = text;
        return joining;
    }

    bool IMinerRemoteSession.TryLeaveServer(out string message)
    {
        (bool leaving, string text) = OnUiThread(LeaveServerFromRemoteCommand);
        message = text;
        return leaving;
    }

    // Poza grą wolno sterować tylko przez sesję w tle — inaczej klawisze trafiłyby
    // do aplikacji, w której użytkownik właśnie pracuje.
    private string? GetRemoteGameInputBlocker()
    {
        IntPtr window = _targetGameWindowHandle;
        if (window == IntPtr.Zero)
            return _gameWindowOwnedByAnotherProfile
                ? "wybrane okno Minecrafta należy do innego konta."
                : "najpierw wybierz okno Minecrafta.";
        if (GetForegroundWindow() == window)
            return null;
        if (!IsBackgroundMiningSelected())
            return "włącz „Kopanie w tle” w tym profilu albo przejdź do okna gry.";
        if (HasForegroundOnlyAutomationRunning())
            return "najpierw wyłącz auto łowienie — działa tylko z grą na wierzchu.";
        if (!BackgroundGameWindow.CanUse(window))
            return "okno gry jest niedostępne dla sterowania w tle (np. zminimalizowane).";
        return null;
    }

    private bool IsProtectionOrReconnectRunning() =>
        _emergencyDamageSoundHandlingAlarm || _emergencyReconnectActive || _autoReconnectStage != AutoReconnectStage.None;

    private (bool Joining, string Message) JoinServerFromRemoteCommand(InventoryCleanupOwner requested)
    {
        if (HasMiningWork(InventoryCleanupOwner.Kopacz533) || HasMiningWork(InventoryCleanupOwner.Kopacz633))
            return (false, "Kopacz już kopie — wejście na serwer nie jest potrzebne.");
        if (IsProtectionOrReconnectRunning())
            return (false, "trwa ochrona albo reconnect — poczekaj na koniec albo użyj stop.");
        if (!CanRunAutoReconnectTest(out string reconnectError))
            return (false, reconnectError);
        if (GetMiningStartBlocker(requested) is { } blocker)
            return (false, blocker);
        if (GetRemoteGameInputBlocker() is { } inputBlocker)
            return (false, inputBlocker);
        AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
        if (profile == null)
            return (false, "wybierz profil powrotu (serwer i home) w zakładce Ochrona.");

        InventoryCleanupOwner owner = requested != InventoryCleanupOwner.None ? requested : GetMiningStartOwner();
        _fullCycleTestGeneration++;
        _fullCycleTestStartOwner = owner;
        _fullCycleStartFromDiscord = true;
        BeginFullAutoReconnect(manual: true);
        if (_autoReconnectStage == AutoReconnectStage.None)
        {
            _fullCycleTestStartOwner = InventoryCleanupOwner.None;
            _fullCycleStartFromDiscord = false;
            return (false, "reconnect się nie uruchomił — sprawdź profil powrotu w zakładce Ochrona.");
        }

        AppLog.Warn("Discord", $"!join: reconnect z profilem „{profile.Name}”, potem {GetInventoryCleanupOwnerLabel(owner)}.");
        UpdateStatusBar($"Discord !join: wchodzę na serwer, potem ruszy {GetInventoryCleanupOwnerLabel(owner)}.", "Orange");
        return (true, $"wchodzę na serwer (profil „{profile.Name}”, powrót {_autoReconnectActiveHomeCommand}) — potem ruszy {GetInventoryCleanupOwnerLabel(owner)}.");
    }

    // Wynik !join: wywoływane po udanym reconnekcie (start kopania) albo po jego przerwaniu.
    private void ReportDiscordJoinResult(bool started, string message)
    {
        _fullCycleStartFromDiscord = false;
        string prefix = DiscordSettingsStore.Load().Prefix;
        string server = GetSelectedAutoReconnectServerProfile()?.Name?.Trim() is { Length: > 0 } name ? name : "—";
        NotifyDiscord(
            DiscordAlertKind.CommandResult,
            started ? "✅ Wszedłem na serwer i kopię" : "❌ Wejście na serwer nieudane",
            started
                ? $"Komenda `{prefix}join`: reconnect, /home i kontrola EQ zakończone — {message}"
                : $"Komenda `{prefix}join`: {message}",
            fields: new DiscordEmbedField[]
            {
                new("🌐 Serwer", server, true),
                new("🏠 Powrót", _autoReconnectActiveHomeCommand, true),
                new("⛏️ Kopanie", started ? "uruchomione" : "nie ruszyło", true)
            },
            color: started ? DiscordColors.Ok : DiscordColors.Error,
            // Po kontroli kilofa EQ jest zamykane tuż przed startem — zrzut po jego zniknięciu.
            screenshotDelay: started ? ScreenshotAfterInventoryClose : TimeSpan.Zero);
    }

    private (bool Leaving, string Message) LeaveServerFromRemoteCommand()
    {
        if (_emergencyDamageSoundHandlingAlarm || _emergencyReconnectActive)
            return (false, "trwa procedura ochrony — poczekaj na jej koniec albo użyj stop.");
        if (_leaveServerInProgress)
            return (false, "wychodzenie z serwera już trwa.");
        if (GetRemoteGameInputBlocker() is { } inputBlocker)
            return (false, inputBlocker);

        bool wasMining = HasMiningWork(InventoryCleanupOwner.Kopacz533) || HasMiningWork(InventoryCleanupOwner.Kopacz633);
        if (wasMining)
            StopMiningFromRemoteCommand();
        // Trwający reconnect (np. wcześniejszy !join) wszedłby z powrotem na serwer.
        if (_autoReconnectStage != AutoReconnectStage.None)
            StopAutoReconnect("Reconnect przerwany komendą leave z Discorda.", resumeMining: false, warning: false);

        _ = LeaveServerAsync();
        return (true, wasMining ? "zatrzymano kopanie, wychodzę z serwera…" : "wychodzę z serwera…");
    }

    private bool _leaveServerInProgress;

    private async Task LeaveServerAsync()
    {
        string prefix = DiscordSettingsStore.Load().Prefix;
        _leaveServerInProgress = true;
        // Jak w testach ochrony: utrzymuje sesję w tle na czas ESC i kliknięcia Disconnect.
        _protectionTestInputActive = true;
        try
        {
            IntPtr window = _targetGameWindowHandle;
            UpdateBackgroundMiningSession(GetForegroundWindow() == window);
            if (!IsGameInputAvailableFor(window))
            {
                NotifyDiscord(DiscordAlertKind.CommandResult, "❌ Nie udało się wyjść z serwera",
                    $"Komenda `{prefix}leave`: gra jest niedostępna dla sterowania (zminimalizowana albo bez fokusu).",
                    color: DiscordColors.Error);
                return;
            }

            (bool disconnected, string result) = await TryDisconnectMinecraftAfterEmergencyAsync(
                window,
                IsMinecraftGuiLikelyOpenForEmergency());
            AppLog.Warn("Discord", disconnected ? "!leave: kliknięto Disconnect." : "!leave nieudane: " + result);
            UpdateStatusBar(disconnected ? "Discord !leave: wyszedłem z serwera." : "Discord !leave nieudane: " + result, disconnected ? "Green" : "Red");
            NotifyDiscord(DiscordAlertKind.CommandResult,
                disconnected ? "🚪 Wyszedłem z serwera" : "❌ Nie udało się wyjść z serwera",
                disconnected
                    ? $"Komenda `{prefix}leave`: kliknięto Disconnect. Powrót na serwer z kopaniem: `{prefix}join`."
                    : $"Komenda `{prefix}leave`: {result}.",
                color: disconnected ? DiscordColors.Warn : DiscordColors.Error,
                // Zrzut ma pokazać ekran poza serwerem, a nie świat gry sprzed kliknięcia.
                screenshotDelay: disconnected ? ScreenshotAfterDisconnect : TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            AppLog.Error("Discord", "Błąd komendy leave", ex);
        }
        finally
        {
            _leaveServerInProgress = false;
            _protectionTestInputActive = false;
            UpdateBackgroundMiningSession(GetForegroundWindow() == _targetGameWindowHandle);
        }
    }
}
