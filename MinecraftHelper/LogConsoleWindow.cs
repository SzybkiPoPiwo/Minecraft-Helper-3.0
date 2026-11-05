using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Konsola zdarzeń: podgląd dziennika AppLog na żywo, z filtrem poziomu i kopiowaniem.
internal sealed class LogConsoleWindow : Window
{
    private static readonly Brush ConsoleBackground = Frozen(Color.FromRgb(0x1A, 0x1D, 0x2B));
    private static readonly Brush TimeBrush = Frozen(Color.FromRgb(0x8B, 0x91, 0xA8));
    private static readonly Brush InfoBrush = Frozen(Color.FromRgb(0x7D, 0xB8, 0xFF));
    private static readonly Brush WarningBrush = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));
    private static readonly Brush ErrorBrush = Frozen(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush SourceBrush = Frozen(Color.FromRgb(0xB4, 0xB7, 0xFF));
    private static readonly Brush MessageBrush = Frozen(Color.FromRgb(0xEC, 0xEE, 0xF5));

    private readonly List<AppLogEntry> _entries = new();
    private readonly ListBox _list;
    private readonly CheckBox _follow;
    private readonly TextBlock _summary;
    private AppLogLevel _minimumLevel = AppLogLevel.Info;

    public LogConsoleWindow()
    {
        Title = "Konsola zdarzeń — Kopacz";
        Width = 920;
        Height = 580;
        MinWidth = 560;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        var filters = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((string label, AppLogLevel level) in new[]
        {
            ("Wszystko", AppLogLevel.Info),
            ("Uwagi i błędy", AppLogLevel.Warning),
            ("Tylko błędy", AppLogLevel.Error)
        })
        {
            var filter = new RadioButton
            {
                Content = label,
                GroupName = "LogConsoleLevel",
                Style = GlassChrome.GetStyle("GlassSegment"),
                IsChecked = level == _minimumLevel
            };
            filter.Checked += (_, __) =>
            {
                _minimumLevel = level;
                Render();
            };
            filters.Children.Add(filter);
        }

        _follow = new CheckBox { Content = "Przewijaj na bieżąco", IsChecked = true, Margin = new Thickness(16, 0, 0, 0) };
        Button copy = CreateButton("Kopiuj", "Kopiuje widoczne wpisy (albo zaznaczone) do schowka.");
        copy.Click += (_, __) => CopyToClipboard();
        Button folder = CreateButton("Folder logów", "Otwiera folder z plikiem app.log tego profilu.");
        folder.Click += (_, __) => OpenLogFolder();
        Button clear = CreateButton("Wyczyść widok", "Czyści tylko to okno; plik app.log zostaje.");
        clear.Click += (_, __) =>
        {
            _entries.Clear();
            Render();
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(copy);
        actions.Children.Add(folder);
        actions.Children.Add(clear);

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassSegmentHost"), Child = filters });
        left.Children.Add(_follow);
        DockPanel.SetDock(left, Dock.Left);
        toolbar.Children.Add(left);
        toolbar.Children.Add(actions);

        _list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = MessageBrush,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            SelectionMode = SelectionMode.Extended,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                CopyToClipboard();
                e.Handled = true;
            }
        };

        _summary = new TextBlock { Style = GlassChrome.GetStyle("GlassSub"), Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_summary);
        root.Children.Add(new Border
        {
            Background = ConsoleBackground,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10, 8, 6, 8),
            Child = _list
        });
        Content = root;

        _entries.AddRange(AppLog.Snapshot());
        Render();
        AppLog.EntryAdded += OnEntryAdded;
        Closed += (_, __) => AppLog.EntryAdded -= OnEntryAdded;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Button CreateButton(string text, string toolTip) =>
        new() { Content = text, ToolTip = toolTip, Margin = new Thickness(8, 0, 0, 0) };

    // AppLog wywołuje zdarzenie z dowolnego wątku (np. potok kont, Raw Input).
    private void OnEntryAdded(AppLogEntry entry) => Dispatcher.BeginInvoke(() =>
    {
        _entries.Add(entry);
        if (_entries.Count > AppLog.MaxEntries)
        {
            AppLogEntry dropped = _entries[0];
            _entries.RemoveAt(0);
            if (_list.Items.Count > 0 && ReferenceEquals((_list.Items[0] as FrameworkElement)?.Tag, dropped))
                _list.Items.RemoveAt(0);
        }

        if (entry.Level >= _minimumLevel)
        {
            _list.Items.Add(CreateRow(entry));
            if (_follow.IsChecked == true)
                _list.ScrollIntoView(_list.Items[^1]);
        }
        UpdateSummary();
    });

    private void Render()
    {
        _list.Items.Clear();
        foreach (AppLogEntry entry in _entries.Where(item => item.Level >= _minimumLevel))
            _list.Items.Add(CreateRow(entry));
        if (_list.Items.Count > 0)
            _list.ScrollIntoView(_list.Items[^1]);
        UpdateSummary();
    }

    private static TextBlock CreateRow(AppLogEntry entry)
    {
        (string label, Brush brush) = entry.Level switch
        {
            AppLogLevel.Error => ("BŁĄD ", ErrorBrush),
            AppLogLevel.Warning => ("UWAGA", WarningBrush),
            _ => ("INFO ", InfoBrush)
        };
        var row = new TextBlock { TextWrapping = TextWrapping.Wrap, Tag = entry, Margin = new Thickness(0, 1, 0, 1) };
        row.Inlines.Add(new Run(entry.Time.ToString("HH:mm:ss.fff") + "  ") { Foreground = TimeBrush });
        row.Inlines.Add(new Run(label + "  ") { Foreground = brush, FontWeight = FontWeights.SemiBold });
        row.Inlines.Add(new Run($"[{entry.Source}] ") { Foreground = SourceBrush });
        row.Inlines.Add(new Run(entry.Message) { Foreground = entry.Level == AppLogLevel.Info ? MessageBrush : brush });
        return row;
    }

    private void UpdateSummary()
    {
        int errors = _entries.Count(entry => entry.Level == AppLogLevel.Error);
        int warnings = _entries.Count(entry => entry.Level == AppLogLevel.Warning);
        string file = AppLog.FilePath ?? "plik dziennika niedostępny";
        _summary.Text = $"{_entries.Count} wpisów · błędy: {errors} · uwagi: {warnings} · {file}";
    }

    private void CopyToClipboard()
    {
        IEnumerable<object> rows = _list.SelectedItems.Count > 0 ? _list.SelectedItems.Cast<object>() : _list.Items.Cast<object>();
        string text = string.Join(Environment.NewLine, rows
            .Select(row => (row as FrameworkElement)?.Tag)
            .OfType<AppLogEntry>()
            .Select(AppLog.Format));
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // Schowek bywa chwilowo zajęty przez inny program.
            AppLog.Warn("Konsola", "Nie udało się skopiować do schowka: " + ex.Message);
        }
    }

    private static void OpenLogFolder()
    {
        string? path = AppLog.FilePath;
        if (path == null)
            return;
        try
        {
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Path.GetDirectoryName(path) is { } directory)
                Process.Start("explorer.exe", $"\"{directory}\"");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Konsola", "Nie udało się otworzyć folderu logów: " + ex.Message);
        }
    }
}
