using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using MinecraftHelper.Services;

namespace MinecraftHelper
{
    // Samouczek w trybie ćwiczeń: przy pierwszym uruchomieniu (jeżeli instalator go nie wyłączył)
    // zamiast Kopacza otwiera się makieta; po jej zamknięciu w tym samym miejscu pojawia się
    // prawdziwe okno. Ponownie: Ustawienia → Pokaż samouczek.
    public partial class MainWindow
    {
        private bool _tutorialPending;

        private static bool ReadInstallerTutorialPreference()
        {
            bool show = PracticeTutorialWindow.ReadInstallerPreference(out string? error);
            if (error != null)
                AppLog.Warn("Samouczek", "Nie udało się odczytać wyboru z instalatora: " + error);
            return show;
        }

        /// <summary>
        /// Wołane z konstruktora. Gdy samouczek ma się pokazać, okno Kopacza startuje poza ekranem,
        /// żeby nie mignęło przed makietą.
        /// </summary>
        private void PrepareStartupTutorial()
        {
            _tutorialPending = !_workspaceWorker
                && !_settings.TutorialSeen
                && ReadInstallerTutorialPreference();
            if (!_tutorialPending)
                return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
        }

        /// <summary>
        /// Wołane przez App, gdy start jest zakończony (komenda workspace-show dostała odpowiedź,
        /// ekran startowy zamknięty). Samouczek rusza w osobnym kroku dispatchera, bo jest modalny.
        /// </summary>
        internal void BeginStartupTutorial()
        {
            if (!_tutorialPending)
                return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => RunStartupTutorialIfPending()));
        }

        /// <summary>
        /// Zabezpieczenie: gdyby App nie wywołał BeginStartupTutorial, okno Kopacza nie może zostać
        /// poza ekranem — po 15 s samouczek (i potem Kopacz) pokazuje się i tak.
        /// </summary>
        private void ArmStartupTutorialFallback()
        {
            var fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            fallback.Tick += (_, _) =>
            {
                fallback.Stop();
                RunStartupTutorialIfPending();
            };
            fallback.Start();
        }

        private bool RunStartupTutorialIfPending()
        {
            if (!_tutorialPending)
                return false;
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
            Rect closedAt = RunTutorial(bounds, fromSettings: false);

            // Prawdziwy Kopacz pojawia się tam, gdzie był samouczek.
            Left = closedAt.Left + (closedAt.Width - Width) / 2;
            Top = closedAt.Top + (closedAt.Height - Height) / 2;
            ShowAfterTutorial();
            return true;
        }

        private void BtnShowTutorial_Click(object sender, RoutedEventArgs e) => ShowTutorialFromProgram(tab: null);

        /// <summary>Samouczek z działającego programu: cały (Ustawienia) albo jednej zakładki.</summary>
        private void ShowTutorialFromProgram(string? tab)
        {
            Rect bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            Hide();
            RunTutorial(bounds, fromSettings: true, tab);
            ShowAfterTutorial();
        }

        private Rect RunTutorial(Rect bounds, bool fromSettings, string? tab = null)
        {
            var tutorial = new PracticeTutorialWindow(KopaczTutorial.Content, bounds, fromSettings, tab);
            try
            {
                tutorial.ShowDialog();
            }
            catch (Exception ex)
            {
                AppLog.Error("Samouczek", "Samouczek zakończył się błędem", ex);
            }

            if (tab == null)
                MarkTutorialSeen();
            return new Rect(tutorial.Left, tutorial.Top, tutorial.Width, tutorial.Height);
        }

        // Nazwy zakładek takie same, jak w krokach samouczka (KopaczTutorial).
        private static readonly Dictionary<string, string> TabTutorialNames = new()
        {
            ["TabBackground"] = "Okno gry",
            ["TabSettings"] = "Ustawienia",
            ["TabKopacz"] = "Kopanie",
            ["TabInventory"] = "Ekwipunek",
            ["TabGoldDeposit"] = "Wpłata złota",
            ["TabEnderchest"] = "Enderchest",
            ["TabProtection"] = "Ochrona",
            ["TabAccounts"] = "Konta",
            ["TabDiscord"] = "Discord",
            ["TabTools"] = "Narzędzia",
        };

        private readonly HashSet<string> _tabsWithTutorialButton = new();

        /// <summary>
        /// Przycisk „Samouczek” obok tytułu każdej zakładki. Wołane po zbudowaniu interfejsu i po
        /// utworzeniu panelu Konta (powstaje dopiero przy pierwszym wejściu w zakładkę);
        /// każda zakładka dostaje przycisk tylko raz.
        /// </summary>
        private void AttachTabTutorialButtons()
        {
            if (TryFindResource("GlassH1") is not Style titleStyle)
                return;

            foreach (object item in MainTabControl.Items)
            {
                if (item is not TabItem tabItem
                    || _tabsWithTutorialButton.Contains(tabItem.Name)
                    || !TabTutorialNames.TryGetValue(tabItem.Name, out string? tab))
                    continue;
                TextBlock? title = FindFirst<TextBlock>(tabItem, text => text.Style == titleStyle);
                if (title?.Parent is not Panel parent)
                    continue;
                _tabsWithTutorialButton.Add(tabItem.Name);

                var button = new Button
                {
                    Content = "Samouczek",
                    Style = GlassChrome.GetStyle("GlassChipButton"),
                    Margin = new Thickness(14, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"Samouczek zakładki {tab} w trybie ćwiczeń — pokaże po kolei każdą jej sekcję."
                };
                AutomationProperties.SetName(button, $"Samouczek zakładki {tab}");
                button.Click += (_, _) => ShowTutorialFromProgram(tab);

                if (parent is StackPanel { Orientation: Orientation.Horizontal })
                {
                    parent.Children.Add(button);
                    continue;
                }

                // Tytuł stoi sam — zamieniamy go na wiersz „tytuł + przycisk” w tym samym miejscu.
                int index = parent.Children.IndexOf(title);
                parent.Children.RemoveAt(index);
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                Grid.SetRow(row, Grid.GetRow(title));
                Grid.SetColumn(row, Grid.GetColumn(title));
                DockPanel.SetDock(row, DockPanel.GetDock(title));
                row.Children.Add(title);
                row.Children.Add(button);
                parent.Children.Insert(index, row);
            }
        }

        private static T? FindFirst<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is T typed && match(typed))
                    return typed;
                if (child is DependencyObject node && FindFirst(node, match) is T found)
                    return found;
            }
            return null;
        }

        private void ShowAfterTutorial()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        // Samouczek zastępuje też starsze okno „pierwsze uruchomienie / zmiana wersji”.
        private void MarkTutorialSeen()
        {
            try
            {
                _settings.TutorialSeen = true;
                _settings.LastAcknowledgedVersion = _currentAppVersion;
                _settingsService.Save(_settings);
            }
            catch (Exception ex)
            {
                UpdateStatusBar("Nie udało się zapisać stanu samouczka: " + ex.Message, "Red");
            }
        }
    }
}
