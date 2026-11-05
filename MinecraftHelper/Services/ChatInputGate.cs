using System;
using System.Runtime.InteropServices;

namespace MinecraftHelper.Services;

// Łączy ChatInputBarrier z prawdziwym oknem gry: wysyła WM_NULL przez
// SendMessageCallback i odbiera potwierdzenia w wątku UI. Działa i w tle (PostMessage),
// i z grą na wierzchu (SendInput).
internal static class ChatInputGate
{
    private const uint WM_NULL = 0x0000;

    private delegate void SendAsyncProc(IntPtr hWnd, uint msg, UIntPtr data, IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendMessageCallback(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        SendAsyncProc callback, UIntPtr data);

    // Delegat musi żyć tak długo, jak oczekujące wywołania zwrotne.
    private static readonly SendAsyncProc Callback = OnAcknowledged;
    private static ChatInputBarrier? _barrier;
    private static IntPtr _window;
    // Spóźnione potwierdzenie poprzedniej komendy nie może zwolnić nowej bariery.
    private static uint _generation;

    public static void Arm(IntPtr window, bool postedInput)
    {
        _generation++;
        _window = window;
        if (window == IntPtr.Zero)
        {
            _barrier = null;
            return;
        }

        _barrier = new ChatInputBarrier(RequestRoundTrip, postedInput ? 2 : 3);
        _barrier.Arm(DateTime.UtcNow);
    }

    public static bool IsReady(DateTime now)
    {
        if (_barrier == null)
            return true;
        if (!_barrier.IsReady(now))
            return false;
        if (_barrier.TimedOut)
            AppLog.Warn("Czat", "Gra nie potwierdziła otwarcia czatu w 5 s — komendę wpisano mimo to.");
        _barrier = null;
        return true;
    }

    private static bool RequestRoundTrip(long token)
    {
        ulong data = ((ulong)_generation << 32) | (uint)token;
        return _window != IntPtr.Zero
            && SendMessageCallback(_window, WM_NULL, IntPtr.Zero, IntPtr.Zero, Callback, (UIntPtr)data);
    }

    // Wywołanie zwrotne przychodzi w wątku UI, który wysłał WM_NULL (przy pompowaniu komunikatów).
    private static void OnAcknowledged(IntPtr hWnd, uint msg, UIntPtr data, IntPtr result)
    {
        ulong value = data.ToUInt64();
        if (hWnd == _window && (uint)(value >> 32) == _generation)
            _barrier?.Acknowledge((long)(value & 0xFFFFFFFF), DateTime.UtcNow);
    }
}
