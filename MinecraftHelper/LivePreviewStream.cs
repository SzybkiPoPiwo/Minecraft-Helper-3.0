using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinecraftHelper.Services;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using Image = System.Windows.Controls.Image;

namespace MinecraftHelper;

// Podgląd Live na żywo, przechwytywany w procesie, który go wyświetla.
// PrintWindow rysuje okno gry do stałego bufora DIB, a obraz w GUI aktualizuje się przez
// WriteableBitmap — bez JPEG, base64 i kanału między procesami. Gdy GUI nie nadąża,
// klatka jest pomijana zamiast czekać w kolejce, więc podgląd nigdy się nie spóźnia.
internal sealed class LivePreviewStream : IDisposable
{
    private const uint PwClientOnly = 0x00000001;
    private const uint PwRenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    // Domyślnie Thread.Sleep ma rozdzielczość ~15,6 ms — za mało na równe 30–60 kl./s.
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    private readonly Image _target;
    private readonly Dispatcher _dispatcher;
    private readonly Thread _worker;
    private readonly IntPtr _window;
    private volatile bool _stopping;
    private volatile int _fps;
    private volatile MinerPreviewRegion _region;
    private int _uiBusy;
    private WriteableBitmap? _bitmap;
    // Piksele Bgr32 jako int — jeden element na piksel ułatwia zmniejszanie obrazu.
    private int[] _frame = Array.Empty<int>();
    private int[] _row = Array.Empty<int>();
    private readonly int _maxFrameWidth;
    private volatile bool _paused;

    // DIB z bitami gry; odtwarzany tylko przy zmianie rozmiaru okna.
    private IntPtr _memoryDc;
    private IntPtr _dib;
    private IntPtr _previousObject;
    private IntPtr _bits;
    private int _dibWidth;
    private int _dibHeight;

