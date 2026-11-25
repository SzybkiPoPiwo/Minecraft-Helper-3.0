using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Elementy wyglądu glass: hotbar slotów, pigułki stanu i podsumowania clickerów.
public partial class MainWindow
{
    private sealed record PvpTool(string Id, string Label, Func<ComboBox> Slot);

    // Proste pikselowe ikony 16×16 (trzy warstwy) — te same co w projekcie canvy.
    private static readonly Dictionary<string, (string Data, string Color)[]> PixelIcons = new()
    {
        ["sword"] = new[] { ("M9 2h5v5l-7 7-5-5z", "#5FE3DA"), ("M2 11l3 3-2 2-3-3z", "#7A5A2F"), ("M4 9l3 3-1 1-3-3z", "#3E2A16") },
        ["block"] = new[] { ("M2 2h12v12H2z", "#8D8D8D"), ("M2 2h12v2H4v10H2z", "#B4B4B4"), ("M14 14H4v-2h8V4h2z", "#5F5F5F") },
        ["snow"] = new[] { ("M8 2a6 6 0 1 1 0 12A6 6 0 0 1 8 2z", "#F4F7FA"), ("M13 8a5 5 0 0 1-8 4 6 6 0 0 0 8-6z", "#B9C6D2"), ("M5 5h2v2H5z", "#FFFFFF") },
        ["pearl"] = new[] { ("M8 2a6 6 0 1 1 0 12A6 6 0 0 1 8 2z", "#12705F"), ("M8 5a3 3 0 1 1 0 6 3 3 0 0 1 0-6z", "#3FD1B0"), ("M5 4h2v1H5z", "#B6F7E6") },
        ["rod"] = new[] { ("M2 14 13 3l1 1L3 15z", "#8A6234"), ("M13 4h1v8h-1z", "#D8D8D8"), ("M12 12h3v2h-3z", "#9AA3AD") }
    };

    private readonly List<PvpTool> _pvpTools = new();
    private readonly List<Button> _pvpHotbarButtons = new();

    private void InitializeGlassUi()
    {
        TxtAppVersion.Text = $"Minecraft Helper {_currentAppVersion}";
        TxtVersionPill.Text = $"wersja {_currentAppVersion}";
        PillVersion.Background = GlassPalette.NeutralSoft;
        TxtSettingsPath.Text = _settingsService.SettingsPath;

        _pvpTools.Add(new PvpTool("sword", "Miecz", () => CbPvpSwordSlot));
        _pvpTools.Add(new PvpTool("block", "Blok", () => CbPvpBlockSlot));
        _pvpTools.Add(new PvpTool("snow", "Śnieżka", () => CbPvpSnowballSlot));
        _pvpTools.Add(new PvpTool("pearl", "Perła", () => CbPvpPearlSlot));
        _pvpTools.Add(new PvpTool("rod", "Wędka", () => CbPvpRodSlot));

        // Pasek tylko pokazuje układ; sloty ustawia się w wierszach tabeli.
        for (int slot = 1; slot <= 9; slot++)
        {
            var button = new Button { Style = (Style)FindResource("HotbarSlotButton"), Focusable = false, IsHitTestVisible = false };
            _pvpHotbarButtons.Add(button);
            PanelPvpHotbar.Children.Add(button);
        }

        IconPvpSword.Child = CreatePixelIcon("sword");
        IconPvpBlock.Child = CreatePixelIcon("block");
        IconPvpSnowball.Child = CreatePixelIcon("snow");
        IconPvpPearl.Child = CreatePixelIcon("pearl");
        IconPvpRod.Child = CreatePixelIcon("rod");

        // Przy włączonym tylko AUTO PPM od razu widać jego ustawienia.
        bool startOnRight = ChkAutoLeftEnabled.IsChecked != true && ChkAutoRightEnabled.IsChecked == true;
        (startOnRight ? RbAutoRightTile : RbAutoLeftTile).IsChecked = true;
        UpdateClickerPanel();

        RefreshGlassHotbar();
        RefreshClickerSummaries();
        RefreshAppleRepairProgress();
    }

