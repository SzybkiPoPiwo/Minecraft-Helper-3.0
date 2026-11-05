using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftHelper.Models;

namespace MinecraftHelper.Services;

internal sealed record MinerWorkspaceRequest(string Command, string? ProfileId = null, MinerWorkspacePlacement? Placement = null);

// One coordinator/tray for the entire workspace. Account engines remain separate
// processes, but only the selected profile exposes its full configuration window.
internal sealed class MinerWorkspaceCoordinator : IDisposable
{
    internal const string PipeName = "MinecraftHelper.Miner.Workspace";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _stop = new();
    private readonly Action _requestLocalExit;
    private readonly MinerAccountStore _store = new();
    private readonly MinerWorkspaceSelection _selection;
    private readonly Dictionary<string, Process> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _processGate = new();
    private Task[]? _listeners;
    private int _exitScheduled;

    public MinerWorkspaceCoordinator(Action requestLocalExit)
    {
        _requestLocalExit = requestLocalExit;
        _selection = new MinerWorkspaceSelection(
            () => _store.Load().Select(profile => profile.Id).ToArray(), EnsureReadyAsync, SendAsync);
    }

    public void Start() => _listeners ??= Enumerable.Range(0, 2)
        .Select(_ => Task.Run(() => ListenAsync(_stop.Token))).ToArray();

    public Task<MinerWorkspaceResponse> SelectAsync(string? profileId, MinerWorkspacePlacement? placement = null,
        CancellationToken cancellationToken = default) => _selection.SelectAsync(profileId, placement, cancellationToken);

    public Task<MinerWorkspaceResponse> EnsureAsync(string profileId, CancellationToken cancellationToken = default) =>
        _selection.EnsureAsync(profileId, cancellationToken);

    public void Dispose()
    {
        _stop.Cancel();
        lock (_processGate)
        {
            foreach (Process process in _processes.Values) process.Dispose();
            _processes.Clear();
        }
    }

    private async Task<bool> EnsureReadyAsync(string profileId, CancellationToken cancellationToken)
    {
        if (await IsReadyAsync(profileId, cancellationToken).ConfigureAwait(false)) return true;
        if (profileId == MinerProfileId.Default) return false;

        MinerAccountProfile? profile = _store.Load().FirstOrDefault(item => string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile == null) return false;
        lock (_processGate)
        {
            if (!_processes.TryGetValue(profileId, out Process? process) || process.HasExited)
            {
                process?.Dispose();
                _processes[profileId] = MinerAccountProcessLauncher.Launch(profile);
            }
        }

        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(10))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsReadyAsync(profileId, cancellationToken).ConfigureAwait(false)) return true;
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private static async Task<bool> IsReadyAsync(string profileId, CancellationToken cancellationToken)
    {
        try
        {
            MinerRemotePipeResponse status = await MinerRemoteControlClient.ExecuteAsync(profileId, "status", cancellationToken).ConfigureAwait(false);
            return status.Success && status.Status != null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }

    private static async Task<MinerWorkspaceCommandResponse> SendAsync(string profileId, string command,
        MinerWorkspacePlacement? placement, CancellationToken cancellationToken)
    {
        MinerRemotePipeResponse response = await MinerRemoteControlClient.ExecuteAsync(profileId, command, cancellationToken, placement).ConfigureAwait(false);
        return new(response.Success, response.Message, response.WorkspacePlacement);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 2,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                AppLog.Warn("Panel kont", ex.Message);
                try { await Task.Delay(150, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        MinerWorkspaceResponse response;
        bool exit = false;
        try
        {
            MinerWorkspaceRequest? request = JsonSerializer.Deserialize<MinerWorkspaceRequest>(line ?? string.Empty, SerializerOptions);
            switch (request?.Command)
            {
                case "select":
                    response = await SelectAsync(request.ProfileId, request.Placement, cancellationToken).ConfigureAwait(false);
                    break;
                case "ensure" when !string.IsNullOrWhiteSpace(request.ProfileId):
                    response = await EnsureAsync(request.ProfileId, cancellationToken).ConfigureAwait(false);
                    break;
                case "exit":
                    await _selection.CloseAsync(cancellationToken).ConfigureAwait(false);
                    response = new(true, "Zamknięto sesje kont.");
                    exit = true;
                    break;
                default:
                    response = new(false, "Nieobsługiwane polecenie panelu kont.");
                    break;
            }
        }
        catch (Exception ex)
        {
            response = new(false, "Nie udało się wykonać polecenia panelu kont: " + ex.Message);
        }
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions)).ConfigureAwait(false);
        if (exit && Interlocked.Exchange(ref _exitScheduled, 1) == 0)
            _ = Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false);
                _requestLocalExit();
            });
    }
}

internal static class MinerWorkspaceClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static Task<MinerWorkspaceResponse> SelectAsync(string? profileId, MinerWorkspacePlacement? placement = null,
        CancellationToken cancellationToken = default) => SendAsync(new("select", profileId, placement), cancellationToken);

    public static Task<MinerWorkspaceResponse> EnsureAsync(string profileId, CancellationToken cancellationToken = default) =>
        SendAsync(new("ensure", profileId), cancellationToken);

    public static Task<MinerWorkspaceResponse> ExitAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new("exit"), cancellationToken);

    private static async Task<MinerWorkspaceResponse> SendAsync(MinerWorkspaceRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        cancellationToken = timeout.Token;
        await using var pipe = new NamedPipeClientStream(".", MinerWorkspaceCoordinator.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // Connection failures are deliberately surfaced so a second application
        // launch can retry while the first coordinator is still starting.
        await pipe.ConnectAsync(500, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, SerializerOptions)).ConfigureAwait(false);
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MinerWorkspaceResponse>(line ?? string.Empty, SerializerOptions)
            ?? new(false, "Brak odpowiedzi panelu kont.");
    }
}
