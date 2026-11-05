using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftHelper.Models;

namespace MinecraftHelper.Services;

internal sealed record DiscordBotStatus(bool Connected, string Text, DateTime UpdatedUtc);

// Bot Discord: działa tylko w głównym procesie Kopacza (panelu kont), odbiera komendy
// właścicieli i przekazuje je kontom tym samym lokalnym kanałem co panel. Z Discorda
// da się czytać stan i dziennik, uruchamiać i zatrzymywać kopanie — nigdy wpisywać komend gry.
internal sealed class DiscordBotHost : IDisposable
{
    private static readonly TimeSpan SettingsPollInterval = TimeSpan.FromSeconds(3);
    private static readonly string StatusFilePath = Path.Combine(Path.GetDirectoryName(DiscordSettingsStore.FilePath)!, "discord-status.json");

    private readonly CancellationTokenSource _stop = new();
    private readonly MinerAccountStore _accounts = new();
    private CancellationTokenSource? _session;
    private DateTime _settingsWriteUtc = DateTime.MinValue.AddTicks(1);
    private DiscordSettings _settings = new();
    private string? _token;
    private DateTime _nextScreenshotUtc = DateTime.MaxValue;
    private DateTime _nextStatusUtc = DateTime.MaxValue;

    public static DiscordBotHost? Current { get; private set; }

    public static void Start()
    {
        if (Current != null)
            return;
        Current = new DiscordBotHost();
        _ = Task.Run(() => Current.SuperviseAsync());
    }

    public void Dispose()
    {
        _stop.Cancel();
        _session?.Cancel();
        WriteStatus(false, "Bot wyłączony (Kopacz zamknięty).");
        if (ReferenceEquals(Current, this))
            Current = null;
    }

