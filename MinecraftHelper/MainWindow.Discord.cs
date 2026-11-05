using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Karta „Bot Discord” w Ustawieniach oraz powiadomienia tego konta.
// Ustawienia są wspólne (discord.json), bot z komendami działa w głównym procesie Kopacza.
public partial class MainWindow
{
    // Ile poczekać ze zrzutem, aż gra pokaże skutek akcji: po Disconnect pojawia się lista
    // serwerów (albo ekran rozłączenia), a po zamknięciu EQ — świat gry zamiast ekwipunku.
    internal static readonly TimeSpan ScreenshotAfterDisconnect = TimeSpan.FromSeconds(2.5);
    internal static readonly TimeSpan ScreenshotAfterInventoryClose = TimeSpan.FromSeconds(1.5);

    private void NotifyDiscord(DiscordAlertKind kind, string title, string? description, bool urgent = false,
        IReadOnlyList<DiscordEmbedField>? fields = null, int? color = null, TimeSpan screenshotDelay = default) =>
        DiscordNotifier.Notify(kind, _accountDisplayName, title, description, _targetGameWindowHandle, urgent, fields, color, screenshotDelay);

    // Trwa !join z Discorda: jego wynik ogłasza jedna karta (ReportDiscordJoinResult), więc
    // karty reconnectu i startu kopania z tej samej procedury byłyby duplikatami.
    private bool IsDiscordJoinAnnouncing => _fullCycleStartFromDiscord;

    private void NotifyDiscordMiningStarted(string mode, string direction, string cleanupMode, bool cobbleX, bool eatAfterCleanup)
    {
        if (IsDiscordJoinAnnouncing)
            return;
        NotifyDiscord(DiscordAlertKind.MiningStartStop, "▶️ Kopanie uruchomione", null, fields: new DiscordEmbedField[]
        {
            new("⛏️ Tryb", mode, true),
            new("🧭 Ruch", direction, true),
            new("🎒 Auto EQ", cleanupMode, true),
            new("🧱 CobbleX", cobbleX ? "włączony" : "wyłączony", true),
            new("🍖 Jedzenie po EQ", eatAfterCleanup ? "włączone" : "wyłączone", true)
        });
    }

    private static readonly TimeSpan StatusErrorDelay = TimeSpan.FromSeconds(2);
    private int _discordStatusErrorGeneration;

