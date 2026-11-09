using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MinecraftHelper;

// Samouczek w trybie ćwiczeń: wspólne okno z Kopaczem, kroki w AutoClickerTutorial.
// Przy pierwszym uruchomieniu (jeżeli instalator go nie wyłączył) pokazuje się zamiast programu,
// a po zamknięciu w tym samym miejscu pojawia się prawdziwe okno.
public partial class MainWindow
{
    private bool _tutorialPending;

    // Wołane z konstruktora: okno startuje poza ekranem, żeby nie mignęło przed makietą.
    private void PrepareStartupTutorial()
    {
        _tutorialPending = !_settings.TutorialSeen
            && PracticeTutorialWindow.ReadInstallerPreference(out _);
        if (!_tutorialPending)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
    }

    private void RunStartupTutorial()
    {
        _tutorialPending = false;
        Rect workArea = SystemParameters.WorkArea;
        double width = Math.Min(Width, workArea.Width);
        double height = Math.Min(Height, workArea.Height);
        var bounds = new Rect(
            workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2,
            width,
            height);
        Hide();
        Rect closedAt = RunTutorial(bounds, fromProgram: false, tab: null);

        // Prawdziwe okno pojawia się tam, gdzie był samouczek.
        Left = closedAt.Left + (closedAt.Width - Width) / 2;
        Top = closedAt.Top + (closedAt.Height - Height) / 2;
        ShowAfterTutorial();
    }

    private void BtnShowTutorial_Click(object sender, RoutedEventArgs e) => ShowTutorialFromProgram(tab: null);

    private void BtnTabTutorial_Click(object sender, RoutedEventArgs e) => ShowTutorialFromProgram(CurrentTutorialTab());

    // Nazwy takie same, jak w krokach samouczka; zakładka Ustawienia ma w nagłówku tylko ikonę.
    private string CurrentTutorialTab() =>
        ReferenceEquals(MainTabs.SelectedItem, TabSettings)
            ? "Ustawienia"
            : (MainTabs.SelectedItem as TabItem)?.Header as string ?? "Clicker";

    private void ShowTutorialFromProgram(string? tab)
    {
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        Hide();
        RunTutorial(bounds, fromProgram: true, tab);
        ShowAfterTutorial();
    }

    private Rect RunTutorial(Rect bounds, bool fromProgram, string? tab)
    {
        var tutorial = new PracticeTutorialWindow(AutoClickerTutorial.Content, bounds, fromProgram, tab);
        try
        {
            tutorial.ShowDialog();
        }
        catch (Exception exception)
        {
            UpdateStatusBar($"Samouczek zakończył się błędem: {exception.Message}", "Red");
        }

        if (tab == null)
            MarkTutorialSeen();
        return new Rect(tutorial.Left, tutorial.Top, tutorial.Width, tutorial.Height);
    }

    private void ShowAfterTutorial()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    // Przy nowej instalacji samouczek zastępuje okno „Co nowego”; po aktualizacji to okno pokaże się po nim.
    private void MarkTutorialSeen()
    {
        try
        {
            _settings.TutorialSeen = true;
            if (_isFirstRun)
                _settings.LastAcknowledgedVersion = _currentAppVersion;
            _settingsService.Save(_settings);
            _pendingChanges = false;
        }
        catch (Exception exception)
        {
            UpdateStatusBar($"Nie zapisano stanu samouczka: {exception.Message}", "Red");
        }
    }
}
