using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MinecraftHelper;

internal sealed class PvpStartupNoticeWindow : Window
{
    private readonly CheckBox _confirmationCheckBox;
    private readonly Button _okButton;
    private readonly TextBlock _validationText;
    private bool _confirmed;

    public PvpStartupNoticeWindow(bool isFirstRun, string currentVersion, string previousVersion, bool requireConfirmation = true)
    {
        string safeCurrentVersion = string.IsNullOrWhiteSpace(currentVersion) ? "nieznana" : currentVersion.Trim();
        string safePreviousVersion = previousVersion?.Trim() ?? string.Empty;

        Title = isFirstRun
            ? "Pierwsze uruchomienie — Minecraft Helper Auto Clicker"
            : $"Informacje o wersji {safeCurrentVersion} — Auto Clicker";
        Width = 650;
        Height = 680;
        MinWidth = 560;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        GlassChrome.Apply(this);

        var root = new Border { Padding = new Thickness(20, 18, 20, 18) };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = isFirstRun
                ? "Witaj w Minecraft Helper Auto Clicker"
                : $"Co nowego w Auto Clickerze {safeCurrentVersion}",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "GlassAccentInkBrush");
        layout.Children.Add(title);

        var introduction = new TextBlock
        {
            Text = BuildIntroduction(isFirstRun, safePreviousVersion, safeCurrentVersion),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GlassPalette.Muted,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(introduction, 1);
        layout.Children.Add(introduction);

        var contentPanel = new StackPanel();
        if (isFirstRun)
        {
            contentPanel.Children.Add(BuildCard(
                "Najpierw przygotuj Auto Clicker",
                new[]
                {
                    "Uruchom Minecrafta, wejdź do gry, wybierz jego proces u góry okna i kliknij Zapisz.",
                    "Włącz tylko potrzebne moduły, przypisz bindy i ustaw bezpieczny zakres CPS.",
                    "Najedź na przyciski ?, aby przeczytać dokładne działanie każdej opcji.",
                    "Clickery i automatyzacje działają wyłącznie w zapisanym, aktywnym oknie Minecrafta."
                }));
        }

        foreach (AppReleaseNotes release in ReleaseNotesCatalog.GetNotesForStartup(
                     safePreviousVersion,
                     safeCurrentVersion,
                     isFirstRun))
        {
            contentPanel.Children.Add(BuildCard(
                $"{release.Version} — {release.Title}",
                release.Changes));
        }

        var scrollViewer = new ScrollViewer
        {
            Content = contentPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Padding = new Thickness(0, 0, 4, 0)
        };
        Grid.SetRow(scrollViewer, 2);
        layout.Children.Add(scrollViewer);

        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var confirmationBorder = new Border
        {
            Style = GlassChrome.GetStyle("GlassInset"),
            Background = Brushes.White
        };
        _confirmationCheckBox = new CheckBox
        {
            Content = "Przeczytałem informacje i wiem, że szczegółowe opisy znajdują się pod przyciskami ?.",
            Cursor = Cursors.Hand
        };
        _confirmationCheckBox.Checked += ConfirmationChanged;
        _confirmationCheckBox.Unchecked += ConfirmationChanged;
        confirmationBorder.Child = _confirmationCheckBox;
        footer.Children.Add(confirmationBorder);

        _validationText = new TextBlock
        {
            Text = "Najpierw zaznacz potwierdzenie powyżej.",
            Foreground = GlassPalette.Warn,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 7, 0, 0),
            Visibility = Visibility.Collapsed
        };
        footer.Children.Add(_validationText);

        _okButton = new Button
        {
            Content = "OK — przejdź do programu",
            Style = GlassChrome.GetStyle("GlassPrimaryButton"),
            Height = 36,
            Padding = new Thickness(18, 0, 18, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            IsEnabled = false,
            IsDefault = true
        };
        _okButton.Click += OkButton_Click;
        footer.Children.Add(_okButton);
        if (!requireConfirmation)
        {
            // Podgląd „Co nowego” z Ustawień: bez obowiązkowego potwierdzenia.
            _confirmed = true;
            confirmationBorder.Visibility = Visibility.Collapsed;
            _okButton.Content = "Zamknij";
            _okButton.IsEnabled = true;
            _okButton.IsCancel = true;
            _okButton.Click -= OkButton_Click;
            _okButton.Click += (_, _) => DialogResult = true;
        }
        Grid.SetRow(footer, 3);
        layout.Children.Add(footer);

        root.Child = layout;
        Content = root;

        Closing += Window_Closing;
        PreviewKeyDown += Window_PreviewKeyDown;
        if (requireConfirmation)
            Loaded += (_, _) => _confirmationCheckBox.Focus();
    }

    private static string BuildIntroduction(bool isFirstRun, string previousVersion, string currentVersion)
    {
        if (isFirstRun)
            return "Nie znaleziono zapisanych ustawień Auto Clickera. Poniżej znajdziesz krótki start oraz najważniejsze zmiany tej wersji.";

        return string.IsNullOrWhiteSpace(previousVersion)
            ? $"Auto Clicker działa teraz w wersji {currentVersion}. Przeczytaj najważniejsze informacje dotyczące tego wydania."
            : $"Auto Clicker został zmieniony z wersji {previousVersion} na {currentVersion}. Twoje ustawienia i bindy pozostają zachowane.";
    }

    private static Border BuildCard(string title, System.Collections.Generic.IEnumerable<string> entries)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 7)
        });

        foreach (string entry in entries)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "• " + entry,
                TextWrapping = TextWrapping.Wrap,
                Foreground = GlassPalette.Muted,
                Margin = new Thickness(0, 2, 0, 3)
            });
        }

        return new Border
        {
            Child = panel,
            Style = GlassChrome.GetStyle("GlassCard")
        };
    }

    private void ConfirmationChanged(object sender, RoutedEventArgs e)
    {
        bool accepted = _confirmationCheckBox.IsChecked == true;
        _okButton.IsEnabled = accepted;
        if (accepted)
            _validationText.Visibility = Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_confirmationCheckBox.IsChecked != true)
        {
            _validationText.Visibility = Visibility.Visible;
            return;
        }

        _confirmed = true;
        DialogResult = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_confirmed)
            return;

        e.Cancel = true;
        _validationText.Visibility = Visibility.Visible;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_confirmed)
        {
            _validationText.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }
}
