using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MinecraftHelper.Services;

namespace MinecraftHelper
{
    /// <summary>
    /// Zasila dzwonek: jedno sprawdzenie przy starcie i kolejne co kilka godzin. Każde sprawdzenie to dwie
    /// niezależne rzeczy: (1) czy jest nowsza wersja programu — zawsze, bo to ona zastępuje dawne okno
    /// aktualizacji, oraz (2) komunikaty od autora — tylko gdy są włączone w ustawieniach. Wspólny dla
    /// obu programów. Tylko okno główne — procesy-silniki kont Kopacza w ogóle go nie tworzą
    /// (jedno zapytanie na komputer, nie na konto).
    /// </summary>
    internal sealed class AnnouncementController : IDisposable
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(2);

        private readonly AnnouncementBell _bell;
        private readonly AnnouncementStateStore _store;
        private readonly string _currentVersion;
        private readonly bool _minerEdition;
        private readonly DispatcherTimer _timer;
        private CancellationTokenSource? _cancellation;
        private Task? _inFlight;
        private AnnouncementItem? _updateItem;
        private IReadOnlyList<AnnouncementItem> _announcements = Array.Empty<AnnouncementItem>();
        private bool _enabled;
        private bool _disposed;

        /// <summary>Po każdym sprawdzeniu aktualizacji: czy się udało i jaka wersja jest dostępna (null = aktualna albo nieznane).</summary>
        public event Action<bool, string?>? UpdateChecked;

        public AnnouncementController(AnnouncementBell bell, string currentVersion, bool minerEdition, AnnouncementStateStore? store = null)
        {
            _bell = bell;
            _currentVersion = currentVersion;
            _minerEdition = minerEdition;
            _store = store ?? new AnnouncementStateStore();
            _timer = new DispatcherTimer { Interval = RefreshInterval };
            _timer.Tick += async (_, _) => await RefreshAsync();
            _bell.MarkedRead += OnMarkedRead;
        }

        /// <summary>Czy komunikaty od autora są włączone (wspólna wartość obu programów, z pliku stanu).</summary>
        public bool ReadEnabledFromStore() => _store.Load().Enabled;

        /// <summary>Czy ostatnie sprawdzenie aktualizacji się udało (false też przed pierwszym).</summary>
        public bool LastUpdateCheckSucceeded { get; private set; }

        /// <summary>Wersja nowsza od zainstalowanej; null, gdy aktualna albo jeszcze nie wiadomo.</summary>
        public string? AvailableVersion { get; private set; }

        public void Start()
        {
            _enabled = _store.Load().Enabled;
            _timer.Start();
#if DEBUG
            StartDevFileWatcher();
#endif
            _ = RefreshAsync();
        }

        public void SetEnabled(bool enabled)
        {
            if (_disposed || enabled == _enabled)
                return;

            _enabled = enabled;
            _store.SetEnabled(enabled);
            if (enabled)
            {
#if DEBUG
                if (_devFileActive)
                {
                    ApplyDevFile();
                    return;
                }
#endif
                _ = RefreshAsync();
            }
            else
            {
                // Komunikaty znikają od razu; informacja o nowej wersji zostaje.
                _announcements = Array.Empty<AnnouncementItem>();
                Publish();
            }
        }

        /// <summary>Sprawdza teraz. Gdy sprawdzanie już trwa, czeka na nie zamiast zaczynać drugie.</summary>
        public async Task RefreshAsync()
        {
            if (_disposed)
                return;
#if DEBUG
            // Testy na żywo: dopóki istnieje plik testowy, dane idą z niego, a nie z GitHuba.
            if (_devFileActive)
                return;
#endif
            if (_inFlight != null)
            {
                await _inFlight;
                return;
            }

            var finished = new TaskCompletionSource();
            _inFlight = finished.Task;
            try
            {
                await RunAsync();
            }
            finally
            {
                _inFlight = null;
                finished.TrySetResult();
            }
        }

        private async Task RunAsync()
        {
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            try
            {
                bool announcementsOn = _enabled;
                Task<UpdateCheckResult> updateTask = UpdateCheckService.CheckAsync(_currentVersion, token);
                Task<IReadOnlyList<AnnouncementItem>?> announcementsTask = announcementsOn
                    ? AnnouncementService.FetchAsync(_currentVersion, _minerEdition, token)
                    : Task.FromResult<IReadOnlyList<AnnouncementItem>?>(null);
                UpdateCheckResult update = await updateTask;
                IReadOnlyList<AnnouncementItem>? announcements = await announcementsTask;
                if (_disposed || token.IsCancellationRequested)
                    return;

                // Nieudane sprawdzenie nic nie zmienia: zostaje to, co program już wie.
                LastUpdateCheckSucceeded = update.Succeeded;
                if (update.Succeeded)
                {
                    AvailableVersion = update.Update?.LatestVersion;
                    _updateItem = update.Update == null ? null : UpdateAnnouncements.FromUpdate(update.Update);
                }
                if (announcementsOn && _enabled && announcements != null)
                    _announcements = announcements;

                Publish();
                UpdateChecked?.Invoke(update.Succeeded, AvailableVersion);
            }
            catch (Exception)
            {
                // Dzwonek jest dodatkiem — żaden błąd nie może zatrzymać programu.
            }
        }

