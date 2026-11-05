using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinecraftHelper;

internal enum GlassMessageKind
{
    Info,
    Warning,
    Error
}

// Komunikat i potwierdzenie w stylu glass zamiast systemowego MessageBox (szarego okna Windows).
// Gdy zasoby motywu nie są dostępne (bardzo wczesny błąd startu), pokazuje zwykły MessageBox.
internal sealed class GlassMessageDialog : Window
{
    private GlassMessageDialog(string title, string message, GlassMessageKind kind, string confirmText, string? cancelText, bool danger)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        (Brush accent, Brush soft, string glyph) = kind switch
        {
            GlassMessageKind.Error => (GlassPalette.Bad, GlassPalette.BadSoft, "!"),
            GlassMessageKind.Warning => (GlassPalette.Warn, GlassPalette.WarnSoft, "!"),
            _ => (GlassPalette.AccentInk, GlassPalette.AccentSoft, "i")
        };
        if (danger)
            (accent, soft) = (GlassPalette.Bad, GlassPalette.BadSoft);

        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = soft,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = glyph, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
        text.Children.Add(new TextBlock { Text = message, FontSize = 13, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Foreground = GlassPalette.Muted });

        var header = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (cancelText != null)
        {
            var cancel = new Button { Content = cancelText, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            cancel.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(cancel);
        }
        var confirm = new Button
        {
            Content = confirmText,
            MinWidth = 96,
            IsDefault = true,
            IsCancel = cancelText == null,
            Style = GlassChrome.GetStyle(danger ? "GlassDangerButton" : "GlassPrimaryButton")
        };
        confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(confirm);

        var card = new Border
        {
            Style = GlassChrome.GetStyle("GlassCard"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(22, 20, 22, 18),
            Margin = new Thickness(14),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Children = { header, buttons } }
        };
        var root = new Grid();
        root.Children.Add(new GlassBlobBackground { IsAnimationEnabled = false });
        root.Children.Add(card);
        Content = root;
    }

    public static void Show(Window? owner, string title, string message, GlassMessageKind kind = GlassMessageKind.Info)
    {
        try
        {
            var dialog = new GlassMessageDialog(title, message, kind, "OK", null, danger: false);
            Present(dialog, owner);
        }
        catch (Exception ex) when (ex is ResourceReferenceKeyNotFoundException or InvalidOperationException or NullReferenceException)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, kind switch
            {
                GlassMessageKind.Error => MessageBoxImage.Error,
                GlassMessageKind.Warning => MessageBoxImage.Warning,
                _ => MessageBoxImage.Information
            });
        }
    }

    /// <summary>Potwierdzenie z przyciskiem akcji (danger: czerwony, np. usunięcie). true = potwierdzono.</summary>
    public static bool Confirm(Window? owner, string title, string message, string confirmText, bool danger = true)
    {
        try
        {
            var dialog = new GlassMessageDialog(title, message, GlassMessageKind.Warning, confirmText, "Anuluj", danger);
            return Present(dialog, owner);
        }
        catch (Exception ex) when (ex is ResourceReferenceKeyNotFoundException or InvalidOperationException or NullReferenceException)
        {
            return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
    }

    private static bool Present(GlassMessageDialog dialog, Window? owner)
    {
        if (owner != null && owner.IsVisible)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }
}
