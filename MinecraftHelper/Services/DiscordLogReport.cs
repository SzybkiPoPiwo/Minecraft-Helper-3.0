using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftHelper.Services;

// Dane z dziennika konta na Discord: karta komendy !dane (ostatnia sesja, co zostało w EQ,
// wyjścia z serwera, suma z dzisiaj) oraz pola podsumowania sesji w powiadomieniu o końcu kopania.
internal static class DiscordLogReport
{
    private const int MaxItemTypes = 8;

    public static DiscordEmbed Build(string accountName, IReadOnlyList<MiningLogEntry> entries, DateTimeOffset now)
    {
        var embed = new DiscordEmbed("📒 Dane z dziennika", DiscordColors.Accent) { Author = "👤 " + accountName };
        MiningLogEntry? run = entries
            .Where(entry => entry.Kind == MiningLogKinds.MiningRun)
            .OrderByDescending(entry => entry.Timestamp)
            .FirstOrDefault();
        if (run == null)
        {
            embed.Description = "Brak zapisanych sesji kopania.";
            return embed;
        }

        List<MiningLogEntry> children = ChildrenOf(entries, run.SessionId);
        bool running = run.Status == MiningLogStatuses.InProgress;
        string owner = string.IsNullOrWhiteSpace(run.Owner) ? "Kopanie" : run.Owner;
        embed.Description = $"**Ostatnia sesja:** {owner} • start {run.Timestamp.LocalDateTime:dd.MM HH:mm} • "
            + (running ? "🟢 trwa" : "zakończona");
        embed.AddFields(SessionFields(run, children, now));
        embed.Field("🎒 Ostatnie EQ", DescribeLastInventory(children), true);

        int reconnects = children.Count(entry => IsEvent(entry, MiningLogEventTypes.AutoReconnectStarted, MiningLogEventTypes.EmergencyReconnectJoined));
        int failed = children.Count(entry => entry.Status == MiningLogStatuses.Aborted
            && IsEvent(entry, MiningLogEventTypes.AutoReconnectFinished, MiningLogEventTypes.EmergencyProtectionFinished));
        int pickaxe = children.Count(entry => IsEvent(entry, MiningLogEventTypes.MissingPickaxeRecovery, MiningLogEventTypes.EmergencyPickaxeMissing));
        if (reconnects > 0 || failed > 0)
            embed.Field("🔄 Reconnecty", failed > 0 ? $"{reconnects} (nieudane: {failed}) ⚠️" : reconnects.ToString(), true);
        if (pickaxe > 0)
            embed.Field("⛏️ Brak kilofa", $"{pickaxe} ⚠️", true);

        MiningLogEntry? lastExit = entries.Where(IsServerExit).OrderByDescending(entry => entry.Timestamp).FirstOrDefault();
        if (lastExit != null)
            embed.Field("🕓 Ostatnie wyjście z serwera", lastExit.Timestamp.LocalDateTime.ToString("dd.MM HH:mm"), true);

        embed.Field("🗑️ Wyrzucone przedmioty", DescribeDiscardedItems(children));
        if (!running)
            embed.Field("🏁 Zakończenie", run.EndDetails.Split('\n')[0]);

        DateTime today = now.LocalDateTime.Date;
        List<MiningLogEntry> todays = entries.Where(entry => entry.Timestamp.LocalDateTime.Date == today).ToList();
        Totals day = Count(todays);
        embed.Field("📅 Dziś",
            $"Sesje **{todays.Count(entry => entry.Kind == MiningLogKinds.MiningRun):N0}** · EQ **{day.Inventories:N0}** · CobbleX **{day.CobbleX:N0}**\n"
            + $"Wyrzucono **{day.DiscardedItems:N0} szt.** · Wyjścia z serwera **{day.ServerExits:N0}**");
        return embed;
    }

    // Do powiadomienia o końcu kopania: dane jednej sesji, od jej uruchomienia.
    public static IReadOnlyList<DiscordEmbedField> BuildSessionFields(IReadOnlyList<MiningLogEntry> entries, string runId, DateTimeOffset now)
    {
        MiningLogEntry? run = string.IsNullOrWhiteSpace(runId)
            ? null
            : entries.FirstOrDefault(entry => entry.Kind == MiningLogKinds.MiningRun
                && string.Equals(entry.SessionId, runId, StringComparison.OrdinalIgnoreCase));
        if (run == null)
            return Array.Empty<DiscordEmbedField>();

        List<MiningLogEntry> children = ChildrenOf(entries, runId);
        var fields = SessionFields(run, children, now).ToList();
        string items = DescribeDiscardedItems(children);
        if (items.Length > 0)
            fields.Add(new DiscordEmbedField("🗑️ Wyrzucone przedmioty", items));
        return fields;
    }

