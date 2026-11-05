using System;
using System.Windows;
using System.Windows.Controls;

namespace MinecraftHelper;

// Osobne okno menu developera. Treść (PanelDeveloperMenu z MainWindow.xaml) jest tu tylko
// przenoszona — przyciski i pola zostają polami MainWindow, więc testy działają tym samym kodem.
// Okno nie jest modalne: można je mieć obok gry i Kopacza.
internal sealed class DeveloperMenuWindow : Window
{
    private readonly ContentControl _host = new();

    public DeveloperMenuWindow(Window owner, string accountName)
    {
        Owner = owner;
        Title = $"Menu developera — {accountName}";
        Width = Math.Min(1040, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640;
        MinHeight = 420;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Nazwane style kart (CardTitle, CardSub…) są zasobami okna Kopacza.
        Resources.MergedDictionaries.Add(owner.Resources);
        GlassChrome.Apply(this);

        var card = new Border
        {
            Style = GlassChrome.GetStyle("GlassCard"),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(22, 4, 22, 20),
            VerticalAlignment = VerticalAlignment.Top,
            Child = _host
        };
        var scroll = new ScrollViewer
        {
            Content = card,
            Padding = new Thickness(16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        var root = new Grid();
        root.Children.Add(new GlassBlobBackground());
        root.Children.Add(scroll);
        Content = root;
    }

    public void Host(FrameworkElement panel) => _host.Content = panel;

    // Oddaje panel przed zamknięciem, żeby mógł wrócić do karty w Ustawieniach.
    public FrameworkElement? Release()
    {
        var panel = _host.Content as FrameworkElement;
        _host.Content = null;
        return panel;
    }
}
