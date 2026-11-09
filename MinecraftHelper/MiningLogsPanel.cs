using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading.Tasks;
using System.Windows.Threading;
using MinecraftHelper.Models;
using MinecraftHelper.Services;
using Microsoft.Win32;

namespace MinecraftHelper
{
    /// <summary>
    /// Logi w osobnym oknie, w stylu konsoli: po lewej lista sesji i zdarzeń, po prawej szczegóły
    /// klikniętego wpisu. Logi innych kont są czytane bez zapisu — ich Kopacze mogą właśnie pracować.
    /// </summary>
    internal sealed class MiningLogsPanel : UserControl
    {
        private enum LogTone { Ok, Warn, Bad, Muted }

        // Pole listy jest ciemne w obu motywach, tak samo jak w konsoli zdarzeń.
        private static readonly Brush ConsoleBackground = Frozen(0x1A, 0x1D, 0x2B);
        private static readonly Brush ConsoleTime = Frozen(0x8B, 0x91, 0xA8);
        private static readonly Brush ConsoleText = Frozen(0xEC, 0xEE, 0xF5);
        private static readonly Brush ConsoleInfo = Frozen(0x7D, 0xB8, 0xFF);
        private static readonly Brush ConsoleOk = Frozen(0x6E, 0xE7, 0xB7);
        private static readonly Brush ConsoleAuto = Frozen(0xB4, 0xB7, 0xFF);
        private static readonly Brush ConsoleWarn = Frozen(0xFB, 0xBF, 0x24);
        private static readonly Brush ConsoleBad = Frozen(0xFF, 0x6B, 0x6B);
        private static Style? _rowStyle;

        private readonly MiningLogService _ownLogService;
        private readonly string _currentProfileId;
        private readonly DispatcherTimer _accountsTimer;
        private readonly DispatcherTimer _refreshDebounce;
        private readonly WrapPanel _accountsHost = new();
        private readonly ListBox _list;
        private readonly TextBlock _emptyText;
        private readonly StackPanel _details = new();
        private readonly TextBlock _summary;
        private readonly HashSet<string> _expandedEntryKeys = new(StringComparer.Ordinal);
        // Wiersze jeszcze widoczne w trakcie animacji zwijania, według klucza sesji.
        private readonly Dictionary<string, List<LogRowElement>> _closingRows = new(StringComparer.Ordinal);
        private readonly Button _clearHistoryButton;
        private readonly Button _saveReportButton;
        private TextBox _searchBox = null!;
        private Button _clearSearchButton = null!;
        private MiningLogService _logService;
        private string _viewedProfileId;
        private string _accountsSignature = "";
        private string _totals = "";
        private string? _selectedKey;
        private bool _viewDirty = true;
        private bool _refreshingAccounts;
        private bool _rendering;
        private bool _resetScroll;
        private bool _problemsOnly;
        private IReadOnlyList<MiningLogEntry> _visibleEntries = Array.Empty<MiningLogEntry>();
        private IReadOnlyList<MiningHistoryGroup> _visibleGroups = Array.Empty<MiningHistoryGroup>();

        private sealed record MiningHistoryGroup(
            MiningLogEntry Root,
            IReadOnlyList<MiningLogEntry> Children,
            IReadOnlyList<MiningLogEntry> AllChildren,
            bool IsMiningRun);

        private const double LogRowHeight = 24;
        private const int MaxAnimatedRows = 40;
        private static readonly Duration RowAnimationDuration = new(TimeSpan.FromMilliseconds(170));
        private static readonly IEasingFunction RowEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        private sealed class LogRowElement : Grid
        {
            public required MiningLogEntry Entry { get; init; }
            public required string Key { get; init; }
            public required TextBlock Arrow { get; init; }
            public MiningHistoryGroup? Run { get; init; }
        }

