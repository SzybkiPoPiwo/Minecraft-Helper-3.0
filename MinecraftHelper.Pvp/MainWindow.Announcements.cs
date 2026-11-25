using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Komunikaty od autora: dzwonek w nagłówku okna (wspólna kontrolka AnnouncementBell, tu w wersji
// zwartej — sama ikona z licznikiem, bo okno jest wąskie). Ustawienie i stan „przeczytane” są
// wspólne z Kopaczem (%AppData%\Minecraft Helper\announcements-state.json).
public partial class MainWindow
{
    private AnnouncementBell? _announcementBell;
    private AnnouncementController? _announcements;
    private bool _isLoadingAnnouncementUi;

    private void InitializeAnnouncements()
    {
        // Odstęp od chipa okna gry należy do dzwonka, żeby ukryty nie zostawiał pustego miejsca.
        // Kliknięcie okienka powiadomienia przywraca program tak samo jak powiadomienie o pracy w tle.
        _announcementBell = new AnnouncementBell(compact: true, opensToTheRight: false)
        {
            Margin = new Thickness(0, 0, 8, 0),
            ToastIconResource = "Assets/autoclicker.ico",
            RestoreWindow = RestoreFromTray
        };
        HostAnnouncementBell.Child = _announcementBell;
        _announcements = new AnnouncementController(_announcementBell, _currentAppVersion, UpdateCheckService.IsMinerEdition);
        _announcements.UpdateChecked += ApplyVersionPill;

        _isLoadingAnnouncementUi = true;
        ChkAnnouncementsEnabled.IsChecked = _announcements.ReadEnabledFromStore();
        _isLoadingAnnouncementUi = false;

        Closed += (_, _) => _announcements?.Dispose();
    }

    private void StartAnnouncements() => _announcements?.Start();

    private void ChkAnnouncementsEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingAnnouncementUi || _announcements == null)
            return;
        _announcements.SetEnabled(ChkAnnouncementsEnabled.IsChecked == true);
    }
}
