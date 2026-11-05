using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MinecraftHelper.Services;

// Jedno okno Minecrafta należy do jednego profilu Kopacza. Zajęcie to nazwany obiekt
// systemowy przypisany do PID gry; znika razem z procesem Kopacza, więc nie zostaje
// po awarii. Pozostałe profile pomijają takie okno przy dopasowaniu fokusu i okna.
internal sealed class MinerGameWindowClaim : IDisposable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    private EventWaitHandle? _handle;

    public int ProcessId { get; private set; }

    public bool TryClaim(int processId)
    {
        if (processId <= 0)
        {
            Release();
            return false;
        }

        if (_handle != null && ProcessId == processId)
            return true;

        Release();
        EventWaitHandle handle;
        bool createdNew;
        try
        {
            handle = new EventWaitHandle(false, EventResetMode.ManualReset, GetName(processId), out createdNew);
        }
        catch (Exception)
        {
            return false;
        }

        if (!createdNew)
        {
            handle.Dispose();
            return false;
        }

        _handle = handle;
        ProcessId = processId;
        return true;
    }

    public bool IsClaimedByAnotherProfile(int processId)
    {
        if (processId <= 0 || (_handle != null && processId == ProcessId))
            return false;

        if (!EventWaitHandle.TryOpenExisting(GetName(processId), out EventWaitHandle? existing))
            return false;

        existing.Dispose();
        return true;
    }

    public void Release()
    {
        _handle?.Dispose();
        _handle = null;
        ProcessId = 0;
    }

    public void Dispose() => Release();

    public static bool IsProcessRunning(int processId)
    {
        if (processId <= 0)
            return false;

        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (process == IntPtr.Zero)
            return false;

        try
        {
            return GetExitCodeProcess(process, out uint exitCode) && exitCode == StillActive;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string GetName(int processId) => $@"Local\MinecraftHelper.Miner.GameWindow.{processId}";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
