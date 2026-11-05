using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinecraftHelper.Models;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Wygląd glass Kopacza: nawigacja boczna, pasek wybranego konta i podsumowania sekcji.
public partial class MainWindow
{
    private DispatcherTimer? _glassUiTimer;
    private MultiAccountPanel? _multiAccountPanel;
    private MiningLogsPanel? _miningLogsPanel;
    private bool _syncingGlassControls;

    private void InitializeGlassUi()
    {
        InitializeKopacz633AreaEditor();
        InitializeDeveloperMenu();
        TxtAppVersion.Text = $"Minecraft Helper {_currentAppVersion}";
        TxtAboutVersion.Text = $"Minecraft Helper — Kopacz {_currentAppVersion}";
        RefreshGlassAccountInfo();

        string accent = GlassAccents.Normalize(_settings.GlassAccent);
        _syncingGlassControls = true;
        try
        {
            RbAccentIris.IsChecked = accent == "iris";
            RbAccentKoral.IsChecked = accent == "koral";
            RbAccentOcean.IsChecked = accent == "ocean";
        }
        finally
        {
            _syncingGlassControls = false;
        }
        GlassAccents.Apply(accent);

        CbKopacz633Direction.SelectionChanged += (_, _) => SyncKopacz633DirectionRadios();
        SyncKopacz633DirectionRadios();
        UpdateProtectionFlowEnd();

        HookMoveSizeLoop();
        MainTabControl.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnPageScrollChanged));
        MainTabControl.SelectionChanged += (_, e) =>
        {
            // SelectionChanged bąbelkuje też z list i ComboBoxów wewnątrz zakładek.
            if (ReferenceEquals(e.OriginalSource, MainTabControl))
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FindPageScroller);
        };

        LoadDiscordSettingsToUi();
        NavKopacz.IsChecked = true;

        _glassUiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _glassUiTimer.Tick += (_, _) => RefreshGlassUi();
        _glassUiTimer.Start();
        RefreshGlassUi();
    }

    private void RefreshGlassAccountInfo()
    {
        if (TxtAccountName == null)
            return;

        string name = string.IsNullOrWhiteSpace(_accountDisplayName) ? "Konto główne" : _accountDisplayName.Trim();
        TxtAccountName.Text = name;
        TxtAccountInitial.Text = name[..1].ToUpper(CultureInfo.CurrentCulture);
        TxtProfileCommandLine.Text = _accountProfileId == MinerProfileId.Default
            ? "MinecraftHelper.Miner.exe"
            : $"MinecraftHelper.Miner.exe --profile {_accountProfileId}";
        TxtProfileDataPath.Text = _settingsService.SettingsDirectoryPath;
    }

    // ===== Nawigacja =====

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tabName } || FindName(tabName) is not TabItem tab)
            return;

        if (tab == TabAccounts && _multiAccountPanel == null)
        {
            _multiAccountPanel = new MultiAccountPanel(_accountProfileId);
            HostAccounts.Content = _multiAccountPanel;
        }
        else if (tab == TabLogs && _miningLogsPanel == null)
        {
            _miningLogsPanel = new MiningLogsPanel(_miningLogService, _accountProfileId);
            HostLogs.Content = _miningLogsPanel;
        }

        MainTabControl.SelectedItem = tab;
        if (tab == TabSettings)
        {
            RefreshResourcePackStatus(); // czyta pliki gry — tylko przy wejściu, nie w cyklicznym odświeżaniu
            RenderDeveloperLog();
        }
        // Konta i Logi dotyczą wszystkich kont, więc pasek wybranego konta byłby tam mylący.
        BorderAccountBar.Visibility = tab == TabAccounts || tab == TabLogs ? Visibility.Collapsed : Visibility.Visible;
        RefreshGlassUi();
    }

    private void NavigateToSection(string tabName)
    {
        foreach (RadioButton item in new[] { NavAccounts, NavLogs, NavKopacz, NavInventory, NavProtection, NavBackground, NavTools, NavSettings })
        {
            if (item.Tag as string == tabName)
            {
                item.IsChecked = true;
                return;
            }
        }
    }

    private bool IsGlassSectionActive(TabItem tab) => MainTabControl?.SelectedItem == tab;

    // ===== Przesuwanie okna =====

    // Animowane plamy w tle przerysowują całe okno (z cieniami kart) kilkanaście razy na
    // sekundę. Podczas przesuwania lub zmiany rozmiaru okna wstrzymujemy je — ruch jest płynny.
    private void HookMoveSizeLoop()
    {
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
            source.AddHook(MoveSizeHook);
        else
            SourceInitialized += (_, __) => HookMoveSizeLoop();
    }

    private IntPtr MoveSizeHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmEnterSizeMove = 0x0231;
        const int WmExitSizeMove = 0x0232;
        if (message == WmEnterSizeMove)
            CyberBackground.IsAnimationEnabled = false;
        else if (message == WmExitSizeMove)
            CyberBackground.IsAnimationEnabled = ChkAnimatedBackgroundEnabled.IsChecked != false;
        return IntPtr.Zero;
    }

    // ===== Wskaźnik „więcej poniżej” =====

    private ScrollViewer? _pageScroller;

    private void FindPageScroller()
    {
        _pageScroller = FindFirstScrollViewer(MainTabControl.SelectedContent as DependencyObject);
        UpdateScrollMoreIndicator();
    }

    // Strona zakładki to pierwszy ScrollViewer w jej treści (w Kontach i Logach — wewnątrz panelu).
    private static ScrollViewer? FindFirstScrollViewer(DependencyObject? root)
    {
        var queue = new Queue<DependencyObject>();
        if (root != null)
            queue.Enqueue(root);
        while (queue.Count > 0)
        {
            DependencyObject node = queue.Dequeue();
            if (node is ScrollViewer scroller)
                return scroller;
            int count = node is Visual ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (int i = 0; i < count; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    private void OnPageScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_pageScroller == null)
            FindPageScroller();
        else if (ReferenceEquals(e.OriginalSource, _pageScroller))
            UpdateScrollMoreIndicator();
    }

    private void UpdateScrollMoreIndicator()
    {
        bool more = _pageScroller != null && _pageScroller.ScrollableHeight - _pageScroller.VerticalOffset > 24;
        BtnScrollMore.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnScrollMore_Click(object sender, RoutedEventArgs e)
    {
        if (_pageScroller != null)
            _pageScroller.ScrollToVerticalOffset(_pageScroller.VerticalOffset + _pageScroller.ViewportHeight * 0.8);
    }

    private void HyperlinkNavigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { Tag: string tabName })
            NavigateToSection(tabName);
    }

    private void AccountChip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => NavigateToSection(nameof(TabAccounts));

    // Podgląd na żądanie w osobnym oknie — odświeża się tylko, dopóki jest otwarte.
    private void BtnAccountPeek_Click(object sender, RoutedEventArgs e)
    {
        if (_targetGameWindowHandle == IntPtr.Zero)
        {
            UpdateStatusBar("Brak okna gry — wybierz je w zakładce Okno gry.", "Orange");
            return;
        }

        var profile = new MinerAccountProfile { Id = _accountProfileId, Name = _accountDisplayName };
        new MinerPreviewWindow(profile, _targetGameWindowHandle) { Owner = this }.Show();
    }

    private void BtnCalibrateWindow_Click(object sender, RoutedEventArgs e)
    {
        // Udana kalibracja sama ustawia zielony pasek stanu; tu zostają tylko odmowy.
        (bool arranged, string message) = ArrangeGameWindowFromRemoteCommand();
        if (!arranged && message.Length > 0)
            UpdateTemporaryStatusBar(char.ToUpper(message[0], CultureInfo.CurrentCulture) + message[1..], "Orange", 6);
        RefreshGlassUi();
    }

    // ===== Start / stop z paska konta =====

    private void BtnHeroToggle_Click(object sender, RoutedEventArgs e)
    {
        MinerRemoteStatus status = BuildRemoteStatus();
        if (status.Running)
        {
            StopMiningFromRemoteCommand();
            UpdateStatusBar("Kopacz zatrzymany z panelu.", "Orange");
        }
        else
        {
            if (GetMiningStartBlocker() is { } blocker)
            {
                UpdateStatusBar($"Nie uruchomiono: {blocker}", "Red");
                RefreshGlassUi();
                return;
            }

            var countdown = new StartCountdownWindow(
                "Start: " + GetInventoryCleanupOwnerLabel(GetMiningStartOwner()),
                IsBackgroundMiningSelected()
                    ? "Przełącz się na inny program albo pulpit — kopanie wystartuje, gdy wyjdziesz z Kopacza."
                    : "Przejdź do okna gry — kopanie wystartuje automatycznie.")
            {
                Owner = this
            };
            if (countdown.ShowDialog() != true)
            {
                UpdateStatusBar("Start kopania anulowany.", "Orange");
                return;
            }

            (bool started, string message) = StartMiningFromRemoteCommand();
            UpdateStatusBar(started ? message : $"Nie uruchomiono: {message}", started ? "Green" : "Red");
        }

        RefreshGlassUi();
    }

    // ===== Synchronizacja kontrolek zastępczych =====

    private void RbKopacz633Direction_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingGlassControls || sender is not RadioButton { Tag: string tag })
            return;

        if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            CbKopacz633Direction.SelectedIndex = index;
    }

    private void SyncKopacz633DirectionRadios()
    {
        _syncingGlassControls = true;
        try
        {
            RbKopacz633Forward.IsChecked = CbKopacz633Direction.SelectedIndex == 1;
            RbKopacz633Up.IsChecked = CbKopacz633Direction.SelectedIndex == 2;
        }
        finally
        {
            _syncingGlassControls = false;
        }
    }

    private void ProtectionAfterMode_Checked(object sender, RoutedEventArgs e)
    {
        UpdateProtectionFlowEnd();
        OnEmergencyReconnectModeChanged();
    }

    private void UpdateProtectionFlowEnd()
    {
        if (TxtProtectionFlowEnd == null || BorderProtectionFlowEnd == null)
            return;

        bool returnAfterAlarm = RbProtectionReturn.IsChecked == true;
        TxtProtectionFlowEnd.Text = returnAfterAlarm ? "3 · Powrót i kopanie" : "Koniec";
        BorderProtectionFlowEnd.Background = returnAfterAlarm ? GlassPalette.OkSoft : GlassPalette.NeutralSoft;
    }

    private void AccentSwatch_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingGlassControls || sender is not RadioButton { Tag: string accent })
            return;

        GlassAccents.Apply(accent);
        CyberBackground.RefreshColors();
        _settings.GlassAccent = GlassAccents.Normalize(accent);
        MarkDirty();
        RefreshOverlayHud(DateTime.UtcNow);
        RefreshGlassUi();
    }

    // ===== Odświeżanie podsumowań =====

    private void RefreshGlassUi()
    {
        if (_glassUiTimer == null || !IsVisible || WindowState == WindowState.Minimized || _isMinimizedToTray)
            return;

        MinerRemoteStatus status = BuildRemoteStatus();
        if (BorderAccountBar.Visibility == Visibility.Visible)
            RefreshHeroCard(status);

        if (IsGlassSectionActive(TabInventory))
            RefreshInventorySummaries();
        else if (IsGlassSectionActive(TabBackground))
            RefreshBackgroundStates(status);
        else if (IsGlassSectionActive(TabSettings))
            RefreshDiscordStatus();
    }

    private void RefreshHeroCard(MinerRemoteStatus status)
    {
        string state;
        string title;
        string hint;
        Brush pillBackground;
        Brush pillForeground;
        if (status.WindowOwnedByAnotherProfile || !status.WindowAvailable)
        {
            state = "BRAK OKNA";
            title = "Kopacz nie ma okna gry";
            hint = status.WindowOwnedByAnotherProfile
                ? "Wybrane okno należy do innego konta. Wybierz inne w zakładce Okno gry."
                : "Wybierz okno Minecrafta w zakładce Okno gry.";
            pillBackground = GlassPalette.BadSoft;
            pillForeground = GlassPalette.Bad;
        }
        else if (status.Running)
        {
            bool backgroundActive = status.BackgroundActive;
            state = backgroundActive ? "KOPIE W TLE" : status.GameFocused ? "KOPIE" : "PAUZA";
            title = $"Kopacz {status.Mode} pracuje";
            hint = backgroundActive
                ? "Gra pracuje bez fokusu — STOP na tym pasku albo w panelu Konta."
                : status.GameFocused
                    ? "Ten sam bind w grze zatrzymuje kopanie."
                    : "Gra nie ma fokusu — kopanie czeka na powrót do okna gry.";
            pillBackground = status.GameFocused || backgroundActive ? GlassPalette.Accent : GlassPalette.WarnSoft;
            pillForeground = status.GameFocused || backgroundActive ? Brushes.White : GlassPalette.Warn;
        }
        else
        {
            state = "GOTOWY";
            InventoryCleanupOwner startOwner = GetMiningStartOwner();
            title = startOwner == InventoryCleanupOwner.None
                ? "Wybierz kanał kopania"
                : "START: " + GetInventoryCleanupOwnerLabel(startOwner);
            hint = startOwner switch
            {
                InventoryCleanupOwner.Kopacz633 => IsKopacz633DirectionSelected()
                    ? $"Ruch: {GetKopacz633DirectionLabel()}. Przy obu włączonych kanałach START wybiera 6/3/3."
                    : "Przed startem wybierz kierunek Kopacza 6/3/3 poniżej.",
                InventoryCleanupOwner.Kopacz533 => "Kopanie w miejscu: LPM + Shift. Aby chodzić, włącz kanał 6/3/3.",
                _ => "Włącz 5/3/3 do kopania w miejscu albo 6/3/3 do kopania z chodzeniem."
            };
            // Zielone „GOTOWY” tylko wtedy, gdy START naprawdę ruszy; inaczej czerwone z powodem.
            if (GetMiningStartBlocker() is { } blocker)
            {
                state = "NIE GOTOWY";
                hint = char.ToUpper(blocker[0], CultureInfo.CurrentCulture) + blocker[1..];
                pillBackground = GlassPalette.BadSoft;
                pillForeground = GlassPalette.Bad;
            }
            else
            {
                pillBackground = GlassPalette.OkSoft;
                pillForeground = GlassPalette.Ok;
            }
        }

        TxtHeroState.Text = state;
        TxtHeroTitle.Text = title;
        TxtHeroHint.Text = hint;
        PillHeroState.Background = pillBackground;
        TxtHeroState.Foreground = pillForeground;
        DotHeroState.Fill = pillForeground;
        TxtHeroClock.Text = MinerRemoteCommandRouter.FormatDuration(status.RuntimeSeconds);
        TxtHeroToggle.Text = status.Running ? "STOP" : "START";
        PathHeroToggle.Data = (Geometry)FindResource(status.Running ? "IcoStopSquare" : "IcoPlay");
        BtnHeroToggle.Style = (Style)FindResource(status.Running ? "GlassDangerButton" : "GlassPrimaryButton");

        // Zmiana rozmiaru w trakcie pracy przesunęłaby środek ekranu, w który celuje kopanie.
        bool canCalibrate = !status.Running && status.WindowAvailable && !status.WindowOwnedByAnotherProfile;
        BtnCalibrateWindow.IsEnabled = canCalibrate;
        BtnCalibrateWindowTab.IsEnabled = canCalibrate;
        BtnCalibrateWindow.ToolTip = status.Running
            ? "Zatrzymaj kopanie, aby skalibrować okno."
            : "Ustawia okno Minecrafta tego konta (854×480) obok okien pozostałych kont.";
    }

    private void RefreshInventorySummaries()
    {
        int slots = GetSelectedInventoryCleanupSlots().Count;
        int types = GetSelectedInventoryCleanupItemTypes().Count;
        bool all = ChkInventoryCleanupAllItemTypes.IsChecked == true;
        bool gold = ChkGoldDepositEnabled.IsChecked == true;
        TxtSlotsCount.Text = $"{slots} z 27";
        TxtItemTypesCount.Text = all ? "tryb: wszystko poza cobblestone" : $"{types} wybranych";
        TxtPipeScanSummary.Text = $"{slots} z 27 slotów · hotbar pomijany";
        TxtPipeDropSummary.Text = (all ? "Wszystko poza cobblestone" : $"{types} typów przedmiotów") + " · całe stacki (Ctrl + Q)";
        TxtPipeGoldSummary.Text = gold
            ? $"{(string.IsNullOrWhiteSpace(TxtGoldDepositCommand.Text) ? "brak komendy" : TxtGoldDepositCommand.Text.Trim())} · pole {TxtGoldDepositSelectedSlot.Text.Split(' ')[0]}"
            : "Wyłączona";
        TxtGoldProtectedHint.Visibility = gold ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshBackgroundStates(MinerRemoteStatus status)
    {
        bool enabled = IsBackgroundMiningSelected();
        bool backgroundActive = BackgroundGameWindow.IsActive;
        bool focused = status.GameFocused && !backgroundActive;
        StyleBackgroundState(BorderBgStateFocus, enabled && focused);
        StyleBackgroundState(BorderBgStateBackground, enabled && backgroundActive);
        TxtBackgroundStateText.Text = !enabled
            ? "Kopanie w tle jest wyłączone — Kopacz działa tylko z grą na wierzchu."
            : backgroundActive
                ? "Sesja w tle trwa. Wejście trafia prosto do okna gry tego konta."
                : status.Running
                    ? "Kopacz pracuje z grą na wierzchu. Po utracie fokusu przejdzie w sesję w tle."
                    : "Gotowe. Uruchom Kopacza bindem w grze albo START na pasku konta.";
        TxtBgCheckWindow.Text = status.GameProcessId > 0 ? $"PID {status.GameProcessId}" : "brak";
        TxtBgCheckWindow.Foreground = status.GameProcessId > 0 ? GlassPalette.Ok : GlassPalette.Bad;

        IntPtr window = _targetGameWindowHandle;
        System.Drawing.Size size = window == IntPtr.Zero ? System.Drawing.Size.Empty : BackgroundGameWindow.GetClientSize(window);
        bool prepared = size.Width == BackgroundGameWindow.PreparedClientWidth && size.Height == BackgroundGameWindow.PreparedClientHeight;
        TxtWindowCalibrationState.Text = window == IntPtr.Zero
            ? "Rozmiar okna: brak wybranego okna."
            : prepared
                ? $"Rozmiar okna: {size.Width}×{size.Height} — skalibrowane."
                : $"Rozmiar okna: {size.Width}×{size.Height} — kalibracja ustawi 854×480 obok innych kont.";
        TxtWindowCalibrationState.Foreground = window == IntPtr.Zero ? GlassPalette.Bad : prepared ? GlassPalette.Ok : GlassPalette.Warn;
    }

    private static void StyleBackgroundState(Border border, bool active)
    {
        border.Background = active ? GlassPalette.AccentSoft : new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        border.BorderBrush = active ? GlassPalette.AccentSolid : GlassPalette.Line;
        border.BorderThickness = new Thickness(active ? 2 : 1);
    }
}
