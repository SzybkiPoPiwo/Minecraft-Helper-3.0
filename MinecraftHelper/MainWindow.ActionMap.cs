using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MinecraftHelper.Models;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Mapa działania w osobnym oknie (przycisk w Ustawieniach): odświeżana co sekundę, tylko gdy okno jest otwarte.
public partial class MainWindow
{
    private ActionMapWindow? _actionMapWindow;
    private DispatcherTimer? _actionMapTimer;

    // Drugie kliknięcie wyciąga istniejące okno na wierzch.
    private void BtnOpenActionMap_Click(object sender, RoutedEventArgs e)
    {
        if (_actionMapWindow != null)
        {
            if (_actionMapWindow.WindowState == WindowState.Minimized)
                _actionMapWindow.WindowState = WindowState.Normal;
            _actionMapWindow.Activate();
            return;
        }

        var window = new ActionMapWindow(this, _accountDisplayName);
        window.Map.GoToRequested += GoToActionMapSetting;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshActionMap();
        window.Closed += (_, _) =>
        {
            timer.Stop();
            _actionMapTimer = null;
            _actionMapWindow = null;
        };
        _actionMapWindow = window;
        _actionMapTimer = timer;
        RefreshActionMap();
        window.Show();
        timer.Start();
    }

    private void RefreshActionMap()
    {
        if (_actionMapWindow == null)
            return;

        var tiles = ActionMapCatalog.Build(BuildActionMapSnapshot());
        string? current = GetActionMapCurrentStep();
        _actionMapWindow.Update(tiles, current, current == null
            ? "Kopacz teraz nie pracuje"
            : "Teraz: " + tiles.First(tile => tile.Id == current).Title);
    }

    private ActionMapSnapshot BuildActionMapSnapshot()
    {
        bool running633 = _kopacz633RuntimeEnabled;
        InventoryCleanupOwner owner = _kopacz533RuntimeEnabled ? InventoryCleanupOwner.Kopacz533
            : running633 ? InventoryCleanupOwner.Kopacz633
            : GetMiningStartOwner();
        bool uses633 = owner == InventoryCleanupOwner.Kopacz633;
        string mode = owner == InventoryCleanupOwner.None ? ""
            : !uses633 ? "5/3/3"
            : CbKopacz633Direction.SelectedIndex == 2 ? "6/3/3 · do góry"
            : CbKopacz633Direction.SelectedIndex == 1 ? "6/3/3 · na wprost"
            : "";

        DiscordSettings discord = DiscordSettingsStore.Load();
        EnderchestOptions enderchest = EnderchestOptionsFromUi();
        GoldDepositOptions gold = GoldOptions();
        return new ActionMapSnapshot
        {
            MiningMode = mode,
            Uses633 = uses633,
            Bind = (uses633 ? TxtKopacz633Key.Text : TxtKopacz533Key.Text).Trim(),
            CommandCount = (uses633 ? _settings.Kopacz633Commands : _settings.Kopacz533Commands)?.Count ?? 0,
            AutoEq = ChkInventoryCleanupEnabled.IsChecked == true,
            IntervalSeconds = GetConfiguredInventoryCleanupIntervalSeconds(),
            SlotSummary = GetInventorySlotSummary(),
            DropSummary = GetInventoryDropSummary(),
            CobbleX = ChkCobbleXEnabled.IsChecked == true,
            CobbleXThreshold = TxtCobbleXRequiredStacks.Text.Trim(),
            CobbleXCommandMissing = string.IsNullOrWhiteSpace(TxtCobbleXCommand.Text),
            Enderchest = ChkEnderchestEnabled.IsChecked == true,
            EnderchestCommand = enderchest.Command,
            EnderchestFull = _enderchestFull,
            EnderchestIssue = enderchest.Validate(),
            Gold = ChkGoldDepositEnabled.IsChecked == true,
            GoldCommand = gold.Command,
            GoldIssue = gold.Validate(),
            Food = ChkInventoryCleanupEatAfterCleanup.IsChecked == true,
            Protection = ChkEmergencyDamageSoundEnabled.IsChecked == true,
            ReconnectAfterAlarm = RbProtectionReturn.IsChecked == true,
            WaitSeconds = TxtEmergencyReconnectDelaySeconds.Text.Trim(),
            Hud = ChkOverlayHudEnabled.IsChecked == true,
            Discord = discord.Enabled,
            DiscordConnected = DiscordBotHost.ReadStatus()?.Connected == true,
            Details = BuildActionMapDetails(uses633, mode, enderchest, gold, discord)
        };
    }

