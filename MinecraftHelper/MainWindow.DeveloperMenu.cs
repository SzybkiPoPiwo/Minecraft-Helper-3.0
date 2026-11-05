using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Menu developera w osobnym oknie: wszystkie przyciski testowe (ochrona i reconnect, Auto EQ,
// złoto, Discord, paczka zasobów) oraz podgląd ostatnich wpisów dziennika — wynik testu widać
// od razu, choć poszczególne funkcje piszą swoje komunikaty w różnych zakładkach.
public partial class MainWindow
{
    private const int DeveloperLogLines = 14;
    private readonly List<AppLogEntry> _developerLog = new();
    private DeveloperMenuWindow? _developerMenuWindow;
    private Panel? _developerMenuHome;
    private int _developerMenuHomeIndex;

    private void InitializeDeveloperMenu()
    {
        _developerLog.AddRange(AppLog.Snapshot().TakeLast(DeveloperLogLines));
        AppLog.EntryAdded += OnDeveloperLogEntry;
        Closed += (_, _) =>
        {
            AppLog.EntryAdded -= OnDeveloperLogEntry;
            _developerMenuWindow?.Close();
        };
        // Panel w XAML stoi w karcie Ustawień tylko jako miejsce przechowania — widać go w oknie.
        PanelDeveloperMenu.Visibility = Visibility.Collapsed;
    }

    // Z karty w Ustawieniach i z zakładki Ochrona. Drugie kliknięcie wyciąga istniejące okno na wierzch.
    private void BtnOpenDeveloperMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_developerMenuWindow != null)
        {
            if (_developerMenuWindow.WindowState == WindowState.Minimized)
                _developerMenuWindow.WindowState = WindowState.Normal;
            _developerMenuWindow.Activate();
            return;
        }

        _developerMenuHome = (Panel)PanelDeveloperMenu.Parent;
        _developerMenuHomeIndex = _developerMenuHome.Children.IndexOf(PanelDeveloperMenu);
        _developerMenuHome.Children.Remove(PanelDeveloperMenu);
        PanelDeveloperMenu.Visibility = Visibility.Visible;

        var window = new DeveloperMenuWindow(this, _accountDisplayName);
        window.Host(PanelDeveloperMenu);
        window.Closed += (_, _) => ReturnDeveloperMenuPanel(window);
        _developerMenuWindow = window;
        window.Show();
        RenderDeveloperLog();
    }

    private void ReturnDeveloperMenuPanel(DeveloperMenuWindow window)
    {
        window.Release();
        PanelDeveloperMenu.Visibility = Visibility.Collapsed;
        if (_developerMenuHome != null)
            _developerMenuHome.Children.Insert(Math.Min(_developerMenuHomeIndex, _developerMenuHome.Children.Count), PanelDeveloperMenu);
        _developerMenuWindow = null;
    }

    private void BtnDeveloperClearLog_Click(object sender, RoutedEventArgs e)
    {
        _developerLog.Clear();
        RenderDeveloperLog();
    }

    // AppLog wywołuje zdarzenie z dowolnego wątku — lista i rysowanie tylko na wątku okna.
    private void OnDeveloperLogEntry(AppLogEntry entry) => Dispatcher.BeginInvoke(() =>
    {
        // Ten sam komunikat często idzie jednocześnie pod źródłem funkcji i „Kopacz” — wystarczy raz.
        if (entry.Source == "Kopacz" && _developerLog.Count > 0 && _developerLog[^1].Message == entry.Message)
            return;
        _developerLog.Add(entry);
        if (_developerLog.Count > DeveloperLogLines)
            _developerLog.RemoveAt(0);
        RenderDeveloperLog();
    });

    // Rysujemy tylko przy otwartym oknie — w tle nic się nie przebudowuje.
    private void RenderDeveloperLog()
    {
        if (TxtDeveloperLog == null || _developerMenuWindow == null)
            return;

        TxtDeveloperLog.Inlines.Clear();
        if (_developerLog.Count == 0)
        {
            TxtDeveloperLog.Inlines.Add(new Run("Brak wpisów — uruchom test, a jego przebieg pojawi się tutaj.") { Foreground = GlassPalette.Muted });
            return;
        }

        for (int index = 0; index < _developerLog.Count; index++)
        {
            AppLogEntry entry = _developerLog[index];
            if (index > 0)
                TxtDeveloperLog.Inlines.Add(new LineBreak());
            TxtDeveloperLog.Inlines.Add(new Run(entry.Time.ToString("HH:mm:ss") + "  ") { Foreground = GlassPalette.Muted });
            TxtDeveloperLog.Inlines.Add(new Run($"[{entry.Source}] ") { Foreground = GlassPalette.AccentInk });
            TxtDeveloperLog.Inlines.Add(new Run(entry.Message)
            {
                Foreground = entry.Level switch
                {
                    AppLogLevel.Error => GlassPalette.Bad,
                    AppLogLevel.Warning => GlassPalette.Warn,
                    _ => GlassPalette.Ink
                }
            });
        }
    }
}
