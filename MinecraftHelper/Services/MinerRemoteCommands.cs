using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftHelper.Services;

internal sealed record MinerRemoteStatus(
    string ProfileId,
    string DisplayName,
    bool Running,
    string Mode,
    string WindowTitle,
    int MiningRuns,
    int InventoryScans,
    int CobbleXCreated,
    int DiscardedItems)
{
    public int GameProcessId { get; init; }

    // Uchwyt okna gry tego konta — podgląd Live przechwytuje je bezpośrednio w panelu.
    public long GameWindowHandle { get; init; }

    public bool WindowAvailable { get; init; }

    public bool WindowOwnedByAnotherProfile { get; init; }

    public bool GameFocused { get; init; }

    public bool BackgroundActive { get; init; }

    public bool BackgroundMiningEnabled { get; init; }

    public int RuntimeSeconds { get; init; }
}

internal sealed record MinerRemoteCommandResult(
    bool Success,
    string Message,
    byte[]? Attachment = null,
    string? AttachmentName = null,
    string? AttachmentContentType = null)
{
    public MinerRemoteStatus? Status { get; init; }
}

internal interface IMinerRemoteSession
{
    string ProfileId { get; }

    string DisplayName { get; }

    MinerRemoteStatus GetRemoteStatus();

    bool TryCaptureRemoteFrame(MinerPreviewRegion region, int maximumWidth, out MinerLiveFrame? frame);

    void StopRemoteMining();

    // mode: "533", "633" albo null — wtedy kanał wybiera profil (6/3/3, jeśli włączony).
    bool TryStartRemoteMining(string? mode, out string message);

    // Wejście na serwer przebiegiem Auto Reconnect (lista serwerów, Direct Connect, /home,
    // kontrola EQ), a po nim start kopania. Zwraca od razu; wynik konto ogłasza na Discordzie.
    bool TryJoinServer(string? mode, out string message);

    // Zatrzymuje kopanie i wychodzi z serwera (ESC → Disconnect). Zwraca od razu.
    bool TryLeaveServer(out string message);

    bool TryAssignGameWindow(int processId, out string message);

    // Ustawia okno gry 854×480 w stałym miejscu obok okien pozostałych kont.
    bool TryArrangeGameWindow(out string message);

    void ShowFromRemote();

    void ReloadProfile();

    // Czyści historię kopania tego konta (trwająca sesja zostaje).
    bool TryClearMiningLogs(out string message);
}

internal static class MinerRemoteCommandRouter
{
    public const int ScreenshotWidth = 1280;
    public const int PreviewWidth = 480;

    // Commands that may later be exposed outside this computer (Discord). They only
    // read state, return images or stop work.
    private static readonly string[] RemoteCommands =
    {
        "status", "stats", "screenshot", "food", "jedzenie", "inventory", "ekwipunek", "stop"
    };

    // Commands for the local multi-account panel only (named pipe, current Windows user).
    private static readonly string[] LocalCommands =
    {
        "start", "join", "leave", "preview", "window", "show", "reload", "arrange", "clear-logs"
    };