    // maxFrameWidth: mała karta nie potrzebuje pełnej rozdzielczości gry — zmniejszony obraz
    // to kilka razy mniej kopiowania w wątku GUI. 0 = pełny rozmiar.
    public LivePreviewStream(Image target, IntPtr window, int fps, MinerPreviewRegion region = MinerPreviewRegion.Full, int maxFrameWidth = 0)
    {
        _maxFrameWidth = maxFrameWidth;
        _target = target;
        _dispatcher = target.Dispatcher;
        _window = window;
        Fps = fps;
        _region = region;
        _worker = new Thread(Run) { IsBackground = true, Name = "Podgląd Live", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    public IntPtr Window => _window;

    public int Fps
    {
        get => _fps;
        set => _fps = Math.Clamp(value, 1, 60);
    }

    public MinerPreviewRegion Region
    {
        get => _region;
        set => _region = value;
    }

    // Wstrzymany strumień nie przechwytuje okna (np. gdy okno pokazuje zapamiętany zrzut EQ).
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    // Rzeczywista liczba klatek pokazanych w ostatniej sekundzie.
    public int MeasuredFps { get; private set; }

    public int FrameWidth { get; private set; }

    public int FrameHeight { get; private set; }

    // Wołane w wątku UI, gdy obraz jest niedostępny (zminimalizowane, zamknięte okno) albo wraca.
    public event Action<bool>? AvailabilityChanged;

    // Jedno zdjęcie okna (karta bez Live). Wolno wołać z wątku w tle — wynik jest zamrożony.
    public static BitmapSource? CaptureOnce(IntPtr window)
    {
        if (!BackgroundGameWindow.TryCaptureClient(window, out Bitmap? bitmap) || bitmap == null)
            return null;

        using (bitmap)
        {
            var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppArgb);
            try
            {
                BitmapSource source = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * area.Height, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        if (Thread.CurrentThread != _worker)
            _worker.Join(TimeSpan.FromSeconds(1));
    }

    private void Run()
    {
        _ = timeBeginPeriod(1);
        bool? lastAvailable = null;
        var clock = Stopwatch.StartNew();
        long nextFrameAt = 0;
        long secondStartedAt = 0;
        int framesThisSecond = 0;
        long failingSince = -1;
        try
        {
            while (!_stopping)
            {
                long now = clock.ElapsedMilliseconds;
                if (_paused)
                {
                    Thread.Sleep(50);
                    continue;
                }
                if (now < nextFrameAt)
                {
                    Thread.Sleep((int)Math.Min(nextFrameAt - now, 50));
                    continue;
                }
                nextFrameAt = now + 1000 / Math.Max(1, _fps);

                bool available = IsWindow(_window) && !IsIconic(_window);
                if (available && Volatile.Read(ref _uiBusy) == 0)
                {
                    if (TryCaptureFrame(out int width, out int height))
                    {
                        failingSince = -1;
                        Interlocked.Exchange(ref _uiBusy, 1);
                        framesThisSecond++;
                        _dispatcher.BeginInvoke(DispatcherPriority.Background, () => Present(width, height));
                    }
                    else
                    {
                        // Pojedynczy nieudany zrzut zdarza się np. przy przerysowaniu gry — zostaje
                        // ostatnia klatka. „Niedostępne” dopiero po sekundzie ciągłych błędów.
                        if (failingSince < 0)
                            failingSince = now;
                        available = now - failingSince < 1000 && lastAvailable != false;
                    }
                }

                if (available != lastAvailable)
                {
                    lastAvailable = available;
                    bool value = available;
                    _dispatcher.BeginInvoke(() => AvailabilityChanged?.Invoke(value));
                }

                if (now - secondStartedAt >= 1000)
                {
                    MeasuredFps = framesThisSecond;
                    framesThisSecond = 0;
                    secondStartedAt = now;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Podgląd", "Podgląd Live przestał działać", ex);
        }
        finally
        {
            ReleaseDib();
            _ = timeEndPeriod(1);
        }
    }

    private bool TryCaptureFrame(out int width, out int height)
    {
        width = height = 0;
        if (!GetClientRect(_window, out NativeRect client))
            return false;
        int fullWidth = client.Right - client.Left;
        int fullHeight = client.Bottom - client.Top;
        if (fullWidth <= 0 || fullHeight <= 0 || !EnsureDib(fullWidth, fullHeight))
            return false;
        if (!PrintWindow(_window, _memoryDc, PwClientOnly | PwRenderFullContent))
            return false;

        Rectangle crop = GetCrop(new Rectangle(0, 0, fullWidth, fullHeight));
        if (crop.Width <= 0 || crop.Height <= 0)
            return false;

        // Zmniejszanie o całkowity współczynnik przez wybór co n-tego piksela: tanie, a na
        // małej karcie niczym nie różni się od skalowania z wygładzaniem.
        int factor = _maxFrameWidth > 0 ? Math.Max(1, (int)Math.Ceiling(crop.Width / (double)_maxFrameWidth)) : 1;
        int outWidth = Math.Max(1, crop.Width / factor);
        int outHeight = Math.Max(1, crop.Height / factor);
        if (_frame.Length != outWidth * outHeight)
            _frame = new int[outWidth * outHeight];
        if (_row.Length < crop.Width)
            _row = new int[crop.Width];

        int sourceStride = _dibWidth * 4;
        for (int y = 0; y < outHeight; y++)
        {
            IntPtr source = _bits + (crop.Top + y * factor) * sourceStride + crop.Left * 4;
            if (factor == 1)
            {
                Marshal.Copy(source, _frame, y * outWidth, outWidth);
                continue;
            }
            Marshal.Copy(source, _row, 0, crop.Width);
            int offset = y * outWidth;
            for (int x = 0; x < outWidth; x++)
                _frame[offset + x] = _row[x * factor];
        }

        width = outWidth;
        height = outHeight;
        return true;
    }

    private Rectangle GetCrop(Rectangle full)
    {
        switch (_region)
        {
            case MinerPreviewRegion.Hotbar:
                return MinerLivePreviewService.GetHotbarRectangle(full);
            default:
                return full;
        }
    }

    private bool EnsureDib(int width, int height)
    {
        if (_dib != IntPtr.Zero && width == _dibWidth && height == _dibHeight)
            return true;

        ReleaseDib();
        var info = new BitmapInfo
        {
            Size = Marshal.SizeOf<BitmapInfo>(),
            Width = width,
            Height = -height, // ujemna wysokość = wiersze od góry, jak w WPF
            Planes = 1,
            BitCount = 32
        };
        _memoryDc = CreateCompatibleDC(IntPtr.Zero);
        _dib = CreateDIBSection(_memoryDc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_memoryDc == IntPtr.Zero || _dib == IntPtr.Zero || _bits == IntPtr.Zero)
        {
            ReleaseDib();
            return false;
        }

        _previousObject = SelectObject(_memoryDc, _dib);
        _dibWidth = width;
        _dibHeight = height;
        return true;
    }

    private void ReleaseDib()
    {
        if (_memoryDc != IntPtr.Zero && _previousObject != IntPtr.Zero)
            SelectObject(_memoryDc, _previousObject);
        if (_dib != IntPtr.Zero)
            DeleteObject(_dib);
        if (_memoryDc != IntPtr.Zero)
            DeleteDC(_memoryDc);
        _dib = _memoryDc = _previousObject = _bits = IntPtr.Zero;
        _dibWidth = _dibHeight = 0;
    }

    // Wątek UI: kopiuje klatkę do jednej, ponownie używanej bitmapy.
    private void Present(int width, int height)
    {
        try
        {
            if (_stopping)
                return;
            if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                _target.Source = _bitmap;
            }
            else if (!ReferenceEquals(_target.Source, _bitmap))
            {
                _target.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _frame, width * 4, 0);
            FrameWidth = width;
            FrameHeight = height;
        }
        finally
        {
            Interlocked.Exchange(ref _uiBusy, 0);
        }
    }
}