    // Stan bota dla GUI dowolnego konta (proces bota zapisuje go do pliku).
    public static DiscordBotStatus? ReadStatus()
    {
        try
        {
            return File.Exists(StatusFilePath)
                ? JsonSerializer.Deserialize<DiscordBotStatus>(File.ReadAllText(StatusFilePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task SuperviseAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                if (DiscordSettingsStore.LastWriteUtc != _settingsWriteUtc)
                    ApplySettings();
                await RunPeriodicTasksAsync().ConfigureAwait(false);
                await Task.Delay(SettingsPollInterval, _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLog.Error("Discord", "Błąd nadzoru bota", ex);
            }
        }
    }

    // Każda zmiana ustawień uruchamia połączenie od nowa — prościej niż porównywać pola.
    private void ApplySettings()
    {
        _settingsWriteUtc = DiscordSettingsStore.LastWriteUtc;
        _settings = DiscordSettingsStore.Load();
        _session?.Cancel();
        _session = null;

        if (!_settings.IsConfigured)
        {
            WriteStatus(false, _settings.Enabled ? "Uzupełnij token bota i ID kanału." : "Bot wyłączony w ustawieniach.");
            _nextScreenshotUtc = _nextStatusUtc = DateTime.MaxValue;
            return;
        }

        _token = DiscordSettingsStore.GetToken(_settings);
        if (_token == null)
        {
            WriteStatus(false, "Nie udało się odczytać tokenu — wpisz go ponownie.");
            return;
        }

        DateTime now = DateTime.UtcNow;
        _nextScreenshotUtc = _settings.ScreenshotIntervalMinutes > 0 ? now.AddMinutes(_settings.ScreenshotIntervalMinutes) : DateTime.MaxValue;
        _nextStatusUtc = _settings.StatusIntervalMinutes > 0 ? now.AddMinutes(_settings.StatusIntervalMinutes) : DateTime.MaxValue;

        _session = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var gateway = new DiscordGateway(_token);
        gateway.StatusChanged += WriteStatus;
        gateway.MessageReceived += HandleMessageAsync;
        CancellationToken token = _session.Token;
        _ = Task.Run(() => gateway.RunAsync(token));
    }

    private async Task RunPeriodicTasksAsync()
    {
        if (_token == null || !_settings.IsConfigured)
            return;
        DateTime now = DateTime.UtcNow;
        if (now >= _nextStatusUtc)
        {
            _nextStatusUtc = now.AddMinutes(Math.Max(1, _settings.StatusIntervalMinutes));
            DiscordEmbed? status = await BuildStatusAsync(null, "📊 Status cykliczny").ConfigureAwait(false);
            if (status != null)
                await ReplyAsync(_settings.ChannelId, status).ConfigureAwait(false);
        }
        if (now >= _nextScreenshotUtc)
        {
            _nextScreenshotUtc = now.AddMinutes(Math.Max(1, _settings.ScreenshotIntervalMinutes));
            foreach (MinerAccountProfile profile in await GetRunningAccountsAsync().ConfigureAwait(false))
                await SendImageCommandAsync(_settings.ChannelId, profile, "screenshot", "📸 Zrzut cykliczny").ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(DiscordMessage message)
    {
        DiscordSettings settings = _settings;
        string prefix = settings.Prefix;
        if (message.AuthorIsBot || !message.Content.StartsWith(prefix, StringComparison.Ordinal))
            return;
        // Komendy tylko na ustawionym kanale albo w wiadomości prywatnej do bota.
        if (message.GuildId != null && message.ChannelId != settings.ChannelId)
            return;
        if (!settings.GetOwnerIds().Contains(message.AuthorId))
        {
            AppLog.Warn("Discord", $"Odrzucono komendę od użytkownika {message.AuthorId} spoza listy właścicieli.");
            await NoticeAsync(message.ChannelId, "⛔ Brak uprawnień", "Twojego ID nie ma na liście właścicieli w ustawieniach Kopacza.", DiscordColors.Alarm).ConfigureAwait(false);
            return;
        }

        string[] parts = message.Content[prefix.Length..].Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return;
        string typed = parts[0].ToLowerInvariant();
        string? selector = parts.Length > 1 ? parts[1] : null;
        AppLog.Info("Discord", $"Komenda z Discorda: {prefix}{typed}{(selector == null ? string.Empty : " " + selector)}");

        if (DiscordCommands.IsHelp(typed))
        {
            await ReplyAsync(message.ChannelId, BuildHelp(settings)).ConfigureAwait(false);
            return;
        }
        DiscordCommandInfo? command = DiscordCommands.Find(typed);
        if (command == null)
        {
            await NoticeAsync(message.ChannelId, "❓ Nieznana komenda", $"Nie znam komendy `{prefix}{typed}`. Wpisz `{prefix}pomoc`, aby zobaczyć listę.").ConfigureAwait(false);
            return;
        }
        if (!settings.IsCommandEnabled(command.Id))
        {
            AppLog.Warn("Discord", $"Odrzucono wyłączoną komendę {prefix}{command.Id}.");
            await NoticeAsync(message.ChannelId, "🚫 Komenda wyłączona",
                $"`{prefix}{command.Id}` jest wyłączona w Kopaczu (Ustawienia → Bot Discord → Komendy bota).", DiscordColors.Neutral).ConfigureAwait(false);
            return;
        }

        switch (command.Id)
        {
            case "status":
            {
                DiscordEmbed? status = await BuildStatusAsync(selector, "📊 Stan kont").ConfigureAwait(false);
                await (status == null ? NotFoundAsync(message.ChannelId, selector) : ReplyAsync(message.ChannelId, status)).ConfigureAwait(false);
                break;
            }
            case "stats":
                await StatsAsync(message.ChannelId, selector).ConfigureAwait(false);
                break;
            case "ss":
                await RunImageCommandAsync(message.ChannelId, selector, "screenshot", "📸 Zrzut ekranu").ConfigureAwait(false);
                break;
            case "jedzenie":
                await RunImageCommandAsync(message.ChannelId, selector, "jedzenie", "🍖 Życie i jedzenie").ConfigureAwait(false);
                break;
            case "eq":
                await RunImageCommandAsync(message.ChannelId, selector, "ekwipunek", "🎒 Ekwipunek").ConfigureAwait(false);
                break;
            case "stop":
                await StopAsync(message.ChannelId, selector).ConfigureAwait(false);
                break;
            case "start":
                await StartAsync(message.ChannelId, selector).ConfigureAwait(false);
                break;
            case "join":
                await JoinAsync(message.ChannelId, selector).ConfigureAwait(false);
                break;
            case "leave":
                await LeaveAsync(message.ChannelId, selector).ConfigureAwait(false);
                break;
            case "dane":
                await ReportAsync(message.ChannelId, selector).ConfigureAwait(false);
                break;
        }
    }

    // Lista tylko włączonych komend, pogrupowana jak w oknie „Komendy bota”.
    internal static DiscordEmbed BuildHelp(DiscordSettings settings)
    {
        string p = settings.Prefix;
        var embed = new DiscordEmbed("📖 Komendy Kopacza", DiscordColors.Accent)
        {
            Description = $"Konto podajesz nazwą z panelu Kont, np. `{p}ss Konto 2`. Bez nazwy komenda dotyczy wszystkich kont, a obrazy, start, join i leave — jedynego pasującego. Tryb kopania dopisz na końcu: `5/3/3` (w miejscu) albo `6/3/3` (z chodzeniem), np. `{p}join Konto 2 5/3/3`."
        };
        foreach (IGrouping<DiscordCommandGroup, DiscordCommandInfo> group in DiscordCommands.All.GroupBy(command => command.Group))
        {
            string[] lines = group
                .Where(command => settings.IsCommandEnabled(command.Id))
                .Select(command => $"`{p}{command.Usage}` — {command.Description}")
                .ToArray();
            if (lines.Length > 0)
                embed.Field(DiscordCommands.GroupTitle(group.Key), string.Join("\n", lines));
        }
        string[] disabled = DiscordCommands.All.Where(command => !settings.IsCommandEnabled(command.Id)).Select(command => $"`{p}{command.Id}`").ToArray();
        if (disabled.Length > 0)
            embed.Field("🚫 Wyłączone w Kopaczu", string.Join(" ", disabled));
        return embed;
    }

    // Karta stanu: jedno pole na konto, z kropką koloru stanu. null — nie znaleziono konta.
    private async Task<DiscordEmbed?> BuildStatusAsync(string? selector, string title)
    {
        IReadOnlyList<MinerAccountProfile> profiles = SelectProfiles(selector);
        if (profiles.Count == 0)
            return null;

        var embed = new DiscordEmbed(title, DiscordColors.Info);
        int running = 0;
        foreach (MinerAccountProfile profile in profiles)
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, "status").ConfigureAwait(false);
            MinerRemoteStatus? status = response?.Success == true ? response.Status : null;
            if (status == null)
            {
                embed.Field($"⚫ {profile.Name}", "nieuruchomione", true);
                continue;
            }

            string dot = status.Running
                ? status.WindowAvailable && (status.GameFocused || status.BackgroundActive) ? "🟢" : "🟠"
                : status.WindowAvailable && !status.WindowOwnedByAnotherProfile ? "🟡" : "🔴";
            if (status.Running)
                running++;
            string value = $"**{MinerRemoteCommandRouter.DescribeState(status)}**";
            if (status.Running)
                value += $"\n⏱️ {MinerRemoteCommandRouter.FormatDuration(status.RuntimeSeconds)}";
            if (status.WindowAvailable)
                value += $"\n🪟 {status.WindowTitle}";
            embed.Field($"{dot} {profile.Name}", value, true);
        }
        embed.Description = running == 0 ? "Żadne konto nie kopie." : $"Kopie: **{running}** z {profiles.Count}.";
        return embed;
    }

    private async Task StatsAsync(string channelId, string? selector)
    {
        IReadOnlyList<MinerAccountProfile> profiles = SelectProfiles(selector);
        if (profiles.Count == 0)
        {
            await NotFoundAsync(channelId, selector).ConfigureAwait(false);
            return;
        }

        var embed = new DiscordEmbed("📈 Statystyki", DiscordColors.Info) { Description = "Liczniki z całej historii konta (Logi w Kopaczu). Wyczyszczenie historii zeruje je; dane ostatniej sesji pokaże `" + _settings.Prefix + "dane`." };
        foreach (MinerAccountProfile profile in profiles)
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, "status").ConfigureAwait(false);
            MinerRemoteStatus? status = response?.Success == true ? response.Status : null;
            embed.Field(profile.Name, status == null
                ? "nieuruchomione"
                : $"⛏️ Sesje **{status.MiningRuns:N0}**\n🎒 EQ **{status.InventoryScans:N0}**\n🧱 CobbleX **{status.CobbleXCreated:N0}**\n🗑️ Wyrzucono **{status.DiscardedItems:N0}** szt.", true);
        }
        await ReplyAsync(channelId, embed).ConfigureAwait(false);
    }

