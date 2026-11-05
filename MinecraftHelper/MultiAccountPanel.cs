using System;
using System.Collections.Generic;
using System.Diagnostics;
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

/// <summary>Wspólny panel kont; silniki profili pracują bez dodatkowych okien.</summary>
internal sealed class MultiAccountPanel : UserControl
{
    private static readonly TimeSpan StartAllDelay = TimeSpan.FromSeconds(1.5);
    private static readonly int[] LiveFpsOptions = { 10, 20, 30 };
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly string _currentProfileId;
    private readonly MinerAccountStore _store = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly WrapPanel _cards = new();
    private readonly CheckBox _liveEnabled;
    private readonly TextBlock _notice;
    private readonly TextBlock _tileRunning = Tile();
    private readonly TextBlock _tileBackground = Tile();
    private readonly TextBlock _tileIdle = Tile();
    private readonly TextBlock _tileNoWindow = Tile();
    private readonly Dictionary<string, AccountCard> _cardByProfile = new(StringComparer.OrdinalIgnoreCase);
    private List<MinerAccountProfile> _profiles;
    private List<GameWindowOption> _gameWindows = new();
    private bool _refreshing;
    private bool _bulkActionRunning;
    private int _liveFps = 30;

    public MultiAccountPanel(string currentProfileId)
    {
        _currentProfileId = MinerProfileId.Normalize(currentProfileId);
        _profiles = _store.Load().ToList();
        // Live robi zrzut okna każdego konta co kilka sekund i potrafi przyciąć grę oraz panel,
        // dlatego startuje wyłączony; pojedyncze konto można podejrzeć kliknięciem w kartę.
        _liveEnabled = new CheckBox { IsChecked = false, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Podgląd Live wszystkich kont" };
        _notice = new TextBlock { Foreground = GlassPalette.Muted, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

        Content = BuildContent();
        RenderCards();

        _refreshTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _refreshTimer.Tick += async (_, __) => await RefreshStatusAsync();
        _liveEnabled.Click += async (_, __) => await RefreshStatusAsync();
        Loaded += async (_, __) =>
        {
            if (IsVisible)
                await ActivatePanelAsync();
        };
        Unloaded += (_, __) =>
        {
            _refreshTimer.Stop();
            StopAllLive();
        };
        IsVisibleChanged += async (_, __) =>
        {
            if (!IsVisible)
            {
                _refreshTimer.Stop();
                // Po wyjściu z Kont Live zawsze się wyłącza — włącza się go ręcznie, gdy jest potrzebny.
                _liveEnabled.IsChecked = false;
                StopAllLive();
            }
            else if (IsLoaded)
                await ActivatePanelAsync();
        };
    }

    private async Task ActivatePanelAsync()
    {
        _profiles = _store.Load().ToList();
        RenderCards();
        _refreshTimer.Start();
        await RefreshStatusAsync();
        await RefreshWindowsAsync(assignFreeWindows: true, announce: false);
    }

    internal Window? HostWindow => Window.GetWindow(this);

    internal int LiveFps => _liveFps;

    // Strumienie Live działają tylko przy widocznym panelu — ukryty panel nie obciąża gier.
    private void StopAllLive()
    {
        foreach (AccountCard card in _cardByProfile.Values)
            card.StopLive();
    }

    private UIElement BuildContent()
    {
        var page = new StackPanel { Margin = new Thickness(4, 2, 8, 12) };

        // Nagłówek
        var header = new DockPanel { Margin = new Thickness(4, 4, 4, 18) };
        Button addButton = CreateButton("Dodaj konto", null, "M12,5 V19 M5,12 H19");
        addButton.VerticalAlignment = VerticalAlignment.Bottom;
        addButton.Click += (_, __) => AddProfile();
        DockPanel.SetDock(addButton, Dock.Right);
        header.Children.Add(addButton);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "Konta", Style = GlassChrome.GetStyle("GlassH1") });
        titles.Children.Add(new TextBlock
        {
            Text = "Wszystkie konta w jednym panelu. Wybierz konto, aby zmienić jego ustawienia; pozostałe dalej pracują w tle.",
            Style = GlassChrome.GetStyle("GlassLead")
        });
        header.Children.Add(titles);
        page.Children.Add(header);

        // Kafelki liczników
        var tiles = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(-9, 0, -9, 0) };
        tiles.Children.Add(BuildTile(_tileRunning, "kopie teraz", GlassPalette.Ok, "M8,5 L19,12 L8,19 Z", filled: true));
        tiles.Children.Add(BuildTile(_tileBackground, "w tle", null, "M5,8 H14 A2,2 0 0 1 16,10 V17 A2,2 0 0 1 14,19 H5 A2,2 0 0 1 3,17 V10 A2,2 0 0 1 5,8 Z M8,8 V6 A2,2 0 0 1 10,4 H19 A2,2 0 0 1 21,6 V13 A2,2 0 0 1 19,15 H16", filled: false));
        tiles.Children.Add(BuildTile(_tileIdle, "bezczynne", null, "M12,3 A9,9 0 1 1 11.99,3 Z M12,7.5 V12 L15,14", filled: false));
        tiles.Children.Add(BuildTile(_tileNoWindow, "bez okna gry", GlassPalette.Bad, "M12,4 L21,19.5 H3 Z M12,10 V14 M12,16.8 H12.01", filled: false));
        page.Children.Add(tiles);

