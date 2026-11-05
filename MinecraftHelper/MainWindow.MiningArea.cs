using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace MinecraftHelper;

// Edytor obszaru kopania Kopacza 6/3/3 w trybie Do góry: podgląd z góry i pola −/+.
// Wartości nadal trzymają TxtKopacz633LengthUp/WidthUp, więc zapis ustawień i ruch się nie zmieniają.
public partial class MainWindow
{
    private const int Kopacz633AreaMaxBlocks = 99;

    private void InitializeKopacz633AreaEditor()
    {
        // Długość to przód/tył (W/S), szerokość lewo/prawo (A/D).
        SetKopacz633AxisLabel(TxtKopacz633LengthUpLabel, "Długość", "(P/T)");
        SetKopacz633AxisLabel(TxtKopacz633WidthUpLabel, "Szerokość", "(L/P)");
        foreach (TextBox box in new[] { TxtKopacz633LengthUp, TxtKopacz633WidthUp })
        {
            box.TextChanged += (_, _) => UpdateKopacz633AreaPreview();
            // Kółko myszy zmienia wartość tylko w aktywnym polu — inaczej przewijałoby stronę.
            box.PreviewMouseWheel += (_, e) =>
            {
                if (!box.IsKeyboardFocusWithin)
                    return;
                StepKopacz633Area(box, e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
            box.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Up or Key.Down)
                {
                    StepKopacz633Area(box, e.Key == Key.Up ? 1 : -1);
                    e.Handled = true;
                }
            };
        }
        UpdateKopacz633AreaPreview();
    }

    private void Kopacz633AreaStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || tag.Split(':') is not [string axis, string deltaText]
            || !int.TryParse(deltaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delta))
            return;
        StepKopacz633Area(axis == "Length" ? TxtKopacz633LengthUp : TxtKopacz633WidthUp, delta);
    }

    private static void StepKopacz633Area(TextBox box, int delta)
    {
        int value = Math.Clamp(Math.Max(1, ParseNonNegativeInt(box.Text)) + delta, 1, Kopacz633AreaMaxBlocks);
        box.Text = value.ToString(CultureInfo.InvariantCulture);
        box.CaretIndex = box.Text.Length;
    }

    private void UpdateKopacz633AreaPreview()
    {
        if (Kopacz633AreaPreview == null)
            return;

        // Podgląd z góry: poziomo szerokość (A/D), pionowo długość (W/S).
        int across = GetConfiguredKopacz633UpwardWidth();
        int depth = GetConfiguredKopacz633UpwardLength();
        Kopacz633AreaPreview.SetArea(across, depth);
        TxtKopacz633AreaSize.Text = $"{across} × {depth}";

        double lapSeconds = 2.0 * (across + depth) * GetKopacz633MsPerBlock() / 1000.0;
        TxtKopacz633AreaRoute.Text = $"Start na zielonym bloku • ruch W → D → S → A • jedno okrążenie ≈ {lapSeconds.ToString("0.#", CultureInfo.CurrentCulture)} s";
    }

    private static void SetKopacz633AxisLabel(TextBlock label, string name, string axis)
    {
        label.Inlines.Clear();
        label.Inlines.Add(new Run(name + " "));
        label.Inlines.Add(new Run(axis) { Foreground = GlassPalette.Muted });
    }
}
