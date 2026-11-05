using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Włączanie i wyłączanie komend bota Discord. Wyłączona komenda nie działa — bot odpowiada,
// że jest wyłączona, i nie pokazuje jej w !pomoc. Zapis trafia do discord.json (wspólny dla kont).
internal sealed class DiscordCommandsWindow : Window
{
    private readonly Dictionary<string, CheckBox> _toggles = new();

    public DiscordCommandsWindow(DiscordSettings settings)
    {
        Title = "Komendy bota";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        string prefix = settings.Prefix;
        var list = new StackPanel();
        list.Children.Add(new TextBlock
        {
            Text = "Komendy bota",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        list.Children.Add(new TextBlock
        {
            // U+2060 skleja prefiks z nazwą — WPF łamał wiersz między „!” a „pomoc”.
            Text = $"Wyłączona komenda nie zadziała — bot odpowie, że jest wyłączona, i nie pokaże jej w {prefix}⁠pomoc. Komenda {prefix}⁠pomoc działa zawsze. Ustawienie jest wspólne dla wszystkich kont.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 19,
            Foreground = GlassPalette.Muted,
            Margin = new Thickness(0, 0, 0, 14)
        });

        foreach (IGrouping<DiscordCommandGroup, DiscordCommandInfo> group in DiscordCommands.All.GroupBy(command => command.Group))
        {
            list.Children.Add(new TextBlock
            {
                Text = DiscordCommands.GroupTitle(group.Key),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13.5,
                Margin = new Thickness(0, 6, 0, 6)
            });
            foreach (DiscordCommandInfo command in group)
                list.Children.Add(BuildRow(command, prefix, settings.IsCommandEnabled(command.Id)));
        }

        var all = new Button { Content = "Włącz wszystkie", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        all.Click += (_, _) =>
        {
            foreach (CheckBox toggle in _toggles.Values)
                toggle.IsChecked = true;
        };
        var cancel = new Button { Content = "Anuluj", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Zapisz", MinWidth = 96, IsDefault = true, Style = GlassChrome.GetStyle("GlassPrimaryButton") };
        save.Click += (_, _) => DialogResult = true;

        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        DockPanel.SetDock(all, Dock.Left);
        buttons.Children.Add(all);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(cancel);
        right.Children.Add(save);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 560,
            Focusable = false
        };
        var body = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);
        body.Children.Add(scroll);

        var card = new Border
        {
            Style = GlassChrome.GetStyle("GlassCard"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(22, 20, 22, 18),
            Margin = new Thickness(14),
            Child = body
        };
        var root = new Grid();
        root.Children.Add(new GlassBlobBackground { IsAnimationEnabled = false });
        root.Children.Add(card);
        Content = root;
    }

    // ID komend, które użytkownik odznaczył.
    public List<string> DisabledCommandIds => _toggles
        .Where(pair => pair.Value.IsChecked != true)
        .Select(pair => pair.Key)
        .ToList();

    private UIElement BuildRow(DiscordCommandInfo command, string prefix, bool enabled)
    {
        var toggle = new CheckBox
        {
            IsChecked = enabled,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0),
            ToolTip = $"Włącz lub wyłącz {prefix}{command.Id}"
        };
        _toggles[command.Id] = toggle;

        var usage = new TextBlock { Text = prefix + command.Usage, FontSize = 13, FontWeight = FontWeights.SemiBold };
        usage.SetResourceReference(TextBlock.FontFamilyProperty, "GlassMonoFont");
        if (command.Kind != DiscordCommandKind.Normal)
            usage.Foreground = command.Kind == DiscordCommandKind.Start ? GlassPalette.Ok : GlassPalette.Bad;
        var text = new StackPanel();
        text.Children.Add(usage);
        string aliases = command.Aliases.Count == 0 ? string.Empty : $" (też: {string.Join(", ", command.Aliases.Select(alias => prefix + alias))})";
        text.Children.Add(new TextBlock
        {
            Text = command.Description + aliases,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = GlassPalette.Muted,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(toggle, Dock.Left);
        row.Children.Add(toggle);
        row.Children.Add(text);
        // Klik w opis przełącza komendę — większy cel niż sam przełącznik.
        text.Background = Brushes.Transparent;
        text.MouseLeftButtonUp += (_, _) => toggle.IsChecked = toggle.IsChecked != true;
        return row;
    }
}
