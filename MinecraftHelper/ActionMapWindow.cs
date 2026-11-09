using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace MinecraftHelper;

// Osobne okno mapy działania. Nie jest modalne: można je mieć obok gry i Kopacza.
internal sealed class ActionMapWindow : Window
{
    private readonly TextBlock _now = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public ActionMapPanel Map { get; } = new();

    public ActionMapWindow(Window owner, string accountName)
    {
        Owner = owner;
        Title = $"Mapa działania — {accountName}";
        Width = Math.Min(1000, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640;
        MinHeight = 420;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Mapa działania", Style = GlassChrome.GetStyle("GlassH1") });
        titles.Children.Add(new TextBlock { Text = "Co robi Kopacz przy obecnych ustawieniach tego konta. Kliknij kafelek, żeby zobaczyć, gdzie to ustawić.", Style = GlassChrome.GetStyle("GlassLead") });

        _now.SetResourceReference(TextBlock.ForegroundProperty, "GlassAccentInkBrush");
        var pill = new Border { Style = GlassChrome.GetStyle("GlassPill"), Height = 30, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(15), Child = _now };
        pill.SetResourceReference(Border.BackgroundProperty, "GlassAccentSoftBrush");
        Grid.SetColumn(pill, 1);

        var header = new Grid { Margin = new Thickness(4, 2, 4, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titles);
        header.Children.Add(pill);

        var card = new Border { Style = GlassChrome.GetStyle("GlassCard"), CornerRadius = new CornerRadius(24), Padding = new Thickness(16), Child = Map };
        var page = new StackPanel();
        page.Children.Add(header);
        page.Children.Add(card);

        var scroll = new ScrollViewer
        {
            Content = page,
            Padding = new Thickness(16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        var root = new Grid();
        root.Children.Add(new GlassBlobBackground { IsAnimationEnabled = false });
        root.Children.Add(scroll);
        Content = root;
    }

    public void Update(IReadOnlyList<ActionMapTile> tiles, string? current, string now)
    {
        Map.Update(tiles, current);
        _now.Text = now;
    }
}