    // Obraz wymaga jednego konta: wskazanego albo jedynego uruchomionego.
    private async Task RunImageCommandAsync(string channelId, string? selector, string command, string title)
    {
        MinerAccountProfile? profile;
        if (selector != null)
        {
            profile = SelectProfiles(selector).FirstOrDefault();
            if (profile == null)
            {
                await NotFoundAsync(channelId, selector).ConfigureAwait(false);
                return;
            }
        }
        else
        {
            IReadOnlyList<MinerAccountProfile> running = await GetRunningAccountsAsync().ConfigureAwait(false);
            if (running.Count != 1)
            {
                string names = running.Count == 0 ? "Żadne konto teraz nie kopie." : "Kopią: " + string.Join(", ", running.Select(item => item.Name)) + ".";
                string example = $"{_settings.Prefix}{command switch { "screenshot" => "ss", "jedzenie" => "jedzenie", _ => "eq" }} {running.FirstOrDefault()?.Name ?? "Konto główne"}";
                await NoticeAsync(channelId, "👉 Podaj konto", $"{names} Wpisz np. `{example}`.").ConfigureAwait(false);
                return;
            }
            profile = running[0];
        }

        await SendImageCommandAsync(channelId, profile, command, title).ConfigureAwait(false);
    }

    private async Task SendImageCommandAsync(string channelId, MinerAccountProfile profile, string command, string title)
    {
        MinerRemotePipeResponse? response = await TryExecuteAsync(profile, command).ConfigureAwait(false);
        if (response == null)
        {
            await NoticeAsync(channelId, "⚫ Konto nie działa", $"**{profile.Name}** nie jest otwarte w Kopaczu.").ConfigureAwait(false);
            return;
        }
        byte[]? image = string.IsNullOrWhiteSpace(response.AttachmentBase64) ? null : Convert.FromBase64String(response.AttachmentBase64);
        var embed = new DiscordEmbed(image == null ? title + " — niedostępny" : title, image == null ? DiscordColors.Warn : DiscordColors.Info)
        {
            Author = "👤 " + profile.Name,
            Description = WithoutAccountName(response.Message, profile.Name)
        };
        await ReplyAsync(channelId, embed, image).ConfigureAwait(false);
    }