        public MiningLogsPanel(MiningLogService logService, string currentProfileId)
        {
            _ownLogService = logService;
            _logService = logService;
            _currentProfileId = MinerProfileId.Normalize(currentProfileId);
            _viewedProfileId = _currentProfileId;

            _clearHistoryButton = new Button
            {
                Content = "Wyczyść historię",
                Style = GlassChrome.GetStyle("GlassDangerButton"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Usuwa historię kopania oglądanego konta. Trwająca sesja zostaje. Nie da się tego cofnąć."
            };
            _clearHistoryButton.Click += async (_, __) => await ClearHistoryAsync();
            _saveReportButton = new Button
            {
                Content = "Zapisz raport",
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Zapisuje aktualnie widoczne wpisy. Domyślne miejsce to Pulpit."
            };
            _saveReportButton.Click += (_, __) => SaveReport();

            _list = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = ConsoleText,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                SelectionMode = SelectionMode.Single,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemContainerStyle = GetRowStyle()
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
            _list.SelectionChanged += (_, __) =>
            {
                if (!_rendering)
                    ShowSelectedRow();
            };
            _list.PreviewKeyDown += List_PreviewKeyDown;

            _emptyText = new TextBlock
            {
                Foreground = ConsoleTime,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
                Visibility = Visibility.Collapsed
            };
            _summary = new TextBlock { Style = GlassChrome.GetStyle("GlassSub"), Margin = new Thickness(2, 10, 2, 0), TextTrimming = TextTrimming.CharacterEllipsis };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(BuildHeader());
            UIElement toolbar = BuildToolbar();
            Grid.SetRow(toolbar, 1);
            root.Children.Add(toolbar);
            UIElement content = BuildContent();
            Grid.SetRow(content, 2);
            root.Children.Add(content);
            Grid.SetRow(_summary, 3);
            root.Children.Add(_summary);
            Content = root;

            _accountsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _accountsTimer.Tick += async (_, __) => await RefreshAccountsAsync();
            _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _refreshDebounce.Tick += (_, __) =>
            {
                _refreshDebounce.Stop();
                if (_viewDirty && IsVisible)
                    RefreshView();
            };
            Loaded += async (_, __) =>
            {
                _ownLogService.Changed += LogService_Changed;
                if (_viewDirty)
                    RefreshView();
                _accountsTimer.Start();
                await RefreshAccountsAsync();
            };
            Unloaded += (_, __) =>
            {
                _ownLogService.Changed -= LogService_Changed;
                _accountsTimer.Stop();
                _refreshDebounce.Stop();
            };
            // Okno schowane razem z Kopaczem do zasobnika nadrabia zmiany po ponownym pokazaniu.
            IsVisibleChanged += (_, __) =>
            {
                if (IsVisible && IsLoaded && _viewDirty)
                    RefreshView();
            };
        }

        private Window? HostWindow => Window.GetWindow(this);

        private static Brush Frozen(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private UIElement BuildHeader()
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 2, 12) };
            header.Children.Add(new TextBlock { Text = "Logi", FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            header.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassSegmentHost"), Child = _accountsHost });
            return header;
        }

        private UIElement BuildToolbar()
        {
            var filters = new StackPanel { Orientation = Orientation.Horizontal };
            foreach ((string label, bool problems) in new[] { ("Wszystko", false), ("Tylko problemy", true) })
            {
                var filter = new RadioButton
                {
                    Content = label,
                    GroupName = "MiningLogsFilter",
                    Style = GlassChrome.GetStyle("GlassSegment"),
                    IsChecked = problems == _problemsOnly
                };
                filter.Checked += (_, __) =>
                {
                    _problemsOnly = problems;
                    RefreshEntries();
                };
                filters.Children.Add(filter);
            }

            _searchBox = new TextBox
            {
                ToolTip = "Wpisz datę, godzinę, tryb kopacza, status, nazwę przedmiotu, CobbleX albo dowolny fragment szczegółów."
            };
            var placeholder = new TextBlock
            {
                Text = "Szukaj: data, tryb, status, przedmiot…",
                Style = GlassChrome.GetStyle("GlassSub"),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(13, 0, 40, 0)
            };
            _clearSearchButton = new Button
            {
                Content = new TextBlock { Text = "✕", FontSize = 11 },
                Style = GlassChrome.GetStyle("GlassMiniIconButton"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Wyczyść wyszukiwanie",
                Visibility = Visibility.Collapsed
            };
            _clearSearchButton.Click += (_, __) =>
            {
                _searchBox.Clear();
                _searchBox.Focus();
            };
            _searchBox.TextChanged += (_, __) =>
            {
                bool empty = _searchBox.Text.Length == 0;
                placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
                _clearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
                RefreshEntries();
            };
            var search = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            search.Children.Add(_searchBox);
            search.Children.Add(placeholder);
            search.Children.Add(_clearSearchButton);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var filterHost = new Border { Style = GlassChrome.GetStyle("GlassSegmentHost"), Child = filters };
            grid.Children.Add(filterHost);
            Grid.SetColumn(search, 1);
            grid.Children.Add(search);
            Grid.SetColumn(_saveReportButton, 2);
            grid.Children.Add(_saveReportButton);
            Grid.SetColumn(_clearHistoryButton, 3);
            grid.Children.Add(_clearHistoryButton);
            return grid;
        }

        private UIElement BuildContent()
        {
            var listHost = new Grid();
            listHost.Children.Add(_list);
            listHost.Children.Add(_emptyText);
            var console = new Border
            {
                Background = ConsoleBackground,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(8, 10, 4, 10),
                Child = listHost
            };

            var details = new Border
            {
                Style = GlassChrome.GetStyle("GlassCard"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                Margin = new Thickness(12, 0, 0, 0),
                Child = new ScrollViewer
                {
                    Content = _details,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(332) });
            grid.Children.Add(console);
            Grid.SetColumn(details, 1);
            grid.Children.Add(details);
            return grid;
        }

        private async Task RefreshAccountsAsync()
        {
            if (_refreshingAccounts)
                return;

            _refreshingAccounts = true;
            try
            {
                List<MinerAccountProfile> profiles = new MinerAccountStore().Load().ToList();
                var rows = new List<(MinerAccountProfile Profile, string State, Brush Dot)>();
                foreach (MinerAccountProfile profile in profiles)
                {
                    MinerRemoteStatus? status = null;
                    try
                    {
                        MinerRemotePipeResponse response = await MinerRemoteControlClient.ExecuteAsync(profile.Id, "status");
                        if (response.Success)
                            status = response.Status;
                    }
                    catch
                    {
                        // Kopacz tego konta nie jest uruchomiony.
                    }
                    (string state, Brush dot) = DescribeAccount(status);
                    rows.Add((profile, state, dot));
                }

                string signature = string.Join("|", rows.Select(row => $"{row.Profile.Id}={row.Profile.Name}={row.State}"));
                if (signature == _accountsSignature)
                    return;
                _accountsSignature = signature;

                _accountsHost.Children.Clear();
                foreach ((MinerAccountProfile profile, string state, Brush dot) in rows)
                    _accountsHost.Children.Add(BuildAccountButton(profile, state, dot));
            }
            finally
            {
                _refreshingAccounts = false;
            }
        }

        private static (string State, Brush Dot) DescribeAccount(MinerRemoteStatus? status)
        {
            if (status == null)
                return ("zamknięty", GlassPalette.Muted);
            if (status.WindowOwnedByAnotherProfile || !status.WindowAvailable)
                return ("brak okna", GlassPalette.Bad);
            if (status.Running)
                return (status.BackgroundActive ? "kopie w tle" : "kopie", GlassPalette.Ok);
            return ("bezczynny", GlassPalette.Info);
        }

        private RadioButton BuildAccountButton(MinerAccountProfile profile, string state, Brush dot)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            content.Children.Add(new TextBlock { Text = profile.Name, VerticalAlignment = VerticalAlignment.Center });

            bool own = string.Equals(profile.Id, _currentProfileId, StringComparison.OrdinalIgnoreCase);
            var button = new RadioButton
            {
                Content = content,
                GroupName = "MiningLogsAccount",
                Style = GlassChrome.GetStyle("GlassSegment"),
                ToolTip = own ? $"To konto • {state}" : state,
                IsChecked = string.Equals(profile.Id, _viewedProfileId, StringComparison.OrdinalIgnoreCase)
            };
            string profileId = profile.Id;
            button.Checked += (_, __) => ViewProfile(profileId);
            return button;
        }

        private void ViewProfile(string profileId)
        {
            profileId = MinerProfileId.Normalize(profileId);
            if (string.Equals(profileId, _viewedProfileId, StringComparison.OrdinalIgnoreCase))
                return;

            _viewedProfileId = profileId;
            bool own = string.Equals(profileId, _currentProfileId, StringComparison.OrdinalIgnoreCase);
            _logService = own ? _ownLogService : MiningLogService.OpenReadOnly(profileId);
            _expandedEntryKeys.Clear();
            _selectedKey = null;
            _resetScroll = true;
            RefreshView();
        }

        // W trakcie kopania dziennik zmienia się co chwilę (EQ, CobbleX, reconnect) — kilka
        // zmian naraz daje jedno przebudowanie, a schowany panel tylko zapamiętuje, że jest nieaktualny.
        private void LogService_Changed(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => LogService_Changed(sender, e)));
                return;
            }

            _viewDirty = true;
            if (!IsVisible)
                return;
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }

        private void RefreshView()
        {
            _viewDirty = false;
            MiningLogSummary summary = _logService.GetSummary();
            int exactStacks = Math.Max(0, summary.DiscardedStacks - summary.LegacyDiscardedStacks);
            string legacy = summary.LegacyDiscardedStacks > 0 ? $" + {summary.LegacyDiscardedStacks:N0} starych stosów*" : "";
            string range = summary.FirstActivity.HasValue && summary.LastActivity.HasValue
                ? $"{summary.FirstActivity.Value.LocalDateTime:dd.MM.yyyy HH:mm} — {summary.LastActivity.Value.LocalDateTime:dd.MM.yyyy HH:mm}"
                : "brak danych";
            _totals = $"Razem: {summary.MiningRuns:N0} sesji, {summary.InventorySessions:N0} EQ, {summary.DiscardedItems:N0} szt. wyrzuconych ({exactStacks:N0} stos.{legacy}), {summary.CobbleXCreated:N0} CobbleX • Zakres: {range}";
            _summary.ToolTip = "Plik historii: " + _logService.LogsFilePath;
            RefreshEntries();
        }