    // ===== Kafelki AUTO LPM / PPM =====

    private FrameworkElement? _clickerShownPanel;
    private bool _autoLeftMoreOpen;
    private FrameworkElement? _revealedBindActions;

    private void ClickerTile_Checked(object sender, RoutedEventArgs e) => UpdateClickerPanel();

    private void UpdateClickerPanel()
    {
        if (GridClickerPanelHost == null || RbAutoLeftTile == null || RbAutoRightTile == null)
            return;

        bool left = RbAutoLeftTile.IsChecked == true;
        bool enabled = (left ? ChkAutoLeftEnabled : ChkAutoRightEnabled).IsChecked == true;
        TxtClickerPanelOff.Text = $"{(left ? "AUTO LPM" : "AUTO PPM")} jest wyłączony. Włącz go przełącznikiem w kafelku.";
        FrameworkElement next = !enabled ? TxtClickerPanelOff : left ? PanelAutoLeftContent : PanelAutoRightContent;
        if (ReferenceEquals(next, _clickerShownPanel))
            return;

        FrameworkElement? previous = _clickerShownPanel;
        _clickerShownPanel = next;
        GlassMotion.Swap(GridClickerPanelHost, previous, next, animate: !_isLoadingUi && IsLoaded);
    }

    private void BtnAutoLeftMore_Click(object sender, RoutedEventArgs e)
    {
        _autoLeftMoreOpen = !_autoLeftMoreOpen;
        TxtAutoLeftMoreArrow.Text = _autoLeftMoreOpen ? "▾" : "▸";
        GlassMotion.SetExpanded(PanelAutoLeftMore, _autoLeftMoreOpen, animate: IsLoaded);
    }

    // Wartość CPS jest przyciskiem: pola do wpisywania pokazują się dopiero na żądanie.
    private void BtnCpsValue_Click(object sender, RoutedEventArgs e)
    {
        FrameworkElement boxes = sender is Button { Tag: "Right" } ? PanelAutoRightCpsBoxes : PanelAutoLeftCpsBoxes;
        GlassMotion.SetExpanded(boxes, boxes.Visibility != Visibility.Visible, animate: IsLoaded);
    }

    // Gdy wybrano już klawisz, Zapisz nie może zniknąć po zjechaniu myszą z pola.
    private void ShowBindActions(TextBox box)
    {
        HideBindActions();
        if (box.Parent is Panel parent && parent.Children.OfType<FrameworkElement>().FirstOrDefault(child => child is not TextBox) is { } actions)
        {
            actions.Tag = "open";
            _revealedBindActions = actions;
        }
    }

    private void HideBindActions()
    {
        if (_revealedBindActions != null)
            _revealedBindActions.Tag = null;
        _revealedBindActions = null;
    }

