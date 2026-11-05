using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Prawie przezroczysta osłona nad oknem gry podczas kopania w tle. Przechwytuje mysz,
// więc przypadkowy ruch czy klik nie trafi do Minecrafta (który wtedy przejąłby fokus
// i przerwał sesję w tle). Nie aktywuje się, nie ma właściciela (żadnego łączenia
// kolejek wejścia z grą) i stoi w kolejności okien tuż nad grą — nie zasłania
// innych programów. Celowe przejęcie: przycisk na osłonie, Alt+Tab albo pasek zadań.
internal sealed class GameWindowShield : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTopmost = 0x00000008;
    private const uint GwHwndPrev = 3;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private static readonly TimeSpan TrackInterval = TimeSpan.FromMilliseconds(50);
    // Niewidoczny pas wokół okna gry: kursor trafia na osłonę, zanim dotknie ramki gry.
    private const double EdgeDip = 16;
    private const int DwmwaExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out NativeRect value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);
    }

    private static readonly Guid VirtualDesktopManagerClsid = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    private readonly Border _card;
    private IVirtualDesktopManager? _desktops;
    private bool _desktopsUnavailable;
    private IntPtr _handle;
    private DateTime _nextTrackAtUtc;
    private (int X, int Y, int Width, int Height) _lastBounds;

    public event EventHandler? TakeOverRequested;

    public GameWindowShield()
    {
        Title = "Minecraft Helper — osłona okna gry";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Focusable = false;
        Width = 1;
        Height = 1;

        Color accent = (GlassPalette.AccentSolid as SolidColorBrush)?.Color ?? Color.FromRgb(0x54, 0x58, 0xE8);

        // Ikona: mysz przekreślona — „ręczne sterowanie zablokowane”.
        var icon = new Grid { Width = 24, Height = 24 };
        icon.Children.Add(new Path
        {
            Data = Geometry.Parse("M12,3 C8.7,3 7,5.4 7,8.5 V15.5 C7,18.6 8.7,21 12,21 C15.3,21 17,18.6 17,15.5 V8.5 C17,5.4 15.3,3 12,3 Z M12,3 V9"),
            Stroke = new SolidColorBrush(accent),
            StrokeThickness = 1.9,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        icon.Children.Add(new Path
        {
            Data = Geometry.Parse("M4,4 L20,20"),
            Stroke = GlassPalette.Bad,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });

        var takeOver = new Button
        {
            Content = "Przejmij grę",
            Margin = new Thickness(10, 0, 0, 0),
            MinHeight = 26,
            Padding = new Thickness(10, 2, 10, 2),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = false,
            ToolTip = "Zatrzymuje kopanie tego konta i przenosi Cię do okna gry."
        };
        takeOver.Click += (_, __) => TakeOverRequested?.Invoke(this, EventArgs.Empty);

        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        label.Inlines.Add(new System.Windows.Documents.Run("Kopacz kopie w tle")
        {
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = GlassPalette.Ink
        });
        label.Inlines.Add(new System.Windows.Documents.Run(" · mysz zablokowana")
        {
            FontSize = 11.5,
            Foreground = GlassPalette.Muted
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Viewbox { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Child = icon });
        row.Children.Add(label);
        row.Children.Add(takeOver);

        // Wąski pasek przy górnej krawędzi — nie zasłania celownika ani kopanego terenu.
        // Lekko przygaszony; wyraźny, gdy mysz jest nad osłoną.
        _card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 5, 6, 5),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0.55,
            Cursor = Cursors.Arrow,
            Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = 0.16,
                Color = Color.FromRgb(0x1E, 0x24, 0x50)
            }
        };

        // Bardzo delikatny odcień akcentu i ramka — widać, że okno jest „zajęte”.
        // Odcień obejmuje całe okno gry (z paskiem tytułu); pas EdgeDip dookoła jest prawie
        // przezroczysty (alfa 1), ale nadal łapie mysz — piksele z alfą 0 przepuszczałyby kliknięcia.
        var veil = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(EdgeDip),
            Child = _card
        };
        var root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)),
            Cursor = Cursors.No,
            Child = veil
        };
        root.MouseEnter += (_, __) => _card.Opacity = 1;
        root.MouseLeave += (_, __) => _card.Opacity = 0.55;
        Content = root;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(_handle, GwlExStyle);
        _ = SetWindowLong(_handle, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    // Wywoływane co tick pętli; samo ogranicza się do kilku aktualizacji na sekundę.
    public void Track(IntPtr gameWindow, bool active)
    {
        DateTime now = DateTime.UtcNow;
        if (active && IsVisible && now < _nextTrackAtUtc)
            return;
        _nextTrackAtUtc = now + TrackInterval;

        if (!active || !CanCover(gameWindow, out (int X, int Y, int Width, int Height) bounds))
        {
            if (IsVisible)
                Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
            _lastBounds = default;
        }

        // Osłona nie może zostać oknem „zawsze na wierzchu”: przepychałaby się wtedy
        // z HUD-em (też topmost) i HUD migotałby co kilkadziesiąt milisekund.
        if ((GetWindowLong(_handle, GwlExStyle) & WsExTopmost) != 0)
            _ = SetWindowPos(_handle, HwndNoTopmost, 0, 0, 0, 0, SwpNoActivate | SwpNoMove | SwpNoSize);

        // Tuż nad grą: pod najbliższym widocznym oknem nad nią. Niewidoczne okna pomijamy,
        // a okna topmost (np. HUD) traktujemy jak górę — wstawienie pod nie dałoby osłonie
        // atrybut topmost. Gdy osłona już stoi bezpośrednio nad grą, kolejności nie ruszamy.
        IntPtr above = GetWindow(gameWindow, GwHwndPrev);
        while (above != IntPtr.Zero && above != _handle && !IsWindowVisible(above))
            above = GetWindow(above, GwHwndPrev);

        uint flags = SwpNoActivate | SwpShowWindow;
        IntPtr insertAfter = above == IntPtr.Zero || (GetWindowLong(above, GwlExStyle) & WsExTopmost) != 0
            ? HwndTop
            : above;
        if (above == _handle)
            flags |= SwpNoZOrder;
        if (bounds == _lastBounds && (flags & SwpNoZOrder) != 0)
            return;

        _lastBounds = bounds;
        _ = SetWindowPos(_handle, insertAfter, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
    }

    private bool CanCover(IntPtr gameWindow, out (int X, int Y, int Width, int Height) bounds)
    {
        bounds = default;
        if (gameWindow == IntPtr.Zero || !IsWindowVisible(gameWindow) || IsIconic(gameWindow) || !IsOnCurrentDesktop(gameWindow))
            return false;
        // Całe widoczne okno (pasek tytułu i ramka też należą do gry), bez niewidzialnych
        // marginesów cienia, powiększone o pas EdgeDip w pikselach tego monitora.
        if (DwmGetWindowAttribute(gameWindow, DwmwaExtendedFrameBounds, out NativeRect frame, Marshal.SizeOf<NativeRect>()) != 0
            && !GetWindowRect(gameWindow, out frame))
            return false;
        if (frame.Right <= frame.Left || frame.Bottom <= frame.Top)
            return false;

        uint dpi = GetDpiForWindow(gameWindow);
        int edge = (int)Math.Round(EdgeDip * (dpi == 0 ? 96 : dpi) / 96.0);
        bounds = (frame.Left - edge, frame.Top - edge, frame.Right - frame.Left + 2 * edge, frame.Bottom - frame.Top + 2 * edge);
        return true;
    }

    // Gra na innym pulpicie wirtualnym: osłona nie może się pojawić na pulpicie użytkownika.
    private bool IsOnCurrentDesktop(IntPtr gameWindow)
    {
        if (_desktopsUnavailable)
            return true;
        try
        {
            _desktops ??= (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(VirtualDesktopManagerClsid)!)!;
            return _desktops.IsWindowOnCurrentVirtualDesktop(gameWindow, out bool onCurrent) != 0 || onCurrent;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            _desktopsUnavailable = true;
            AppLog.Warn("Osłona", "Brak informacji o pulpitach wirtualnych — osłona zakłada, że gra jest na bieżącym pulpicie.");
            return true;
        }
    }
}
