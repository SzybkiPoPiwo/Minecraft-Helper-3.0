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
    private readonly Dictionary<string, TextBlock> _pvpToolSlotLabels = new();
    private readonly Dictionary<string, RadioButton> _pvpToolButtons = new();
    private readonly List<Button> _pvpHotbarButtons = new();
    private string _selectedPvpTool = "block";

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

        foreach (PvpTool tool in _pvpTools)
        {
            var slotLabel = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("GlassMonoFont"),
                FontSize = 10.5,
                Foreground = GlassPalette.Muted,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(7),
                Background = GlassPalette.NeutralSoft,
                Padding = new Thickness(3),
                Child = CreatePixelIcon(tool.Id),
                Margin = new Thickness(0, 0, 6, 0)
            });
            content.Children.Add(new TextBlock { Text = tool.Label, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(slotLabel);

            var button = new RadioButton
            {
                GroupName = "PvpTool",
                Style = (Style)FindResource("GlassChoiceRadio"),
                Content = content,
                Margin = new Thickness(0, 0, 6, 6),
                IsChecked = tool.Id == _selectedPvpTool
            };
            string toolId = tool.Id;
            button.Checked += (_, _) =>
            {
                _selectedPvpTool = toolId;
                RefreshGlassHotbar();
            };
            _pvpToolSlotLabels[tool.Id] = slotLabel;
            _pvpToolButtons[tool.Id] = button;
            PanelPvpTools.Children.Add(button);
        }

        for (int slot = 1; slot <= 9; slot++)
        {
            int targetSlot = slot;
            var button = new Button { Style = (Style)FindResource("HotbarSlotButton") };
            button.Click += (_, _) => AssignSelectedPvpTool(targetSlot);
            _pvpHotbarButtons.Add(button);
            PanelPvpHotbar.Children.Add(button);
        }

        IconPvpBlock.Child = CreatePixelIcon("block");
        IconPvpSnowball.Child = CreatePixelIcon("snow");
        IconPvpPearl.Child = CreatePixelIcon("pearl");
        IconPvpRod.Child = CreatePixelIcon("rod");

        RefreshGlassHotbar();
        RefreshClickerSummaries();
        RefreshAppleRepairProgress();
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

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusBar("Sprawdzanie aktualizacji…", "Green");
        AppUpdateInfo? update = await UpdateCheckService.CheckForUpdateAsync(_currentAppVersion);
        if (update == null)
        {
            TxtVersionPill.Text = $"{_currentAppVersion} aktualna";
            TxtVersionPill.Foreground = GlassPalette.Ok;
            PillVersion.Background = GlassPalette.OkSoft;
            UpdateStatusBar($"Masz aktualną wersję {_currentAppVersion}.", "Green");
            return;
        }

        TxtVersionPill.Text = $"dostępna {update.LatestVersion}";
        TxtVersionPill.Foreground = GlassPalette.Warn;
        PillVersion.Background = GlassPalette.WarnSoft;

        var dialog = new UpdateAvailableWindow(update) { Owner = this };
        dialog.ShowDialog();
    }

    private void AssignSelectedPvpTool(int slot)
    {
        PvpTool? selected = _pvpTools.Find(tool => tool.Id == _selectedPvpTool);
        if (selected == null)
            return;

        int previousSlot = GetPvpQuickActionSelectedSlot(selected.Slot(), 1);
        foreach (PvpTool tool in _pvpTools)
        {
            if (tool != selected && GetPvpQuickActionSelectedSlot(tool.Slot(), 1) == slot)
                tool.Slot().SelectedIndex = previousSlot - 1;
        }
        selected.Slot().SelectedIndex = slot - 1;
        RefreshGlassHotbar();
    }

    private void RefreshGlassHotbar()
    {
        if (_pvpHotbarButtons.Count == 0)
            return;

        var toolBySlot = new Dictionary<int, string>();
        foreach (PvpTool tool in _pvpTools)
        {
            int slot = GetPvpQuickActionSelectedSlot(tool.Slot(), 1);
            if (!toolBySlot.ContainsKey(slot) || tool.Id == _selectedPvpTool)
                toolBySlot[slot] = tool.Id;
            _pvpToolSlotLabels[tool.Id].Text = slot.ToString(CultureInfo.InvariantCulture);
        }

        var accent = (Brush)FindResource("GlassAccentSolidBrush");
        for (int index = 0; index < _pvpHotbarButtons.Count; index++)
        {
            int slot = index + 1;
            Button button = _pvpHotbarButtons[index];
            bool hasTool = toolBySlot.TryGetValue(slot, out string? toolId);
            bool selected = hasTool && toolId == _selectedPvpTool;

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
            button.BorderBrush = selected ? accent : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
            string label = hasTool && toolId != null ? _pvpTools.Find(tool => tool.Id == toolId)!.Label : "pusty";
            button.ToolTip = $"Slot {slot}: {label}";
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

        UpdateClickerSummary(ChkAutoLeftEnabled, CbAutoLeftMode, TxtAutoLeftMinCps, TxtAutoLeftMaxCps, TxtAutoLeftSummary, SliderAutoLeftCps, TxtAutoLeftScaleMid, TxtAutoLeftScaleMax, TxtAutoLeftModeHint);
        UpdateClickerSummary(ChkAutoRightEnabled, CbAutoRightMode, TxtAutoRightMinCps, TxtAutoRightMaxCps, TxtAutoRightSummary, SliderAutoRightCps, TxtAutoRightScaleMid, TxtAutoRightScaleMax, TxtAutoRightModeHint);
    }

    private static void UpdateClickerSummary(CheckBox enabled, ComboBox mode, TextBox minBox, TextBox maxBox, TextBlock summary, GlassRangeSlider slider, TextBlock scaleMid, TextBlock scaleMax, TextBlock? modeHint)
    {
        if (summary == null || slider == null || minBox == null || maxBox == null || scaleMid == null || scaleMax == null)
            return;

        int min = ParseCps(minBox.Text);
        int max = Math.Max(min, ParseCps(maxBox.Text));
        // Skala suwaka rośnie dziesiątkami, gdy zakres przekracza typowe 20 CPS.
        // Podczas przeciągania skala się nie zmienia, żeby uchwyt nie uciekał spod kursora.
        int top = slider.IsDragging
            ? (int)slider.Maximum
            : max <= 20 ? 20 : (int)Math.Ceiling(max / 10.0) * 10;
        string modeName = (mode?.SelectedItem as ComboBoxItem)?.Content as string ?? "Tryb";
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
            : doubleClick ? modeName : $"{modeName} · {min}–{max} CPS";
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