    // Wiersze „Obecne ustawienia” po kliknięciu kafelka. Token bota nigdy tu nie trafia.
    private Dictionary<string, IReadOnlyList<ActionMapDetail>> BuildActionMapDetails(
        bool uses633, string mode, EnderchestOptions enderchest, GoldDepositOptions gold, DiscordSettings discord)
    {
        static ActionMapDetail Row(string label, string? value) => new(label, string.IsNullOrWhiteSpace(value) ? "nie ustawiono" : value.Trim());
        static string YesNo(bool value) => value ? "tak" : "nie";
        static string Join(IEnumerable<string> items, string empty)
        {
            string joined = string.Join(", ", items);
            return joined.Length > 0 ? joined : empty;
        }

        string bind = (uses633 ? TxtKopacz633Key.Text : TxtKopacz533Key.Text).Trim();
        var commands = (uses633 ? _settings.Kopacz633Commands : _settings.Kopacz533Commands) ?? new List<MinerCommand>();
        var slots = GetSelectedInventoryCleanupSlots();
        int mainCount = InventoryMarkerDetector.MainSlotCount;
        var offSlots = Enumerable.Range(0, mainCount).Where(slot => !slots.Contains(slot)).Select(slot => (slot + 1).ToString(CultureInfo.InvariantCulture));
        var hotbarSlots = slots.Where(slot => slot >= mainCount).OrderBy(slot => slot).Select(slot => "H" + (slot - mainCount + 1).ToString(CultureInfo.InvariantCulture));
        bool everything = ChkInventoryCleanupAllItemTypes.IsChecked == true;
        var dropItems = GetSelectedInventoryCleanupItemTypes().Select(GetInventoryCleanupItemLabel).OrderBy(label => label, StringComparer.CurrentCultureIgnoreCase);
        var enderItems = EnderchestCatalog.Where(item => enderchest.ItemTypes.Contains(item.Id)).Select(item => item.Label);
        AutoReconnectServerProfile? profile = GetSelectedAutoReconnectServerProfile();
        bool returns = RbProtectionReturn.IsChecked == true;

        var mine = new List<ActionMapDetail> { Row("Tryb", mode), Row("Kopanie w tle", YesNo(IsBackgroundMiningSelected())) };
        if (uses633 && CbKopacz633Direction.SelectedIndex == 1)
            mine.Add(Row("Szerokość", GetConfiguredKopacz633ForwardWidth() + " bloków"));
        else if (uses633 && CbKopacz633Direction.SelectedIndex == 2)
            mine.Add(Row("Trasa", $"{GetConfiguredKopacz633UpwardLength()} × {GetConfiguredKopacz633UpwardWidth()} bloków"));

        var notifications = new List<string>();
        if (discord.NotifyProtection) notifications.Add("alarm");
        if (discord.NotifyReconnect) notifications.Add("reconnect");
        if (discord.NotifyMissingPickaxe) notifications.Add("brak kilofa");
        if (discord.NotifyMiningStartStop) notifications.Add("start i koniec");
        if (discord.NotifyErrors) notifications.Add("błędy");

        return new Dictionary<string, IReadOnlyList<ActionMapDetail>>
        {
            ["start"] = new[] { Row("Bind", bind), Row("Tryb", mode) },
            ["pick"] = new[] { Row("Slot kilofa", "H1"), Row("Przedmiot", "diamentowy kilof") },
            ["mine"] = mine,
            ["cmd"] = commands.Count == 0
                ? new[] { Row("Komendy", "brak") }
                : commands.Select((command, index) => Row($"{index + 1}.", $"{command.Command} · po {command.Seconds} s")).ToArray(),
            ["open"] = new[] { Row("Skanuj co", GetConfiguredInventoryCleanupIntervalSeconds() + " s") },
            ["scan"] = new[]
            {
                Row("Główne EQ", $"{slots.Count(slot => slot < mainCount)} z {mainCount}"),
                Row("Wyłączone sloty", Join(offSlots, "żadne")),
                Row("Pasek na dole", Join(hotbarSlots, "wyłączony"))
            },
            ["drop"] = new[]
            {
                Row("Tryb", everything ? "wszystko poza cobblestone" : "wybrane przedmioty"),
                Row("Przedmioty", everything ? "wszystkie oprócz cobblestone i kilofa" : Join(dropItems, "nie wybrano"))
            },
            ["cobble"] = new[]
            {
                Row("Komenda", GetConfiguredCobbleXCommand()),
                Row("Próg", TxtCobbleXRequiredStacks.Text + " stacków 64"),
                Row("Wysyłanie", GetConfiguredCobbleXCommandCount() + " raz(y) z rzędu")
            },
            ["ender"] = new[]
            {
                Row("Komenda", enderchest.Command),
                Row("Tytuł okna", enderchest.MenuTitle),
                Row("Czekaj na okno", enderchest.OpenDelayMs + " ms"),
                Row("Przedmioty", Join(enderItems, "nie wybrano"))
            },
            ["gold"] = new[]
            {
                Row("Komenda", gold.Command),
                Row("Tytuł menu", gold.MenuTitle),
                Row("Pole wpłaty", gold.TargetSlot.ToString(CultureInfo.InvariantCulture)),
                Row("Co wpłaca", Join(new[] { gold.Blocks ? "bloki" : "", gold.Ingots ? "sztabki" : "" }.Where(item => item.Length > 0), "nic")),
                Row("Także z paska", YesNo(gold.IncludeHotbar))
            },
            ["food"] = new[] { Row("Slot", "2"), Row("Czas", "4 s"), Row("Kiedy", "po każdym cyklu Auto EQ") },
            ["alarm"] = new[]
            {
                Row("Tryb testowy", YesNo(ChkEmergencyDamageSoundTestMode.IsChecked == true)),
                Row("Czułość", SlEmergencyDamageSoundSimilarity.Value.ToString("0", CultureInfo.InvariantCulture) + "%"),
                Row("Urządzenie", CbEmergencyDamageSoundDevice.Text)
            },
            ["exit"] = new[] { Row("Po alarmie", returns ? "wraca i kopie dalej" : "tylko wyjście") },
            ["wait"] = new[] { Row("Czas", TxtEmergencyReconnectDelaySeconds.Text + " s") },
            ["join"] = new[] { Row("Serwer", profile?.Name), Row("Adres", profile?.ServerAddress), Row("Limit prób", profile?.MaxAttempts.ToString(CultureInfo.InvariantCulture)) },
            ["rcheck"] = new[] { Row("Przedmiot", "diamentowy kilof"), Row("Brak kilofa", profile?.MissingPickaxeRecoveryEnabled == true ? "powrót na home i koniec" : "koniec pracy") },
            ["home"] = new[]
            {
                Row("Komenda", profile?.HomeCommand),
                Row("Wybór w menu", profile == null ? null : profile.HomeHasGui ? "pole " + profile.HomeGuiSlot.ToString(CultureInfo.InvariantCulture) : "brak"),
                Row("Czas teleportu", profile == null ? null : profile.TeleportDelaySeconds + " s")
            },
            ["resume"] = new[] { Row("Tryb", mode) },
            ["hud"] = new[] { Row("Monitor", CbOverlayMonitor.Text), Row("Pozycja", CbOverlayCorner.Text), Row("Animacje", YesNo(ChkOverlayAnimationsEnabled.IsChecked == true)) },
            ["discord"] = new[]
            {
                Row("ID kanału", discord.ChannelId),
                Row("Właściciele", discord.GetOwnerIds().Count.ToString(CultureInfo.InvariantCulture)),
                Row("Prefiks", discord.Prefix),
                Row("Powiadomienia", Join(notifications, "wyłączone")),
                Row("Status co", discord.StatusIntervalMinutes > 0 ? discord.StatusIntervalMinutes + " min" : "wyłączony"),
                Row("Zrzut co", discord.ScreenshotIntervalMinutes > 0 ? discord.ScreenshotIntervalMinutes + " min" : "wyłączony"),
                Row("Wyłączone komendy", (discord.DisabledCommands?.Count ?? 0).ToString(CultureInfo.InvariantCulture))
            },
        };
    }

