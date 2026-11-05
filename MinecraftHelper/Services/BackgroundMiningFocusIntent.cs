using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace MinecraftHelper.Services;

// A held button (GetAsyncKeyState) does not identify a click or its destination.
// Only recent device input can authorize leaving the background session.
internal sealed class BackgroundMiningFocusIntent
{
    private const uint SwitchWindowMilliseconds = 1000;
    private int _lastSwitchAt;
    private bool _switchRequested;
    private bool _keyboardSwitch;
    private bool _lastSwitchByKeyboard;
    private bool _unexpectedActivation;
    private int _unexpectedActivationAt;
    private int _altKeys;
    private int _windowsKeys;

    public void ObserveMouse(ushort buttonFlags, bool gameOrTaskbar, int timestamp)
    {
        // DOWN transitions only: movement, wheel and releasing a button cannot
        // turn an unsolicited activation into an intentional switch.
        if ((buttonFlags & 0x0155) == 0)
            return;

        _switchRequested = gameOrTaskbar;
        _keyboardSwitch = false;
        _lastSwitchByKeyboard = false;
        _lastSwitchAt = timestamp;
    }

    public void ObserveKeyboard(ushort key, bool up, int timestamp)
    {
        int altBit = key switch { 0x12 or 0xA4 => 1, 0xA5 => 2, _ => 0 };
        int windowsBit = key switch { 0x5B => 1, 0x5C => 2, _ => 0 };
        if (altBit != 0)
            _altKeys = up ? _altKeys & ~altBit : _altKeys | altBit;
        if (windowsBit != 0)
            _windowsKeys = up ? _windowsKeys & ~windowsBit : _windowsKeys | windowsBit;

        bool switchKeys = (_altKeys != 0 && key is 0x09 or 0x1B)
            || (_windowsKeys != 0 && (key is 0x09 or 0x44 || key is >= 0x25 and <= 0x28 || key is >= 0x30 and <= 0x39));
        if (!up && switchKeys)
        {
            _switchRequested = true;
            _keyboardSwitch = true;
            _lastSwitchByKeyboard = true;
            _lastSwitchAt = timestamp;
        }
        else if (up && _keyboardSwitch && (altBit != 0 || windowsBit != 0))
        {
            // Alt+Tab may remain open longer than the click allowance. Its final
            // modifier release, rather than the first Tab, commits the switch.
            _lastSwitchAt = timestamp;
            if (_altKeys == 0 && _windowsKeys == 0)
                _keyboardSwitch = false;
        }
    }

    public bool IsSwitchRequested(int timestamp) => _switchRequested
        && unchecked((uint)(timestamp - _lastSwitchAt)) <= SwitchWindowMilliseconds;

    public bool IsKeyboardSwitchRequested(int timestamp) => _lastSwitchByKeyboard && IsSwitchRequested(timestamp);

    public bool ShouldReturnForeground(bool gameFocused, int timestamp)
    {
        if (!gameFocused || IsSwitchRequested(timestamp))
        {
            _unexpectedActivation = false;
            return false;
        }

        if (!_unexpectedActivation)
        {
            _unexpectedActivation = true;
            _unexpectedActivationAt = timestamp;
        }

        // Foreground activation and WM_INPUT are delivered to different process
        // queues. Give a real click time to arrive; stay on PostMessage meanwhile.
        return unchecked((uint)(timestamp - _unexpectedActivationAt)) >= 50;
    }
}

// Raw Input observes device transitions without a global mouse hook, suppressing
// legacy messages, or intercepting input meant for the user's other applications.
// Registered only for the lifetime of one background mining session.
internal sealed class BackgroundMiningFocusObserver : IDisposable
{
    private const uint InputSink = 0x00000100;
    private const uint Remove = 0x00000001;
    private const uint RawInputCommand = 0x10000003;
    private const int WmInput = 0x00FF;
    private static readonly object RegistrationGate = new();
    private static BackgroundMiningFocusObserver? _registrationOwner;
    private readonly IntPtr _gameWindow;
    private readonly BackgroundMiningFocusIntent _intent = new();
    private readonly object _intentGate = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private readonly BackgroundCursorStateStore _cursorState = new();
    private HwndSource? _source;
    private Dispatcher? _dispatcher;
    private volatile bool _registered;
    private int _disposeRequested;
    private string _gameClass = string.Empty;

