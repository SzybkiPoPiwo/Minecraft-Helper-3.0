using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MinecraftHelper.Services;

internal enum DiscordAlertKind
{
    MiningStartStop,
    Protection,
    Reconnect,
    MissingPickaxe,
    Error,
    // Wynik komendy z Discorda (np. !leave, !join) — zawsze wysyłany, to odpowiedź na prośbę.
    CommandResult
}

// Powiadomienia z konta na kanał Discord. Każdy proces konta wysyła je sam przez REST,
// więc działają niezależnie od tego, które okno Kopacza jest otwarte.
internal static class DiscordNotifier
{
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastSentUtc = new(StringComparer.Ordinal);

    // urgent: oznacz właścicieli także przy rodzaju, który zwykle ich nie oznacza (np. nieudany reconnect).
    // color: kolor paska karty; domyślnie wynika z rodzaju powiadomienia.
    // screenshotDelay: zrzut (i wysyłka) dopiero po tym czasie — gdy ekran ma pokazać skutek akcji,
    // np. listę serwerów po Disconnect zamiast świata gry sprzed ułamka sekundy.
    public static void Notify(
        DiscordAlertKind kind,
        string accountName,
        string title,
        string? description,
        IntPtr gameWindow = default,
        bool urgent = false,
        IReadOnlyList<DiscordEmbedField>? fields = null,
        int? color = null,
        TimeSpan screenshotDelay = default)
    {
        DiscordSettings settings = DiscordSettingsStore.Load();
        if (!settings.IsConfigured || !IsEnabled(settings, kind) || string.IsNullOrWhiteSpace(title))
            return;

        // Ten sam komunikat z tego samego konta najwyżej raz na 30 s (np. powtarzany błąd).
        // Pola też się liczą — dwa alarmy z innym pomiarem to dwa różne zdarzenia.
        string key = $"{accountName}|{kind}|{title}|{description}|{string.Join("|", fields?.Select(field => field.Value) ?? Enumerable.Empty<string>())}";
        lock (Gate)
        {
            DateTime now = DateTime.UtcNow;
            if (LastSentUtc.TryGetValue(key, out DateTime last) && now - last < RepeatWindow)
                return;
            LastSentUtc[key] = now;
        }

        (DiscordEmbed embed, string? content) = BuildAlert(settings, kind, accountName, title, description, urgent, fields, color);
        bool attach = WantsScreenshot(settings, kind) && gameWindow != IntPtr.Zero;

        _ = Task.Run(async () =>
        {
            try
            {
                string? token = DiscordSettingsStore.GetToken(settings);
                if (token == null)
                    return;
                byte[]? image = null;
                if (attach && screenshotDelay > TimeSpan.Zero)
                    await Task.Delay(screenshotDelay).ConfigureAwait(false);
                if (attach && MinerLivePreviewService.TryCapture(gameWindow, MinerPreviewRegion.Full, 1280, out MinerLiveFrame? frame) && frame != null)
                    image = frame.Jpeg;
                await DiscordRest.SendEmbedAsync(token, settings.ChannelId, embed, content, image).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Bez AppLog.Error — błąd wysyłki nie może sam wywołać kolejnego powiadomienia o błędzie.
                AppLog.Warn("Discord", "Nie udało się wysłać powiadomienia: " + ex.GetBaseException().Message);
            }
        });
    }

    // Karta i tekst nad nią — wspólne dla prawdziwych powiadomień i przycisku „Testuj powiadomienia”.
    public static (DiscordEmbed Embed, string? Content) BuildAlert(
        DiscordSettings settings,
        DiscordAlertKind kind,
        string accountName,
        string title,
        string? description,
        bool urgent = false,
        IReadOnlyList<DiscordEmbedField>? fields = null,
        int? color = null)
    {
        bool important = urgent || kind is DiscordAlertKind.Protection or DiscordAlertKind.MissingPickaxe or DiscordAlertKind.Error;
        // Tekst nad kartą widać w powiadomieniu na telefonie, więc przy alarmie mówi, co się stało.
        string? content = important && settings.MentionOwnersOnAlerts && settings.GetOwnerIds().Count > 0
            ? string.Concat(settings.GetOwnerIds().Select(id => $"<@{id}> ")) + $"**{title}** — {accountName}"
            : null;
        var embed = new DiscordEmbed(title, color ?? DefaultColor(kind, urgent))
        {
            Author = "👤 " + accountName,
            Description = description?.Trim()
        }.AddFields(fields);
        return (embed, content);
    }

    public static bool WantsScreenshot(DiscordSettings settings, DiscordAlertKind kind) =>
        settings.AttachScreenshotToAlerts && kind != DiscordAlertKind.MiningStartStop;

    public static bool IsEnabled(DiscordSettings settings, DiscordAlertKind kind) => kind switch
    {
        DiscordAlertKind.MiningStartStop => settings.NotifyMiningStartStop,
        DiscordAlertKind.Protection => settings.NotifyProtection,
        DiscordAlertKind.Reconnect => settings.NotifyReconnect,
        DiscordAlertKind.MissingPickaxe => settings.NotifyMissingPickaxe,
        DiscordAlertKind.Error => settings.NotifyErrors,
        DiscordAlertKind.CommandResult => true,
        _ => false
    };

    private static int DefaultColor(DiscordAlertKind kind, bool urgent) => kind switch
    {
        DiscordAlertKind.Protection => DiscordColors.Alarm,
        DiscordAlertKind.Error => DiscordColors.Error,
        DiscordAlertKind.MissingPickaxe => DiscordColors.Warn,
        DiscordAlertKind.Reconnect => urgent ? DiscordColors.Alarm : DiscordColors.Info,
        _ => DiscordColors.Ok
    };
}