    private async Task StopAsync(string channelId, string? selector)
    {
        bool all = selector == null || selector.Equals("wszystkie", StringComparison.OrdinalIgnoreCase) || selector.Equals("all", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<MinerAccountProfile> profiles = all ? await GetRunningAccountsAsync().ConfigureAwait(false) : SelectProfiles(selector);
        if (profiles.Count == 0)
        {
            await (all
                ? NoticeAsync(channelId, "⏹️ Nic do zatrzymania", "Żadne konto nie kopie.", DiscordColors.Neutral)
                : NotFoundAsync(channelId, selector)).ConfigureAwait(false);
            return;
        }

        var embed = new DiscordEmbed("⏹️ STOP", DiscordColors.Alarm);
        foreach (MinerAccountProfile profile in profiles)
        {
            // Konto, które nie kopało, nie wyśle karty „Kopanie zakończone” — wtedy mówi to bot.
            MinerRemotePipeResponse? before = await TryExecuteAsync(profile, "status").ConfigureAwait(false);
            bool wasMining = before?.Status?.Running == true;
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, "stop").ConfigureAwait(false);
            if (response?.Success != true)
                embed.Field($"❌ {profile.Name}", before == null ? "konto nie jest otwarte w Kopaczu" : "nie odpowiada");
            else if (!wasMining)
                embed.Field($"⚪ {profile.Name}", "kopanie nie było uruchomione; zatrzymano ewentualną automatyzację (np. reconnect).");
            else if (!AccountAnnouncesStartStop())
                embed.Field($"✅ {profile.Name}", WithoutAccountName(response.Message, profile.Name));
        }
        AppLog.Warn("Discord", "STOP z Discorda: " + string.Join(", ", profiles.Select(item => item.Name)));
        await ReplyIfAnyAsync(channelId, embed).ConfigureAwait(false);
    }

