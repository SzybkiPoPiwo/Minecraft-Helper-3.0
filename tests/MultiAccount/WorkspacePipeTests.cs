using MinecraftHelper.Services;

namespace MultiAccountTests;

internal static partial class Program
{
    private static void CheckWorkspacePipe() => CheckWorkspacePipeAsync().GetAwaiter().GetResult();

    private static async Task CheckWorkspacePipeAsync()
    {
        // A private GUID pipe and in-memory session: no real accounts, settings,
        // processes, WPF windows, game input or application coordinator involved.
        var session = new WorkspacePipeSession("pipe_test_" + Guid.NewGuid().ToString("N"));
        using var server = new MinerRemoteControlServer(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        server.Start();
        Task<MinerRemotePipeResponse> Send(string command, MinerWorkspacePlacement? placement = null) =>
            MinerRemoteControlClient.ExecuteAsync(session.ProfileId, command, timeout.Token, placement);

        MinerRemotePipeResponse status = await Send("status");
        Check(status.Success && status.Status is { Running: true } actual && actual.ProfileId == session.ProfileId,
            "Prawdziwy kanał profilu musi nadal zwracać status sesji.");

        MinerRemotePipeResponse hidden = await Send("workspace-hide");
        Check(hidden.Success && session.HideCalls == 1,
            "Lokalna komenda ukrycia musi wywołać callback właściwej sesji.");
        Check(hidden.WorkspacePlacement == session.HiddenPlacement,
            "JSON kanału musi zachować ujemne współrzędne, ułamki, rozmiar i maksymalizację okna.");

        var requested = new MinerWorkspacePlacement(-2560.5, -200.25, 1280.75, 820.5, false);
        MinerRemotePipeResponse shown = await Send("workspace-show", requested);
        Check(shown.Success && session.ShowCalls == 1 && session.LastShownPlacement == requested,
            "Prawdziwy kanał musi przekazać położenie z żądania do callbacku pokazywania.");
        MinerRemotePipeResponse unchanged = await Send("status");
        Check(unchanged.Success && unchanged.Status == status.Status && session.Engine.StopCalls == 0 && session.Engine.StartCalls == 0,
            "Przełączenie widoku nie może zmieniać kopania ani blokować dalszych zapytań statusu.");

        Check((await Send("workspace-show")).Success && session.ShowCalls == 2 && session.LastShownPlacement == null,
            "Żądanie bez położenia musi zachować null zamiast używać starych danych JSON.");
        Check(!(await Send("workspace-unknown")).Success && session.HideCalls == 1 && session.ShowCalls == 2,
            "Nieznana komenda panelu nie może wywołać żadnego callbacku widoku.");
        foreach (string command in new[] { "workspace-hide", "workspace-show", "workspace-close" })
            Check(!MinerRemoteCommandRouter.Execute(command, new IMinerRemoteSession[] { session }).Success,
                "Komendy sterujące oknem muszą pozostać poza zdalną białą listą: " + command);

        MinerRemotePipeResponse closing = await Send("workspace-close");
        Check(closing.Success, "Zamknięcie sesji musi najpierw potwierdzić przyjęcie przez prawdziwy kanał.");
        await session.Closed.Task.WaitAsync(timeout.Token);
        Check(session.CloseCalls == 1, "Opóźniony callback zamknięcia musi zostać wykonany dokładnie raz.");
    }

    private sealed class WorkspacePipeSession : IMinerRemoteSession, IMinerWorkspaceSession
    {
        public WorkspacePipeSession(string profileId) => Engine = new FakeSession(profileId, "Pipe fixture", running: true);

        public FakeSession Engine { get; }
        public string ProfileId => Engine.ProfileId;
        public string DisplayName => Engine.DisplayName;
        public MinerWorkspacePlacement HiddenPlacement { get; } = new(-1920.5, -100.25, 1280.75, 820.5, true);
        public MinerWorkspacePlacement? LastShownPlacement { get; private set; }
        public int HideCalls { get; private set; }
        public int ShowCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MinerWorkspacePlacement? HideWorkspaceWindow() { HideCalls++; return HiddenPlacement; }
        public void ShowWorkspaceWindow(MinerWorkspacePlacement? placement) { ShowCalls++; LastShownPlacement = placement; }
        public void CloseWorkspaceSession() { CloseCalls++; Closed.TrySetResult(true); }

        public MinerRemoteStatus GetRemoteStatus() => Engine.GetRemoteStatus();
        public bool TryCaptureRemoteFrame(MinerPreviewRegion region, int maximumWidth, out MinerLiveFrame? frame) =>
            Engine.TryCaptureRemoteFrame(region, maximumWidth, out frame);
        public void StopRemoteMining() => Engine.StopRemoteMining();
        public bool TryStartRemoteMining(string? mode, out string message) => Engine.TryStartRemoteMining(mode, out message);
        public bool TryJoinServer(string? mode, out string message) => Engine.TryJoinServer(mode, out message);
        public bool TryLeaveServer(out string message) => Engine.TryLeaveServer(out message);
        public bool TryAssignGameWindow(int processId, out string message) => Engine.TryAssignGameWindow(processId, out message);
        public bool TryArrangeGameWindow(out string message) => Engine.TryArrangeGameWindow(out message);
        public void ShowFromRemote() => Engine.ShowFromRemote();
        public void ReloadProfile() => Engine.ReloadProfile();
        public bool TryClearMiningLogs(out string message) => Engine.TryClearMiningLogs(out message);
    }
}
