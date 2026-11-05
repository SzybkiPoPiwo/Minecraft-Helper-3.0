using System;
using System.Runtime.InteropServices;

namespace MinecraftHelper.Services;

// Freeze window: suspends the thread that owns the game window (render and
// client tick in Minecraft 1.8) while the bind is held. The original tool had
// no safety net; here every path resumes the thread exactly once.
internal sealed class GameThreadFreezer : IDisposable
{
    private const uint ThreadSuspendResume = 0x0002;
    private const uint SuspendFailed = 0xFFFFFFFF;

    private readonly TimedResourceHold _hold = new();
    private readonly IThreadControl _threads;

    public GameThreadFreezer()
        : this(NativeThreadControl.Instance)
    {
    }

    internal GameThreadFreezer(IThreadControl threads)
    {
        _threads = threads ?? throw new ArgumentNullException(nameof(threads));
    }

    internal interface IThreadControl
    {
        uint GetWindowThreadId(IntPtr window);
        IntPtr OpenThread(uint threadId);
        bool Suspend(IntPtr thread);
        bool Resume(IntPtr thread);
        void Close(IntPtr thread);
    }

    public bool IsFrozen => _hold.IsHeld;

    public bool ExpiredByDeadline => _hold.ExpiredByDeadline;

    public bool Freeze(IntPtr window, TimeSpan maxDuration)
    {
        if (window == IntPtr.Zero)
            return false;

        return _hold.TryAcquire(() =>
        {
            uint threadId = _threads.GetWindowThreadId(window);
            if (threadId == 0)
                return null;

            IntPtr thread = _threads.OpenThread(threadId);
            if (thread == IntPtr.Zero)
                return null;

            try
            {
                if (!_threads.Suspend(thread))
                {
                    _threads.Close(thread);
                    return null;
                }
            }
            catch
            {
                _threads.Close(thread);
                throw;
            }

            return () =>
            {
                try
                {
                    _threads.Resume(thread);
                }
                finally
                {
                    _threads.Close(thread);
                }
            };
        }, maxDuration);
    }

    public void Resume() => _hold.Release();

    public void Dispose() => _hold.Dispose();

    private sealed class NativeThreadControl : IThreadControl
    {
        public static readonly NativeThreadControl Instance = new();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SuspendThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public uint GetWindowThreadId(IntPtr window) => GetWindowThreadProcessId(window, out _);

        IntPtr IThreadControl.OpenThread(uint threadId) => OpenThread(ThreadSuspendResume, false, threadId);

        public bool Suspend(IntPtr thread) => SuspendThread(thread) != SuspendFailed;

        public bool Resume(IntPtr thread) => ResumeThread(thread) != SuspendFailed;

        public void Close(IntPtr thread) => _ = CloseHandle(thread);
    }
}