    private static List<MiningLogEntry> ChildrenOf(IEnumerable<MiningLogEntry> entries, string runId) =>
        string.IsNullOrWhiteSpace(runId)
            ? new List<MiningLogEntry>()
            : entries.Where(entry => entry.Kind != MiningLogKinds.MiningRun
                    && string.Equals(entry.MiningRunId, runId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Timestamp)
                .ToList();

    private static IEnumerable<DiscordEmbedField> SessionFields(MiningLogEntry run, IReadOnlyCollection<MiningLogEntry> children, DateTimeOffset now)
    {
        TimeSpan duration = (run.CompletedAt ?? now) - run.Timestamp;
        Totals session = Count(children);
        yield return new DiscordEmbedField("⏱️ Czas sesji", $"{MinerRemoteCommandRouter.FormatDuration((int)duration.TotalSeconds)}\nod {run.Timestamp.LocalDateTime:HH:mm}", true);
        yield return new DiscordEmbedField("🎒 Otwarcia EQ", session.Inventories.ToString("N0"), true);
        yield return new DiscordEmbedField("🧱 CobbleX", session.CobbleX.ToString("N0"), true);
        yield return new DiscordEmbedField("🗑️ Wyrzucono", $"{session.DiscardedItems:N0} szt.\n{Stacks(session.DiscardedStacks)}", true);
        yield return new DiscordEmbedField("🚪 Wyjścia z serwera", session.ServerExits == 0 ? "brak ✅" : $"{session.ServerExits} ⚠️", true);
    }

    // 1 stack, 2 stacki, 5 stacków (12–14 też „stacków”).
    internal static string Stacks(int count)
    {
        int lastDigit = count % 10;
        int lastTwo = count % 100;
        string word = count == 1 ? "stack" : lastDigit is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? "stacki" : "stacków";
        return $"{count:N0} {word}";
    }

    private readonly record struct Totals(int Inventories, int CobbleX, int DiscardedItems, int DiscardedStacks, int ServerExits);

    // Te same reguły co podsumowanie dziennika (MiningLogService.GetSummary).
    private static Totals Count(IReadOnlyCollection<MiningLogEntry> entries)
    {
        IEnumerable<MiningLogEntry> discards = entries.Where(entry =>
            entry.Kind is MiningLogKinds.InventorySession or MiningLogKinds.ItemsDiscarded);
        return new Totals(
            entries.Count(entry => entry.Kind == MiningLogKinds.InventorySession),
            entries.Sum(entry => entry.Kind == MiningLogKinds.CobbleXCreated
                ? Math.Max(0, entry.Count)
                : entry.Kind == MiningLogKinds.InventorySession && entry.CobbleXCreated ? 1 : 0),
            discards.Sum(entry => Math.Max(0, entry.ItemCount)),
            discards.Sum(MiningLogService.GetDiscardedStackCount),
            entries.Count(IsServerExit));
    }

    private static bool IsEvent(MiningLogEntry entry, params string[] eventTypes) =>
        entry.Kind == MiningLogKinds.AutomationEvent && eventTypes.Contains(entry.EventType);

    // Prawdziwe wyjście z serwera po alarmie obrażeń (bez wpisów z trybu testowego).
    private static bool IsServerExit(MiningLogEntry entry) =>
        IsEvent(entry, MiningLogEventTypes.EmergencyDamageSoundDetected)
        && !entry.Details.StartsWith("Test:", StringComparison.Ordinal);

    // Jedna pozycja na wiersz, od największej liczby sztuk.
    private static string DescribeDiscardedItems(IEnumerable<MiningLogEntry> children)
    {
        var groups = children
            .SelectMany(entry => entry.Items ?? new List<MiningLogItemDetail>())
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Label) ? item.ItemId : item.Label)
            .Select(group => (Name: group.Key, Count: group.Sum(item => Math.Max(0, item.ItemCount))))
            .Where(group => group.Count > 0)
            .OrderByDescending(group => group.Count)
            .ToList();
        string shown = string.Join("\n", groups.Take(MaxItemTypes).Select(group => $"• {group.Name} — **{group.Count:N0}**"));
        return groups.Count > MaxItemTypes ? $"{shown}\n…i {groups.Count - MaxItemTypes} inne" : shown;
    }

    // „Co zostało”: stan po ostatnim otwarciu EQ w tej sesji.
    private static string DescribeLastInventory(IEnumerable<MiningLogEntry> children)
    {
        MiningLogEntry? last = children.LastOrDefault(entry => entry.Kind == MiningLogKinds.InventorySession);
        if (last == null)
            return "jeszcze nie otwierano";

        string state = last.Status switch
        {
            MiningLogStatuses.InProgress => "trwa porządkowanie",
            MiningLogStatuses.Aborted or MiningLogStatuses.Interrupted => "przerwane ⚠️",
            _ => last.RemainingStacks > 0 ? $"zostało {Stacks(last.RemainingStacks)}" : "czyste ✅"
        };
        string cobble = last.CobbleXEnabled && last.RequiredCobblestoneStacks > 0
            ? $"\ncobble 64: {Math.Max(0, last.FullCobblestoneStacks)}/{last.RequiredCobblestoneStacks}"
            : string.Empty;
        return $"{state}{cobble}\n({last.Timestamp.LocalDateTime:HH:mm})";
    }
}