        private void Publish()
        {
            IReadOnlyList<AnnouncementItem> items = UpdateAnnouncements.Combine(_updateItem, _announcements);
            _bell.SetItems(items, _store.Load().Read);
        }

        private void OnMarkedRead(IReadOnlyCollection<string> ids) => _store.MarkRead(ids);

#if DEBUG
        // ===== Tylko kompilacja Debug (testy na żywo; w Release i w instalatorze tego kodu nie ma) =====
        // Plik %AppData%\Minecraft Helper\announcements-dev.json zastępuje GitHub: ma ten sam format co
        // announcements.json (przechodzi przez ten sam parser i filtry), a opcjonalne pole „update”
        // udaje nową wersję: { "update": { "version": "3.0.4", "changes": [ "…", "…" ] } }.
        // Zmiana pliku odświeża dzwonek w ciągu sekundy; usunięcie pliku wraca do GitHuba.
        private static readonly string DevFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Helper", "announcements-dev.json");

        private DispatcherTimer? _devTimer;
        private DateTime _devStampUtc;
        private bool _devFileActive;

        private void StartDevFileWatcher()
        {
            _devTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _devTimer.Tick += (_, _) => CheckDevFile();
            _devTimer.Start();
            CheckDevFile();
        }

        private void CheckDevFile()
        {
            if (_disposed)
                return;
            bool exists = System.IO.File.Exists(DevFilePath);
            DateTime stamp = exists ? System.IO.File.GetLastWriteTimeUtc(DevFilePath) : DateTime.MinValue;
            if (exists == _devFileActive && stamp == _devStampUtc)
                return;

            _devFileActive = exists;
            _devStampUtc = stamp;
            if (exists)
                ApplyDevFile();
            else
                _ = RefreshAsync();
        }

        private void ApplyDevFile()
        {
            try
            {
                string json = System.IO.File.ReadAllText(DevFilePath);
                if (!AnnouncementParser.TryParse(json, _minerEdition, _currentVersion, DateTime.UtcNow, out IReadOnlyList<AnnouncementItem> items))
                    return;

                _updateItem = null;
                AvailableVersion = null;
                using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json,
                           new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip }))
                {
                    if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                        && document.RootElement.TryGetProperty("update", out System.Text.Json.JsonElement update)
                        && update.ValueKind == System.Text.Json.JsonValueKind.Object
                        && update.TryGetProperty("version", out System.Text.Json.JsonElement versionElement)
                        && versionElement.GetString() is string version && version.Length > 0)
                    {
                        var changes = new List<string>();
                        if (update.TryGetProperty("changes", out System.Text.Json.JsonElement array) && array.ValueKind == System.Text.Json.JsonValueKind.Array)
                            changes.AddRange(array.EnumerateArray().Select(c => c.GetString() ?? string.Empty));
                        var page = new Uri($"https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases/tag/v{version}");
                        var info = new AppUpdateInfo(_currentVersion, version, $"Minecraft Helper {version}", page, DateTimeOffset.UtcNow.AddHours(-2),
                            changes.Count == 0 ? null : new[] { new UpdateReleaseNotes(version, new[] { new UpdateNotesSection("Oba programy", changes) }) }, page);
                        _updateItem = UpdateAnnouncements.FromUpdate(info);
                        AvailableVersion = version;
                    }
                }

                _announcements = _enabled ? items : Array.Empty<AnnouncementItem>();
                LastUpdateCheckSucceeded = true;
                Publish();
                UpdateChecked?.Invoke(true, AvailableVersion);
            }
            catch (Exception)
            {
                // Plik jest właśnie zapisywany albo jest uszkodzony: spróbujemy przy następnej zmianie.
                _devStampUtc = DateTime.MinValue;
            }
        }
#endif

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer.Stop();
#if DEBUG
            _devTimer?.Stop();
#endif
            _cancellation?.Cancel();
            _bell.MarkedRead -= OnMarkedRead;
        }
    }
}
