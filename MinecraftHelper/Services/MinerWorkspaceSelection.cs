using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services;

internal sealed record MinerWorkspacePlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    public static MinerWorkspacePlacement? FromBounds(double left, double top, double width, double height, bool maximized) =>
        double.IsFinite(left) && double.IsFinite(top) && double.IsFinite(width) && double.IsFinite(height)
        && width > 0 && height > 0
            ? new(left, top, width, height, maximized)
            : null;
}

internal interface IMinerWorkspaceSession
{
    MinerWorkspacePlacement? HideWorkspaceWindow();
    void ShowWorkspaceWindow(MinerWorkspacePlacement? placement);
    void CloseWorkspaceSession();
}

internal sealed record MinerWorkspaceResponse(bool Success, string Message, string? ProfileId = null);

internal sealed record MinerWorkspaceCommandResponse(bool Success, string Message, MinerWorkspacePlacement? Placement = null);

// Serializes the visible-account handoff independently of WPF or pipe transport.
// Every account retains its own settings and running automation while hidden.
internal sealed class MinerWorkspaceSelection
{
    private readonly Func<IReadOnlyCollection<string>> _profileIds;
    private readonly Func<string, CancellationToken, Task<bool>> _ensureReady;
    private readonly Func<string, string, MinerWorkspacePlacement?, CancellationToken, Task<MinerWorkspaceCommandResponse>> _send;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _managed = new(StringComparer.OrdinalIgnoreCase) { MinerProfileId.Default };
    private string _activeProfileId = MinerProfileId.Default;
    private MinerWorkspacePlacement? _lastPlacement;
    private bool _closing;

    public MinerWorkspaceSelection(Func<IReadOnlyCollection<string>> profileIds,
        Func<string, CancellationToken, Task<bool>> ensureReady,
        Func<string, string, MinerWorkspacePlacement?, CancellationToken, Task<MinerWorkspaceCommandResponse>> send)
    {
        _profileIds = profileIds;
        _ensureReady = ensureReady;
        _send = send;
    }

    public async Task<MinerWorkspaceResponse> EnsureAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? selected = FindProfile(profileId);
            if (_closing) return new(false, "Kopacz jest zamykany.");
            if (selected == null) return new(false, "Nie znaleziono wybranego konta.");
            if (!await _ensureReady(selected, cancellationToken).ConfigureAwait(false))
                return new(false, "Nie udało się uruchomić silnika tego konta.", selected);
            _managed.Add(selected);
            return new(true, "Konto jest gotowe.", selected);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, "Nie udało się przygotować konta: " + ex.Message);
        }
        finally { _gate.Release(); }
    }

    public async Task<MinerWorkspaceResponse> SelectAsync(string? profileId, MinerWorkspacePlacement? placement = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string previous = _activeProfileId;
        string? selected = null;
        MinerWorkspacePlacement? previousPlacement = placement;
        bool previousHidden = false;
        bool showAttempted = false;
        try
        {
            if (_closing) return new(false, "Kopacz jest zamykany.");
            selected = string.IsNullOrWhiteSpace(profileId) ? _activeProfileId : FindProfile(profileId);
            if (selected == null) return new(false, "Nie znaleziono wybranego konta.");
            if (!await _ensureReady(selected, cancellationToken).ConfigureAwait(false))
                return new(false, "Nie udało się uruchomić silnika tego konta.", selected);
            _managed.Add(selected);

            if (!string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase))
            {
                // The worker can hide successfully before its acknowledgement
                // is cancelled/lost. Treat even that uncertain result as a hide
                // attempt and restore the old window on failure.
                previousHidden = true;
                MinerWorkspaceCommandResponse hidden = await _send(previous, "workspace-hide", null, cancellationToken).ConfigureAwait(false);
                if (!hidden.Success) throw new InvalidOperationException(hidden.Message);
                previousPlacement = placement ?? hidden.Placement;
                _lastPlacement = previousPlacement ?? _lastPlacement;
            }
            else if (placement != null)
                _lastPlacement = placement;

            showAttempted = true;
            MinerWorkspacePlacement? showPlacement = string.Equals(previous, selected, StringComparison.OrdinalIgnoreCase)
                ? placement : _lastPlacement;
            MinerWorkspaceCommandResponse shown = await _send(selected, "workspace-show", showPlacement, cancellationToken).ConfigureAwait(false);
            if (!shown.Success) throw new InvalidOperationException(shown.Message);
            _activeProfileId = selected;
            return new(true, "Wybrano konto.", selected);
        }
        catch (Exception ex)
        {
            // Showing may have succeeded before the transport failed. Hide that
            // uncertain window before restoring the previous one.
            bool canRestore = true;
            if (previousHidden && showAttempted && selected != null)
            {
                try { canRestore = (await _send(selected, "workspace-hide", null, CancellationToken.None).ConfigureAwait(false)).Success; }
                catch { canRestore = false; }
            }
            if (previousHidden && canRestore)
            {
                try { _ = await _send(previous, "workspace-show", previousPlacement, CancellationToken.None).ConfigureAwait(false); }
                catch { /* The tray can retry restoring this profile. */ }
            }
            else if (previousHidden && selected != null)
                _activeProfileId = selected;
            if (ex is OperationCanceledException) throw;
            return new(false, "Nie udało się przełączyć konta: " + ex.Message, _activeProfileId);
        }
        finally { _gate.Release(); }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_closing) return;
            _closing = true;
            string[] profiles = _managed.Concat(_profileIds()).Append(MinerProfileId.Default)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            await Task.WhenAll(profiles.Select(async profile =>
            {
                try { _ = await _send(profile, "stop", null, cancellationToken).ConfigureAwait(false); }
                catch { /* Stopped/unavailable profiles must not prevent exit. */ }
                if (profile == MinerProfileId.Default) return;
                try { _ = await _send(profile, "workspace-close", null, cancellationToken).ConfigureAwait(false); }
                catch { /* Workers also watch the coordinator process lifetime. */ }
            })).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private string? FindProfile(string? profileId)
    {
        string requested = profileId?.Trim() ?? string.Empty;
        if (string.Equals(requested, MinerProfileId.Default, StringComparison.OrdinalIgnoreCase))
            return MinerProfileId.Default;
        return _profileIds().FirstOrDefault(id => string.Equals(id, requested, StringComparison.OrdinalIgnoreCase));
    }
}