    // Czerwone komunikaty paska: tylko w trakcie kopania (przy komputerze widać je w oknie),
    // po 2 s i jako jedna karta — kilka komunikatów z jednej awarii zastępuje ostatni, a koniec
    // sesji w tym czasie anuluje kartę, bo „Kopanie przerwane” podaje już powód.
    private void NotifyDiscordStatusError(AppLogEntry entry)
    {
        if (!_kopacz533RuntimeEnabled && !_kopacz633RuntimeEnabled)
            return;
        int generation = ++_discordStatusErrorGeneration;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(StatusErrorDelay);
            if (generation == _discordStatusErrorGeneration && (_kopacz533RuntimeEnabled || _kopacz633RuntimeEnabled))
                NotifyDiscord(DiscordAlertKind.Error, "❗ Błąd: " + entry.Source, entry.Message);
        });
    }

    // Koniec sesji z podsumowaniem od jej uruchomienia (dziennik ma już zapisany koniec).
    private void NotifyDiscordMiningEnded(string mode, string reason, bool regularEnd, string runId)
    {
        _discordStatusErrorGeneration++;
        var fields = new List<DiscordEmbedField> { new("⛏️ Tryb", mode, true) };
        fields.AddRange(DiscordLogReport.BuildSessionFields(_miningLogService.GetEntriesNewestFirst(), runId, DateTimeOffset.Now));
        NotifyDiscord(
            regularEnd ? DiscordAlertKind.MiningStartStop : DiscordAlertKind.Error,
            regularEnd ? "⏹️ Kopanie zakończone" : "⚠️ Kopanie przerwane",
            reason,
            fields: fields,
            color: regularEnd ? DiscordColors.Neutral : null);
    }

    // Zdarzenia ochrony i reconnectu z dziennika automatyzacji. Kontrole połączenia
    // (health check) pomijamy — są cykliczne i zalałyby kanał.
    private void NotifyDiscordAutomationEvent(string eventType, string status, string details)
    {
        // Moment alarmu (zatrzymanie, Disconnect, ewentualny błąd) ogłasza jedno
        // powiadomienie NotifyDiscordServerExit — zdarzenia z tej chwili byłyby duplikatami.
        if (_emergencyDamageSoundHandlingAlarm)
            return;

        // Nieudany reconnect zostawia konto poza serwerem — to wymaga reakcji.
        bool failed = status == MiningLogStatuses.Aborted;
        (DiscordAlertKind Kind, string Title)? alert = eventType switch
        {
            MiningLogEventTypes.EmergencyShutdown => (DiscordAlertKind.Protection, "🛑 Koniec pracy konta po awaryjnym home"),
            MiningLogEventTypes.AutoReconnectStarted => (DiscordAlertKind.Reconnect, "🔄 Reconnect — łączę ponownie"),
            MiningLogEventTypes.AutoReconnectFinished => (DiscordAlertKind.Reconnect, failed ? "❌ Reconnect nieudany — konto poza serwerem" : "✅ Reconnect zakończony"),
            MiningLogEventTypes.EmergencyReconnectJoined => (DiscordAlertKind.Reconnect, "🔌 Wróciłem na serwer po alarmie"),
            MiningLogEventTypes.EmergencyMiningResumed => (DiscordAlertKind.Reconnect, "⛏️ Wznowiono kopanie po alarmie"),
            MiningLogEventTypes.EmergencyProtectionFinished => (DiscordAlertKind.Reconnect, failed ? "❌ Powrót po alarmie nieudany" : "🛡️ Ochrona po alarmie zakończona"),
            MiningLogEventTypes.MissingPickaxeRecovery => (DiscordAlertKind.MissingPickaxe, "⛏️ Brak kilofa — wracam na home"),
            MiningLogEventTypes.MissingPickaxeRecoveryFinished => (DiscordAlertKind.MissingPickaxe, "⛏️ Brak kilofa — powrót zakończony"),
            MiningLogEventTypes.EmergencyPickaxeMissing => (DiscordAlertKind.MissingPickaxe, "⛏️ Brak kilofa po powrocie na serwer"),
            _ => null
        };
        if (IsDiscordJoinAnnouncing && eventType is MiningLogEventTypes.AutoReconnectStarted or MiningLogEventTypes.AutoReconnectFinished)
            return;
        bool urgent = failed && eventType is MiningLogEventTypes.AutoReconnectFinished or MiningLogEventTypes.EmergencyProtectionFinished;
        // Udany reconnect kończy się zamknięciem EQ po kontroli kilofa — zrzut po zniknięciu ekwipunku.
        TimeSpan delay = !failed && eventType == MiningLogEventTypes.AutoReconnectFinished ? ScreenshotAfterInventoryClose : TimeSpan.Zero;
        if (alert != null)
            NotifyDiscord(alert.Value.Kind, alert.Value.Title, details.Split('\n')[0], urgent, screenshotDelay: delay);
    }

    // Jedno powiadomienie z oznaczeniem po alarmie obrażeń: czy konto wyszło z serwera
    // i co dalej. Zrzut powstaje chwilę po Disconnect, gdy gra pokazuje już ekran poza serwerem.
    private void NotifyDiscordServerExit(bool disconnected, bool reconnectStarted, string measurement, string result)
    {
        string next = !disconnected
            ? "**Sprawdź grę!**"
            : reconnectStarted
                ? $"reconnect za {Math.Clamp(_settings.EmergencyReconnectDelaySeconds, 1, 600)} s"
                : "bez ponownego wejścia";
        NotifyDiscord(
            DiscordAlertKind.Protection,
            disconnected ? "🚨 Alarm obrażeń — wyszedłem z serwera" : "🚨 Alarm obrażeń — NIE udało się wyjść z serwera",
            disconnected
                ? "Kopacz usłyszał dźwięk obrażeń, zatrzymał kopanie i kliknął **Disconnect**."
                : $"Kopacz zatrzymał kopanie, ale nie udało się kliknąć Disconnect: {result}.",
            urgent: true,
            fields: new DiscordEmbedField[]
            {
                new("🔊 Wykryty dźwięk", measurement, true),
                new("⛏️ Kopanie", "zatrzymane", true),
                new("➡️ Co dalej", next, true)
            },
            screenshotDelay: disconnected ? ScreenshotAfterDisconnect : TimeSpan.Zero);
    }

    private void NotifyDiscordAlarmTest() =>
        NotifyDiscord(
            DiscordAlertKind.Protection,
            "🧪 Test alarmu obrażeń",
            "Tryb testowy — Kopacz **nie wychodzi** z serwera. Tak wygląda alarm z oznaczeniem.",
            urgent: true,
            color: DiscordColors.Accent);

    private void LoadDiscordSettingsToUi()
    {
        DiscordSettings settings = DiscordSettingsStore.Load();
        ChkDiscordEnabled.IsChecked = settings.Enabled;
        TxtDiscordChannelId.Text = settings.ChannelId;
        TxtDiscordOwnerIds.Text = settings.OwnerUserIds;
        TxtDiscordPrefix.Text = settings.Prefix;
        TxtDiscordScreenshotMinutes.Text = settings.ScreenshotIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        TxtDiscordStatusMinutes.Text = settings.StatusIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        ChkDiscordNotifyProtection.IsChecked = settings.NotifyProtection;
        ChkDiscordNotifyReconnect.IsChecked = settings.NotifyReconnect;
        ChkDiscordNotifyPickaxe.IsChecked = settings.NotifyMissingPickaxe;
        ChkDiscordNotifyStartStop.IsChecked = settings.NotifyMiningStartStop;
        ChkDiscordNotifyErrors.IsChecked = settings.NotifyErrors;
        ChkDiscordAttachScreenshot.IsChecked = settings.AttachScreenshotToAlerts;
        ChkDiscordMention.IsChecked = settings.MentionOwnersOnAlerts;
        PwdDiscordToken.Password = string.Empty;
        UpdateDiscordTokenState(settings);
        RefreshDiscordCommandPills(settings);
        RefreshDiscordStatus();
    }

    // Okno z przełącznikiem przy każdej komendzie; zapis od razu trafia do bota.
    private void BtnDiscordCommands_Click(object sender, RoutedEventArgs e)
    {
        DiscordSettings current = DiscordSettingsStore.Load();
        var window = new DiscordCommandsWindow(current) { Owner = this };
        if (window.ShowDialog() != true)
            return;

        // Świeży odczyt: między otwarciem okna a zapisem ktoś mógł zapisać kartę w innym oknie konta.
        DiscordSettings settings = DiscordSettingsStore.Load();
        settings.DisabledCommands = window.DisabledCommandIds;
        try
        {
            DiscordSettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            UpdateStatusBar("Nie udało się zapisać komend bota: " + ex.Message, "Red");
            return;
        }

        RefreshDiscordCommandPills(settings);
        int disabled = settings.DisabledCommands.Count;
        AppLog.Info("Discord", disabled == 0 ? "Wszystkie komendy bota włączone." : "Wyłączone komendy bota: " + string.Join(", ", settings.DisabledCommands) + ".");
        UpdateStatusBar(disabled == 0
            ? "Zapisano: wszystkie komendy bota są włączone."
            : $"Zapisano: wyłączone komendy ({disabled}) — bot odpowie, że są wyłączone.", "Green");
    }

    private void RefreshDiscordCommandPills(DiscordSettings settings)
    {
        PanelDiscordCommandPills.Children.Clear();
        AddDiscordCommandPill(DiscordCommands.Help, DiscordCommandKind.Normal, enabled: true);
        foreach (DiscordCommandInfo command in DiscordCommands.All)
            AddDiscordCommandPill(command.Usage, command.Kind, settings.IsCommandEnabled(command.Id));
    }

    private void AddDiscordCommandPill(string usage, DiscordCommandKind kind, bool enabled)
    {
        var text = new TextBlock { Text = usage, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "GlassMonoFont");
        var pill = new Border { Style = GlassChrome.GetStyle("GlassPill"), Margin = new Thickness(0, 0, 6, 6), Child = text };
        if (kind != DiscordCommandKind.Normal)
        {
            pill.Background = GlassChrome.GetBrush(kind == DiscordCommandKind.Start ? "GlassOkSoftBrush" : "GlassBadSoftBrush");
            text.Foreground = GlassChrome.GetBrush(kind == DiscordCommandKind.Start ? "GlassOkBrush" : "GlassBadBrush");
        }
        if (!enabled)
        {
            pill.Opacity = 0.45;
            text.TextDecorations = TextDecorations.Strikethrough;
            pill.ToolTip = "Wyłączona — zmień w „Włącz / wyłącz komendy”.";
        }
        PanelDiscordCommandPills.Children.Add(pill);
    }

    // Wklejony token nie jest jeszcze zapisany — mówimy to wprost, zamiast pokazywać stary stan.
    private void PwdDiscordToken_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (PwdDiscordToken.Password.Trim().Length > 0)
            TxtDiscordTokenState.Text = "Nowy token jeszcze niezapisany — naciśnij Enter albo kliknij „Zapisz ustawienia Discord”.";
        else
            UpdateDiscordTokenState(DiscordSettingsStore.Load());
    }

    private void UpdateDiscordTokenState(DiscordSettings settings)
    {
        TxtDiscordTokenState.Text = settings.HasToken
            ? "Token zapisany (zaszyfrowany). Wklej nowy, aby go zmienić; puste pole zostawia obecny."
            : "Brak zapisanego tokenu.";
    }

    // Zbiera pola karty; token tylko wtedy, gdy wpisano nowy.
    private DiscordSettings ReadDiscordSettingsFromUi(out string? error)
    {
        error = null;
        DiscordSettings settings = DiscordSettingsStore.Load();
        string channel = TxtDiscordChannelId.Text.Trim();
        string owners = TxtDiscordOwnerIds.Text.Trim();
        if (channel.Length > 0 && !channel.All(char.IsDigit))
            error = "ID kanału to same cyfry — PPM na kanał → Kopiuj ID kanału.";
        else if (owners.Length > 0 && owners.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Any(id => !id.All(char.IsDigit)))
            error = "ID właścicieli to same cyfry, oddzielone przecinkami.";

        var updated = new DiscordSettings
        {
            Enabled = ChkDiscordEnabled.IsChecked == true,
            ChannelId = channel,
            OwnerUserIds = owners,
            CommandPrefix = string.IsNullOrWhiteSpace(TxtDiscordPrefix.Text) ? "!" : TxtDiscordPrefix.Text.Trim(),
            ScreenshotIntervalMinutes = Math.Clamp(ParseNonNegativeInt(TxtDiscordScreenshotMinutes.Text), 0, 1440),
            StatusIntervalMinutes = Math.Clamp(ParseNonNegativeInt(TxtDiscordStatusMinutes.Text), 0, 1440),
            NotifyProtection = ChkDiscordNotifyProtection.IsChecked == true,
            NotifyReconnect = ChkDiscordNotifyReconnect.IsChecked == true,
            NotifyMissingPickaxe = ChkDiscordNotifyPickaxe.IsChecked == true,
            NotifyMiningStartStop = ChkDiscordNotifyStartStop.IsChecked == true,
            NotifyErrors = ChkDiscordNotifyErrors.IsChecked == true,
            AttachScreenshotToAlerts = ChkDiscordAttachScreenshot.IsChecked == true,
            MentionOwnersOnAlerts = ChkDiscordMention.IsChecked == true,
            TokenProtected = settings.TokenProtected,
            // Ustawiane w oknie „Komendy bota”, nie w polach karty — przenosimy bez zmian.
            DisabledCommands = settings.DisabledCommands?.ToList() ?? new()
        };
        if (PwdDiscordToken.Password.Trim().Length > 0)
            DiscordSettingsStore.SetToken(updated, PwdDiscordToken.Password);
        return updated;
    }

    private void BtnDiscordSave_Click(object sender, RoutedEventArgs e) => SaveDiscordSettingsFromUi();

    // Enter w polu tokenu zapisuje ustawienia — wklejenie i Enter to naturalny sposób zatwierdzenia.
    private void PwdDiscordToken_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter)
            return;
        e.Handled = true;
        SaveDiscordSettingsFromUi();
    }

    private bool SaveDiscordSettingsFromUi(string? successPrefix = null)
    {
        DiscordSettings settings = ReadDiscordSettingsFromUi(out string? error);
        if (error != null)
        {
            UpdateStatusBar(error, "Orange");
            return false;
        }

        // Pierwszy zapis tokenu szedł z wyłączonym botem (przełącznik w rogu karty łatwo
        // przeoczyć) — token był zapisany, ale bot milczał do drugiego zapisu. Wpisanie
        // nowego tokenu oznacza chęć użycia bota, więc go włączamy.
        bool enabledByToken = false;
        if (!settings.Enabled && PwdDiscordToken.Password.Trim().Length > 0)
        {
            settings.Enabled = true;
            ChkDiscordEnabled.IsChecked = true;
            enabledByToken = true;
        }

        try
        {
            DiscordSettingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            UpdateStatusBar("Nie udało się zapisać ustawień Discord: " + ex.Message, "Red");
            return false;
        }

        PwdDiscordToken.Password = string.Empty;
        UpdateDiscordTokenState(settings);
        AppLog.Info("Discord", settings.Enabled ? "Zapisano ustawienia bota (włączony)." : "Zapisano ustawienia bota (wyłączony).");
        string prefix = successPrefix ?? "Zapisano ustawienia Discord.";
        if (!settings.Enabled)
            UpdateStatusBar(prefix + " Bot jest wyłączony — włącz przełącznik w rogu karty „Bot Discord” i zapisz ponownie, aby odpowiadał na komendy.", "Orange");
        else if (settings.ChannelId.Length == 0)
            UpdateStatusBar(prefix + " Uzupełnij jeszcze ID kanału — bez niego bot się nie połączy.", "Orange");
        else if (settings.GetOwnerIds().Count == 0)
            UpdateStatusBar(prefix + " Dodaj swoje ID właściciela — bez niego bot nie przyjmie żadnej komendy (powiadomienia działają).", "Orange");
        else
            UpdateStatusBar(prefix + (enabledByToken ? " Bot został włączony i" : " Bot") + " połączy się w ciągu kilku sekund.", "Green");
        RefreshDiscordStatus();
        return true;
    }

    private DiscordSetupGuideWindow? _discordGuideWindow;

    // Poradnik bez blokowania Kopacza — użytkownik przełącza się między nim, przeglądarką i tą kartą.
    private void BtnDiscordGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_discordGuideWindow != null)
        {
            if (_discordGuideWindow.WindowState == WindowState.Minimized)
                _discordGuideWindow.WindowState = WindowState.Normal;
            _discordGuideWindow.Activate();
            return;
        }

        _discordGuideWindow = new DiscordSetupGuideWindow { Owner = this };
        _discordGuideWindow.Closed += (_, _) => _discordGuideWindow = null;
        _discordGuideWindow.Show();
    }

    private void BtnDiscordClearToken_Click(object sender, RoutedEventArgs e)
    {
        DiscordSettings settings = DiscordSettingsStore.Load();
        settings.TokenProtected = string.Empty;
        DiscordSettingsStore.Save(settings);
        PwdDiscordToken.Password = string.Empty;
        UpdateDiscordTokenState(settings);
        UpdateStatusBar("Usunięto token bota Discord.", "Orange");
    }

    // Test z bieżących pól (także niezapisanych): sprawdza token, kanał i uprawnienia bota.
    private async void BtnDiscordTest_Click(object sender, RoutedEventArgs e)
    {
        DiscordSettings settings = ReadDiscordSettingsFromUi(out string? error);
        string? token = PwdDiscordToken.Password.Trim().Length > 0 ? PwdDiscordToken.Password.Trim() : DiscordSettingsStore.GetToken(settings);
        if (error != null || token == null || settings.ChannelId.Length == 0)
        {
            UpdateStatusBar(error ?? "Do testu potrzebny jest token bota i ID kanału.", "Orange");
            return;
        }

        BtnDiscordTest.IsEnabled = false;
        try
        {
            string botName = await DiscordRest.GetBotNameAsync(token);
            string mention = string.Concat(settings.GetOwnerIds().Select(id => $"<@{id}> "));
            var embed = new DiscordEmbed("✅ Bot Kopacza działa", DiscordColors.Ok)
            {
                Author = "👤 " + _accountDisplayName,
                Description = $"Wiadomość testowa z ustawień. Wpisz `{settings.Prefix}pomoc`, aby zobaczyć komendy."
            }
                .Field("🤖 Bot", botName, true)
                .Field("🔔 Komendy", settings.Enabled ? "włączone" : "wyłączone — włącz przełącznik i zapisz", true);
            await DiscordRest.SendEmbedAsync(token, settings.ChannelId, embed, mention.Length > 0 ? mention + "Test z Kopacza" : null);
            // Token sprawdzony przez Discorda — zapisujemy go od razu, żeby udany test
            // nie zostawiał niezapisanego tokenu w polu.
            if (PwdDiscordToken.Password.Trim() == token)
                SaveDiscordSettingsFromUi($"Wysłano test jako {botName} i zapisano token.");
            else if (settings.Enabled)
                UpdateStatusBar($"Wysłano wiadomość testową jako {botName}.", "Green");
            else
                UpdateStatusBar($"Wysłano test jako {botName}. Teraz włącz przełącznik w rogu karty i kliknij „Zapisz ustawienia Discord” — dopiero wtedy bot odpowie na komendy.", "Orange");
        }
        catch (Exception ex)
        {
            UpdateStatusBar("Test Discord nie powiódł się: " + ex.GetBaseException().Message, "Red");
        }
        finally
        {
            BtnDiscordTest.IsEnabled = true;
        }
    }

    // Przykład każdego włączonego powiadomienia, z bieżących pól karty (także niezapisanych):
    // ten sam wygląd, oznaczenie i zrzut z okna gry tego konta co przy prawdziwym zdarzeniu.
    private async void BtnDiscordTestNotifications_Click(object sender, RoutedEventArgs e)
    {
        DiscordSettings settings = ReadDiscordSettingsFromUi(out string? error);
        string? token = PwdDiscordToken.Password.Trim().Length > 0 ? PwdDiscordToken.Password.Trim() : DiscordSettingsStore.GetToken(settings);
        if (error != null || token == null || settings.ChannelId.Length == 0)
        {
            UpdateStatusBar(error ?? "Do testu potrzebny jest token bota i ID kanału.", "Orange");
            return;
        }

        DiscordAlertSample[] samples = DiscordNotificationSamples.All.Where(sample => DiscordNotifier.IsEnabled(settings, sample.Kind)).ToArray();
        if (samples.Length == 0)
        {
            UpdateStatusBar("Wszystkie powiadomienia są wyłączone — zaznacz przynajmniej jedno w karcie Bot Discord.", "Orange");
            return;
        }

        BtnDiscordTestNotifications.IsEnabled = false;
        try
        {
            IntPtr window = _targetGameWindowHandle;
            byte[]? screenshot = settings.AttachScreenshotToAlerts && window != IntPtr.Zero
                ? await Task.Run(() => MinerLivePreviewService.TryCapture(window, MinerPreviewRegion.Full, 1280, out MinerLiveFrame? frame) ? frame?.Jpeg : null)
                : null;

            var header = new DiscordEmbed("🧪 Test powiadomień", DiscordColors.Accent)
            {
                Author = "👤 " + _accountDisplayName,
                Description = $"Poniżej przykład każdego włączonego powiadomienia ({samples.Length}). To **TEST** — w grze nic się nie stało."
            }.Field("📸 Zrzut ekranu", screenshot != null ? "z okna gry tego konta" : settings.AttachScreenshotToAlerts ? "brak okna gry — bez zrzutu" : "wyłączony", true)
             .Field("🔔 Oznaczenie", settings.MentionOwnersOnAlerts && settings.GetOwnerIds().Count > 0 ? "przy ważnych zdarzeniach" : "wyłączone", true);
            await DiscordRest.SendEmbedAsync(token, settings.ChannelId, header);

            for (int index = 0; index < samples.Length; index++)
            {
                DiscordAlertSample sample = samples[index];
                UpdateStatusBar($"Wysyłam test powiadomień {index + 1}/{samples.Length}…", "DeepSkyBlue");
                (DiscordEmbed embed, string? content) = DiscordNotifier.BuildAlert(
                    settings, sample.Kind, _accountDisplayName, sample.Title, sample.Description, sample.Urgent, sample.Fields, sample.Color);
                embed.Footer = DiscordEmbed.DefaultFooter + " • TEST";
                byte[]? image = DiscordNotifier.WantsScreenshot(settings, sample.Kind) ? screenshot : null;
                await Task.Delay(800);
                await DiscordRest.SendEmbedAsync(token, settings.ChannelId, embed, content, image);
            }

            int skipped = DiscordNotificationSamples.All.Count - samples.Length;
            UpdateStatusBar($"Wysłano {samples.Length} przykładowych powiadomień" + (skipped > 0 ? $" (pominięto wyłączone: {skipped})." : "."), "Green");
        }
        catch (Exception ex)
        {
            UpdateStatusBar("Test powiadomień nie powiódł się: " + ex.GetBaseException().Message, "Red");
        }
        finally
        {
            BtnDiscordTestNotifications.IsEnabled = true;
        }
    }

    // Stan bota z pliku zapisywanego przez główny proces — działa w oknie każdego konta.
    private void RefreshDiscordStatus()
    {
        DiscordSettings settings = DiscordSettingsStore.Load();
        DiscordBotStatus? status = DiscordBotHost.ReadStatus();
        string text;
        bool ok = false;
        if (!settings.Enabled)
            text = "Bot wyłączony.";
        else if (status == null)
            text = "Bot jeszcze nie działał — zapisz ustawienia; połączy się w głównym oknie Kopacza.";
        else
        {
            ok = status.Connected;
            text = status.Text;
        }

        TxtDiscordStatus.Text = text;
        EllDiscordStatus.Fill = ok ? GlassPalette.Ok : settings.Enabled ? GlassPalette.Warn : GlassPalette.Muted;
    }
}
