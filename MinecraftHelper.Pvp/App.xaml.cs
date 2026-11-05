using System;
using System.Threading;
using System.Windows;

namespace MinecraftHelper;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\MinecraftHelper.AutoClicker.SingleInstance";
    private const string ShowWindowEventName = @"Local\MinecraftHelper.AutoClicker.ShowWindow";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private RegisteredWaitHandle? _showWindowRegistration;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Elevated lag switch helper: no window, no single-instance mutex.
        if (e.Args.Length == 3 && e.Args[0] == Services.LagSwitchClient.HelperArgument)
        {
            int parentProcessId = int.TryParse(e.Args[2], out int parsed) ? parsed : 0;
            int exitCode = Services.LagSwitchHelperHost.Run(e.Args[1], parentProcessId);
            Shutdown(exitCode);
            return;
        }

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using EventWaitHandle showWindowEvent = EventWaitHandle.OpenExisting(ShowWindowEventName);
                showWindowEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Pierwsza instancja jest jeszcze w trakcie uruchamiania.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        _ownsSingleInstanceMutex = true;
        _showWindowEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShowWindowEventName);
        _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showWindowEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow mainWindow)
                    mainWindow.RestoreFromBackground();
            }),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWindowRegistration?.Unregister(null);
        _showWindowRegistration = null;
        _showWindowEvent?.Dispose();
        _showWindowEvent = null;

        if (_ownsSingleInstanceMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Proces kończy działanie; uchwyt zostanie zaraz zwolniony.
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _ownsSingleInstanceMutex = false;
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (MainWindow is MainWindow mainWindow)
            mainWindow.AllowApplicationExit();

        base.OnSessionEnding(e);
    }
}