        // Pasek narzędzi
        var toolbar = new DockPanel();
        var live = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        live.Children.Add(_liveEnabled);
        live.Children.Add(new TextBlock { Text = "Podgląd Live", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(10, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        var rates = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int fps in LiveFpsOptions)
        {
            var rate = new RadioButton
            {
                Content = $"{fps} kl./s",
                GroupName = "AccountsLiveRate",
                Style = GlassChrome.GetStyle("GlassSegment"),
                IsChecked = fps == _liveFps
            };
            int selected = fps;
            rate.Checked += (_, __) =>
            {
                _liveFps = selected;
                foreach (AccountCard card in _cardByProfile.Values)
                    card.ApplyLiveFps(selected);
            };
            rates.Children.Add(rate);
        }
        live.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassSegmentHost"), Child = rates });
        DockPanel.SetDock(live, Dock.Left);
        toolbar.Children.Add(live);

        // WrapPanel: przy wąskim oknie przyciski schodzą do nowej linii zamiast nachodzić na Live.
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button refreshWindows = AccountCard.CreateIconButton("M20,11 A8,8 0 1 0 17.7,16.7 M20,5 V11 H14", "Odśwież listę okien Minecrafta");
        refreshWindows.Click += async (_, __) => await RefreshWindowsAsync(assignFreeWindows: true, announce: true);
        Button arrangeAll = CreateButton("Ułóż wszystkie okna", null, "M3,3 H10 V10 H3 Z M14,3 H21 V10 H14 Z M3,14 H10 V21 H3 Z M14,14 H21 V21 H14 Z");
        arrangeAll.ToolTip = "Kalibruje okna wszystkich zatrzymanych kont: 854×480, każde w swoim miejscu obok innych.";
        arrangeAll.Click += async (_, __) => await ArrangeAllAsync();
        Button stopAll = CreateButton("Zatrzymaj wszystkie", "GlassDangerButton", "M7,7 H17 V17 H7 Z", filledIcon: true);
        stopAll.Click += async (_, __) => await StopAllAsync();
        Button startAll = CreateButton("Uruchom wszystkie", "GlassPrimaryButton", "M8,5 L19,12 L8,19 Z", filledIcon: true);
        startAll.ToolTip = "Bezczynne konta startują co 1,5 s, żeby nie wchodzić na serwer naraz.";
        startAll.Click += async (_, __) => await StartAllAsync();
        foreach (Button button in new[] { refreshWindows, arrangeAll, stopAll, startAll })
        {
            button.Margin = new Thickness(8, 4, 0, 4);
            actions.Children.Add(button);
        }
        toolbar.Children.Add(actions);
        page.Children.Add(new Border
        {
            Style = GlassChrome.GetStyle("GlassCard"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = toolbar
        });

        page.Children.Add(new Border
        {
            Style = GlassChrome.GetStyle("GlassNote"),
            Child = _notice,
            Visibility = Visibility.Collapsed,
            Name = "NoticeBox"
        });

        _cards.Orientation = Orientation.Horizontal;
        _cards.Margin = new Thickness(0, 4, -18, 0);
        page.Children.Add(_cards);

        page.Children.Add(new Border
        {
            Style = GlassChrome.GetStyle("GlassNote"),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock
            {
                Text = "START i „Uruchom wszystkie” przygotowują konta automatycznie. Do pracy bez fokusu włącz „Kopanie w tle” w ustawieniach konta. Kliknij podgląd karty, aby podejrzeć konto; ⤢ otwiera powiększenie.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            }
        });

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = page
        };
    }

    private static TextBlock Tile() => new() { Text = "0", FontSize = 22, FontWeight = FontWeights.Bold };

    private static Border BuildTile(TextBlock value, string label, Brush? tint, string icon, bool filled)
    {
        var iconPath = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(icon),
            Style = GlassChrome.GetStyle("GlassIconPath")
        };
        if (filled)
            iconPath.Fill = tint ?? GlassPalette.AccentInk;
        var iconHost = new Grid { Width = 24, Height = 24 };
        iconHost.Children.Add(iconPath);
        var badge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(13),
            Background = tint == null ? GlassPalette.AccentSoft : GlassPalette.SoftFor(tint),
            Child = new Viewbox { Width = 20, Height = 20, Child = iconHost }
        };
        badge.SetValue(TextElement.ForegroundProperty, tint ?? GlassPalette.AccentInk);

        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(value);
        text.Children.Add(new TextBlock { Text = label, Style = GlassChrome.GetStyle("GlassSub") });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(badge);
        row.Children.Add(text);
        return new Border
        {
            Style = GlassChrome.GetStyle("GlassCard"),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16),
            Margin = new Thickness(9, 0, 9, 14),
            Child = row
        };
    }

    private void RenderCards()
    {
        StopAllLive();
        _cards.Children.Clear();
        _cardByProfile.Clear();

        for (int index = 0; index < _profiles.Count; index++)
        {
            MinerAccountProfile profile = _profiles[index];
            var card = new AccountCard(profile, this, index);
            _cardByProfile[profile.Id] = card;
            _cards.Children.Add(card.Root);
            card.UpdateWindowChoices(_gameWindows, BuildWindowOwners());
        }
    }

    private void ShowNotice(string text, Brush brush)
    {
        _notice.Text = text;
        _notice.Foreground = brush;
        _notice.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        if (_notice.Parent is Border box)
            box.Visibility = _notice.Visibility;
    }

    private async Task RefreshStatusAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            AccountCard[] cards = _cardByProfile.Values.ToArray();
            await Task.WhenAll(cards.Select(card => card.RefreshAsync(_liveEnabled.IsChecked == true)));
            _tileRunning.Text = cards.Count(card => card.Status?.Running == true).ToString();
            _tileBackground.Text = cards.Count(card => card.Status?.BackgroundActive == true).ToString();
            _tileIdle.Text = cards.Count(card => card.ProcessRunning && card.Status?.Running == false).ToString();
            _tileNoWindow.Text = cards.Count(card => card.ProcessRunning && card.Status?.WindowAvailable == false).ToString();

            Dictionary<int, string> owners = BuildWindowOwners();
            foreach (AccountCard card in cards)
                card.UpdateWindowChoices(_gameWindows, owners);

            // A profile launched from this panel gets a free window as soon as it answers.
            if (cards.Any(card => card.AssignWindowWhenReady && card.ProcessRunning))
                await AssignFreeWindowsAsync(onlyPending: true);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshWindowsAsync(bool assignFreeWindows, bool announce)
    {
        _gameWindows = await Task.Run(GameWindowOption.FindAll);
        if (assignFreeWindows)
            await AssignFreeWindowsAsync(onlyPending: false);

        Dictionary<int, string> owners = BuildWindowOwners();
        foreach (AccountCard card in _cardByProfile.Values)
            card.UpdateWindowChoices(_gameWindows, owners);

        if (announce)
        {
            ShowNotice(
                _gameWindows.Count == 0 ? "Nie znaleziono okien Minecrafta." : $"Znaleziono okien Minecrafta: {_gameWindows.Count}.",
                _gameWindows.Count == 0 ? GlassPalette.Warn : GlassPalette.Muted);
        }
    }

    internal async Task RefreshWindowListAsync()
    {
        _gameWindows = await Task.Run(GameWindowOption.FindAll);
    }

    private Dictionary<int, string> BuildWindowOwners()
    {
        var owners = new Dictionary<int, string>();
        foreach (AccountCard card in _cardByProfile.Values)
        {
            int processId = card.Status?.GameProcessId ?? 0;
            if (card.ProcessRunning && processId > 0)
                owners[processId] = card.Profile.Name;
        }

        return owners;
    }

    // Like choosing a window by hand, but for every running profile without one.
    private async Task AssignFreeWindowsAsync(bool onlyPending)
    {
        if (onlyPending)
            _gameWindows = await Task.Run(GameWindowOption.FindAll);

        HashSet<int> owned = BuildWindowOwners().Keys.ToHashSet();
        foreach (AccountCard card in _cardByProfile.Values)
        {
            if (!card.ProcessRunning || card.Status == null || card.Status.GameProcessId > 0)
                continue;
            if (onlyPending && !card.AssignWindowWhenReady)
                continue;

            card.AssignWindowWhenReady = false;
            foreach (GameWindowOption window in _gameWindows.Where(window => !owned.Contains(window.ProcessId)))
            {
                MinerRemotePipeResponse? response = await card.SendAsync($"window {window.ProcessId}");
                if (response?.Success == true)
                {
                    owned.Add(window.ProcessId);
                    await card.RefreshAsync(_liveEnabled.IsChecked == true);
                    break;
                }
            }
        }
    }

    private async Task StartAllAsync()
    {
        if (_bulkActionRunning)
            return;

        _bulkActionRunning = true;
        try
        {
            var errors = new List<string>();
            foreach (AccountCard card in _cardByProfile.Values.Where(card => card.Profile.Enabled && !card.ProcessRunning).ToArray())
                if (!await EnsureProfileReadyAsync(card))
                    errors.Add($"{card.Profile.Name}: nie udało się przygotować konta.");
            await RefreshStatusAsync();
            await RefreshWindowsAsync(assignFreeWindows: true, announce: false);
            AccountCard[] idle = _cardByProfile.Values
                .Where(card => card.Profile.Enabled)
                .Where(card => card.ProcessRunning && card.Status?.Running == false)
                .ToArray();
            if (idle.Length == 0)
            {
                ShowNotice(errors.Count > 0 ? string.Join(Environment.NewLine, errors)
                    : "Brak gotowych kont do uruchomienia. Sprawdź ustawienia i przypisanie okien Minecrafta.", GlassPalette.Warn);
                return;
            }

            int expected = idle.Length + errors.Count;
            int started = 0;
            for (int index = 0; index < idle.Length; index++)
            {
                // Staggered start, so the clients do not all hit the server at once.
                if (index > 0)
                    await Task.Delay(StartAllDelay);

                MinerRemotePipeResponse? response = await idle[index].SendAsync("start");
                if (response?.Success == true)
                    started++;
                else
                    errors.Add(response?.Message ?? $"{idle[index].Profile.Name}: brak odpowiedzi.");
            }

            string text = $"Uruchomiono kont: {started}/{expected}.";
            if (errors.Count > 0)
                text += Environment.NewLine + string.Join(Environment.NewLine, errors);
            ShowNotice(text, errors.Count == 0 ? GlassPalette.Ok : GlassPalette.Warn);
            await RefreshStatusAsync();
        }
        finally
        {
            _bulkActionRunning = false;
        }
    }

    private async Task StopAllAsync()
    {
        AccountCard[] running = _cardByProfile.Values.Where(card => card.ProcessRunning).ToArray();
        if (running.Length == 0)
        {
            ShowNotice("Żaden Kopacz nie jest uruchomiony.", GlassPalette.Muted);
            return;
        }

        await Task.WhenAll(running.Select(card => card.SendAsync("stop")));
        ShowNotice("Zatrzymano wszystkie konta.", GlassPalette.Ok);
        await RefreshStatusAsync();
    }

    private void AddProfile()
    {
        // Nazwę liczymy na świeżej liście z pliku — inny proces mógł właśnie dodać konto.
        _profiles = _store.Update(profiles =>
        {
            int number = 2;
            var names = new HashSet<string>(profiles.Select(profile => profile.Name), StringComparer.OrdinalIgnoreCase);
            while (names.Contains($"Konto {number}"))
                number++;

            profiles.Add(new MinerAccountProfile
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Name = $"Konto {number}"
            });
        }).ToList();
        AppLog.Info("Konta", $"Dodano konto „{_profiles[^1].Name}”.");
        RenderCards();
    }

    internal async Task RenameProfileAsync(AccountCard card)
    {
        var dialog = new AccountNameWindow(card.Profile.Name) { Owner = HostWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.AccountName))
            return;

        string newName = dialog.AccountName.Trim();
        string profileId = card.Profile.Id;
        _profiles = _store.Update(profiles =>
        {
            foreach (MinerAccountProfile profile in profiles.Where(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase)))
                profile.Name = newName;
        }).ToList();
        card.Profile.Name = newName;
        AppLog.Info("Konta", $"Zmieniono nazwę konta {profileId} na „{newName}”.");
        // A running Kopacz reads the new name from accounts.json.
        if (card.ProcessRunning)
            await card.SendAsync("reload");
        RenderCards();
        await RefreshStatusAsync();
    }

    internal async Task OpenProfileAsync(AccountCard card)
    {
        if (IsCurrentProfile(card.Profile) && HostWindow is MainWindow current)
        {
            current.OpenAccountConfiguration();
            return;
        }
        if (!await EnsureProfileReadyAsync(card))
            return;
        try
        {
            await RefreshWindowsAsync(assignFreeWindows: true, announce: false);
            MinerWorkspaceResponse response = await MinerWorkspaceClient.SelectAsync(card.Profile.Id);
            if (!response.Success)
                ShowNotice(response.Message, GlassPalette.Bad);
        }
        catch (Exception ex)
        {
            card.SetState(ex.GetBaseException().Message, GlassPalette.Bad);
        }
    }

    internal async Task ToggleMiningAsync(AccountCard card)
    {
        if (!await EnsureProfileReadyAsync(card))
            return;
        await RefreshWindowsAsync(assignFreeWindows: true, announce: false);

        string command = card.Status?.Running == true ? "stop" : "start";
        MinerRemotePipeResponse? response = await card.SendAsync(command);
        await card.RefreshAsync(_liveEnabled.IsChecked == true);
        if (response != null)
            ShowNotice(response.Message, response.Success ? GlassPalette.Ok : GlassPalette.Warn);
    }

    // Kolejno, bo każde konto zajmuje wolne miejsce w układzie przez wspólną blokadę slotów.
    private async Task ArrangeAllAsync()
    {
        if (_bulkActionRunning)
            return;
        _bulkActionRunning = true;
        try
        {
            AccountCard[] ready = _cardByProfile.Values.Where(card => card.ProcessRunning && card.Status?.WindowAvailable == true).ToArray();
            if (ready.Length == 0)
            {
                ShowNotice("Brak kont z przypisanym oknem Minecrafta.", GlassPalette.Warn);
                return;
            }

            int arranged = 0;
            var refused = new List<string>();
            foreach (AccountCard card in ready)
            {
                MinerRemotePipeResponse? response = await card.SendAsync("arrange");
                if (response?.Success == true)
                    arranged++;
                else
                    refused.Add(response?.Message ?? $"{card.Profile.Name}: nie odpowiada.");
            }

            string text = $"Ułożono okna: {arranged} z {ready.Length}.";
            if (refused.Count > 0)
                text += " " + string.Join(" ", refused);
            ShowNotice(text, refused.Count == 0 ? GlassPalette.Ok : GlassPalette.Warn);
            await RefreshStatusAsync();
        }
        finally
        {
            _bulkActionRunning = false;
        }
    }

    internal async Task ArrangeWindowAsync(AccountCard card)
    {
        if (!await EnsureProfileReadyAsync(card))
            return;
        await RefreshWindowsAsync(assignFreeWindows: true, announce: false);
        MinerRemotePipeResponse? response = await card.SendAsync("arrange");
        if (response == null)
            ShowNotice("Kopacz tego konta nie odpowiada.", GlassPalette.Bad);
        else
            ShowNotice(response.Message, response.Success ? GlassPalette.Ok : GlassPalette.Warn);
        await card.RefreshAsync(_liveEnabled.IsChecked == true);
    }

    internal async Task AssignWindowAsync(AccountCard card, int processId)
    {
        if (!await EnsureProfileReadyAsync(card))
            return;
        MinerRemotePipeResponse? response = await card.SendAsync($"window {processId}");
        if (response != null)
            ShowNotice(response.Message, response.Success ? GlassPalette.Ok : GlassPalette.Warn);
        await card.RefreshAsync(_liveEnabled.IsChecked == true);
        card.UpdateWindowChoices(_gameWindows, BuildWindowOwners(), force: true);
    }

    private async Task<bool> EnsureProfileReadyAsync(AccountCard card)
    {
        if (card.ProcessRunning)
            return true;
        card.SetState("Przygotowanie konta…", GlassPalette.Info);
        try
        {
            MinerWorkspaceResponse response = await MinerWorkspaceClient.EnsureAsync(card.Profile.Id);
            if (!response.Success)
            {
                ShowNotice(response.Message, GlassPalette.Bad);
                return false;
            }
            card.AssignWindowWhenReady = true;
            await card.RefreshAsync(includePreview: false);
            return card.ProcessRunning;
        }
        catch (Exception ex)
        {
            ShowNotice("Nie udało się przygotować konta: " + ex.GetBaseException().Message, GlassPalette.Bad);
            return false;
        }
    }

    internal async Task RemoveProfileAsync(AccountCard card)
    {
        await card.RefreshAsync(includePreview: false);
        if (IsCurrentProfile(card.Profile))
        {
            card.SetState("Wybierz inne konto przed usunięciem tego profilu.", GlassPalette.Bad);
            return;
        }
        if (card.Status?.Running == true)
        {
            card.SetState("Zatrzymaj kopanie na tym koncie przed usunięciem profilu.", GlassPalette.Bad);
            return;
        }

        if (!GlassMessageDialog.Confirm(HostWindow, "Usuń konto",
                $"Usunąć profil „{card.Profile.Name}” razem z jego ustawieniami i logami? Tego nie da się cofnąć.", "Usuń konto"))
            return;

        if (card.ProcessRunning)
        {
            await card.SendAsync("workspace-close");
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                await card.RefreshAsync(includePreview: false);
                if (!card.ProcessRunning)
                    break;
            }
            if (card.ProcessRunning)
            {
                ShowNotice("Silnik konta nie zakończył jeszcze pracy. Spróbuj usunąć profil ponownie.", GlassPalette.Warn);
                return;
            }
        }

        string removedId = card.Profile.Id;
        _profiles = _store.Update(profiles => profiles.RemoveAll(profile => string.Equals(profile.Id, removedId, StringComparison.OrdinalIgnoreCase))).ToList();
        AppLog.Info("Konta", $"Usunięto konto „{card.Profile.Name}” ({removedId}).");
        try
        {
            MinerAccountStore.DeleteProfileData(card.Profile.Id);
        }
        catch (Exception ex)
        {
            ShowNotice("Profil usunięty, ale nie udało się skasować jego plików: " + ex.Message, GlassPalette.Warn);
        }

        RenderCards();
    }

    internal void OpenPreview(AccountCard card)
    {
        if (!card.ProcessRunning)
            return;

        var window = new MinerPreviewWindow(card.Profile, new IntPtr(card.Status?.GameWindowHandle ?? 0)) { Owner = HostWindow };
        window.Show();
    }

    internal bool IsCurrentProfile(MinerAccountProfile profile) =>
        string.Equals(profile.Id, _currentProfileId, StringComparison.OrdinalIgnoreCase);

    internal bool LiveEnabled => _liveEnabled.IsChecked == true;

    internal static Button CreateButton(string text, string? styleKey, string? icon = null, bool filledIcon = false)
    {
        var button = new Button { Content = text };
        if (styleKey != null)
            button.Style = GlassChrome.GetStyle(styleKey);
        if (icon == null)
            return button;

        var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(icon), Style = GlassChrome.GetStyle("GlassIconPath") };
        if (filledIcon)
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding("Stroke") { RelativeSource = System.Windows.Data.RelativeSource.Self });
        var iconHost = new Grid { Width = 24, Height = 24 };
        iconHost.Children.Add(path);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Viewbox { Width = 15, Height = 15, Margin = new Thickness(0, 0, 7, 0), Child = iconHost });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        return button;
    }
}

