using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services;

internal sealed record MinerRemotePipeRequest(string Command, MinerWorkspacePlacement? WorkspacePlacement = null);

internal sealed record MinerRemotePipeResponse(
    bool Success,
    string Message,
    string? AttachmentBase64,
    string? AttachmentName,
    string? AttachmentContentType,
    MinerRemoteStatus? Status = null,
    MinerWorkspacePlacement? WorkspacePlacement = null);

internal sealed class MinerRemoteControlServer : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IMinerRemoteSession _session;
    // Two listeners: the panel may ask for a status while a screenshot is still
    // being encoded.
    private const int ListenerCount = 2;
    private readonly CancellationTokenSource _stop = new();
    private Task[]? _workers;

    public MinerRemoteControlServer(IMinerRemoteSession session)
    {
        _session = session;
        PipeName = GetPipeName(session.ProfileId);
    }

    public string PipeName { get; }

    public void Start()
    {
        _workers ??= Enumerable.Range(0, ListenerCount)
            .Select(_ => Task.Run(() => RunAsync(_stop.Token)))
            .ToArray();
    }

    public void Dispose()
    {
        _stop.Cancel();
        // The worker may still be leaving WaitForConnectionAsync. Disposing the
        // source here could race with its final cancellation registration.
    }

    public static string GetPipeName(string? profileId) =>
        "MinecraftHelper.Miner." + MinerProfileId.Normalize(profileId);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    ListenerCount,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Kanał kont", $"Kanał zdalny profilu {_session.ProfileId}: {ex.Message}");
                try
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        MinerRemotePipeResponse response;

        try
        {
            MinerRemotePipeRequest? request = JsonSerializer.Deserialize<MinerRemotePipeRequest>(line ?? string.Empty, SerializerOptions);
            if (_session is IMinerWorkspaceSession workspace && request?.Command is "workspace-hide" or "workspace-show" or "workspace-close")
            {
                MinerWorkspacePlacement? placement = null;
                switch (request.Command)
                {
                    case "workspace-hide": placement = workspace.HideWorkspaceWindow(); break;
                    case "workspace-show": workspace.ShowWorkspaceWindow(request.WorkspacePlacement); break;
                    case "workspace-close":
                        // Let this request's acknowledgement leave the pipe before
                        // the worker shuts its dispatcher/process down.
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(150).ConfigureAwait(false);
                            workspace.CloseWorkspaceSession();
                        });
                        break;
                }
                response = new MinerRemotePipeResponse(true, "Zmieniono widok konta.", null, null, null, WorkspacePlacement: placement);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions)).ConfigureAwait(false);
                return;
            }
            MinerRemoteCommandResult result = MinerRemoteCommandRouter.Execute(
                request?.Command,
                new[] { _session },
                allowLocalCommands: true);
            response = new MinerRemotePipeResponse(
                result.Success,
                result.Message,
                result.Attachment == null ? null : Convert.ToBase64String(result.Attachment),
                result.AttachmentName,
                result.AttachmentContentType,
                result.Status);
        }
        catch (Exception ex)
        {
            response = new MinerRemotePipeResponse(false, "Nieprawidłowe żądanie: " + ex.Message, null, null, null);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, SerializerOptions)).ConfigureAwait(false);
    }
}

internal static class MinerRemoteControlClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    public static async Task<MinerRemotePipeResponse> ExecuteAsync(
        string profileId,
        string command,
        CancellationToken cancellationToken = default,
        MinerWorkspacePlacement? workspacePlacement = null)
    {
        // A hung Kopacz must not block the panel, so the whole exchange has a limit.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResponseTimeout);
        cancellationToken = timeout.Token;

        await using var pipe = new NamedPipeClientStream(
            ".",
            MinerRemoteControlServer.GetPipeName(profileId),
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        // The manager probes all configured profiles periodically. A short timeout
        // keeps stopped profiles from delaying the whole dashboard.
        await pipe.ConnectAsync(500, cancellationToken).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new MinerRemotePipeRequest(command, workspacePlacement), SerializerOptions)).ConfigureAwait(false);
        string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<MinerRemotePipeResponse>(line ?? string.Empty, SerializerOptions)
            ?? new MinerRemotePipeResponse(false, "Brak odpowiedzi Kopacza.", null, null, null);
    }
}

