using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace MinecraftHelper
{
    internal sealed class BindConflictDialogWindow : Window
    {
        private readonly Button _okButton;

        public BindConflictDialogWindow(string requestedOwnerLabel, string keyText, string conflictOwnerLabel)
            : this(requestedOwnerLabel, keyText, conflictOwnerLabel, sharedAutoClickerModeConflict: false)
        {
        }

        private BindConflictDialogWindow(
            string requestedOwnerLabel,
            string keyText,
            string conflictOwnerLabel,
            bool sharedAutoClickerModeConflict)
        {
            string requestedOwner = string.IsNullOrWhiteSpace(requestedOwnerLabel) ? "funkcji" : requestedOwnerLabel.Trim();
            string key = string.IsNullOrWhiteSpace(keyText) ? "?" : keyText.Trim();
            string conflictOwner = string.IsNullOrWhiteSpace(conflictOwnerLabel) ? "innej funkcji" : conflictOwnerLabel.Trim();

            Title = sharedAutoClickerModeConflict ? "Konflikt wspólnego bindu" : "Klawisz zajety";
            Width = 520;
            MinWidth = 460;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            GlassChrome.Apply(this);

            var root = new Border
            {
                Style = GlassChrome.GetStyle("GlassCard"),
                Margin = new Thickness(16)
            };

            var panel = new StackPanel();

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            header.Children.Add(new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(10),
                Background = GlassPalette.WarnSoft,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "!",
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Foreground = GlassPalette.Warn,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            header.Children.Add(new TextBlock
            {
                Text = sharedAutoClickerModeConflict
                    ? "Najpierw rozdziel wspólny bind"
                    : "Klawisz jest juz zajety",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 15,
                FontWeight = FontWeights.Bold
            });
            panel.Children.Add(header);

            panel.Children.Add(new TextBlock
            {
                Text = sharedAutoClickerModeConflict
                    ? "Nie można zmienić tego trybu, ponieważ AUTO LPM i AUTO PPM korzystają z tego samego klawisza. Wspólny bind wymaga trybu Trzymanie albo Kombinacja dla LPM oraz trybu Kombinacja dla PPM."
                    : $"Nie mozna zapisac bindu dla: {requestedOwner}.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 13
            });

            panel.Children.Add(BuildValueLine(sharedAutoClickerModeConflict ? "Wspólny klawisz:" : "Klawisz:", key));
            panel.Children.Add(BuildValueLine(sharedAutoClickerModeConflict ? "Używany przez:" : "Zajety przez:", conflictOwner));

            panel.Children.Add(new TextBlock
            {
                Text = sharedAutoClickerModeConflict
                    ? "Aby wybrać Przełączanie, Trzymanie PPM albo tryb Aktywacja bindem + fizyczny przycisk, najpierw kliknij „Usuń” w AUTO LPM albo AUTO PPM lub przypisz jednemu z nich inny klawisz. Potem ponownie wybierz żądany tryb."
                    : "Wybierz inny klawisz i kliknij \"Zapisz\" ponownie.",
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = GlassPalette.Muted
            });

            _okButton = new Button
            {
                Content = "OK",
                MinWidth = 98,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0),
                Style = GlassChrome.GetStyle("GlassPrimaryButton")
            };
            _okButton.Click += (_, __) =>
            {
                DialogResult = true;
                Close();
            };
            panel.Children.Add(_okButton);

            root.Child = panel;
            Content = root;

            PreviewKeyDown += BindConflictDialogWindow_PreviewKeyDown;
            Loaded += (_, __) => _okButton.Focus();
        }

        public static BindConflictDialogWindow CreateSharedAutoClickerModeConflict(string keyText)
        {
            return new BindConflictDialogWindow(
                "zmiany trybu",
                keyText,
                "AUTO LPM i AUTO PPM",
                sharedAutoClickerModeConflict: true);
        }

        private static UIElement BuildValueLine(string label, string value)
        {
            var line = new TextBlock
            {
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            line.Inlines.Add(new Run(label + " ")
            {
                Foreground = GlassPalette.Muted,
                FontWeight = FontWeights.SemiBold
            });
            var valueRun = new Run(value) { FontWeight = FontWeights.Bold };
            valueRun.SetResourceReference(TextElement.ForegroundProperty, "GlassAccentInkBrush");
            line.Inlines.Add(valueRun);

            return line;
        }

        private void BindConflictDialogWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }
    }
}