    public static MinerRemoteCommandResult Execute(
        string? rawCommand,
        IReadOnlyCollection<IMinerRemoteSession> sessions,
        bool allowLocalCommands = false)
    {
        string[] parts = (rawCommand ?? string.Empty)
            .Trim()
            .TrimStart('/')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool supported = parts.Length > 0
            && (RemoteCommands.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
                || (allowLocalCommands && LocalCommands.Contains(parts[0], StringComparer.OrdinalIgnoreCase)));
        if (!supported)
            return new MinerRemoteCommandResult(false, "Nieobsługiwana komenda. Dostępne: /status, /stats, /screenshot, /jedzenie, /ekwipunek, /stop.");

        string command = parts[0].ToLowerInvariant();
        if (command == "window")
            return ExecuteWindowCommand(parts, sessions);

        // start/join [konto] [5/3/3|6/3/3] — tryb na końcu, żeby nie mylił się z nazwą konta.
        string? miningMode = null;
        if (command is "start" or "join" && parts.Length > 1 && TryParseMiningMode(parts[^1], out string parsedMode))
        {
            miningMode = parsedMode;
            parts = parts[..^1];
        }

        string? selector = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null;
        IReadOnlyList<IMinerRemoteSession> selected = SelectSessions(sessions, selector);
        if (selected.Count == 0)
            return new MinerRemoteCommandResult(false, selector == null ? "Brak aktywnych profili Kopacza." : $"Nie znaleziono konta „{selector}”.");

        if (command is "status" or "stats")
        {
            MinerRemoteStatus[] statuses = selected.Select(session => session.GetRemoteStatus()).ToArray();
            Func<MinerRemoteStatus, string> describe = command == "status" ? BuildStatusLine : BuildStatsLine;
            string text = string.Join(Environment.NewLine, statuses.Select(describe));
            return new MinerRemoteCommandResult(true, text)
            {
                Status = statuses.Length == 1 ? statuses[0] : null
            };
        }

        if (selected.Count != 1)
            return new MinerRemoteCommandResult(false, "Podaj nazwę albo identyfikator jednego konta.");

        IMinerRemoteSession session = selected[0];
        switch (command)
        {
            case "stop":
                session.StopRemoteMining();
                return new MinerRemoteCommandResult(true, $"{session.DisplayName}: zatrzymano kopanie i bieżącą automatyzację.");
            case "start":
            {
                bool started = session.TryStartRemoteMining(miningMode, out string message);
                return new MinerRemoteCommandResult(started, $"{session.DisplayName}: {message}");
            }
            case "join":
            {
                bool joining = session.TryJoinServer(miningMode, out string message);
                return new MinerRemoteCommandResult(joining, $"{session.DisplayName}: {message}");
            }
            case "leave":
            {
                bool leaving = session.TryLeaveServer(out string message);
                return new MinerRemoteCommandResult(leaving, $"{session.DisplayName}: {message}");
            }
            case "show":
                session.ShowFromRemote();
                return new MinerRemoteCommandResult(true, $"{session.DisplayName}: okno Kopacza na wierzchu.");
            case "reload":
                session.ReloadProfile();
                return new MinerRemoteCommandResult(true, $"{session.DisplayName}: odświeżono profil.");
            case "arrange":
            {
                bool arranged = session.TryArrangeGameWindow(out string message);
                return new MinerRemoteCommandResult(arranged, $"{session.DisplayName}: {message}");
            }
            case "clear-logs":
            {
                bool cleared = session.TryClearMiningLogs(out string message);
                return new MinerRemoteCommandResult(cleared, $"{session.DisplayName}: {message}");
            }
        }

        MinerPreviewRegion region = command switch
        {
            "food" or "jedzenie" => MinerPreviewRegion.Hotbar,
            "inventory" or "ekwipunek" => MinerPreviewRegion.Inventory,
            _ => MinerPreviewRegion.Full
        };
        int width = command == "preview" ? PreviewWidth : ScreenshotWidth;

        if (!session.TryCaptureRemoteFrame(region, width, out MinerLiveFrame? frame) || frame == null)
            return new MinerRemoteCommandResult(false, $"{session.DisplayName}: obraz okna Minecrafta jest niedostępny.");

        string suffix = region switch
        {
            MinerPreviewRegion.Hotbar => "hotbar",
            MinerPreviewRegion.Inventory => "ekwipunek",
            _ => "podglad"
        };
        string note = string.IsNullOrWhiteSpace(frame.Note) ? string.Empty : " " + frame.Note;
        return new MinerRemoteCommandResult(
            true,
            $"{session.DisplayName}: {suffix} z {frame.CapturedAt.LocalDateTime:HH:mm:ss}.{note}",
            frame.Jpeg,
            $"{session.ProfileId}-{suffix}.jpg",
            "image/jpeg");
    }

    // window <pid> [konto]
    private static MinerRemoteCommandResult ExecuteWindowCommand(
        string[] parts,
        IReadOnlyCollection<IMinerRemoteSession> sessions)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int processId) || processId <= 0)
            return new MinerRemoteCommandResult(false, "Podaj identyfikator procesu okna Minecrafta.");

        string? selector = parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : null;
        IReadOnlyList<IMinerRemoteSession> selected = SelectSessions(sessions, selector);
        if (selected.Count != 1)
            return new MinerRemoteCommandResult(false, "Podaj nazwę albo identyfikator jednego konta.");

        IMinerRemoteSession session = selected[0];
        bool assigned = session.TryAssignGameWindow(processId, out string message);
        return new MinerRemoteCommandResult(assigned, $"{session.DisplayName}: {message}");
    }

    private static IReadOnlyList<IMinerRemoteSession> SelectSessions(
        IReadOnlyCollection<IMinerRemoteSession> sessions,
        string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return sessions.ToList();

        return sessions
            .Where(session => string.Equals(session.ProfileId, selector, StringComparison.OrdinalIgnoreCase)
                || string.Equals(session.DisplayName, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // „533”, „5/3/3”, „5-3-3” → "533"; tak samo 6/3/3.
    public static bool TryParseMiningMode(string? token, out string mode)
    {
        string normalized = (token ?? string.Empty).Replace("/", string.Empty).Replace("-", string.Empty).Trim();
        mode = normalized is "533" or "633" ? normalized : string.Empty;
        return mode.Length > 0;
    }

    public static string DescribeState(MinerRemoteStatus status)
    {
        if (status.WindowOwnedByAnotherProfile)
            return "okno zajęte przez inne konto";
        if (!status.WindowAvailable)
            return status.Running ? $"kopie ({status.Mode}) — brak okna gry" : "brak wybranego okna Minecrafta";
        if (!status.Running)
            return "gotowy";
        if (status.BackgroundActive)
            return $"kopie w tle ({status.Mode})";
        return status.GameFocused ? $"kopie ({status.Mode})" : $"wstrzymany ({status.Mode}) — gra bez fokusu";
    }

    public static string FormatDuration(int seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string BuildStatusLine(MinerRemoteStatus status)
    {
        string runtime = status.Running ? $" | czas {FormatDuration(status.RuntimeSeconds)}" : string.Empty;
        return $"{status.DisplayName}: {DescribeState(status)}{runtime} | okno: {status.WindowTitle}";
    }

    private static string BuildStatsLine(MinerRemoteStatus status)
    {
        return $"{status.DisplayName}: kopania {status.MiningRuns} | EQ {status.InventoryScans} | CX {status.CobbleXCreated} | wyrzucono {status.DiscardedItems}";
    }
}