    // Udany start i stop ogłasza samo konto kartą „Kopanie uruchomione/zakończone”
    // (z podsumowaniem sesji), więc bot nie dubluje ich własną odpowiedzią.
    private bool AccountAnnouncesStartStop() => _settings.NotifyMiningStartStop;

    private Task ReplyIfAnyAsync(string channelId, DiscordEmbed embed) =>
        embed.Fields.Count == 0 ? Task.CompletedTask : ReplyAsync(channelId, embed);

    // Start bez nazwy działa tylko wtedy, gdy jest dokładnie jedno konto gotowe do kopania.
    private async Task StartAsync(string channelId, string? selector)
    {
        // !start [konto] [5/3/3|6/3/3] — tryb na końcu; bez niego kanał wybiera profil konta.
        string? mode = null;
        if (selector != null)
        {
            string[] words = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (MinerRemoteCommandRouter.TryParseMiningMode(words[^1], out string parsed))
            {
                mode = parsed;
                selector = words.Length > 1 ? string.Join(' ', words[..^1]) : null;
            }
        }
        string startCommand = mode == null ? "start" : "start " + mode;

        bool all = selector != null && (selector.Equals("wszystkie", StringComparison.OrdinalIgnoreCase) || selector.Equals("all", StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<MinerAccountProfile> profiles;
        if (selector == null || all)
        {
            IReadOnlyList<MinerAccountProfile> idle = await GetIdleAccountsAsync().ConfigureAwait(false);
            if (idle.Count == 0)
            {
                await NoticeAsync(channelId, "▶️ Brak konta do startu", "Konto musi być otwarte w Kopaczu i nie kopać.").ConfigureAwait(false);
                return;
            }
            if (!all && idle.Count > 1)
            {
                await NoticeAsync(channelId, "👉 Podaj konto",
                    $"Gotowe do startu: {string.Join(", ", idle.Select(item => item.Name))}. Wpisz np. `{_settings.Prefix}start {idle[0].Name}` albo `{_settings.Prefix}start wszystkie` (tryb możesz dopisać na końcu: `5/3/3` albo `6/3/3`).").ConfigureAwait(false);
                return;
            }
            profiles = idle;
        }
        else
        {
            profiles = SelectProfiles(selector).Take(1).ToArray();
            if (profiles.Count == 0)
            {
                await NotFoundAsync(channelId, selector).ConfigureAwait(false);
                return;
            }
        }

        var embed = new DiscordEmbed("▶️ Start kopania", DiscordColors.Ok);
        foreach (MinerAccountProfile profile in profiles)
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, startCommand).ConfigureAwait(false);
            if (response?.Success != true)
                embed.Field($"❌ {profile.Name}", response == null ? "konto nie jest otwarte w Kopaczu" : WithoutAccountName(response.Message, profile.Name));
            else if (!AccountAnnouncesStartStop())
                embed.Field($"✅ {profile.Name}", WithoutAccountName(response.Message, profile.Name));
        }
        AppLog.Warn("Discord", "START z Discorda: " + string.Join(", ", profiles.Select(item => item.Name)));
        await ReplyIfAnyAsync(channelId, embed).ConfigureAwait(false);
    }