    // Krok, który Kopacz wykonuje w tej chwili; null = nie pracuje.
    private string? GetActionMapCurrentStep()
    {
        if (_emergencyDamageSoundHandlingAlarm)
            return "alarm";

        if (_autoReconnectStage != AutoReconnectStage.None)
        {
            return _autoReconnectStage switch
            {
                AutoReconnectStage.EmergencyWaitBeforeReconnect => "wait",
                AutoReconnectStage.EmergencyOpenInventory or AutoReconnectStage.EmergencyVerifyInventory
                    or AutoReconnectStage.HealthOpenInventory or AutoReconnectStage.HealthVerifyInventory
                    or AutoReconnectStage.OpenVerificationInventory or AutoReconnectStage.VerifyAfterTeleport => "rcheck",
                AutoReconnectStage.OpenHomeChat or AutoReconnectStage.TypeHomeCommand or AutoReconnectStage.SubmitHomeCommand
                    or AutoReconnectStage.WaitForHomeMenu or AutoReconnectStage.ClickHomeSlot or AutoReconnectStage.WaitForTeleport => "home",
                _ => "join"
            };
        }

        if (!_kopacz533RuntimeEnabled && !_kopacz633RuntimeEnabled)
            return null;
        if (_miningToolStage != MiningToolStage.None)
            return "pick";

        switch (_inventoryCleanupStage)
        {
            case InventoryCleanupStage.None:
                break;
            case InventoryCleanupStage.ReturnToMiningStart:
            case InventoryCleanupStage.OpenInventory:
                return "open";
            case InventoryCleanupStage.WaitForInventory:
                return "scan";
            case InventoryCleanupStage.MoveToSlot:
            case InventoryCleanupStage.PressDropModifier:
            case InventoryCleanupStage.PressDropKey:
            case InventoryCleanupStage.ReleaseDropKeys:
            case InventoryCleanupStage.CloseInventory:
                return "drop";
            case InventoryCleanupStage.OpenCobbleXChat:
            case InventoryCleanupStage.TypeCobbleXCommand:
            case InventoryCleanupStage.SubmitCobbleXCommand:
                return "cobble";
            case InventoryCleanupStage.MoveToEnderchest:
                return "ender";
            case InventoryCleanupStage.DepositGold:
                return "gold";
            case InventoryCleanupStage.SelectFoodSlot:
            case InventoryCleanupStage.StartEating:
            case InventoryCleanupStage.StopEatingAndRestoreTool:
                return "food";
            case InventoryCleanupStage.ResumeMining:
                return "back";
        }

        return _kopacz533CommandStage != Kopacz533CommandStage.None || _kopacz633CommandStage != Kopacz633CommandStage.None
            ? "cmd"
            : "mine";
    }