        private void RefreshEntries()
        {
            IReadOnlyList<MiningLogEntry> allEntries = _logService.GetEntriesNewestFirst();
            string query = _searchBox?.Text?.Trim() ?? string.Empty;
            bool searching = !string.IsNullOrWhiteSpace(query);
            var runs = allEntries
                .Where(entry => entry.Kind == MiningLogKinds.MiningRun)
                .ToList();
            var runIds = runs
                .Where(run => !string.IsNullOrWhiteSpace(run.SessionId))
                .Select(run => run.SessionId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groups = new List<MiningHistoryGroup>();
            foreach (MiningLogEntry run in runs)
            {
                List<MiningLogEntry> allChildren = allEntries
                    .Where(entry => entry.Kind != MiningLogKinds.MiningRun
                        && !string.IsNullOrWhiteSpace(entry.MiningRunId)
                        && string.Equals(entry.MiningRunId, run.SessionId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(entry => entry.Timestamp)
                    .ToList();

                bool runMatches = !searching || EntryMatchesSearch(run, query);
                List<MiningLogEntry> visibleChildren = runMatches
                    ? allChildren
                    : allChildren.Where(entry => EntryMatchesSearch(entry, query)).ToList();
                bool runProblem = IsProblem(run);
                if (_problemsOnly && !runProblem)
                    visibleChildren = visibleChildren.Where(IsProblem).ToList();
                bool rootVisible = runMatches && (!_problemsOnly || runProblem);
                if (!rootVisible && visibleChildren.Count == 0)
                    continue;

                // Trafienie tylko w zdarzeniu ma być od razu widoczne, a nie schowane w sesji.
                if (visibleChildren.Count > 0 && !rootVisible)
                    _expandedEntryKeys.Add(GetEntryKey(run));
                groups.Add(new MiningHistoryGroup(run, visibleChildren, allChildren, IsMiningRun: true));
            }

            IEnumerable<MiningLogEntry> standaloneEntries = allEntries.Where(entry =>
                entry.Kind != MiningLogKinds.MiningRun
                && (string.IsNullOrWhiteSpace(entry.MiningRunId) || !runIds.Contains(entry.MiningRunId)));
            if (searching)
                standaloneEntries = standaloneEntries.Where(entry => EntryMatchesSearch(entry, query));
            if (_problemsOnly)
                standaloneEntries = standaloneEntries.Where(IsProblem);
            groups.AddRange(standaloneEntries.Select(entry =>
                new MiningHistoryGroup(entry, Array.Empty<MiningLogEntry>(), Array.Empty<MiningLogEntry>(), IsMiningRun: false)));

            _visibleGroups = groups
                .OrderByDescending(group => group.Root.Timestamp)
                .ToList();
            _visibleEntries = _visibleGroups
                .SelectMany(group => group.IsMiningRun
                    ? new[] { group.Root }.Concat(group.Children)
                    : new[] { group.Root })
                .ToList();

            _summary.Text = $"{_visibleGroups.Count:N0} sesji/wpisów • {_visibleEntries.Count:N0} zdarzeń • {_totals}";
            _saveReportButton.IsEnabled = _visibleEntries.Count > 0;
            _clearHistoryButton.IsEnabled = allEntries.Count > 0;
            _emptyText.Text = allEntries.Count == 0
                ? "Brak zapisanych sesji. Historia pojawi się po pierwszym uruchomieniu Kopacza."
                : "Brak wpisów pasujących do filtra.";
            _emptyText.Visibility = _visibleGroups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderRows();
        }

        private void RenderRows()
        {
            double offset = _resetScroll ? 0 : FindScrollViewer(_list)?.VerticalOffset ?? 0;
            _resetScroll = false;

            _rendering = true;
            _closingRows.Clear();
            _list.Items.Clear();
            foreach (MiningHistoryGroup group in _visibleGroups)
            {
                bool expanded = group.Children.Count > 0 && _expandedEntryKeys.Contains(GetEntryKey(group.Root));
                _list.Items.Add(BuildRow(group.Root, group.IsMiningRun ? group : null, nested: false, expanded));
                if (expanded)
                {
                    foreach (MiningLogEntry child in group.Children)
                        _list.Items.Add(BuildRow(child, null, nested: true, expanded: false));
                }
            }

            LogRowElement? selected = _list.Items.OfType<LogRowElement>().FirstOrDefault(row => row.Key == _selectedKey)
                ?? _list.Items.OfType<LogRowElement>().FirstOrDefault();
            _list.SelectedItem = selected;
            _rendering = false;
            ShowSelectedRow();

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => FindScrollViewer(_list)?.ScrollToVerticalOffset(offset)));
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is ScrollViewer viewer)
                    return viewer;
                if (FindScrollViewer(child) is { } nested)
                    return nested;
            }
            return null;
        }

        private LogRowElement BuildRow(MiningLogEntry entry, MiningHistoryGroup? run, bool nested, bool expanded)
        {
            bool expandable = run is { Children.Count: > 0 };
            (string tag, Brush tagBrush) = GetTag(entry);
            var row = new LogRowElement
            {
                Entry = entry,
                Key = GetEntryKey(entry),
                Run = run,
                Arrow = RowText(expandable ? (expanded ? "▼" : "▶") : "", ConsoleTime),
                MinHeight = 24,
                Background = Brushes.Transparent
            };
            foreach (double width in new[] { nested ? 20.0 : 4.0, 16, 112, 52 })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            AddRowCell(row, 1, row.Arrow);
            AddRowCell(row, 2, RowText(entry.Timestamp.LocalDateTime.ToString("dd.MM HH:mm:ss"), ConsoleTime));
            TextBlock tagText = RowText(tag, tagBrush);
            tagText.FontWeight = FontWeights.SemiBold;
            AddRowCell(row, 3, tagText);
            TextBlock title = RowText(GetEventLabel(entry), ConsoleText);
            title.FontWeight = run != null ? FontWeights.SemiBold : FontWeights.Normal;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            AddRowCell(row, 4, title);

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = ConsoleToneBrush(GetStatusTone(entry)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = GetStatusLabel(entry)
            };
            Grid.SetColumn(dot, 5);
            row.Children.Add(dot);

            if (expandable)
                row.MouseLeftButtonUp += (_, __) => ToggleRow(row, null);
            return row;
        }

        private static TextBlock RowText(string text, Brush brush) =>
            new() { Text = text, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };

        private static void AddRowCell(Grid row, int column, UIElement cell)
        {
            Grid.SetColumn(cell, column);
            row.Children.Add(cell);
        }

        private void ToggleRow(LogRowElement row, bool? expand)
        {
            if (row.Run is not { Children.Count: > 0 } run)
                return;

            bool open = _expandedEntryKeys.Contains(row.Key);
            bool target = expand ?? !open;
            int index = _list.Items.IndexOf(row);
            if (target == open || index < 0)
                return;

            // Bardzo długich sesji nie animujemy — setki wierszy naraz przycięłyby listę.
            bool animate = run.Children.Count <= MaxAnimatedRows && SystemParameters.ClientAreaAnimation;

            // Wstawianie w miejscu: lista nie skacze, a reszta wierszy zostaje bez zmian.
            if (target)
            {
                RemoveClosingRows(row.Key);
                _expandedEntryKeys.Add(row.Key);
                for (int child = 0; child < run.Children.Count; child++)
                {
                    LogRowElement childRow = BuildRow(run.Children[child], null, nested: true, expanded: false);
                    _list.Items.Insert(index + 1 + child, childRow);
                    if (animate)
                        AnimateRowIn(childRow);
                }
            }
            else
            {
                _expandedEntryKeys.Remove(row.Key);
                if (animate)
                {
                    var closing = new List<LogRowElement>(run.Children.Count);
                    for (int child = 0; child < run.Children.Count; child++)
                        closing.Add((LogRowElement)_list.Items[index + 1 + child]);
                    _closingRows[row.Key] = closing;
                    AnimateRowsOut(row.Key, closing);
                }
                else
                {
                    for (int child = 0; child < run.Children.Count; child++)
                        _list.Items.RemoveAt(index + 1);
                }
                _list.SelectedItem = row;
            }
            row.Arrow.Text = target ? "▼" : "▶";
        }

        private static void AnimateRowIn(LogRowElement row)
        {
            row.MinHeight = 0;
            var height = new DoubleAnimation(0, LogRowHeight, RowAnimationDuration) { EasingFunction = RowEase };
            height.Completed += (_, _) =>
            {
                row.BeginAnimation(HeightProperty, null);
                row.MinHeight = LogRowHeight;
            };
            row.BeginAnimation(HeightProperty, height);
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, RowAnimationDuration));
        }

        private void AnimateRowsOut(string key, List<LogRowElement> rows)
        {
            var height = new DoubleAnimation(LogRowHeight, 0, RowAnimationDuration) { EasingFunction = RowEase };
            height.Freeze();
            var fade = new DoubleAnimation(1, 0, RowAnimationDuration);
            fade.Freeze();
            // Wspólne zakończenie: osobna animacja ostatniego wiersza z obsługą Completed.
            var last = new DoubleAnimation(LogRowHeight, 0, RowAnimationDuration) { EasingFunction = RowEase };
            last.Completed += (_, _) =>
            {
                // Ponowne rozwinięcie albo przebudowa listy już te wiersze usunęły.
                if (_closingRows.TryGetValue(key, out List<LogRowElement>? current) && ReferenceEquals(current, rows))
                    RemoveClosingRows(key);
            };

            for (int index = 0; index < rows.Count; index++)
            {
                LogRowElement row = rows[index];
                row.IsHitTestVisible = false;
                row.MinHeight = 0;
                row.BeginAnimation(HeightProperty, index == rows.Count - 1 ? last : height);
                row.BeginAnimation(OpacityProperty, fade);
            }
        }

        private void RemoveClosingRows(string key)
        {
            if (!_closingRows.Remove(key, out List<LogRowElement>? rows))
                return;
            foreach (LogRowElement row in rows)
                _list.Items.Remove(row);
        }

        private void List_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_list.SelectedItem is not LogRowElement row)
                return;
            bool? expand = e.Key switch
            {
                Key.Right => true,
                Key.Left => false,
                _ => null
            };
            if (e.Key == Key.Enter || expand != null)
            {
                ToggleRow(row, expand);
                e.Handled = true;
            }
        }

        private static (string Text, Brush Brush) GetTag(MiningLogEntry entry) => entry.Kind switch
        {
            MiningLogKinds.MiningRun => ("SESJA", ConsoleInfo),
            MiningLogKinds.InventorySession => ("EQ", ConsoleOk),
            MiningLogKinds.AutomationEvent when GetStatusTone(entry) == LogTone.Bad => ("ALARM", ConsoleBad),
            MiningLogKinds.AutomationEvent => ("AUTO", ConsoleAuto),
            MiningLogKinds.CobbleXCreated => ("CX", ConsoleWarn),
            _ => ("STARY", ConsoleTime)
        };

        private static Style GetRowStyle()
        {
            if (_rowStyle != null)
                return _rowStyle;

            var border = new FrameworkElementFactory(typeof(Border), "Bd");
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));

            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(0x2A, 0x2E, 0x40), "Bd"));
            template.Triggers.Add(hover);
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(0x2E, 0x3D, 0x5C), "Bd"));
            template.Triggers.Add(selected);

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            style.Seal();
            return _rowStyle = style;
        }

        private static bool EntryMatchesSearch(MiningLogEntry entry, string query)
        {
            string eventLabel = GetEventLabel(entry);
            string countLabel = GetCountLabel(entry);
            string searchable = string.Join(" ",
                entry.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"),
                entry.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                eventLabel,
                countLabel,
                entry.Owner ?? string.Empty,
                entry.EventType ?? string.Empty,
                entry.CleanupMode ?? string.Empty,
                GetStatusLabel(entry),
                entry.Details ?? string.Empty,
                entry.EndDetails ?? string.Empty,
                entry.CobbleXCommand ?? string.Empty,
                string.Join(" ", entry.SelectedSlots ?? new List<int>()),
                string.Join(" ", entry.SelectedItemTypes ?? new List<string>()),
                string.Join(" ", (entry.Items ?? new List<MiningLogItemDetail>()).Select(item => $"{item.Label} {item.ItemId} {item.ItemCount} {item.StackCount}")));
            string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return terms.All(term => searchable.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetEntryKey(MiningLogEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.SessionId))
                return entry.SessionId;

            return string.Join("|",
                entry.Kind ?? string.Empty,
                entry.Timestamp.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture),
                entry.Count.ToString(CultureInfo.InvariantCulture),
                entry.ItemCount.ToString(CultureInfo.InvariantCulture),
                entry.Details ?? string.Empty);
        }

        private static string GetEventLabel(MiningLogEntry entry)
        {
            if (entry.Kind == MiningLogKinds.MiningRun)
                return string.IsNullOrWhiteSpace(entry.Owner) ? "Sesja kopania" : $"Sesja kopania • {entry.Owner}";
            if (entry.Kind == MiningLogKinds.InventorySession)
                return string.IsNullOrWhiteSpace(entry.Owner) ? "Skan EQ" : $"Skan EQ • {entry.Owner}";
            if (entry.Kind == MiningLogKinds.AutomationEvent)
            {
                return entry.EventType switch
                {
                    MiningLogEventTypes.AutoReconnectStarted => "Auto Reconnect • start",
                    MiningLogEventTypes.AutoReconnectFinished => "Auto Reconnect • wynik",
                    MiningLogEventTypes.HealthCheckStarted => "Kontrola kopania • start",
                    MiningLogEventTypes.HealthCheckFinished => "Kontrola kopania • wynik",
                    MiningLogEventTypes.MissingPickaxeRecovery => "Brak kilofa • powrót do home",
                    MiningLogEventTypes.MissingPickaxeRecoveryFinished => "Powrót do home • wynik",
                    MiningLogEventTypes.EmergencyDamageSoundDetected => "Alarm obrażeń • awaryjne wyjście",
                    MiningLogEventTypes.EmergencyProtectionStarted => "Ochrona awaryjna • start",
                    MiningLogEventTypes.EmergencyReconnectJoined => "Ochrona awaryjna • dołączono",
                    MiningLogEventTypes.EmergencyPickaxeDetected => "Kontrola kilofa • wykryto",
                    MiningLogEventTypes.EmergencyPickaxeMissing => "Kontrola kilofa • brak",
                    MiningLogEventTypes.EmergencyHomeCommandSent => "Ochrona awaryjna • home",
                    MiningLogEventTypes.EmergencyMiningResumed => "Ochrona awaryjna • wznowiono kopanie",
                    MiningLogEventTypes.EmergencyShutdown => "Ochrona awaryjna • zamknięcie programu",
                    MiningLogEventTypes.EmergencyProtectionFinished => "Ochrona awaryjna • wynik",
                    "gold-deposit" => "Wpłata złota",
                    "enderchest" => "Enderchest",
                    _ => "Zdarzenie automatyzacji"
                };
            }
            return entry.Kind == MiningLogKinds.CobbleXCreated ? "Utworzono CobbleX" : "Stary wpis wyrzucania*";
        }

        private static string GetCountLabel(MiningLogEntry entry)
        {
            if (entry.Kind == MiningLogKinds.CobbleXCreated)
                return $"{Math.Max(0, entry.Count):N0} szt.";
            if (entry.Kind == MiningLogKinds.MiningRun)
                return "Folder sesji";
            if (entry.Kind == MiningLogKinds.AutomationEvent)
                return "Automatyzacja";
            if (entry.Kind == MiningLogKinds.InventorySession)
            {
                if (entry.Status == MiningLogStatuses.InProgress)
                    return "W toku";

                string result = $"{Math.Max(0, entry.ItemCount):N0} szt. / {Math.Max(0, entry.StackCount):N0} stos.";
                return entry.CobbleXCreated ? result + "\n+ 1 CobbleX" : result;
            }
            if (entry.ItemCount > 0)
                return $"{entry.ItemCount:N0} szt. / {MiningLogService.GetDiscardedStackCount(entry):N0} stos.";

            return $"{MiningLogService.GetDiscardedStackCount(entry):N0} stos.\n(stary wpis*)";
        }

        private static string GetStatusLabel(MiningLogEntry entry)
        {
            if (entry.Kind == MiningLogKinds.MiningRun)
            {
                string duration = entry.CompletedAt.HasValue
                    ? FormatDuration(entry.CompletedAt.Value - entry.Timestamp)
                    : FormatDuration(DateTimeOffset.Now - entry.Timestamp);
                return entry.Status switch
                {
                    MiningLogStatuses.Completed => $"Zakończono • {duration}",
                    MiningLogStatuses.Aborted => $"Przerwano • {duration}",
                    MiningLogStatuses.Interrupted => $"Niedokończono • {duration}",
                    _ => $"Kopanie trwa • {duration}"
                };
            }
            if (entry.Kind == MiningLogKinds.AutomationEvent)
            {
                if (entry.EventType is MiningLogEventTypes.AutoReconnectStarted
                    or MiningLogEventTypes.HealthCheckStarted
                    or MiningLogEventTypes.MissingPickaxeRecovery
                    or MiningLogEventTypes.EmergencyProtectionStarted)
                {
                    return "Uruchomiono";
                }
                return entry.Status switch
                {
                    MiningLogStatuses.InProgress => "Rozpoczęto",
                    MiningLogStatuses.Aborted => "Błąd / zatrzymano",
                    MiningLogStatuses.Interrupted => "Niedokończono",
                    _ => "Wykonano"
                };
            }
            if (entry.Kind != MiningLogKinds.InventorySession)
                return "Wpis starszego formatu";

            return entry.Status switch
            {
                MiningLogStatuses.Completed when entry.RemainingStacks > 0 =>
                    $"Nie wszystko wyrzucono • {FormatRemainingStacks(entry.RemainingStacks)} • {FormatDropPasses(entry.DropPasses)}",
                MiningLogStatuses.Completed when entry.DropPasses == 0 => "Zakończono — EQ było czyste",
                MiningLogStatuses.Completed => $"Zakończono — EQ wyczyszczone • {FormatDropPasses(entry.DropPasses)}",
                MiningLogStatuses.Aborted => "Przerwano — sprawdź powód",
                MiningLogStatuses.Interrupted => "Niedokończono — program lub makro zatrzymane",
                _ => "Auto EQ w toku"
            };
        }

        private static string FormatRemainingStacks(int count)
        {
            count = Math.Max(0, count);
            if (count == 1)
                return "pozostał 1 stos";
            if (UsesPolishPaucalForm(count))
                return $"pozostały {count:N0} stosy";
            return $"pozostało {count:N0} stosów";
        }

        private static string FormatDropPasses(int count)
        {
            count = Math.Max(0, count);
            if (count == 1)
                return "1 przebieg";
            if (UsesPolishPaucalForm(count))
                return $"{count:N0} przebiegi";
            return $"{count:N0} przebiegów";
        }

        private static string FormatDropPassesAfter(int count)
        {
            count = Math.Max(0, count);
            return count == 1 ? "1 przebiegu" : $"{count:N0} przebiegach";
        }

        private static bool UsesPolishPaucalForm(int count)
        {
            int lastTwoDigits = count % 100;
            int lastDigit = count % 10;
            return lastDigit is >= 2 and <= 4 && lastTwoDigits is not (>= 12 and <= 14);
        }

        private static LogTone GetStatusTone(MiningLogEntry entry)
        {
            if (entry.Kind == MiningLogKinds.AutomationEvent
                && entry.EventType is MiningLogEventTypes.EmergencyDamageSoundDetected
                    or MiningLogEventTypes.EmergencyPickaxeMissing
                    or MiningLogEventTypes.EmergencyShutdown
                    or MiningLogEventTypes.MissingPickaxeRecovery)
            {
                return LogTone.Bad;
            }
            if (entry.Kind == MiningLogKinds.MiningRun || entry.Kind == MiningLogKinds.AutomationEvent)
            {
                return entry.Status switch
                {
                    MiningLogStatuses.Completed => LogTone.Ok,
                    MiningLogStatuses.InProgress => LogTone.Warn,
                    _ => LogTone.Bad
                };
            }
            if (entry.Kind != MiningLogKinds.InventorySession)
                return LogTone.Muted;
            if (entry.Status == MiningLogStatuses.Completed && entry.RemainingStacks == 0)
                return LogTone.Ok;
            if (entry.Status == MiningLogStatuses.InProgress || entry.RemainingStacks > 0)
                return LogTone.Warn;
            return LogTone.Bad;
        }

        // Trwająca sesja ma bursztynową kropkę, ale nie jest problemem.
        private static bool IsProblem(MiningLogEntry entry)
        {
            LogTone tone = GetStatusTone(entry);
            return tone == LogTone.Bad || (tone == LogTone.Warn && entry.Status != MiningLogStatuses.InProgress);
        }

        private static Brush ConsoleToneBrush(LogTone tone) => tone switch
        {
            LogTone.Ok => ConsoleOk,
            LogTone.Warn => ConsoleWarn,
            LogTone.Bad => ConsoleBad,
            _ => ConsoleTime
        };

        private static Brush ThemedToneBrush(LogTone tone) => tone switch
        {
            LogTone.Ok => GlassPalette.Ok,
            LogTone.Warn => GlassPalette.Warn,
            LogTone.Bad => GlassPalette.Bad,
            _ => GlassPalette.Muted
        };

        private void ShowSelectedRow()
        {
            var row = _list.SelectedItem as LogRowElement;
            _selectedKey = row?.Key ?? _selectedKey;
            _details.Children.Clear();
            if (row == null)
            {
                _details.Children.Add(new TextBlock { Text = "Kliknij wpis po lewej, żeby zobaczyć szczegóły.", Style = GlassChrome.GetStyle("GlassSub"), TextWrapping = TextWrapping.Wrap });
                return;
            }

            MiningLogEntry entry = row.Entry;
            string kind = entry.Kind switch
            {
                MiningLogKinds.MiningRun => "SESJA KOPANIA",
                MiningLogKinds.InventorySession => "SZCZEGÓŁY OTWARCIA EQ",
                MiningLogKinds.AutomationEvent => "SZCZEGÓŁY AUTOMATYZACJI",
                _ => "SZCZEGÓŁY STAREGO WPISU"
            };
            _details.Children.Add(new TextBlock { Text = kind, Style = GlassChrome.GetStyle("GlassCaption"), Margin = new Thickness(0, 0, 0, 8) });
            _details.Children.Add(new TextBlock { Text = GetEventLabel(entry), FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            _details.Children.Add(new TextBlock { Text = GetStatusLabel(entry), FontWeight = FontWeights.SemiBold, Foreground = ThemedToneBrush(GetStatusTone(entry)), TextWrapping = TextWrapping.Wrap });
            AddDetailDivider(_details);

            if (row.Run != null)
                AddRunDetails(_details, entry, row.Run.AllChildren);
            else if (entry.Kind == MiningLogKinds.InventorySession)
                AddInventoryDetails(_details, entry);
            else
                AddTimeDetails(_details, "Data zdarzenia", entry);

            if (row.Run == null && !string.IsNullOrWhiteSpace(entry.Details))
                AddDetailLine(_details, "Wynik", entry.Details);
        }

        private static void AddRunDetails(StackPanel panel, MiningLogEntry run, IReadOnlyList<MiningLogEntry> children)
        {
            var scans = children.Where(entry => entry.Kind == MiningLogKinds.InventorySession).ToList();
            string duration = run.CompletedAt.HasValue
                ? FormatDuration(run.CompletedAt.Value - run.Timestamp)
                : FormatDuration(DateTimeOffset.Now - run.Timestamp) + " (trwa)";

            AddDetailLine(panel, "Start", run.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"));
            if (run.CompletedAt.HasValue)
                AddDetailLine(panel, "Koniec", run.CompletedAt.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"));
            AddDetailLine(panel, "Czas", duration);
            AddDetailLine(panel, "Otwarcia EQ", scans.Count.ToString("N0"));
            AddDetailLine(panel, "Automatyzacje", children.Count(entry => entry.Kind == MiningLogKinds.AutomationEvent).ToString("N0"));
            AddDetailLine(panel, "Wyrzucone", $"{scans.Sum(entry => Math.Max(0, entry.ItemCount)):N0} szt. • {scans.Sum(MiningLogService.GetDiscardedStackCount):N0} stos.");
            AddDetailLine(panel, "CobbleX", scans.Count(entry => entry.CobbleXCreated).ToString("N0"));
            AddDetailLine(panel, "Konfiguracja", string.IsNullOrWhiteSpace(run.Details) ? "Brak zapisanego opisu konfiguracji." : run.Details);
            if (!string.IsNullOrWhiteSpace(run.EndDetails))
                AddDetailLine(panel, "Zakończenie", run.EndDetails);
            if (children.Count == 0)
                panel.Children.Add(new TextBlock { Text = "Ta sesja nie zawiera jeszcze otwarć EQ ani zdarzeń automatyzacji.", Style = GlassChrome.GetStyle("GlassSub"), FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }

        private static void AddTimeDetails(StackPanel panel, string startLabel, MiningLogEntry entry)
        {
            AddDetailLine(panel, startLabel, entry.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"));
            if (entry.CompletedAt.HasValue)
            {
                AddDetailLine(panel, "Zakończenie", entry.CompletedAt.Value.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"));
                AddDetailLine(panel, "Czas", FormatDuration(entry.CompletedAt.Value - entry.Timestamp));
            }
            if (!string.IsNullOrWhiteSpace(entry.Owner))
                AddDetailLine(panel, "Tryb", entry.Owner);
        }

        private static void AddInventoryDetails(StackPanel panel, MiningLogEntry entry)
        {
            AddTimeDetails(panel, "Otwarcie EQ", entry);
            string cleanupMode = entry.CleanupMode switch
            {
                "all-except-cobblestone" => "Wszystko poza cobblestone",
                "selected-items" => "Tylko wybrane typy",
                _ => "Starsza sesja — brak zapisanej konfiguracji"
            };
            AddDetailLine(panel, "Tryb wyrzucania", cleanupMode);
            if (entry.SelectedSlots is { Count: > 0 })
                AddDetailLine(panel, "Skanowane sloty", string.Join(", ", entry.SelectedSlots));
            if (entry.CleanupMode == "selected-items")
                AddDetailLine(panel, "Wybrane przedmioty", entry.SelectedItemTypes is { Count: > 0 } ? string.Join(", ", entry.SelectedItemTypes) : "Brak zapisanej listy");
            AddDetailLine(panel, "CobbleX włączony", entry.CobbleXEnabled ? "Tak" : "Nie");
            AddDetailLine(
                panel,
                "Jedzenie po EQ",
                entry.EatAfterCleanup
                    ? entry.EatingCompleted ? "Wykonano — slot 2, PPM 4 s, powrót na slot 1" : "Włączone, ale nie zakończono"
                    : "Wyłączone",
                entry.EatAfterCleanup && !entry.EatingCompleted ? GlassPalette.Warn : null);
            AddDetailLine(panel, "Przebiegi EQ", FormatDropPasses(entry.DropPasses));
            AddDetailLine(
                panel,
                "Wynik wyrzucania",
                entry.RemainingStacks > 0 ? FormatRemainingStacks(entry.RemainingStacks) : "Brak pozostałych stosów — EQ czyste",
                entry.RemainingStacks > 0 ? GlassPalette.Warn : GlassPalette.Ok);
            if (entry.RemainingStacks > 0)
            {
                AddDetailLine(
                    panel,
                    "Dlaczego przerwano wyrzucanie",
                    $"Po {FormatDropPassesAfter(entry.DropPasses)} skaner nadal wykrywał przedmioty do wyrzucenia. Program zamknął EQ, aby uniknąć nieskończonej pętli.",
                    GlassPalette.Warn);
            }

            AddDetailDivider(panel);
            panel.Children.Add(new TextBlock { Text = "WYRZUCONE PRZEDMIOTY", Style = GlassChrome.GetStyle("GlassCaption"), Margin = new Thickness(0, 0, 0, 7) });
            IReadOnlyList<MiningLogItemDetail> items = entry.Items ?? new List<MiningLogItemDetail>();
            if (items.Count == 0)
                panel.Children.Add(new TextBlock { Text = "Nie wyrzucono żadnych oznaczonych przedmiotów.", Style = GlassChrome.GetStyle("GlassSub"), FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            foreach (MiningLogItemDetail item in items)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                var count = new TextBlock { Text = $"{Math.Max(0, item.ItemCount):N0} szt. • {Math.Max(0, item.StackCount):N0} stos.", FontWeight = FontWeights.SemiBold, Foreground = GlassPalette.Ok, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(count, Dock.Right);
                line.Children.Add(count);
                line.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(item.Label) ? item.ItemId : item.Label, TextTrimming = TextTrimming.CharacterEllipsis });
                panel.Children.Add(line);
            }

            string cobbleX = entry.CobbleXCreated
                ? $"Utworzono • komenda: {(string.IsNullOrWhiteSpace(entry.CobbleXCommand) ? "brak" : entry.CobbleXCommand)}"
                : "Nie utworzono";
            AddDetailLine(panel, "CobbleX", $"{cobbleX} • Cobble 64: {Math.Max(0, entry.FullCobblestoneStacks)}/{Math.Max(0, entry.RequiredCobblestoneStacks)}");
        }

        private static void AddDetailDivider(StackPanel panel)
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 10) };
            divider.SetResourceReference(Border.BackgroundProperty, "GlassLineBrush");
            panel.Children.Add(divider);
        }

        private static void AddDetailLine(StackPanel panel, string label, string value, Brush? valueBrush = null)
        {
            var line = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            line.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = GlassPalette.Muted });
            var text = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            if (valueBrush != null)
                text.Foreground = valueBrush;
            line.Children.Add(text);
            panel.Children.Add(line);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }

        // Czyści historię oglądanego konta. Dziennik innego konta czyści jego własny Kopacz
        // (trzyma go w pamięci i nadpisałby zmieniony plik); zamknięte konto czyścimy w pliku.
        private async Task ClearHistoryAsync()
        {
            string profileId = _viewedProfileId;
            bool own = string.Equals(profileId, _currentProfileId, StringComparison.OrdinalIgnoreCase);
            string accountName = new MinerAccountStore().Load()
                .FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))?.Name
                ?? profileId;
            var confirmation = new DarkLogDialog(
                "Wyczyść logi kopania",
                $"Usunąć całą historię kopania konta „{accountName}”? Trwająca sesja zostanie zachowana. Tej operacji nie można cofnąć.",
                confirmText: "Usuń historię",
                showCancel: true)
            {
                Owner = HostWindow
            };
            if (confirmation.ShowDialog() != true)
                return;

            _clearHistoryButton.IsEnabled = false;
            string? error = null;
            if (own)
            {
                if (!_logService.Clear(out string clearError))
                    error = clearError;
            }
            else
            {
                MinerRemotePipeResponse? response = null;
                try
                {
                    response = await MinerRemoteControlClient.ExecuteAsync(profileId, "clear-logs");
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
                {
                    // Kopacz tego konta nie działa — nikt nie trzyma jego dziennika w pamięci.
                }

                if (response == null)
                {
                    if (!MiningLogService.OpenReadOnly(profileId).Clear(out string clearError))
                        error = clearError;
                }
                else if (!response.Success)
                {
                    error = response.Message;
                }

                // Podgląd czyta plik od nowa, żeby pokazać stan po czyszczeniu.
                if (string.Equals(_viewedProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                    _logService = MiningLogService.OpenReadOnly(profileId);
            }

            if (error != null)
                ShowDarkInfo("Błąd", "Nie udało się wyczyścić historii: " + error);
            AppLog.Info("Logi", error == null ? $"Wyczyszczono historię kopania konta „{accountName}”." : $"Czyszczenie historii konta „{accountName}” nie powiodło się: {error}");
            _expandedEntryKeys.Clear();
            RefreshView();
            await RefreshAccountsAsync();
        }

        private void SaveReport()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var dialog = new SaveFileDialog
            {
                Title = "Zapisz raport z logów kopania",
                InitialDirectory = Directory.Exists(desktop) ? desktop : null,
                FileName = $"MinecraftHelper-raport-kopania-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                Filter = "Raport CSV (*.csv)|*.csv|Raport tekstowy (*.txt)|*.txt",
                FilterIndex = 1,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(HostWindow) != true)
                return;

            try
            {
                string report = Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                    ? BuildTextReport(_visibleEntries)
                    : BuildCsvReport(_visibleEntries);
                File.WriteAllText(dialog.FileName, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                ShowDarkInfo(
                    "Raport zapisany",
                    $"Zapisano {_visibleEntries.Count:N0} widocznych wpisów do:\n{dialog.FileName}");
            }
            catch (Exception ex)
            {
                ShowDarkInfo("Błąd zapisu", "Nie udało się zapisać raportu: " + ex.Message);
            }
        }

        private string BuildCsvReport(IReadOnlyList<MiningLogEntry> entries)
        {
            (int cobbleX, int discardedItems, int exactStacks, int legacyStacks) = CalculateReportTotals(entries);
            var builder = new StringBuilder();
            builder.AppendLine("Raport Minecraft Helper - logi kopania");
            builder.AppendLine($"Wygenerowano;{EscapeCsv(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"))}");
            builder.AppendLine($"Widoczne wpisy;{entries.Count}");
            builder.AppendLine($"Sesje kopania;{entries.Count(entry => entry.Kind == MiningLogKinds.MiningRun)}");
            builder.AppendLine($"Zdarzenia automatyzacji;{entries.Count(entry => entry.Kind == MiningLogKinds.AutomationEvent)}");
            builder.AppendLine($"Utworzone CobbleX;{cobbleX}");
            builder.AppendLine($"Wyrzucone przedmioty (dokładne);{discardedItems}");
            builder.AppendLine($"Wyrzucone stosy (dokładne);{exactStacks}");
            builder.AppendLine($"Stare stosy bez liczby sztuk;{legacyStacks}");
            builder.AppendLine();
            builder.AppendLine("Data i godzina;ID sesji kopania;Rodzaj;Kopacz / właściciel;Zdarzenie;Status;Sztuki;Stosy;CobbleX;Szczegóły");
            foreach (MiningLogEntry entry in entries)
            {
                bool isLegacyCobbleX = entry.Kind == MiningLogKinds.CobbleXCreated;
                string items = isLegacyCobbleX
                    ? string.Empty
                    : entry.ItemCount > 0 ? entry.ItemCount.ToString() : "0";
                string stacks = isLegacyCobbleX
                    ? string.Empty
                    : MiningLogService.GetDiscardedStackCount(entry).ToString();
                string cobbleXValue = isLegacyCobbleX
                    ? Math.Max(0, entry.Count).ToString()
                    : entry.CobbleXCreated ? "1" : "0";
                string miningRunId = entry.Kind == MiningLogKinds.MiningRun ? entry.SessionId : entry.MiningRunId;
                builder.Append(EscapeCsv(entry.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"))).Append(';')
                    .Append(EscapeCsv(miningRunId)).Append(';')
                    .Append(EscapeCsv(entry.Kind)).Append(';')
                    .Append(EscapeCsv(entry.Owner)).Append(';')
                    .Append(EscapeCsv(GetEventLabel(entry))).Append(';')
                    .Append(EscapeCsv(GetStatusLabel(entry))).Append(';')
                    .Append(EscapeCsv(items)).Append(';')
                    .Append(EscapeCsv(stacks)).Append(';')
                    .Append(EscapeCsv(cobbleXValue)).Append(';')
                    .AppendLine(EscapeCsv(BuildReportDetails(entry)));
            }

            return builder.ToString();
        }

        private string BuildTextReport(IReadOnlyList<MiningLogEntry> entries)
        {
            (int cobbleX, int discardedItems, int exactStacks, int legacyStacks) = CalculateReportTotals(entries);
            var builder = new StringBuilder();
            builder.AppendLine("MINECRAFT HELPER - RAPORT Z LOGÓW KOPANIA");
            builder.AppendLine($"Wygenerowano: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            builder.AppendLine($"Widoczne wpisy: {entries.Count:N0}");
            builder.AppendLine($"Sesje kopania: {entries.Count(entry => entry.Kind == MiningLogKinds.MiningRun):N0}");
            builder.AppendLine($"Zdarzenia automatyzacji: {entries.Count(entry => entry.Kind == MiningLogKinds.AutomationEvent):N0}");
            builder.AppendLine($"Utworzone CobbleX: {cobbleX:N0}");
            builder.AppendLine($"Wyrzucone przedmioty: {discardedItems:N0} szt. z {exactStacks:N0} stosów");
            if (legacyStacks > 0)
                builder.AppendLine($"Starsze dane: {legacyStacks:N0} stosów bez zapisanej liczby sztuk");
            builder.AppendLine();

            foreach (MiningLogEntry entry in entries)
            {
                string miningRunId = entry.Kind == MiningLogKinds.MiningRun ? entry.SessionId : entry.MiningRunId;
                builder.Append('[').Append(entry.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss")).Append("] ")
                    .Append(GetEventLabel(entry)).Append(" | ")
                    .Append("Sesja: ").Append(string.IsNullOrWhiteSpace(miningRunId) ? "starszy wpis" : miningRunId).Append(" | ")
                    .Append(GetStatusLabel(entry)).Append(" | ")
                    .Append(GetCountLabel(entry).Replace('\n', ' ')).Append(" | ")
                    .Append(BuildReportDetails(entry));
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static (int CobbleX, int Items, int ExactStacks, int LegacyStacks) CalculateReportTotals(
            IReadOnlyList<MiningLogEntry> entries)
        {
            int cobbleX = entries
                .Sum(entry => entry.Kind == MiningLogKinds.CobbleXCreated
                    ? Math.Max(0, entry.Count)
                    : entry.Kind == MiningLogKinds.InventorySession && entry.CobbleXCreated ? 1 : 0);
            int items = entries
                .Where(entry => entry.Kind is MiningLogKinds.ItemsDiscarded or MiningLogKinds.InventorySession)
                .Sum(entry => Math.Max(0, entry.ItemCount));
            int exactStacks = entries
                .Where(entry => (entry.Kind == MiningLogKinds.ItemsDiscarded && entry.ItemCount > 0)
                    || entry.Kind == MiningLogKinds.InventorySession)
                .Sum(MiningLogService.GetDiscardedStackCount);
            int legacyStacks = entries
                .Where(entry => entry.Kind == MiningLogKinds.ItemsDiscarded && entry.ItemCount <= 0)
                .Sum(MiningLogService.GetDiscardedStackCount);
            return (cobbleX, items, exactStacks, legacyStacks);
        }

        private static string BuildReportDetails(MiningLogEntry entry)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Details))
                parts.Add(entry.Details.Trim());
            if (!string.IsNullOrWhiteSpace(entry.EndDetails))
                parts.Add("Zakończenie: " + entry.EndDetails.Trim());
            if (entry.Items is { Count: > 0 })
            {
                parts.Add("Przedmioty: " + string.Join(", ", entry.Items.Select(item =>
                    $"{(string.IsNullOrWhiteSpace(item.Label) ? item.ItemId : item.Label)}: {Math.Max(0, item.ItemCount)} szt. / {Math.Max(0, item.StackCount)} stos.")));
            }
            if (entry.Kind == MiningLogKinds.InventorySession)
            {
                string cleanupMode = entry.CleanupMode == "all-except-cobblestone"
                    ? "wszystko poza cobblestone"
                    : entry.CleanupMode == "selected-items"
                        ? "wybrane typy"
                        : "brak danych (stary wpis)";
                parts.Add($"Tryb wyrzucania: {cleanupMode}.");
                if (entry.SelectedSlots is { Count: > 0 })
                    parts.Add("Sloty: " + string.Join(",", entry.SelectedSlots) + ".");
                if (entry.SelectedItemTypes is { Count: > 0 })
                    parts.Add("Wybrane typy: " + string.Join(", ", entry.SelectedItemTypes) + ".");
                parts.Add(entry.EatAfterCleanup
                    ? entry.EatingCompleted ? "Jedzenie po EQ: wykonano." : "Jedzenie po EQ: nie zakończono."
                    : "Jedzenie po EQ: wyłączone.");
                parts.Add($"Przebiegi: {Math.Max(0, entry.DropPasses)}; pozostało: {Math.Max(0, entry.RemainingStacks)} stos.");
                parts.Add(entry.CobbleXCreated
                    ? $"CobbleX: utworzono ({entry.CobbleXCommand}); Cobble 64: {entry.FullCobblestoneStacks}/{entry.RequiredCobblestoneStacks}"
                    : $"CobbleX: nie utworzono; Cobble 64: {entry.FullCobblestoneStacks}/{entry.RequiredCobblestoneStacks}");
            }
            else if (entry.Kind == MiningLogKinds.MiningRun && entry.CompletedAt.HasValue)
            {
                parts.Add($"Czas sesji: {FormatDuration(entry.CompletedAt.Value - entry.Timestamp)}.");
            }
            else if (entry.Kind == MiningLogKinds.AutomationEvent && !string.IsNullOrWhiteSpace(entry.EventType))
            {
                parts.Add($"Typ automatyzacji: {entry.EventType}.");
            }

            return string.Join(" ", parts);
        }

        private static string EscapeCsv(string? value)
        {
            string text = value ?? string.Empty;
            return '"' + text.Replace("\"", "\"\"") + '"';
        }

        private void ShowDarkInfo(string title, string message)
        {
            var dialog = new DarkLogDialog(title, message, confirmText: "OK", showCancel: false)
            {
                Owner = HostWindow
            };
            dialog.ShowDialog();
        }

        private sealed class DarkLogDialog : Window
        {
            public DarkLogDialog(string title, string message, string confirmText, bool showCancel)
            {
                Title = title;
                Width = 500;
                MinHeight = 190;
                SizeToContent = SizeToContent.Height;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowInTaskbar = false;
                GlassChrome.Apply(this);

                var root = new Grid { Margin = new Thickness(20) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var heading = new TextBlock
                {
                    Text = title,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush(127, 200, 255),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                root.Children.Add(heading);

                var messageBorder = new Border
                {
                    Background = Brush(22, 31, 43),
                    BorderBrush = Brush(75, 98, 131),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(14),
                    Child = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush(207, 219, 235),
                        FontSize = 12
                    }
                };
                Grid.SetRow(messageBorder, 1);
                root.Children.Add(messageBorder);

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                if (showCancel)
                {
                    Button cancel = BuildButton("Anuluj", primary: false);
                    cancel.Click += (_, __) => DialogResult = false;
                    buttons.Children.Add(cancel);
                }

                Button confirm = BuildButton(confirmText, primary: true);
                confirm.Margin = showCancel ? new Thickness(10, 0, 0, 0) : new Thickness(0);
                confirm.Click += (_, __) => DialogResult = true;
                buttons.Children.Add(confirm);
                Grid.SetRow(buttons, 2);
                root.Children.Add(buttons);

                Content = root;
                PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape)
                    {
                        DialogResult = false;
                        e.Handled = true;
                    }
                };
            }
        }

        private static Button BuildButton(string text, bool primary)
        {
            var button = new Button { Content = text, MinWidth = 110 };
            if (primary)
                button.Style = GlassChrome.GetStyle("GlassPrimaryButton");
            return button;
        }

        // Kolory starego, ciemnego okna logów zamieniane na odpowiedniki glass.
        private static SolidColorBrush Brush(byte red, byte green, byte blue) => GlassPalette.MapLegacyBrush(red, green, blue);
    }
}
