using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace MinecraftHelper.Services
{
    // Praca Kopacza, gdy Minecraft nie ma fokusu: inny pulpit wirtualny, okno zasłonięte
    // albo zminimalizowane (wtedy na ślepo, bez obrazu).
    // Wejście trafia bezpośrednio do kolejki okna gry (PostMessage), a obraz jest
    // pobierany przez PrintWindow, więc nic nie dotyka aktywnej aplikacji użytkownika.
    // Klasa jest używana wyłącznie przez edycję Kopacza (nie jest linkowana do PvP).
    internal static class BackgroundGameWindow
    {
        private const uint WM_ACTIVATE = 0x0006;
        private const uint WM_SETFOCUS = 0x0007;
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_SYSKEYDOWN = 0x0104;
        private const uint WM_SYSKEYUP = 0x0105;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const int WA_ACTIVE = 1;
        private const int MK_LBUTTON = 0x0001;
        private const int MK_RBUTTON = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const uint PW_CLIENTONLY = 0x00000001;
        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        // Rozmiar obszaru gry 854×480 — domyślne okno Minecrafta.
        public const int PreparedClientWidth = 854;
        public const int PreparedClientHeight = 480;

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;

        // LWJGL centruje kursor po przejęciu myszy. Pozycję w odległości do 2 px od
        // środka okna gry traktujemy jako taki skok, a nie ruch użytkownika.
        private const int CursorCenterTolerance = 2;

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClipCursor(out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClipCursor(IntPtr rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustWindowRectEx(ref RECT rect, int style, bool menu, int exStyle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LockSetForegroundWindow(uint lockCode);

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint GetWindowProcessId(IntPtr window, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public int Length;
            public int Flags;
            public int ShowCmd;
            public POINT MinPosition;
            public POINT MaxPosition;
            public RECT NormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private static int _heldMouseButtons;
        private static int _clientCursorX;
        private static int _clientCursorY;
        private static POINT _lastUserCursor;
        private static bool _hasLastUserCursor;
        private static Point _focusMousePosition;
        private static bool _needsInitialMouseBaseline;
        private static BackgroundMiningFocusObserver? _focusObserver;
        private static bool _foregroundLocked;
        private static MinerLayoutSlotLease? _layoutSlot;
        private static IntPtr _layoutWindow;

        public static string? LastPrepareError { get; private set; }

        public static void ReleaseLayoutUnlessTarget(IntPtr window)
        {
            if (_layoutWindow == window)
                return;
            _layoutSlot?.Dispose();
            _layoutSlot = null;
            _layoutWindow = IntPtr.Zero;
        }

        public static IntPtr Window { get; private set; }

        public static bool IsActive => Window != IntPtr.Zero;

        public static bool NeedsInitialMouseBaseline => _needsInitialMouseBaseline;

        public static bool IsUserSwitchRequested => _focusObserver?.IsSwitchRequested == true;

        public static bool ShouldReturnForeground(bool gameFocused) =>
            _focusObserver?.ShouldReturnForeground(gameFocused) ?? gameFocused;

        // Sterowanie komunikatami działa także przy zminimalizowanej
        // grze ("na ślepo"). Obraz (PrintWindow) jest wtedy niedostępny.
        public static bool CanUse(IntPtr window)
        {
            return window != IntPtr.Zero && IsWindow(window);
        }

        public static bool CanCapture(IntPtr window)
        {
            return CanUse(window) && !IsIconic(window);
        }

        public static bool IsMinimized(IntPtr window)
        {
            return window != IntPtr.Zero && IsIconic(window);
        }

        // Stable slots shared by all profiles; never move another active game.
        // Restore without activation. Fullscreen (F11, no caption) is left alone.
        public static bool PrepareWindow(IntPtr window)
        {
            LastPrepareError = null;
            ReleaseLayoutUnlessTarget(window);
            if (!CanUse(window))
            {
                LastPrepareError = "Okno gry jest niedostępne.";
                return false;
            }

            int style = GetWindowLong(window, GWL_STYLE);
            int exStyle = GetWindowLong(window, GWL_EXSTYLE);
            if ((style & WS_CAPTION) != WS_CAPTION)
                return false;

            var frame = new RECT { Left = 0, Top = 0, Right = PreparedClientWidth, Bottom = PreparedClientHeight };
            if (!AdjustWindowRectEx(ref frame, style, false, exStyle))
            {
                LastPrepareError = "Nie udało się odczytać rozmiaru ramki okna gry.";
                return false;
            }

            var screens = System.Windows.Forms.Screen.AllScreens.OrderByDescending(screen => screen.Primary)
                .ThenBy(screen => screen.DeviceName, StringComparer.Ordinal).ToArray();
            var tiles = MinerWindowLayout.CreateGameTiles(screens.Select(screen => screen.WorkingArea),
                new Size(PreparedClientWidth, PreparedClientHeight),
                new Size(frame.Right - frame.Left - PreparedClientWidth, frame.Bottom - frame.Top - PreparedClientHeight));
            if (_layoutSlot != null && _layoutSlot.Index >= tiles.Count)
                ReleaseLayoutUnlessTarget(IntPtr.Zero);
            _layoutSlot ??= MinerLayoutSlotLease.TryAcquire("Game", tiles.Count);
            if (_layoutSlot == null)
            {
                LastPrepareError = "Brak miejsca na kolejne okno gry. Zwolnij miejsce, zamykając inny profil Kopacza, albo podłącz dodatkowy monitor.";
                return false;
            }
            _layoutWindow = window;
            Rectangle target = tiles[_layoutSlot.Index].Bounds;
            var screen = screens.First(item => item.WorkingArea.Contains(target));
            var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(window, ref placement))
            {
                LastPrepareError = "Nie udało się odczytać położenia okna gry.";
                return false;
            }

            // WINDOWPLACEMENT uses workspace coordinates; SetWindowPos uses screen pixels.
            int x = target.X - (screen.WorkingArea.Left - screen.Bounds.Left);
            int y = target.Y - (screen.WorkingArea.Top - screen.Bounds.Top);
            placement.ShowCmd = SW_SHOWNOACTIVATE;
            placement.NormalPosition = new RECT { Left = x, Top = y, Right = x + target.Width, Bottom = y + target.Height };
            _ = SetWindowPlacement(window, ref placement);
            bool moved = SetWindowPos(window, IntPtr.Zero, target.X, target.Y, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            if (!moved)
                LastPrepareError = "Nie udało się ustawić okna gry obok pozostałych kont.";
            return moved;
        }

        public static Size GetClientSize(IntPtr window)
        {
            if (GetClientRect(window, out RECT rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
            return new Size(PreparedClientWidth, PreparedClientHeight);
        }

        // WindowsMouse stores Y from the bottom, whereas WM_MOUSEMOVE carries Y
        // from the top. Keep the old grabbed-camera baseline across window resizing.
        internal static Point CalculateFocusMousePosition(int previousWidth, int previousHeight, int currentHeight)
        {
            int oldX = Math.Max(1, previousWidth) / 2;
            int oldYFromBottom = Math.Max(1, previousHeight) - 1 - Math.Max(1, previousHeight) / 2;
            return new Point(oldX, Math.Max(1, currentHeight) - 1 - oldYFromBottom);
        }

        public static void Activate(IntPtr window, Size previousClientSize)
        {
            _focusObserver?.Dispose();
            _focusObserver = BackgroundMiningFocusObserver.TryCreate(window);
            Window = window;
            _heldMouseButtons = 0;
            _hasLastUserCursor = false;

            // W grze kursor jest przejęty, a kopanie celuje w środek ekranu. Zminimalizowane
            // okno ma pusty obszar — wtedy stały środek 854×480.
            if (GetClientRect(window, out RECT rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                _clientCursorX = (rect.Right - rect.Left) / 2;
                _clientCursorY = (rect.Bottom - rect.Top) / 2;
            }
            else
            {
                _clientCursorX = PreparedClientWidth / 2;
                _clientCursorY = PreparedClientHeight / 2;
            }

            _focusMousePosition = CalculateFocusMousePosition(
                previousClientSize.Width, previousClientSize.Height, GetClientSize(window).Height);
            _needsInitialMouseBaseline = true;
        }

        public static void Deactivate()
        {
            _focusObserver?.Dispose();
            _focusObserver = null;
            if (_foregroundLocked)
            {
                _ = LockSetForegroundWindow(2); // LSFW_UNLOCK; user switches also unlock it.
                _foregroundLocked = false;
            }
            Window = IntPtr.Zero;
            _heldMouseButtons = 0;
            _hasLastUserCursor = false;
            _needsInitialMouseBaseline = false;
        }

        // Gra na innym pulpicie dostała WM_KILLFOCUS przy przełączeniu. LWJGL 2 przyjmuje
        // klawisze dopiero po ponownym WM_SETFOCUS (docs/EXPERIMENTS.md), a Minecraft
        // otwiera menu pauzy po ~500 ms bez aktywnego okna.
        //
        // LWJGL może odpowiedzieć na WM_SETFOCUS żądaniem aktywacji własnego okna.
        // PostMessage unika synchronicznego wywołania z aktywnego wątku Kopacza,
        // ale sam nie zapobiega późniejszej kradzieży fokusu po ruchu myszy.
        // Pierwszy plan chronią dodatkowo blokada aktywnego Kopacza i strażnik sesji.
        public static bool SendFakeFocus(bool resetMouseBaseline = false)
        {
            if (!IsActive)
                return false;
            PrepareCursorTransition();

            // Mouse.poll() keeps adding DX/DY while Minecraft is unfocused. In
            // grabbed mode, returning its virtual position to the last camera
            // baseline cancels that unread movement before the first active frame.
            // Do this once only: a later reset could itself rotate the camera after
            // the game has consumed the deltas. Never move the real Windows cursor.
            if (_needsInitialMouseBaseline && resetMouseBaseline)
            {
                if (!PostMessage(Window, WM_MOUSEMOVE, IntPtr.Zero,
                    PositionLParam(_focusMousePosition.X, _focusMousePosition.Y)))
                    return false;
            }

            bool activated = PostMessage(Window, WM_ACTIVATE, (IntPtr)WA_ACTIVE, IntPtr.Zero);
            bool focused = PostMessage(Window, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            if (activated && focused)
                _needsInitialMouseBaseline = false;
            return activated && focused;
        }

        // When Kopacz itself is foreground, prevent LWJGL's fake focus from
        // activating the game. Windows still allows an actual user click/Alt+Tab.
        public static bool TryProtectOwnForeground()
        {
            _ = GetWindowProcessId(GetForegroundWindow(), out uint processId);
            if (processId != (uint)Environment.ProcessId)
                return false;

            bool locked = LockSetForegroundWindow(1); // LSFW_LOCK
            _foregroundLocked |= locked;
            return locked;
        }

        // Gra przejęła pierwszy plan bez działania użytkownika: oddajemy go oknu, w którym
        // użytkownik był. Kopacz nie jest wtedy aktywnym programem, więc zwykłe
        // SetForegroundWindow zostałoby odrzucone — na chwilę dzielimy kolejkę wejścia
        // z wątkiem gry (AttachThreadInput), co Windows traktuje jak zgodę aktywnego okna.
        public static bool ReturnForegroundTo(IntPtr target, IntPtr expectedForeground)
        {
            IntPtr foreground = GetForegroundWindow();
            if (target == IntPtr.Zero || foreground != expectedForeground || !IsWindow(target))
            {
                return false;
            }

            uint ownThread = GetCurrentThreadId();
            uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            bool attached = foregroundThread != 0 && foregroundThread != ownThread
                && AttachThreadInput(ownThread, foregroundThread, true);
            try
            {
                SetForegroundWindow(target);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(ownThread, foregroundThread, false);
            }

            bool returned = GetForegroundWindow() == target;
            if (!returned)
                AppLog.Warn("Tło", "Nie udało się oddać fokusu poprzedniemu oknu — gra może zostać na wierzchu.");
            return returned;
        }

        public static bool SendKey(int virtualKey, bool down)
        {
            if (!IsActive)
                return false;
            if (down && virtualKey is 0x0D or 0x1B or 0x45) // Enter / Escape / inventory
                PrepareCursorTransition();

            int scanSourceKey = virtualKey;
            bool extended = IsExtendedKey(virtualKey);
            // LWJGL 2 rozróżnia lewy/prawy modyfikator po kodzie skanowania i bicie
            // rozszerzenia, tak jak przy prawdziwej klawiaturze (VK_SHIFT + 0x2A itd.).
            switch (virtualKey)
            {
                case VK_LSHIFT:
                case VK_RSHIFT:
                    virtualKey = VK_SHIFT;
                    break;
                case VK_LCONTROL:
                    virtualKey = VK_CONTROL;
                    break;
                case VK_RCONTROL:
                    virtualKey = VK_CONTROL;
                    extended = true;
                    break;
                case VK_LMENU:
                    virtualKey = VK_MENU;
                    break;
                case VK_RMENU:
                    virtualKey = VK_MENU;
                    extended = true;
                    break;
            }

            uint scanCode = MapVirtualKey((uint)scanSourceKey, MAPVK_VK_TO_VSC) & 0xFF;
            uint lParam = 1u | (scanCode << 16) | (extended ? 1u << 24 : 0u);
            if (!down)
                lParam |= (1u << 30) | (1u << 31);

            uint message = virtualKey == VK_MENU
                ? down ? WM_SYSKEYDOWN : WM_SYSKEYUP
                : down ? WM_KEYDOWN : WM_KEYUP;
            return PostMessage(Window, message, (IntPtr)virtualKey, unchecked((IntPtr)(int)lParam));
        }

        // Pair WM_CHAR with a private pressed/released carrier so LWJGL cannot
        // attach the leading slash to the chat-opening key's release event.
        public static bool SendChar(char ch)
        {
            return IsActive && BackgroundTextInput.SendCharacter(ch, PostTextMessage);
        }

        private static bool PostTextMessage(uint message, IntPtr wParam, IntPtr lParam) =>
            PostMessage(Window, message, wParam, lParam);

        public static bool MoveCursorToScreenPoint(int screenX, int screenY)
        {
            if (!IsActive)
                return false;
            PrepareCursorTransition();

            var point = new POINT { X = screenX, Y = screenY };
            if (!ScreenToClient(Window, ref point))
                return false;

            _clientCursorX = point.X;
            _clientCursorY = point.Y;
            return PostMessage(Window, WM_MOUSEMOVE, (IntPtr)_heldMouseButtons, CursorLParam());
        }

        // Pozycja kursora gry (nie użytkownika) we współrzędnych ekranu.
        public static bool TryGetCursorScreenPosition(out int x, out int y)
        {
            x = 0;
            y = 0;
            var point = new POINT { X = _clientCursorX, Y = _clientCursorY };
            if (!IsActive || !ClientToScreen(Window, ref point))
                return false;

            x = point.X;
            y = point.Y;
            return true;
        }

        public static bool SendMouseButton(bool leftButton, bool down)
        {
            if (!IsActive)
                return false;
            if (down)
                PrepareCursorTransition();

            int flag = leftButton ? MK_LBUTTON : MK_RBUTTON;
            _heldMouseButtons = down ? _heldMouseButtons | flag : _heldMouseButtons & ~flag;
            uint message = leftButton
                ? down ? WM_LBUTTONDOWN : WM_LBUTTONUP
                : down ? WM_RBUTTONDOWN : WM_RBUTTONUP;
            return PostMessage(Window, message, (IntPtr)_heldMouseButtons, CursorLParam());
        }

        public static bool SendMouseClick(bool leftButton, bool holdPulseMode)
        {
            // Ta sama kolejność co NativeInput.SendMouseClick: impuls przy trzymaniu
            // kończy się wciśnięciem, zwykłe kliknięcie zwolnieniem przycisku.
            return holdPulseMode
                ? SendMouseButton(leftButton, down: false) && SendMouseButton(leftButton, down: true)
                : SendMouseButton(leftButton, down: true) && SendMouseButton(leftButton, down: false);
        }

        public static bool TryCaptureClient(IntPtr window, out Bitmap? bitmap)
        {
            bitmap = null;
            if (!CanCapture(window) || !GetClientRect(window, out RECT rect))
                return false;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                return false;

            try
            {
                using var rendered = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                using (Graphics graphics = Graphics.FromImage(rendered))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        // PW_RENDERFULLCONTENT jest potrzebne dla okien OpenGL; bez niego
                        // PrintWindow często zwraca czarny obraz.
                        if (!PrintWindow(window, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
                            return false;
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                if (IsBlank(rendered))
                    return false;

                // Detektory czytają ARGB; klon nadaje pełną nieprzezroczystość.
                bitmap = rendered.Clone(new Rectangle(0, 0, width, height), PixelFormat.Format32bppArgb);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Obraz", "PrintWindow nie zwrócił obrazu okna gry: " + ex.GetBaseException().Message);
                bitmap?.Dispose();
                bitmap = null;
                return false;
            }
        }

        // Po przejęciu myszy (np. zamknięcie EQ) LWJGL ustawia kursor na środku okna gry.
        // Okno jest na innym pulpicie, ale współrzędne są wspólne, więc użytkownikowi
        // kursor "skacze". Dedykowany obserwator obsługuje wspólną pozycję silników
        // również pomiędzy tickami; stara ochrona środka jest awaryjnym fallbackiem.
        public static void ProtectUserCursor()
        {
            if (_focusObserver?.IsCursorProtectionAvailable == true)
            {
                _focusObserver.ProtectUserCursor();
                return;
            }
            if (!IsActive || IsIconic(Window) || !GetCursorPos(out POINT cursor) || !TryGetClientRectOnScreen(Window, out RECT client))
                return;

            if (GetClipCursor(out RECT clip)
                && clip.Left == client.Left
                && clip.Top == client.Top
                && clip.Right == client.Right
                && clip.Bottom == client.Bottom)
            {
                ClipCursor(IntPtr.Zero);
            }

            int centerX = client.Left + (client.Right - client.Left) / 2;
            int centerY = client.Top + (client.Bottom - client.Top) / 2;
            bool atGameCenter = IsNear(cursor, centerX, centerY);
            if (atGameCenter && _hasLastUserCursor && !IsNear(_lastUserCursor, centerX, centerY))
            {
                SetCursorPos(_lastUserCursor.X, _lastUserCursor.Y);
                return;
            }

            _lastUserCursor = cursor;
            _hasLastUserCursor = true;
        }

        private static IntPtr CursorLParam()
        {
            return PositionLParam(_clientCursorX, _clientCursorY);
        }

        public static void PrepareCursorTransition() => _focusObserver?.ArmCursorTransition();

        internal static bool TryGetCursorGuardGeometry(IntPtr window, out Rectangle rectangle)
        {
            bool known = TryGetClientRectOnScreen(window, out RECT client);
            rectangle = known ? Rectangle.FromLTRB(client.Left, client.Top, client.Right, client.Bottom) : Rectangle.Empty;
            return known;
        }

        private static IntPtr PositionLParam(int x, int y)
        {
            int lParam = (x & 0xFFFF) | ((y & 0xFFFF) << 16);
            return (IntPtr)lParam;
        }

        private static bool TryGetClientRectOnScreen(IntPtr window, out RECT rect)
        {
            rect = default;
            if (!GetClientRect(window, out RECT client))
                return false;

            var topLeft = new POINT { X = client.Left, Y = client.Top };
            var bottomRight = new POINT { X = client.Right, Y = client.Bottom };
            if (!ClientToScreen(window, ref topLeft) || !ClientToScreen(window, ref bottomRight))
                return false;

            rect = new RECT { Left = topLeft.X, Top = topLeft.Y, Right = bottomRight.X, Bottom = bottomRight.Y };
            return true;
        }

        private static bool IsNear(POINT point, int x, int y)
        {
            return Math.Abs(point.X - x) <= CursorCenterTolerance && Math.Abs(point.Y - y) <= CursorCenterTolerance;
        }

        private static bool IsExtendedKey(int virtualKey)
        {
            return virtualKey is >= 0x21 and <= 0x28 // PageUp, PageDown, End, Home, strzałki
                or 0x2D or 0x2E                       // Insert, Delete
                or 0x6F                               // Numpad /
                or 0x5B or 0x5C                       // Windows
                or 0x90;                              // NumLock
        }

        private static bool IsBlank(Bitmap bitmap)
        {
            // Zminimalizowane lub nierenderujące okno daje jednolicie czarny obraz.
            const int samples = 8;
            for (int sy = 1; sy <= samples; sy++)
            {
                for (int sx = 1; sx <= samples; sx++)
                {
                    Color pixel = bitmap.GetPixel(
                        bitmap.Width * sx / (samples + 1),
                        bitmap.Height * sy / (samples + 1));
                    if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                        return false;
                }
            }

            return true;
        }
    }
}
