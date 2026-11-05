using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper
{
    public partial class App : Application
    {
        private const int SplashMinDurationMs = 1300;
#if MINER_EDITION
        private const string SingleInstanceMutexName = @"Local\MinecraftHelper.Miner.SingleInstance";
#else
        private const string SingleInstanceMutexName = @"Local\MinecraftHelper.SingleInstance";
#endif
        private Mutex? _singleInstanceMutex;
        private bool _ownsSingleInstanceMutex;
        private readonly CancellationTokenSource _lifetimeStop = new();

        internal MinerWorkspaceCoordinator? Workspace { get; private set; }

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            // Nieobsłużony wyjątek nadal kończy program, ale jego przyczyna zostaje w app.log.
            DispatcherUnhandledException += (_, args) => AppLog.Error("Awaria", "Nieobsłużony wyjątek w wątku UI", args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                AppLog.Error("Awaria", "Nieobsłużony wyjątek", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                AppLog.Error("Awaria", "Nieobserwowany wyjątek zadania w tle", args.Exception);
                args.SetObserved();
            };

            string? requestedProfileId = GetArgumentValue(e.Args, "--profile");
            bool workspaceWorker = Array.Exists(e.Args, argument => string.Equals(argument, "--worker", StringComparison.OrdinalIgnoreCase));
            // Normal launches always enter the one workspace. Profiles are only
            // separate engines when started internally by its coordinator.
            string profileId = workspaceWorker ? MinerProfileId.Normalize(requestedProfileId) : MinerProfileId.Default;
            AppLog.Initialize(new SettingsService(profileId).SettingsDirectoryPath);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string mutexName = profileId == MinerProfileId.Default
                ? SingleInstanceMutexName
                : $"{SingleInstanceMutexName}.{profileId}";

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                mutexName,
                out bool createdNew);

            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                if (!workspaceWorker)
                    await ForwardToWorkspaceAsync(requestedProfileId);
                Shutdown(0);
                return;
            }

            _ownsSingleInstanceMutex = true;
            StartupSplashWindow? splash = null;
            try
            {
                int workspaceProcessId = 0;
                if (workspaceWorker)
                {
                    if (!int.TryParse(GetArgumentValue(e.Args, "--workspace-pid"), out workspaceProcessId)
                        || workspaceProcessId <= 0 || workspaceProcessId == Environment.ProcessId)
                        throw new InvalidOperationException("Silnik konta wymaga działającego panelu Kopacza.");
                    using Process owner = Process.GetProcessById(workspaceProcessId);
                    if (owner.HasExited)
                        throw new InvalidOperationException("Panel Kopacza został już zamknięty.");
                }
                else
                {
                    splash = new StartupSplashWindow(SplashMinDurationMs);
                    splash.Show();
                    await Task.Delay(SplashMinDurationMs);
                }

                var mainWindow = new MainWindow(profileId, workspaceWorker);
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                if (workspaceWorker)
                {
                    // Constructor starts the engine's timers. No Show(), splash,
                    // startup notice or taskbar window is needed for background work.
                    _ = ObserveWorkspaceOwnerAsync(mainWindow, workspaceProcessId, _lifetimeStop.Token);
                }
                else
                {
                    Workspace = new MinerWorkspaceCoordinator(mainWindow.RequestWorkspaceExit);
                    Workspace.Start();
                    // Jeden bot Discord na cały Kopacz — w głównym procesie, nie w procesach kont.
                    DiscordBotHost.Start();
                    mainWindow.Closed += (_, __) => DiscordBotHost.Current?.Dispose();
                    // Paczkę zasobów tworzy tylko główny proces — konta nie ścigają się o ten sam plik.
                    mainWindow.Loaded += async (_, __) => await mainWindow.EnsureResourcePackAsync();
                    MinerWorkspaceResponse response = await Workspace.SelectAsync(requestedProfileId ?? MinerProfileId.Default);
                    if (!response.Success)
                    {
                        await Workspace.SelectAsync(MinerProfileId.Default);
                        GlassMessageDialog.Show(mainWindow, "Minecraft Helper — konto", response.Message, GlassMessageKind.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Program", "Nie udało się uruchomić Kopacza", ex);
                if (!workspaceWorker)
                    GlassMessageDialog.Show(null, "Minecraft Helper — błąd uruchamiania",
                        "Nie udało się uruchomić programu.\n\n" + ex.GetBaseException().Message, GlassMessageKind.Error);
                Shutdown(-1);
            }
            finally
            {
                splash?.Close();
            }
        }

        private static async Task ForwardToWorkspaceAsync(string? profileId)
        {
            var waiting = Stopwatch.StartNew();
            while (waiting.Elapsed < TimeSpan.FromSeconds(10))
            {
                try
                {
                    MinerWorkspaceResponse response = await MinerWorkspaceClient.SelectAsync(profileId);
                    if (!response.Success)
                        GlassMessageDialog.Show(null, "Minecraft Helper — konto", response.Message, GlassMessageKind.Warning);
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.Warn("Program", "Oczekiwanie na panel Kopacza: " + ex.Message);
                    await Task.Delay(150);
                }
            }

            GlassMessageDialog.Show(null, "Minecraft Helper — otwarty panel",
                "Nie można połączyć się z otwartym panelem Kopacza. Jeśli działa poprzednia wersja, zamknij wszystkie jej profile i uruchom Kopacza ponownie.", GlassMessageKind.Warning);
        }

        private async Task ObserveWorkspaceOwnerAsync(MainWindow worker, int workspaceProcessId, CancellationToken cancellationToken)
        {
            try
            {
                using Process owner = Process.GetProcessById(workspaceProcessId);
                await owner.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ArgumentException)
            {
                // The owner exited before the watcher attached.
            }
            catch (InvalidOperationException)
            {
                // A process that has already exited also ends this engine.
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // If the owner can no longer be monitored, do not leave an
                // unattended engine alive after its workspace disappears.
                AppLog.Warn("Program", "Nie można obserwować panelu Kopacza: " + ex.Message);
            }

            if (!cancellationToken.IsCancellationRequested && !Dispatcher.HasShutdownStarted)
            {
                try
                {
                    await Dispatcher.InvokeAsync(worker.RequestWorkspaceExit);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || Dispatcher.HasShutdownStarted)
                {
                }
            }
        }

        private static string? GetArgumentValue(string[] args, string option)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                    continue;

                return index + 1 < args.Length ? args[index + 1] : null;
            }

            return null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _lifetimeStop.Cancel();
            Workspace?.Dispose();
            Workspace = null;
            if (_ownsSingleInstanceMutex)
            {
                try
                {
                    _singleInstanceMutex?.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Proces kończy działanie; uchwyt zostanie zwolniony poniżej.
                }
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;
            base.OnExit(e);
        }
    }
}
