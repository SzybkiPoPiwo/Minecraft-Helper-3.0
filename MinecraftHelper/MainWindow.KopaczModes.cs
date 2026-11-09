using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MinecraftHelper.Models;

namespace MinecraftHelper;

// Zakładka Kopanie: dwa kafelki trybów i wspólny panel ustawień trybu wybranego kafelkiem.
public partial class MainWindow
{
    // Poniżej tej szerokości trzy kolumny 6/3/3 ściskałyby wiersze komend.
    private const double Kopacz633ThreeColumnsMinWidth = 860;

    private FrameworkElement? _kopaczModeShownPanel;

    private void InitializeKopaczModeTiles()
    {
        foreach (TextBox box in new[] { TxtKopacz533Key, TxtKopacz633Key, TxtKopacz633Width, TxtKopacz633LengthUp, TxtKopacz633WidthUp })
            box.TextChanged += (_, _) => UpdateKopaczModeTiles();
        CbKopacz633Direction.SelectionChanged += (_, _) => UpdateKopaczModeTiles();
    }

    // Przy obu włączonych START uruchamia 6/3/3, więc to jego ustawienia pokazujemy najpierw.
    private void SelectDefaultKopaczModeTile()
    {
        if (ChkKopacz533Enabled.IsChecked == true && ChkKopacz633Enabled.IsChecked != true)
            RbKopacz533Tile.IsChecked = true;
        else
            RbKopacz633Tile.IsChecked = true;
    }

    private void KopaczModeTile_Checked(object sender, RoutedEventArgs e) => UpdateKopaczModePanel();

    private void UpdateKopaczModePanel()
    {
        bool show633 = RbKopacz633Tile.IsChecked == true;
        bool enabled = (show633 ? ChkKopacz633Enabled : ChkKopacz533Enabled).IsChecked == true;
        TxtKopaczModeOff.Text = $"Kopacz {(show633 ? "6/3/3" : "5/3/3")} jest wyłączony. Włącz go przełącznikiem w kafelku.";
        FrameworkElement next = !enabled ? TxtKopaczModeOff : show633 ? PanelKopacz633Content : PanelKopacz533Content;
        if (ReferenceEquals(next, _kopaczModeShownPanel))
            return;

        FrameworkElement? previous = _kopaczModeShownPanel;
        _kopaczModeShownPanel = next;
        GlassMotion.Swap(GridKopaczModeBody, previous, next, animate: !_isLoadingUi && IsLoaded);
    }

    private void UpdateKopaczModeTiles()
    {
        UpdateKopaczModeTile(ChkKopacz533Enabled, TxtKopacz533Key, _settings.Kopacz533Commands, TxtKopacz533TileBind, TxtKopacz533TileCommands, ChipKopacz533Commands);
        UpdateKopaczModeTile(ChkKopacz633Enabled, TxtKopacz633Key, _settings.Kopacz633Commands, TxtKopacz633TileBind, TxtKopacz633TileCommands, ChipKopacz633Commands);

        int direction = CbKopacz633Direction.SelectedIndex;
        bool enabled633 = ChkKopacz633Enabled.IsChecked == true;
        ChipKopacz633Area.Visibility = enabled633 ? Visibility.Visible : Visibility.Collapsed;
        TxtKopacz633TileArea.Text = direction switch
        {
            1 => "na wprost " + GetConfiguredKopacz633ForwardWidth().ToString(CultureInfo.InvariantCulture),
            2 => $"do góry {GetConfiguredKopacz633UpwardWidth()} × {GetConfiguredKopacz633UpwardLength()}",
            _ => "wybierz kierunek"
        };
        TxtKopacz633AreaSize.Visibility = direction == 2 ? Visibility.Visible : Visibility.Hidden;
        TxtKopacz633NoDirection.Visibility = direction is 1 or 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void UpdateKopaczModeTile(CheckBox enabled, TextBox bind, List<MinerCommand>? commands, TextBlock bindText, TextBlock commandsText, Border commandsChip)
    {
        bool on = enabled.IsChecked == true;
        string key = bind.Text.Trim();
        bindText.Text = !on ? "wyłączony" : key.Length > 0 ? "Bind " + key : "Bind: brak";
        commandsText.Text = "Komendy: " + (commands?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
        commandsChip.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PanelKopacz633Content_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < Kopacz633ThreeColumnsMinWidth;
        if (compact == (Grid.GetRow(BorderKopacz633Commands) == 1))
            return;

        Grid.SetRow(BorderKopacz633Commands, compact ? 1 : 0);
        Grid.SetColumn(BorderKopacz633Commands, compact ? 0 : 2);
        Grid.SetColumnSpan(BorderKopacz633Commands, compact ? 2 : 1);
        ColumnKopacz633Commands.Width = compact ? new GridLength(0) : new GridLength(1.25, GridUnitType.Star);
        BorderKopacz633Commands.BorderThickness = compact ? new Thickness(0) : new Thickness(1, 0, 0, 0);
        BorderKopacz633Commands.Margin = compact ? new Thickness(0, 20, 0, 0) : new Thickness(18, 0, 0, 0);
        BorderKopacz633Commands.Padding = compact ? new Thickness(0) : new Thickness(18, 0, 0, 0);
    }
}