    public bool IsCursorProtectionAvailable => _registered && _cursorState.IsAvailable;

    private BackgroundMiningFocusObserver(IntPtr gameWindow)
    {
        _gameWindow = gameWindow;
        _thread = new Thread(RunInputLoop) { IsBackground = true, Name = "MinecraftHelper.BackgroundInput" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    private void RunInputLoop()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _source = new HwndSource(new HwndSourceParameters("MinecraftHelper.BackgroundFocusInput")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE: never shown or activated.
                WindowStyle = 0,
                Width = 0,
                Height = 0
            });
            _source.AddHook(WindowProc);
            var className = new StringBuilder(128);
            _ = GetClassName(_gameWindow, className, className.Capacity);
            _gameClass = className.ToString();
            RawInputDevice[] devices = Devices(InputSink, _source.Handle);
            lock (RegistrationGate)
            {
                _registered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
                if (_registered)
                    _registrationOwner = this;
            }
            if (!_registered)
            {
                AppLog.Warn("Tło", $"Nie udało się włączyć obserwacji myszy (Raw Input, kod {Marshal.GetLastWin32Error()}) — klik w grę może nie kończyć sesji w tle.");
                return;
            }
            // Chat/EQ changes grab state asynchronously, including while the main
            // thread is busy with PrintWindow/OCR. Keep this pump independent.
            var guardTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(5)
            };
            guardTimer.Tick += (_, _) => ProtectUserCursor();
            guardTimer.Start();
            _ready.Set();
            if (Volatile.Read(ref _disposeRequested) == 0)
                Dispatcher.Run();
            guardTimer.Stop();
        }
        catch (Exception ex)
        {
            AppLog.Error("Tło", "Obserwacja wejścia w tle przestała działać", ex);
        }
        finally
        {
            lock (RegistrationGate)
            {
                // A delayed shutdown must never remove a newer session's target.
                if (ReferenceEquals(_registrationOwner, this))
                {
                    RawInputDevice[] devices = Devices(Remove, IntPtr.Zero);
                    _ = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
                    _registrationOwner = null;
                }
                _registered = false;
            }
            _source?.RemoveHook(WindowProc);
            _source?.Dispose();
            _cursorState.Dispose();
            _ready.Set();
        }
    }

    public static BackgroundMiningFocusObserver? TryCreate(IntPtr gameWindow)
    {
        var observer = new BackgroundMiningFocusObserver(gameWindow);
        observer._thread.Start();
        if (observer._ready.Wait(TimeSpan.FromSeconds(2)) && observer._registered)
            return observer;
        observer.Dispose();
        return null;
    }

    public bool IsSwitchRequested
    {
        get { lock (_intentGate) return _intent.IsSwitchRequested(Environment.TickCount); }
    }

    public bool ShouldReturnForeground(bool gameFocused)
    {
        lock (_intentGate) return _intent.ShouldReturnForeground(gameFocused, Environment.TickCount);
    }

    public void ArmCursorTransition()
    {
        if (_gameWindow == IntPtr.Zero || Volatile.Read(ref _disposeRequested) != 0)
            return;
        ProtectUserCursor();
        if (GetCursorPos(out Point cursor))
            _cursorState.Arm(new Drawing.Point(cursor.X, cursor.Y), Environment.TickCount);
    }

    public void ProtectUserCursor()
    {
        if (Volatile.Read(ref _disposeRequested) != 0 || !_registered || !GetCursorPos(out Point cursor)
            || !BackgroundGameWindow.TryGetCursorGuardGeometry(_gameWindow, out Drawing.Rectangle client))
            return;
        int timestamp = Environment.TickCount;
        if (IsUserSelectedGame(timestamp))
            return;

        if (GetClipCursor(out NativeRect clip) && clip.Left == client.Left && clip.Top == client.Top
            && clip.Right == client.Right && clip.Bottom == client.Bottom)
            _ = ClipCursor(IntPtr.Zero);

        var center = new Drawing.Point(client.Left + client.Width / 2, client.Top + client.Height / 2);
        Drawing.Point? target = _cursorState.RestoreTarget(new Drawing.Point(cursor.X, cursor.Y), center, false, timestamp);
        if (target is { } restore)
            RestoreCursor(restore);
    }

    private bool IsUserSelectedGame(int timestamp)
    {
        bool switchRequested;
        bool keyboardSwitch;
        lock (_intentGate)
        {
            switchRequested = _intent.IsSwitchRequested(timestamp);
            keyboardSwitch = _intent.IsKeyboardSwitchRequested(timestamp);
        }
        IntPtr foreground = GetForegroundWindow();
        if (keyboardSwitch)
            _cursorState.SelectUserWindow(foreground);
        if (foreground == IntPtr.Zero || (_cursorState.UserWindow != foreground
            && !(switchRequested && foreground == _gameWindow)))
            return false;
        var name = new StringBuilder(128);
        _ = GetClassName(foreground, name, name.Capacity);
        return _gameClass.Length > 0 && name.ToString() == _gameClass;
    }

    private void RestoreCursor(Drawing.Point point)
    {
        _ = SetCursorPos(point.X, point.Y);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmInput || Volatile.Read(ref _disposeRequested) != 0)
            return IntPtr.Zero;

        uint size = (uint)Marshal.SizeOf<RawInput>();
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint read = GetRawInputData(lParam, RawInputCommand, out RawInput input, ref size, headerSize);
        if (read == uint.MaxValue || read < headerSize + 16)
            return IntPtr.Zero;

        // Use the event's timestamp and position, before LWJGL can recenter the
        // current cursor. Discard queued old events rather than extending them.
        int timestamp = GetMessageTime();
        if (input.Header.Type == 0)
        {
            uint position = GetMessagePos();
            var point = new Point { X = unchecked((short)position), Y = unchecked((short)(position >> 16)) };
            if ((input.Data.MouseButtons & 0x0155) != 0)
            {
                IntPtr root = GetAncestor(WindowFromPoint(point), 2); // GA_ROOT
                var className = new StringBuilder(128);
                _ = GetClassName(root, className, className.Capacity);
                bool destination = root == _gameWindow || className.ToString() is
                    "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "TaskListThumbnailWnd";
                lock (_intentGate) _intent.ObserveMouse(input.Data.MouseButtons, destination, timestamp);
                if (_gameWindow != IntPtr.Zero)
                    _cursorState.SelectUserWindow(root);
            }
            if (_gameWindow != IntPtr.Zero
                && (input.Data.MouseX != 0 || input.Data.MouseY != 0 || (input.Data.MouseFlags & 1) != 0))
            {
                Drawing.Point? correction = _cursorState.ObserveMouse(new Drawing.Point(point.X, point.Y),
                    input.Data.MouseX, input.Data.MouseY, (input.Data.MouseFlags & 1) != 0, timestamp,
                    IsUserSelectedGame(Environment.TickCount));
                if (correction is { } corrected)
                    RestoreCursor(corrected);
            }
        }
        else if (input.Header.Type == 1)
        {
            ushort key = input.Data.VirtualKey;
            if (key == 0x12)
                key = (input.Data.KeyboardFlags & 2) != 0 ? (ushort)0xA5 : (ushort)0xA4;
            lock (_intentGate) _intent.ObserveKeyboard(key, (input.Data.KeyboardFlags & 1) != 0, timestamp);
        }

        // Let DefWindowProc perform WM_INPUT cleanup.
        return IntPtr.Zero;
    }

    private static RawInputDevice[] Devices(uint flags, IntPtr target) => new[]
    {
        new RawInputDevice { UsagePage = 1, Usage = 2, Flags = flags, Target = target },
        new RawInputDevice { UsagePage = 1, Usage = 6, Flags = flags, Target = target }
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            return;
        Dispatcher? dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
        {
            if (_thread.Join(TimeSpan.FromSeconds(2)))
                _ready.Dispose();
        }
        else if (!_thread.IsAlive)
            _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage, Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type, Size;
        public IntPtr Device, WParam;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct RawInputData
    {
        [FieldOffset(0)] public ushort MouseFlags;
        [FieldOffset(4)] public ushort MouseButtons;
        [FieldOffset(12)] public int MouseX;
        [FieldOffset(16)] public int MouseY;
        [FieldOffset(2)] public ushort KeyboardFlags;
        [FieldOffset(6)] public ushort VirtualKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawInputData Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetClipCursor(out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rectangle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr input, uint command, out RawInput data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern int GetMessageTime();

    [DllImport("user32.dll")]
    private static extern uint GetMessagePos();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
}