    // !join [konto] [5/3/3|6/3/3] — ten sam przebieg co Auto Reconnect (lista serwerów,
    // Direct Connect, /home, kontrola EQ), a na końcu start kopania. Wynik ogłasza konto.
    private async Task JoinAsync(string channelId, string? selector)
    {
        string? mode = null;
        if (selector != null)
        {
            string[] words = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (MinerRemoteCommandRouter.TryParseMiningMode(words[^1], out string parsed))
            {
                mode = parsed;
                selector = words.Length > 1 ? string.Join(' ', words[..^1]) : null;
            }
        }

        IReadOnlyList<MinerAccountProfile>? profiles = await SelectServerTargetsAsync(channelId, selector, "join",
            status => !status.Running && status.WindowAvailable && !status.WindowOwnedByAnotherProfile,
            "🌐 Brak konta do wejścia", "Konto musi być otwarte w Kopaczu, mieć wybrane okno gry i nie kopać.").ConfigureAwait(false);
        if (profiles == null)
            return;

        string command = mode == null ? "join" : "join " + mode;
        var embed = new DiscordEmbed("🌐 Wchodzę na serwer", DiscordColors.Info)
        {
            Description = "Lista serwerów → Direct Connect → wejście → /home → kontrola EQ → kopanie. Wynik przyjdzie od konta."
        };
        foreach (MinerAccountProfile profile in profiles)
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, command).ConfigureAwait(false);
            embed.Field(response?.Success == true ? $"⏳ {profile.Name}" : $"❌ {profile.Name}",
                response == null ? "konto nie jest otwarte w Kopaczu" : WithoutAccountName(response.Message, profile.Name));
        }
        AppLog.Warn("Discord", "JOIN z Discorda: " + string.Join(", ", profiles.Select(item => item.Name)));
        await ReplyAsync(channelId, embed).ConfigureAwait(false);
    }

    // !leave [konto] — zatrzymuje kopanie i wychodzi z serwera (ESC → Disconnect).
    private async Task LeaveAsync(string channelId, string? selector)
    {
        IReadOnlyList<MinerAccountProfile>? profiles = await SelectServerTargetsAsync(channelId, selector, "leave",
            status => status.WindowAvailable && !status.WindowOwnedByAnotherProfile,
            "🚪 Brak konta do wyjścia", "Żadne otwarte konto nie ma wybranego okna gry.").ConfigureAwait(false);
        if (profiles == null)
            return;

        var embed = new DiscordEmbed("🚪 Wychodzę z serwera", DiscordColors.Warn)
        {
            Description = $"Wynik przyjdzie od konta. Powrót: `{_settings.Prefix}join`."
        };
        foreach (MinerAccountProfile profile in profiles)
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, "leave").ConfigureAwait(false);
            embed.Field(response?.Success == true ? $"⏳ {profile.Name}" : $"❌ {profile.Name}",
                response == null ? "konto nie jest otwarte w Kopaczu" : WithoutAccountName(response.Message, profile.Name));
        }
        AppLog.Warn("Discord", "LEAVE z Discorda: " + string.Join(", ", profiles.Select(item => item.Name)));
        await ReplyAsync(channelId, embed).ConfigureAwait(false);
    }

    // Konta dla komend serwera: wskazane z nazwy, „wszystkie” pasujące albo jedyne pasujące.
    // null — bot już odpowiedział (nie znaleziono konta albo trzeba je wskazać).
    private async Task<IReadOnlyList<MinerAccountProfile>?> SelectServerTargetsAsync(
        string channelId,
        string? selector,
        string commandId,
        Func<MinerRemoteStatus, bool> eligible,
        string noneTitle,
        string noneText)
    {
        bool all = selector != null && (selector.Equals("wszystkie", StringComparison.OrdinalIgnoreCase) || selector.Equals("all", StringComparison.OrdinalIgnoreCase));
        if (selector != null && !all)
        {
            MinerAccountProfile[] picked = SelectProfiles(selector).Take(1).ToArray();
            if (picked.Length == 0)
            {
                await NotFoundAsync(channelId, selector).ConfigureAwait(false);
                return null;
            }
            return picked;
        }

        var candidates = new List<MinerAccountProfile>();
        foreach (MinerAccountProfile profile in _accounts.Load())
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, "status").ConfigureAwait(false);
            if (response?.Success == true && response.Status != null && eligible(response.Status))
                candidates.Add(profile);
        }
        if (candidates.Count == 0)
        {
            await NoticeAsync(channelId, noneTitle, noneText).ConfigureAwait(false);
            return null;
        }
        if (!all && candidates.Count > 1)
        {
            await NoticeAsync(channelId, "👉 Podaj konto",
                $"Pasujące konta: {string.Join(", ", candidates.Select(item => item.Name))}. Wpisz np. `{_settings.Prefix}{commandId} {candidates[0].Name}` albo `{_settings.Prefix}{commandId} wszystkie`.").ConfigureAwait(false);
            return null;
        }
        return candidates;
    }

    // Dziennik czytamy z pliku konta (tylko odczyt), więc działa też dla zamkniętych kont.
    private async Task ReportAsync(string channelId, string? selector)
    {
        IReadOnlyList<MinerAccountProfile> profiles = SelectProfiles(selector);
        if (profiles.Count == 0)
        {
            await NotFoundAsync(channelId, selector).ConfigureAwait(false);
            return;
        }
        foreach (MinerAccountProfile profile in profiles)
        {
            DiscordEmbed report;
            try
            {
                IReadOnlyList<MiningLogEntry> entries = await Task.Run(() => MiningLogService.OpenReadOnly(profile.Id).GetEntriesNewestFirst()).ConfigureAwait(false);
                report = DiscordLogReport.Build(profile.Name, entries, DateTimeOffset.Now);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                report = new DiscordEmbed("📒 Dziennik niedostępny", DiscordColors.Warn) { Author = "👤 " + profile.Name, Description = ex.Message };
            }
            await ReplyAsync(channelId, report).ConfigureAwait(false);
        }
    }

    // Odpowiedzi kont zaczynają się od „Nazwa: …” — w karcie nazwa jest już w nagłówku.
    private static string WithoutAccountName(string message, string name) =>
        message.StartsWith(name + ": ", StringComparison.Ordinal) ? message[(name.Length + 2)..] : message;

    private IReadOnlyList<MinerAccountProfile> SelectProfiles(string? selector)
    {
        IReadOnlyList<MinerAccountProfile> profiles = _accounts.Load();
        if (string.IsNullOrWhiteSpace(selector))
            return profiles;
        string wanted = selector.Trim();
        MinerAccountProfile[] exact = profiles.Where(item =>
            item.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) || item.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
        return exact.Length > 0
            ? exact
            : profiles.Where(item => item.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)).Take(1).ToArray();
    }

    private Task<IReadOnlyList<MinerAccountProfile>> GetRunningAccountsAsync() => GetAccountsAsync(running: true);

    // Otwarte w Kopaczu, ale bez kopania.
    private Task<IReadOnlyList<MinerAccountProfile>> GetIdleAccountsAsync() => GetAccountsAsync(running: false);

    private async Task<IReadOnlyList<MinerAccountProfile>> GetAccountsAsync(bool running)
    {
        var matching = new List<MinerAccountProfile>();
        foreach (MinerAccountProfile profile in _accounts.Load())
        {
            MinerRemotePipeResponse? response = await TryExecuteAsync(profile, "status").ConfigureAwait(false);
            if (response?.Success == true && response.Status != null && response.Status.Running == running)
                matching.Add(profile);
        }
        return matching;
    }

    private static async Task<MinerRemotePipeResponse?> TryExecuteAsync(MinerAccountProfile profile, string command)
    {
        try
        {
            return await MinerRemoteControlClient.ExecuteAsync(profile.Id, command).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            // Konto nie działa albo nie odpowiada w 5 s.
            return null;
        }
    }

    private Task NotFoundAsync(string channelId, string? selector) =>
        NoticeAsync(channelId, "🔎 Nie znaleziono konta", $"Nie ma konta „{selector}”. Nazwy kont sprawdzisz komendą `{_settings.Prefix}status`.");

    private Task NoticeAsync(string channelId, string title, string description, int color = DiscordColors.Warn) =>
        ReplyAsync(channelId, new DiscordEmbed(title, color) { Description = description });

    private async Task ReplyAsync(string channelId, DiscordEmbed embed, byte[]? image = null)
    {
        if (_token == null)
            return;
        try
        {
            await DiscordRest.SendEmbedAsync(_token, channelId, embed, image: image).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            AppLog.Warn("Discord", "Nie udało się odpowiedzieć na Discordzie: " + ex.Message);
        }
    }

    private static void WriteStatus(bool connected, string text)
    {
        try
        {
            File.WriteAllText(StatusFilePath, JsonSerializer.Serialize(new DiscordBotStatus(connected, text, DateTime.UtcNow)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Stan jest tylko podpowiedzią w GUI.
        }
    }
}