internal sealed record GameWindowOption(int ProcessId, string ProcessName, string Title)
{
    // Programy, które często mają „Minecraft” w tytule (otwarte pliki, karty), ale nie są grą.
    private static readonly HashSet<string> NonGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "devenv", "rider64", "idea64", "notepad", "notepad++", "sublime_text",
        "explorer", "chrome", "msedge", "firefox", "opera", "brave", "Discord", "Spotify",
        "WindowsTerminal", "powershell", "pwsh", "cmd", "obs64", "Photoshop"
    };

    // Minecraft clients (javaw/java) and windows titled "Minecraft", without launchers,
    // without Minecraft Helper itself and without editors/browsers showing such titles.
    public static List<GameWindowOption> FindAll()
    {
        var result = new List<GameWindowOption>();
        int ownProcessId = Environment.ProcessId;
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == ownProcessId || process.MainWindowHandle == IntPtr.Zero)
                    continue;

                string name = process.ProcessName ?? string.Empty;
                string title = (process.MainWindowTitle ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(title)
                    || name.StartsWith("MinecraftHelper", StringComparison.OrdinalIgnoreCase)
                    || title.Contains("Launcher", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool javaClient = name.Equals("javaw", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("java", StringComparison.OrdinalIgnoreCase);
                bool minecraftTitle = title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase)
                    && !title.Contains("Minecraft Helper", StringComparison.OrdinalIgnoreCase)
                    && !title.Contains("MinecraftHelper", StringComparison.OrdinalIgnoreCase)
                    && !NonGameProcesses.Contains(name);
                if (javaClient || minecraftTitle)
                    result.Add(new GameWindowOption(process.Id, name, title));
            }
            catch
            {
                // Processes that cannot be inspected are skipped.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

internal sealed class AccountCard
{
    private static readonly (Color From, Color To)[] AvatarGradients =
    {
        (Color.FromRgb(0x54, 0x58, 0xE8), Color.FromRgb(0x93, 0x33, 0xEA)),
        (Color.FromRgb(0xEA, 0x58, 0x0C), Color.FromRgb(0xDC, 0x26, 0x26)),
        (Color.FromRgb(0x03, 0x69, 0xA1), Color.FromRgb(0x0F, 0x76, 0x6E)),
        (Color.FromRgb(0x47, 0x55, 0x69), Color.FromRgb(0x64, 0x74, 0x8B))
    };

    private readonly MultiAccountPanel _owner;
    private readonly TextBlock _state;
    private readonly Border _statePill;
    private readonly TextBlock _windowLine;
    private readonly TextBlock _time = MiniValue(mono: true);
    private readonly TextBlock _eq = MiniValue();
    private readonly TextBlock _cobbleX = MiniValue();
    private readonly TextBlock _discarded = MiniValue();
    private readonly Border _hint;
    private readonly Image _preview;
    private readonly TextBlock _previewEmpty;
    private readonly Border _previewSurface;
    private readonly Border _liveBadge;
    private readonly TextBlock _liveBadgeText;
    private readonly ComboBox _window;
    private readonly Button _open;
    private readonly Button _arrange;
    private readonly Button _toggle;
    private string _windowSignature = string.Empty;
    private bool _updatingWindowChoices;
    private bool _toggleBusy;
    private bool _peekBusy;
    private LivePreviewStream? _live;
    private bool _hasSnapshot;
    private DateTime? _assignWindowRequestedAtUtc;

    public AccountCard(MinerAccountProfile profile, MultiAccountPanel owner, int index)
    {
        Profile = profile;
        _owner = owner;

        // Podgląd Live
        _preview = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
        _previewEmpty = new TextBlock
        {
            Text = "Brak podglądu",
            Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(16)
        };
        _liveBadgeText = new TextBlock { Text = "LIVE", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold, Margin = new Thickness(6, 0, 0, 0) };
        var liveRow = new StackPanel { Orientation = Orientation.Horizontal };
        liveRow.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F)), VerticalAlignment = VerticalAlignment.Center });
        liveRow.Children.Add(_liveBadgeText);
        _liveBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x16, 0x1A, 0x2B)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = liveRow,
            Visibility = Visibility.Collapsed
        };
        var enlarge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x16, 0x1A, 0x2B)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.Hand,
            ToolTip = "Powiększony podgląd tego konta (odświeżany na bieżąco)",
            Child = new TextBlock { Text = "⤢ Powiększ", Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.Bold }
        };
        enlarge.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _owner.OpenPreview(this);
        };
        var previewGrid = new Grid();
        previewGrid.Children.Add(_previewEmpty);
        previewGrid.Children.Add(_preview);
        previewGrid.Children.Add(_liveBadge);
        previewGrid.Children.Add(enlarge);
        _previewSurface = new Border
        {
            Height = 172,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x30)),
            CornerRadius = new CornerRadius(18, 18, 0, 0),
            ClipToBounds = true,
            Cursor = Cursors.Hand,
            ToolTip = "Kliknij, aby podejrzeć to konto",
            Child = previewGrid
        };
        // Bez Live klik robi jedno zdjęcie tylko tego konta zamiast odświeżania wszystkich.
        _previewSurface.MouseLeftButtonUp += async (_, __) =>
        {
            if (_owner.LiveEnabled)
                _owner.OpenPreview(this);
            else
                await PeekAsync();
        };
        Border previewSurface = _previewSurface;

        // Nagłówek karty
        (Color from, Color to) = AvatarGradients[index % AvatarGradients.Length];
        var avatar = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(from, to, 45),
            Child = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(profile.Name) ? "?" : profile.Name.Trim()[..1].ToUpperInvariant(),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _state = new TextBlock { Text = "Sprawdzanie…", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _statePill = new Border { Style = GlassChrome.GetStyle("GlassPill"), Child = _state, MaxWidth = 150 };
        _windowLine = new TextBlock
        {
            Text = profile.Id,
            FontFamily = (FontFamily)Application.Current.FindResource("GlassMonoFont"),
            FontSize = 11,
            Foreground = GlassPalette.Muted,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var names = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = profile.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 });
        if (owner.IsCurrentProfile(profile))
        {
            nameRow.Children.Add(new Border
            {
                Style = GlassChrome.GetStyle("GlassPill"),
                Background = GlassPalette.AccentSoft,
                Height = 20,
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = "wybrane", Foreground = GlassPalette.AccentInk, FontSize = 10, VerticalAlignment = VerticalAlignment.Center }
            });
        }
        names.Children.Add(nameRow);
        names.Children.Add(_windowLine);
        var head = new DockPanel();
        DockPanel.SetDock(avatar, Dock.Left);
        head.Children.Add(avatar);
        DockPanel.SetDock(_statePill, Dock.Right);
        head.Children.Add(_statePill);
        head.Children.Add(names);

        // Mini statystyki
        var stats = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(-4, 14, -4, 0) };
        stats.Children.Add(MiniTile(_time, "czas"));
        stats.Children.Add(MiniTile(_eq, "EQ"));
        stats.Children.Add(MiniTile(_cobbleX, "CobbleX"));
        stats.Children.Add(MiniTile(_discarded, "wyrzucone"));

        _hint = new Border
        {
            Style = GlassChrome.GetStyle("GlassWarnNote"),
            Margin = new Thickness(0, 12, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "„Kopanie w tle” jest wyłączone — START działa tylko z oknem gry na wierzchu.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5
            }
        };

        _window = new ComboBox { IsEnabled = false, ToolTip = "Okno Minecrafta przypisane do konta" };
        _window.SelectionChanged += async (_, __) => await OnWindowSelectedAsync();
        _window.DropDownOpened += async (_, __) =>
        {
            await _owner.RefreshWindowListAsync();
            _windowSignature = string.Empty;
        };

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        _toggle = MultiAccountPanel.CreateButton("START", "GlassPrimaryButton");
        _toggle.MinWidth = 86;
        _toggle.Click += async (_, __) =>
        {
            if (_toggleBusy)
                return;
            _toggleBusy = true;
            try { await _owner.ToggleMiningAsync(this); }
            finally { _toggleBusy = false; }
        };
        _arrange = MultiAccountPanel.CreateButton("Kalibruj okno", null, "M4,8 V4 H8 M16,4 H20 V8 M20,16 V20 H16 M8,20 H4 V16 M9,12 H15 M12,9 V15");
        _arrange.ToolTip = "Ustawia okno Minecrafta tego konta (854×480) obok okien pozostałych kont.";
        ToolTipService.SetShowOnDisabled(_arrange, true);
        _arrange.Click += async (_, __) => await _owner.ArrangeWindowAsync(this);
        _open = CreateIconButton(SettingsIcon, "Wybierz konto");
        _open.Click += async (_, __) => await _owner.OpenProfileAsync(this);

        // Nazwa i Usuń rzadko potrzebne — w menu ⋯, żeby nie klikać ich przypadkiem.
        Button more = CreateIconButton(null, "Więcej: nazwa, usuń");
        more.Content = new TextBlock { Text = "⋯", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, -4, 0, 0) };
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Zmień nazwę" };
        rename.Click += async (_, __) => await _owner.RenameProfileAsync(this);
        menu.Items.Add(rename);
        if (!string.Equals(profile.Id, MinerProfileId.Default, StringComparison.OrdinalIgnoreCase))
        {
            var remove = new MenuItem { Header = "Usuń konto", Foreground = GlassPalette.Bad };
            remove.Click += async (_, __) => await _owner.RemoveProfileAsync(this);
            menu.Items.Add(remove);
        }
        more.ContextMenu = menu;
        more.Click += (_, __) =>
        {
            menu.PlacementTarget = more;
            menu.IsOpen = true;
        };

        buttons.Children.Add(_toggle);
        buttons.Children.Add(_arrange);
        buttons.Children.Add(_open);
        buttons.Children.Add(more);
        foreach (UIElement button in buttons.Children)
            ((Button)button).Margin = new Thickness(0, 0, 6, 0);

        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        body.Children.Add(head);
        body.Children.Add(stats);
        body.Children.Add(_hint);
        body.Children.Add(new TextBlock { Text = "Okno Minecrafta", Style = GlassChrome.GetStyle("GlassLabel"), Margin = new Thickness(0, 12, 0, 4) });
        body.Children.Add(_window);
        body.Children.Add(buttons);

        var layout = new StackPanel();
        layout.Children.Add(previewSurface);
        layout.Children.Add(body);

        Root = new Border
        {
            Style = GlassChrome.GetStyle("GlassCard"),
            Width = 360,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            Margin = new Thickness(0, 0, 18, 18),
            Child = layout
        };
    }

    public MinerAccountProfile Profile { get; }

    public Border Root { get; }

    public MinerRemoteStatus? Status { get; private set; }

    public bool ProcessRunning { get; private set; }

    // Set after launching from the panel; expires if the process never answers.
    public bool AssignWindowWhenReady
    {
        get => _assignWindowRequestedAtUtc != null;
        set => _assignWindowRequestedAtUtc = value ? DateTime.UtcNow : null;
    }

    // Ikona ustawień (suwaki) — ta sama geometria co IcoSliders w zasobach MainWindow.
    private const string SettingsIcon = "M4,6 H13 M17,6 H20 M4,12 H7 M11,12 H20 M4,18 H15 M19,18 H20 M17,6 A2,2 0 1 1 13,6 A2,2 0 1 1 17,6 Z M11,12 A2,2 0 1 1 7,12 A2,2 0 1 1 11,12 Z M19,18 A2,2 0 1 1 15,18 A2,2 0 1 1 19,18 Z";

    internal static Button CreateIconButton(string? icon, string toolTip)
    {
        var button = new Button { Style = GlassChrome.GetStyle("GlassIconButton"), ToolTip = toolTip };
        System.Windows.Automation.AutomationProperties.SetName(button, toolTip);
        if (icon == null)
            return button;

        var iconHost = new Grid { Width = 24, Height = 24 };
        iconHost.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(icon),
            Style = GlassChrome.GetStyle("GlassIconPath")
        });
        button.Content = new Viewbox { Width = 16, Height = 16, Child = iconHost };
        return button;
    }

    private static TextBlock MiniValue(bool mono = false)
    {
        var value = new TextBlock { Text = "—", FontSize = mono ? 13 : 15, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (mono)
            value.FontFamily = (FontFamily)Application.Current.FindResource("GlassMonoFont");
        return value;
    }

    private static Border MiniTile(TextBlock value, string label)
    {
        var panel = new StackPanel();
        panel.Children.Add(value);
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = GlassPalette.Muted, HorizontalAlignment = HorizontalAlignment.Center });
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF)),
            BorderBrush = GlassPalette.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4, 8, 4, 7),
            Margin = new Thickness(4, 0, 4, 0),
            Child = panel
        };
    }

    public async Task<MinerRemotePipeResponse?> SendAsync(string command)
    {
        try
        {
            return await MinerRemoteControlClient.ExecuteAsync(Profile.Id, command);
        }
        catch
        {
            // Silnik konta nie działa albo nie odpowiada — karta pokaże stan „Nieuruchomiony”.
            return null;
        }
    }

    public async Task RefreshAsync(bool includePreview)
    {
        MinerRemotePipeResponse? response = await SendAsync("status");
        if (response?.Success != true || response.Status == null)
        {
            if (_assignWindowRequestedAtUtc != null && DateTime.UtcNow - _assignWindowRequestedAtUtc > TimeSpan.FromSeconds(30))
                AssignWindowWhenReady = false;
            bool wasStarting = AssignWindowWhenReady;
            ProcessRunning = false;
            Status = null;
            if (!wasStarting)
                SetState("Nieuruchomiony", GlassPalette.Muted);
            _time.Text = _eq.Text = _cobbleX.Text = _discarded.Text = "—";
            _windowLine.Text = "Gotowe do przygotowania";
            _hint.Visibility = Visibility.Collapsed;
            StopLive();
            _preview.Source = null;
            _hasSnapshot = false;
            _previewEmpty.Text = "Wybierz konto lub kliknij START";
            _liveBadge.Visibility = Visibility.Collapsed;
            _window.IsEnabled = true;
            _open.ToolTip = "Wybierz konto";
            _arrange.IsEnabled = true;
            _toggle.Content = "START";
            _toggle.Style = GlassChrome.GetStyle("GlassPrimaryButton");
            return;
        }

        ProcessRunning = true;
        Status = response.Status;
        MinerRemoteStatus status = response.Status;
        Brush stateBrush = status.WindowOwnedByAnotherProfile || !status.WindowAvailable
            ? GlassPalette.Bad
            : status.Running
                ? status.BackgroundActive || status.GameFocused ? GlassPalette.Ok : GlassPalette.Warn
                : GlassPalette.Info;
        SetState(Capitalize(MinerRemoteCommandRouter.DescribeState(status)), stateBrush);
        _time.Text = status.Running ? MinerRemoteCommandRouter.FormatDuration(status.RuntimeSeconds) : "—";
        _eq.Text = status.InventoryScans.ToString();
        _cobbleX.Text = status.CobbleXCreated.ToString();
        _discarded.Text = status.DiscardedItems.ToString();
        _windowLine.Text = status.GameProcessId > 0 ? $"okno · PID {status.GameProcessId}" : "brak okna gry";
        _hint.Visibility = status.BackgroundMiningEnabled || status.Running ? Visibility.Collapsed : Visibility.Visible;
        _window.IsEnabled = !status.Running;
        _window.ToolTip = status.Running ? "Zatrzymaj kopanie, aby zmienić okno." : "Okno Minecrafta przypisane do konta";
        _open.ToolTip = _owner.IsCurrentProfile(Profile) ? "Ustawienia konta" : "Wybierz konto i otwórz jego ustawienia";
        _arrange.IsEnabled = !status.Running;
        _arrange.ToolTip = status.Running
            ? "Zatrzymaj kopanie, aby skalibrować okno."
            : "Ustawia okno Minecrafta tego konta (854×480) obok okien pozostałych kont.";
        _toggle.Content = status.Running ? "STOP" : "START";
        _toggle.Style = GlassChrome.GetStyle(status.Running ? "GlassDangerButton" : "GlassPrimaryButton");

        _previewSurface.ToolTip = includePreview ? "Kliknij, aby powiększyć podgląd" : "Kliknij, aby podejrzeć to konto (jedno zdjęcie)";

        if (!status.WindowAvailable)
        {
            StopLive();
            _preview.Source = null;
            _hasSnapshot = false;
            _previewEmpty.Text = "Brak wybranego okna";
            _liveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        if (!includePreview)
        {
            StopLive();
            // Zdjęcie z kliknięcia zostaje (z godziną na plakietce) do następnego kliknięcia.
            if (_hasSnapshot || _peekBusy)
                return;
            _preview.Source = null;
            _previewEmpty.Text = "Kliknij, aby podejrzeć";
            _liveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        _hasSnapshot = false;
        var window = new IntPtr(status.GameWindowHandle);
        if (_live == null || _live.Window != window)
        {
            StopLive();
            _live = new LivePreviewStream(_preview, window, _owner.LiveFps, maxFrameWidth: 480);
            _live.AvailabilityChanged += available =>
            {
                _preview.Visibility = available ? Visibility.Visible : Visibility.Hidden;
                _previewEmpty.Text = available ? string.Empty : "Obraz okna niedostępny (zminimalizowane?)";
            };
        }
        ApplyLiveFps(_owner.LiveFps);
    }

    public void ApplyLiveFps(int fps)
    {
        if (_live == null)
            return;
        _live.Fps = fps;
        _liveBadgeText.Text = $"LIVE · {fps} kl./s";
        _liveBadge.Visibility = Visibility.Visible;
    }

    public void StopLive()
    {
        if (_live == null)
            return;
        _live.Dispose();
        _live = null;
        _preview.Source = null;
        _preview.Visibility = Visibility.Visible;
        _liveBadge.Visibility = Visibility.Collapsed;
    }

    // Jedno zdjęcie okna tego konta na żądanie, bez włączania Live dla wszystkich.
    private async Task PeekAsync()
    {
        if (_peekBusy || !ProcessRunning || Status?.WindowAvailable != true)
            return;

        _peekBusy = true;
        _previewEmpty.Text = "Pobieranie podglądu…";
        try
        {
            var window = new IntPtr(Status.GameWindowHandle);
            BitmapSource? snapshot = await Task.Run(() => LivePreviewStream.CaptureOnce(window));
            if (snapshot != null)
            {
                _preview.Source = snapshot;
                _hasSnapshot = true;
                _liveBadgeText.Text = $"ZDJĘCIE · {DateTime.Now:HH:mm:ss}";
                _liveBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _preview.Source = null;
                _hasSnapshot = false;
                _previewEmpty.Text = "Obraz okna niedostępny (zminimalizowane?)";
                _liveBadge.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _peekBusy = false;
        }
    }

    public void SetState(string text, Brush brush)
    {
        _state.Text = text;
        _state.Foreground = brush;
        _statePill.Background = GlassPalette.SoftFor(brush);
    }

    public void UpdateWindowChoices(IReadOnlyList<GameWindowOption> windows, IReadOnlyDictionary<int, string> owners, bool force = false)
    {
        if (_window.IsDropDownOpen)
            return;

        int selectedProcessId = Status?.GameProcessId ?? 0;
        var options = windows.ToList();
        if (selectedProcessId > 0 && options.All(window => window.ProcessId != selectedProcessId))
            options.Insert(0, new GameWindowOption(selectedProcessId, string.Empty, Status?.WindowTitle ?? $"Proces {selectedProcessId}"));

        string signature = selectedProcessId + "|" + string.Join(";", options.Select(window =>
            $"{window.ProcessId}:{window.Title}:{(owners.TryGetValue(window.ProcessId, out string? owner) ? owner : string.Empty)}"));
        if (!force && signature == _windowSignature)
            return;

        _windowSignature = signature;
        _updatingWindowChoices = true;
        try
        {
            _window.Items.Clear();
            foreach (GameWindowOption option in options)
            {
                string label = $"{option.Title} [{option.ProcessId}]";
                bool ownedByOther = owners.TryGetValue(option.ProcessId, out string? ownerName)
                    && option.ProcessId != selectedProcessId;
                if (ownedByOther)
                    label += $" — zajęte: {ownerName}";

                _window.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = option.ProcessId,
                    IsEnabled = !ownedByOther
                });
            }

            _window.SelectedIndex = options.FindIndex(option => option.ProcessId == selectedProcessId);
        }
        finally
        {
            _updatingWindowChoices = false;
        }
    }

    private async Task OnWindowSelectedAsync()
    {
        if (_updatingWindowChoices || _window.SelectedItem is not ComboBoxItem { Tag: int processId })
            return;
        if (processId == (Status?.GameProcessId ?? 0))
            return;

        await _owner.AssignWindowAsync(this, processId);
    }

    private static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text[1..];
}