    private void ApplyClickerTileGlow(RadioButton tile, bool running)
    {
        if (!running)
        {
            if (tile.Effect != null)
                tile.Effect = null;
            return;
        }

        var color = (Color)FindResource("GlassAccentColor");
        if (tile.Effect is not System.Windows.Media.Effects.DropShadowEffect current || current.Color != color)
            tile.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.4 };
    }

    // Slot zajęty przez inny przedmiot: oba zamieniają się miejscami.
    private bool _syncingPvpSlots;

    private void PvpSlotCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoadingUi && !_syncingPvpSlots && sender is ComboBox box && box.SelectedIndex >= 0 && e.RemovedItems.Count == 1)
        {
            int previous = box.Items.IndexOf(e.RemovedItems[0]);
            _syncingPvpSlots = true;
            try
            {
                foreach (PvpTool tool in _pvpTools)
                {
                    if (!ReferenceEquals(tool.Slot(), box) && tool.Slot().SelectedIndex == box.SelectedIndex)
                        tool.Slot().SelectedIndex = previous;
                }
            }
            finally
            {
                _syncingPvpSlots = false;
            }
        }
        PvpQuickActionSetting_Changed(sender, e);
    }

    partial void OnAutoArmorCalibrationRefreshed(int configured, int total, bool calibrating)
    {
        if (TxtAutoArmorProgress == null)
            return;

        bool complete = configured >= total;
        TxtAutoArmorProgress.Text = complete ? $"{configured}/{total} gotowe" : $"{configured}/{total}";
        TxtAutoArmorProgress.Foreground = complete ? GlassPalette.Ok : GlassPalette.Muted;
        PillAutoArmorProgress.Background = complete ? GlassPalette.OkSoft : GlassPalette.NeutralSoft;
        PrgAutoArmorCalibration.Maximum = total;
        PrgAutoArmorCalibration.Value = configured;
        TxtAutoArmorHint.Text = calibrating
            ? "Teraz otwórz EQ w grze, najedź kursorem na slot tej części i naciśnij lewy Ctrl."
            : complete
                ? "Kalibracja gotowa. Zamknij EQ i użyj binda, aby zamienić sety."
                : "Kliknij część, otwórz EQ, najedź na jej slot i naciśnij lewy Ctrl. Ustaw 4 sloty założonego setu i 4 miejsca drugiego setu.";
    }

    private void BtnWindowChip_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = TabSettings;

    // Ręczne sprawdzenie: wynik trafia do dzwonka w nagłówku (nowa wersja jest tam komunikatem), a tu tylko krótka informacja.
    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_announcements == null)
            return;

        UpdateStatusBar("Sprawdzanie aktualizacji…", "Green");
        await _announcements.RefreshAsync();
        if (!_announcements.LastUpdateCheckSucceeded)
            UpdateStatusBar("Nie udało się sprawdzić aktualizacji. Sprawdź połączenie z internetem i spróbuj później.", "Red");
        else if (_announcements.AvailableVersion is string latest)
            UpdateStatusBar($"Dostępna wersja {latest} — szczegóły w dzwonku „Komunikaty” w nagłówku.", "Orange");
        else
            UpdateStatusBar($"Masz aktualną wersję {_currentAppVersion}.", "Green");
    }

    // Plakietka wersji w Ustawieniach: aktualna albo dostępna nowa. Odświeżana po każdym sprawdzeniu (start, co kilka godzin, „Sprawdź”).
    private void ApplyVersionPill(bool checkSucceeded, string? availableVersion)
    {
        if (!checkSucceeded)
            return;

        if (availableVersion == null)
        {
            TxtVersionPill.Text = $"{_currentAppVersion} aktualna";
            TxtVersionPill.Foreground = GlassPalette.Ok;
            PillVersion.Background = GlassPalette.OkSoft;
        }
        else
        {
            TxtVersionPill.Text = $"dostępna {availableVersion}";
            TxtVersionPill.Foreground = GlassPalette.Warn;
            PillVersion.Background = GlassPalette.WarnSoft;
        }
    }

    private void RefreshGlassHotbar()
    {
        if (_pvpHotbarButtons.Count == 0)
            return;

        var toolBySlot = new Dictionary<int, string>();
        foreach (PvpTool tool in _pvpTools)
        {
            int slot = GetPvpQuickActionSelectedSlot(tool.Slot(), 1);
            toolBySlot.TryAdd(slot, tool.Id);
        }

        for (int index = 0; index < _pvpHotbarButtons.Count; index++)
        {
            int slot = index + 1;
            Button button = _pvpHotbarButtons[index];
            bool hasTool = toolBySlot.TryGetValue(slot, out string? toolId);

            var cell = new Grid();
            cell.Children.Add(new TextBlock
            {
                Text = slot.ToString(CultureInfo.InvariantCulture),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(4, 1, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            });
            if (hasTool && toolId != null)
            {
                FrameworkElement icon = CreatePixelIcon(toolId);
                icon.Margin = new Thickness(9);
                cell.Children.Add(icon);
            }

            button.Content = cell;
            button.Background = new SolidColorBrush(Color.FromArgb(hasTool ? (byte)0x1F : (byte)0x12, 0xFF, 0xFF, 0xFF));
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        }

        int swordSlot = GetPvpQuickActionSelectedSlot(CbPvpSwordSlot, 1);
        TxtPvpBlockFlow.Text = $"slot {GetPvpQuickActionSelectedSlot(CbPvpBlockSlot, 5)} → PPM → miecz {swordSlot}";
        TxtPvpSnowballFlow.Text = $"slot {GetPvpQuickActionSelectedSlot(CbPvpSnowballSlot, 2)} → PPM → miecz {swordSlot}";
        TxtPvpPearlFlow.Text = $"slot {GetPvpQuickActionSelectedSlot(CbPvpPearlSlot, 3)} → PPM → miecz {swordSlot}";
        TxtPvpRodFlow.Text = $"slot {GetPvpQuickActionSelectedSlot(CbPvpRodSlot, 4)} → trzyma PPM → miecz {swordSlot}";

        TxtAppleStepShears.Text = $"slot {CbAppleCutterShearsSlot.SelectedIndex + 1}";
        TxtAppleStepLeaves.Text = $"slot {CbAppleCutterLeavesSlot.SelectedIndex + 1}";
    }

    private void RefreshClickerSummaries()
    {
        if (TxtAutoLeftSummary == null || TxtAutoRightSummary == null)
            return;

        UpdateClickerSummary(ChkAutoLeftEnabled, CbAutoLeftMode, TxtAutoLeftKey, TxtAutoLeftMinCps, TxtAutoLeftMaxCps, TxtAutoLeftSummary, TxtAutoLeftCpsValue, SliderAutoLeftCps, TxtAutoLeftScaleMid, TxtAutoLeftScaleMax, TxtAutoLeftModeHint);
        UpdateClickerSummary(ChkAutoRightEnabled, CbAutoRightMode, TxtAutoRightKey, TxtAutoRightMinCps, TxtAutoRightMaxCps, TxtAutoRightSummary, TxtAutoRightCpsValue, SliderAutoRightCps, TxtAutoRightScaleMid, TxtAutoRightScaleMax, TxtAutoRightModeHint);
    }

    private static void UpdateClickerSummary(CheckBox enabled, ComboBox mode, TextBox keyBox, TextBox minBox, TextBox maxBox, TextBlock summary, TextBlock cpsValue, GlassRangeSlider slider, TextBlock scaleMid, TextBlock scaleMax, TextBlock? modeHint)
    {
        if (summary == null || slider == null || minBox == null || maxBox == null || scaleMid == null || scaleMax == null || cpsValue == null || keyBox == null)
            return;

        int min = ParseCps(minBox.Text);
        int max = Math.Max(min, ParseCps(maxBox.Text));
        // Skala suwaka rośnie dziesiątkami, gdy zakres przekracza typowe 20 CPS.
        // Podczas przeciągania skala się nie zmienia, żeby uchwyt nie uciekał spod kursora.
        int top = slider.IsDragging
            ? (int)slider.Maximum
            : max <= 20 ? 20 : (int)Math.Ceiling(max / 10.0) * 10;
        string modeName = ((mode?.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "Hold" => "Trzymanie",
            "Combo" => "Bind + przycisk",
            "PhysicalToggle" => "Aktywacja",
            "DoubleClick" => "Podwójne kliknięcie",
            _ => "Przełączanie"
        };
        string key = string.IsNullOrWhiteSpace(keyBox.Text) ? "Brak bindu" : keyBox.Text.Trim();
        cpsValue.Text = $"{min}–{max} CPS";
        if (modeHint != null)
        {
            modeHint.Text = ((mode?.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Hold" => "Klika, dopóki trzymasz bind.",
                "Combo" => "Klika przy trzymaniu bindu i fizycznego przycisku myszy.",
                "PhysicalToggle" => "Bind uzbraja, klika tylko przy trzymaniu fizycznego przycisku myszy.",
                "DoubleClick" => "Bind uzbraja; każde Twoje kliknięcie dostaje drugie.",
                _ => "Bind włącza, ponowny bind wyłącza."
            };
        }
        bool doubleClick = ((mode?.SelectedItem as ComboBoxItem)?.Tag as string) == "DoubleClick";
        summary.Text = enabled?.IsChecked != true
            ? "Wyłączony"
            : doubleClick ? $"{key} · {modeName}" : $"{key} · {modeName} · {min}–{max} CPS";
        scaleMid.Text = (top / 2).ToString(CultureInfo.InvariantCulture);
        scaleMax.Text = top.ToString(CultureInfo.InvariantCulture);
        slider.Maximum = top;
        slider.LowerValue = Math.Min(min, top);
        slider.UpperValue = Math.Min(max, top);
    }

    private void RefreshAppleRepairProgress()
    {
        if (TxtAppleRepairProgress == null || PrgAppleRepair == null)
            return;

        int every = Math.Max(1, GetAppleCutterRepairEveryCycles());
        int done = _appleCutterCyclesCompleted % every;
        TxtAppleRepairProgress.Text = $"{done} / {every} cykli";
        PrgAppleRepair.Maximum = every;
        PrgAppleRepair.Value = done;
    }

    private void SliderAutoLeftCps_RangeChanged(object? sender, EventArgs e) =>
        ApplySliderToCps(SliderAutoLeftCps, TxtAutoLeftMinCps, TxtAutoLeftMaxCps);

    private void SliderAutoRightCps_RangeChanged(object? sender, EventArgs e) =>
        ApplySliderToCps(SliderAutoRightCps, TxtAutoRightMinCps, TxtAutoRightMaxCps);

    // Suwak wpisuje wartości do pól CPS; ich zdarzenia TextChanged zapisują ustawienia jak przy wpisywaniu.
    private static void ApplySliderToCps(GlassRangeSlider slider, TextBox minBox, TextBox maxBox)
    {
        string min = ((int)slider.LowerValue).ToString(CultureInfo.InvariantCulture);
        string max = ((int)slider.UpperValue).ToString(CultureInfo.InvariantCulture);
        if (minBox.Text != min)
            minBox.Text = min;
        if (maxBox.Text != max)
            maxBox.Text = max;
    }

    private static int ParseCps(string text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, 1, 100)
            : 1;

    private static void ApplyStatePill(Border pill, TextBlock label, string state)
    {
        Brush background;
        Brush foreground;
        switch (state)
        {
            case "ON":
                background = (Brush)pill.FindResource("GlassAccentBrush");
                foreground = Brushes.White;
                break;
            case "GOTOWY":
                background = GlassPalette.InfoSoft;
                foreground = GlassPalette.Info;
                break;
            case "OFF":
                background = GlassPalette.NeutralSoft;
                foreground = GlassPalette.Muted;
                break;
            default:
                background = GlassPalette.WarnSoft;
                foreground = GlassPalette.Warn;
                break;
        }

        if (!ReferenceEquals(pill.Background, background))
            pill.Background = background;
        if (!ReferenceEquals(label.Foreground, foreground))
            label.Foreground = foreground;
    }

    private static FrameworkElement CreatePixelIcon(string id)
    {
        var canvas = new Grid { Width = 16, Height = 16 };
        if (PixelIcons.TryGetValue(id, out (string Data, string Color)[]? layers))
        {
            foreach ((string data, string color) in layers)
            {
                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(data),
                    Fill = (Brush)new BrushConverter().ConvertFromString(color)!,
                    SnapsToDevicePixels = true
                });
            }
        }

        RenderOptions.SetEdgeMode(canvas, EdgeMode.Aliased);
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }
}
