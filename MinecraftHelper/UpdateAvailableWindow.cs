using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using MinecraftHelper.Services;

namespace MinecraftHelper
{
    internal sealed class UpdateAvailableWindow : Window
    {
        private readonly Button _okButton;
        private readonly TextBlock _linkErrorText;

        public UpdateAvailableWindow(AppUpdateInfo update)
        {
            ArgumentNullException.ThrowIfNull(update);

            Title = "Aktualizacja Minecraft Helper";
            Width = 570;
            MinWidth = 500;
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
                Margin = new Thickness(0, 0, 0, 14)
            };
            header.Children.Add(new Border
            {
                Style = GlassChrome.GetStyle("GlassIconTile"),
                Child = new TextBlock
                {
                    Text = "↑",
                    FontWeight = FontWeights.Bold,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            header.Children.Add(new TextBlock
            {
                Text = "Dostępna nowa wersja",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 18,
                FontWeight = FontWeights.Bold
            });
            panel.Children.Add(header);

            panel.Children.Add(new TextBlock
            {
                Text = "Dostępna jest nowa wersja Minecraft Helper.",
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });

            panel.Children.Add(BuildVersionLine("Obecna wersja:", update.CurrentVersion));
            panel.Children.Add(BuildVersionLine("Najnowsza wersja:", update.LatestVersion));

            if (!string.IsNullOrWhiteSpace(update.ReleaseName))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = update.ReleaseName,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = GlassPalette.Muted
                });
            }

            var downloadLink = new Hyperlink
            {
                NavigateUri = update.DownloadUri,
                FontWeight = FontWeights.Bold
            };
            downloadLink.SetResourceReference(TextElement.ForegroundProperty, "GlassAccentInkBrush");
            downloadLink.Inlines.Add(update.IsDirectInstaller
                ? "Pobierz najnowszy instalator"
                : "Otwórz stronę najnowszego wydania");
            downloadLink.RequestNavigate += DownloadLink_RequestNavigate;

            var linkText = new TextBlock
            {
                Margin = new Thickness(0, 16, 0, 0),
                FontSize = 14
            };
            linkText.Inlines.Add(downloadLink);
            panel.Children.Add(linkText);

            panel.Children.Add(new TextBlock
            {
                Text = update.IsDirectInstaller
                    ? "Link prowadzi do instalatora dołączonego do oficjalnego wydania na GitHubie."
                    : "Instalator nie został znaleziony w wydaniu. Link prowadzi do oficjalnej strony wydania na GitHubie.",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = GlassPalette.Muted
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Program nie pobiera ani nie uruchamia instalatora automatycznie.",
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = GlassPalette.Muted
            });

            _linkErrorText = new TextBlock
            {
                Text = "Nie udało się otworzyć linku. Otwórz ręcznie stronę GitHub projektu.",
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = GlassPalette.Bad,
                Visibility = Visibility.Collapsed
            };
            panel.Children.Add(_linkErrorText);

            _okButton = new Button
            {
                Content = "OK — przypomnij później",
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
                Style = GlassChrome.GetStyle("GlassPrimaryButton")
            };
            _okButton.Click += (_, __) => CloseDialog();
            panel.Children.Add(_okButton);

            root.Child = panel;
            Content = root;

            PreviewKeyDown += UpdateAvailableWindow_PreviewKeyDown;
            Loaded += (_, __) => _okButton.Focus();
        }

        private static TextBlock BuildVersionLine(string label, string value)
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

        private void DownloadLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                _linkErrorText.Visibility = Visibility.Visible;
            }
        }

        private void UpdateAvailableWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                CloseDialog();
            }
        }

        private void CloseDialog()
        {
            DialogResult = true;
            Close();
        }
    }
}