// Powiększony podgląd jednego konta: strumień 15–60 kl./s przechwytywany w tym procesie.
internal sealed class MinerPreviewWindow : Window
{
    private static readonly (string Label, MinerPreviewRegion Region)[] Regions =
    {
        ("Cały ekran", MinerPreviewRegion.Full),
        ("Hotbar i jedzenie", MinerPreviewRegion.Hotbar),
        ("Ekwipunek", MinerPreviewRegion.Inventory)
    };

    private static readonly int[] FpsOptions = { 15, 30, 60 };

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _empty = new()
    {
        Text = "Łączenie z oknem gry…",
        Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _caption = new() { Foreground = GlassPalette.Muted, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _captionTimer;
    private readonly LivePreviewStream? _stream;
    private readonly string _profileId;
    private readonly DispatcherTimer _inventoryTimer;
    private bool _inventoryMode;
    private bool _inventoryBusy;

    public MinerPreviewWindow(MinerAccountProfile profile, IntPtr gameWindow)
    {
        _profileId = profile.Id;
        Title = $"Podgląd Live — {profile.Name}";
        Width = 980;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        var regions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((string label, MinerPreviewRegion region) in Regions)
        {
            var option = new RadioButton
            {
                Content = label,
                GroupName = "PreviewRegion" + profile.Id,
                Style = GlassChrome.GetStyle("GlassSegment"),
                IsChecked = region == MinerPreviewRegion.Full
            };
            option.Checked += async (_, __) => await SelectRegionAsync(region);
            regions.Children.Add(option);
        }

        var rates = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int fps in FpsOptions)
        {
            var option = new RadioButton
            {
                Content = $"{fps} kl./s",
                GroupName = "PreviewFps" + profile.Id,
                Style = GlassChrome.GetStyle("GlassSegment"),
                IsChecked = fps == 30
            };
            option.Checked += (_, __) =>
            {
                if (_stream != null)
                    _stream.Fps = fps;
            };
            rates.Children.Add(option);
        }

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var ratesHost = new Border { Style = GlassChrome.GetStyle("GlassSegmentHost"), Child = rates };
        DockPanel.SetDock(ratesHost, Dock.Right);
        top.Children.Add(ratesHost);
        top.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassSegmentHost"), Child = regions, HorizontalAlignment = HorizontalAlignment.Left });

        var view = new Grid();
        view.Children.Add(_empty);
        view.Children.Add(_image);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_caption, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(_caption);
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x30)),
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Child = view
        });
        Content = root;

        if (gameWindow != IntPtr.Zero)
        {
            _stream = new LivePreviewStream(_image, gameWindow, fps: 30);
            _stream.AvailabilityChanged += available =>
            {
                _empty.Text = available ? string.Empty : "Obraz okna niedostępny — gra jest zminimalizowana albo zamknięta.";
                _image.Visibility = available ? Visibility.Visible : Visibility.Hidden;
            };
        }
        else
        {
            _empty.Text = "To konto nie ma przypisanego okna Minecrafta.";
        }

        // EQ jest otwierane tylko na chwilę podczas Auto EQ — pokazujemy ostatni zrzut z konta.
        _inventoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _inventoryTimer.Tick += async (_, __) => await RefreshInventorySnapshotAsync();

        _captionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _captionTimer.Tick += (_, __) => UpdateCaption();
        _captionTimer.Start();
        UpdateCaption();
        Closed += (_, __) =>
        {
            _captionTimer.Stop();
            _inventoryTimer.Stop();
            _stream?.Dispose();
        };
    }

    private async Task SelectRegionAsync(MinerPreviewRegion region)
    {
        _inventoryMode = region == MinerPreviewRegion.Inventory;
        if (_stream != null)
        {
            _stream.Paused = _inventoryMode;
            if (!_inventoryMode)
                _stream.Region = region;
        }

        if (_inventoryMode)
        {
            _inventoryTimer.Start();
            await RefreshInventorySnapshotAsync();
        }
        else
        {
            _inventoryTimer.Stop();
            UpdateCaption();
        }
    }

    // Konto zwraca żywy wycinek, gdy EQ jest akurat otwarte, a w przeciwnym razie ostatni skan EQ.
    private async Task RefreshInventorySnapshotAsync()
    {
        if (!_inventoryMode || _inventoryBusy)
            return;

        _inventoryBusy = true;
        try
        {
            MinerRemotePipeResponse response = await MinerRemoteControlClient.ExecuteAsync(_profileId, "ekwipunek");
            if (!_inventoryMode)
                return;
            _caption.Text = response.Message;
            if (response.Success && !string.IsNullOrWhiteSpace(response.AttachmentBase64))
            {
                string base64 = response.AttachmentBase64;
                _image.Source = await Task.Run(() => DecodeJpeg(base64));
                _image.Visibility = Visibility.Visible;
                _empty.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            // Wołane z kliknięcia i timera — żaden błąd kanału nie może zamknąć programu.
            AppLog.Warn("Podgląd", "Nie udało się pobrać zrzutu EQ: " + ex.GetBaseException().Message);
            _caption.Text = "Kopacz tego konta nie odpowiada — nie można pobrać zrzutu EQ.";
        }
        finally
        {
            _inventoryBusy = false;
        }
    }

    private static BitmapImage DecodeJpeg(string base64)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(base64));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void UpdateCaption()
    {
        if (_inventoryMode)
            return;
        _caption.Text = _stream == null
            ? "Brak okna gry."
            : $"Na żywo · {_stream.MeasuredFps} kl./s · {_stream.FrameWidth}×{_stream.FrameHeight} px · obraz pobierany bezpośrednio z okna gry (działa też, gdy jest zasłonięte albo na innym pulpicie).";
    }
}

internal sealed class AccountNameWindow : Window
{
    private readonly TextBox _name;

    public AccountNameWindow(string currentName)
    {
        Title = "Nazwa konta";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        _name = new TextBox
        {
            Text = currentName,
            Margin = new Thickness(0, 6, 0, 16)
        };
        var ok = new Button { Content = "Zapisz", MinWidth = 90, IsDefault = true, Style = GlassChrome.GetStyle("GlassPrimaryButton") };
        ok.Click += (_, __) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "Anuluj", MinWidth = 90, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Nazwa wyświetlana", Style = GlassChrome.GetStyle("GlassLabel") });
        panel.Children.Add(_name);
        panel.Children.Add(buttons);
        Content = new Border { Style = GlassChrome.GetStyle("GlassCard"), Margin = new Thickness(16), Child = panel };
        Loaded += (_, __) => { _name.Focus(); _name.SelectAll(); };
    }

    public string AccountName => _name.Text;
}