    private void GoToActionMapSetting(ActionMapTile tile)
    {
        if (_isMinimizedToTray)
            RestoreFromTray();
        else if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        if (tile.Id == "logs")
        {
            OpenMiningLogsWindow();
            return;
        }
        NavigateToSection(tile.Tab!);
        // Zakładka buduje się przy najbliższym układzie; dopiero potem karta ma rozmiar i położenie.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            FrameworkElement? target = tile.Targets
                .Select(name => FindName(name) as FrameworkElement)
                .FirstOrDefault(element => element is { IsVisible: true, ActualHeight: > 0 });
            if (target == null)
                return;

            // Kafelek trybu w zakładce Kopanie: od razu pokazujemy jego ustawienia.
            if (target is RadioButton { GroupName: "KopaczMode" } modeTile)
                modeTile.IsChecked = true;

            if (FindAncestor<ScrollViewer>(target) is { } scroller)
                scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + target.TranslatePoint(new Point(0, 0), scroller).Y - 16));
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
                FillBehavior = FillBehavior.Stop
            });
        }));
    }

    private string GetInventorySlotSummary()
    {
        var selected = GetSelectedInventoryCleanupSlots();
        int main = selected.Count(slot => slot < InventoryMarkerDetector.MainSlotCount);
        int hotbar = selected.Count - main;
        return hotbar > 0 ? $"{main} z 27 + pasek: {hotbar}" : $"{main} z 27";
    }

    private string GetInventoryDropSummary() => ChkInventoryCleanupAllItemTypes.IsChecked == true
        ? "wszystko poza cobble"
        : string.Format(CultureInfo.InvariantCulture, "wybrane typy: {0}", GetSelectedInventoryCleanupItemTypes().Count);
}
