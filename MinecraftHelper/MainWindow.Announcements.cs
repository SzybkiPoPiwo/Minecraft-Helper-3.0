using System.Windows;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Komunikaty od autora: dzwonek w pasku bocznym (wspólna kontrolka AnnouncementBell). Okno główne
// pobiera plik z GitHuba przy starcie i co kilka godzin; procesy-silniki kont niczego nie pobierają.
public partial class MainWindow
{
    private AnnouncementBell? _announcementBell;
    private AnnouncementController? _announcements;
    private bool _isLoadingAnnouncementUi;

    private void InitializeAnnouncements()
    {
        if (_workspaceWorker)
            return;

        // Odstęp pod dzwonkiem należy do niego, żeby ukryty dzwonek nie zostawiał pustego miejsca.
        // Kliknięcie okienka powiadomienia przywraca program tak samo jak powiadomienie o pracy w tle.
        _announcementBell = new AnnouncementBell(compact: false, opensToTheRight: true)
        {
            Margin = new Thickness(0, 0, 0, 10),
            ToastIconResource = "Assets/miner.ico",
            RestoreWindow = RestoreFromTray
        };
        HostAnnouncementBell.Child = _announcementBell;
        _announcements = new AnnouncementController(_announcementBell, _currentAppVersion, UpdateCheckService.IsMinerEdition);

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
