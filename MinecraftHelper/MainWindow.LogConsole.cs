using System;
using System.Globalization;
using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Dziennik zdarzeń Kopacza: plik app.log w folderze profilu i konsola pod przyciskiem
// w pasku bocznym. Plakietka pokazuje błędy, które pojawiły się od ostatniego otwarcia.
public partial class MainWindow
{
    private LogConsoleWindow? _logConsole;
    private int _unseenLogErrors;

    private void InitializeAppLog()
    {
        AppLog.Initialize(_settingsService.SettingsDirectoryPath);
        AppLog.EntryAdded += OnAppLogEntryAdded;
        Closed += (_, __) => AppLog.EntryAdded -= OnAppLogEntryAdded;
        AppLog.Info("Program", $"Start Kopacza {ReleaseNotesCatalog.CurrentVersion}, profil „{_accountDisplayName}” ({_accountProfileId}).");
    }

    private void OnAppLogEntryAdded(AppLogEntry entry)
    {
        if (entry.Level != AppLogLevel.Error)
            return;

        // Ochrona, reconnect i złoto mają własne powiadomienia; błędy Discorda nie mogą zapętlić wysyłki.
        // W trakcie alarmu obrażeń wszystko ogłasza jedna karta alarmu.
        if (entry.Source is not ("Discord" or "Ochrona" or "Reconnect" or "Złoto") && !_emergencyDamageSoundHandlingAlarm)
        {
            if (_loggingStatusMessage)
                NotifyDiscordStatusError(entry);
            else
                NotifyDiscord(DiscordAlertKind.Error, "❗ Błąd: " + entry.Source, entry.Message);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_logConsole != null)
                return;
            _unseenLogErrors++;
            UpdateLogErrorBadge();
        });
    }

    private void BtnLogConsole_Click(object sender, RoutedEventArgs e)
    {
        _unseenLogErrors = 0;
        UpdateLogErrorBadge();

        if (_logConsole != null)
        {
            if (_logConsole.WindowState == WindowState.Minimized)
                _logConsole.WindowState = WindowState.Normal;
            _logConsole.Activate();
            return;
        }

        _logConsole = new LogConsoleWindow { Owner = this };
        _logConsole.Closed += (_, __) => _logConsole = null;
        _logConsole.Show();
    }

    private void UpdateLogErrorBadge()
    {
        if (BadgeLogErrors == null)
            return;
        BadgeLogErrors.Visibility = _unseenLogErrors > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtLogErrorsCount.Text = _unseenLogErrors > 99 ? "99+" : _unseenLogErrors.ToString(CultureInfo.InvariantCulture);
    }

    // Komunikaty ze statusów w GUI trafiają też do dziennika; kolor wyznacza poziom.
    // Czerwony komunikat paska to też wpis BŁĄD w dzienniku — EntryAdded przychodzi synchronicznie,
    // więc ta flaga mówi obsłudze dziennika, że to komunikat stanu, a nie wyjątek programu.
    [ThreadStatic]
    private static bool _loggingStatusMessage;

    private static void LogStatus(string source, string message, string colorName)
    {
        if (string.IsNullOrWhiteSpace(message) || message == "Gotowy")
            return;
        if (colorName == "Red")
        {
            _loggingStatusMessage = true;
            try
            {
                AppLog.Error(source, message);
            }
            finally
            {
                _loggingStatusMessage = false;
            }
        }
        else if (colorName == "Orange")
            AppLog.Warn(source, message);
        else
            AppLog.Info(source, message);
    }
}
