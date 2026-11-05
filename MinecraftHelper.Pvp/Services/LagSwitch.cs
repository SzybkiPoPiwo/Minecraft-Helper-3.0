using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MinecraftHelper.Services;

internal enum LagSwitchState
{
    Idle,
    Starting,
    Ready,
    Holding,
    Failed,
    Unavailable
}

// Lag switch, parent side. Auto Clicker itself stays non-elevated: the first
// hold starts this same executable with --lag-helper through UAC. The helper
// owns the WinDivert handle; this class only tells it to hold or release.
internal sealed class LagSwitchClient : IDisposable
{
    public const string HelperArgument = "--lag-helper";
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan HelperConnectTimeout = TimeSpan.FromSeconds(60);

    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _desiredHold;
    private int _desiredProcessId;
    private TimeSpan _desiredMaxDuration;
    private bool _holding;
    private volatile bool _disposed;

    public LagSwitchClient()
    {
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Auto Clicker lag switch client" };
        _worker.Start();
    }

    public LagSwitchState State { get; private set; } = LagSwitchState.Idle;

    public string LastMessage { get; private set; } = string.Empty;

    public bool IsHelperConnected
    {
        get
        {
            lock (_sync)
                return _pipe?.IsConnected == true;
        }
    }

    public static bool AreDriverFilesPresent(out string message)
    {
        string directory = AppContext.BaseDirectory;
        string[] required = { "WinDivert.dll", "WinDivert64.sys" };
        foreach (string file in required)
        {
            if (!File.Exists(Path.Combine(directory, file)))
            {
                message = $"Brak pliku {file} obok Auto Clickera.";
                return false;
            }
        }
        message = string.Empty;
        return true;
    }

    public void SetHeld(bool held, int processId, TimeSpan maxDuration)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _desiredHold = held;
            _desiredProcessId = processId;
            _desiredMaxDuration = maxDuration;
        }
        _wake.Set();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _desiredHold = false;
        }
        _wake.Set();
        _ = _worker.Join(TimeSpan.FromSeconds(2));
        CloseHelper(sendQuit: true);
        _wake.Dispose();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _wake.WaitOne(TimeSpan.FromSeconds(1));
            bool desiredHold;
            int processId;
            TimeSpan maxDuration;
            lock (_sync)
            {
                desiredHold = _desiredHold && !_disposed;
                processId = _desiredProcessId;
                maxDuration = _desiredMaxDuration;
            }

            try
            {
                if (desiredHold && !_holding)
                    Hold(processId, maxDuration);
                else if (!desiredHold && _holding)
                    Release();
            }
            catch (Exception exception)
            {
                Fail($"Lag switch: {exception.Message}");
                CloseHelper(sendQuit: false);
            }

            if (_disposed)
            {
                if (_holding)
                {
                    try { Release(); }
                    catch { CloseHelper(sendQuit: false); }
                }
                return;
            }
        }
    }

    private void Hold(int processId, TimeSpan maxDuration)
    {
        if (!AreDriverFilesPresent(out string missing))
        {
            State = LagSwitchState.Unavailable;
            LastMessage = missing;
            ClearDesiredHold();
            return;
        }

        if (!EnsureHelper())
        {
            ClearDesiredHold();
            return;
        }

        // The UAC prompt takes focus, so the bind is usually released by now:
        // the first press only starts the helper.
        lock (_sync)
        {
            if (!_desiredHold || _disposed)
            {
                LastMessage = "Pomocnik gotowy — użyj binda ponownie.";
                return;
            }
        }

        int maxMilliseconds = (int)Math.Clamp(maxDuration.TotalMilliseconds, 1, 60_000);
        string reply = SendCommand($"HOLD {processId.ToString(CultureInfo.InvariantCulture)} {maxMilliseconds.ToString(CultureInfo.InvariantCulture)}");
        if (reply.StartsWith("OK", StringComparison.Ordinal))
        {
            _holding = true;
            State = LagSwitchState.Holding;
            LastMessage = "Lag switch: pakiety wstrzymane.";
            return;
        }

        Fail("Lag switch: " + (reply.StartsWith("ERR ", StringComparison.Ordinal) ? reply[4..] : "pomocnik nie odpowiedział."));
        ClearDesiredHold();
    }

    private void Release()
    {
        _holding = false;
        string reply = SendCommand("RELEASE");
        State = LagSwitchState.Ready;
        LastMessage = reply.StartsWith("OK", StringComparison.Ordinal)
            ? "Lag switch: pakiety puszczone."
            : "Lag switch: pomocnik nie potwierdził zwolnienia.";
    }

    private bool EnsureHelper()
    {
        lock (_sync)
        {
            if (_pipe?.IsConnected == true)
                return true;
        }

        CloseHelper(sendQuit: false);
        State = LagSwitchState.Starting;
        LastMessage = "Lag switch: czekam na zgodę administratora (UAC)…";
        string pipeName = "MinecraftHelper.LagSwitch." + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        try
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("nie znaleziono pliku programu.");
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"{HelperArgument} {pipeName} {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}"
            };
            using (Process.Start(start))
            {
            }

            if (!pipe.WaitForConnectionAsync().Wait(HelperConnectTimeout))
                throw new TimeoutException("pomocnik nie połączył się w ciągu 60 s.");

            var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            if (reader.ReadLine() != "READY")
                throw new InvalidOperationException("pomocnik zgłosił błąd przy starcie.");

            lock (_sync)
            {
                _pipe = pipe;
                _reader = reader;
                _writer = writer;
            }
            State = LagSwitchState.Ready;
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            pipe.Dispose();
            Fail("Lag switch: odmówiono zgody administratora.");
            return false;
        }
        catch (Exception exception)
        {
            pipe.Dispose();
            Fail($"Lag switch: {exception.Message}");
            return false;
        }
    }

    private string SendCommand(string command)
    {
        StreamWriter? writer;
        StreamReader? reader;
        lock (_sync)
        {
            writer = _writer;
            reader = _reader;
        }
        if (writer == null || reader == null)
            return "ERR brak połączenia z pomocnikiem.";

        writer.WriteLine(command);
        return reader.ReadLine() ?? "ERR pomocnik zakończył działanie.";
    }

    private void CloseHelper(bool sendQuit)
    {
        NamedPipeServerStream? pipe;
        StreamWriter? writer;
        StreamReader? reader;
        lock (_sync)
        {
            pipe = _pipe;
            writer = _writer;
            reader = _reader;
            _pipe = null;
            _writer = null;
            _reader = null;
        }

        if (sendQuit && pipe?.IsConnected == true)
        {
            try { writer?.WriteLine("QUIT"); }
            catch { }
        }
        // Closing the pipe also makes the helper release the handle and exit.
        try { writer?.Dispose(); } catch { }
        try { reader?.Dispose(); } catch { }
        try { pipe?.Dispose(); } catch { }
        _holding = false;
    }

    private void ClearDesiredHold()
    {
        lock (_sync)
            _desiredHold = false;
    }

    private void Fail(string message)
    {
        _holding = false;
        State = LagSwitchState.Failed;
        LastMessage = message;
    }
}

// Lag switch, elevated helper side (same executable, --lag-helper). It holds
// the WinDivert handle and closes it on RELEASE, on its own deadline, when the
// parent closes the pipe or exits, and on any error.
internal static class LagSwitchHelperHost
{
    public static int Run(string pipeName, int parentProcessId)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect(10_000);
        }
        catch
        {
            return 2;
        }

        using var hold = new TimedResourceHold();
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        using Process? parent = TryGetProcess(parentProcessId);
        try
        {
            writer.WriteLine("READY");
            while (true)
            {
                string? line = reader.ReadLine();
                if (line == null || line == "QUIT" || parent?.HasExited == true)
                    return 0;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && parts[0] == "HOLD"
                    && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId)
                    && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxMilliseconds))
                {
                    writer.WriteLine(TryHold(hold, processId, maxMilliseconds));
                }
                else if (parts.Length == 1 && parts[0] == "RELEASE")
                {
                    hold.Release();
                    writer.WriteLine("OK");
                }
                else
                {
                    writer.WriteLine("ERR nieznane polecenie.");
                }
            }
        }
        catch
        {
            return 1;
        }
        finally
        {
            hold.Release();
        }
    }

    private static string TryHold(TimedResourceHold hold, int processId, int maxMilliseconds)
    {
        if (hold.IsHeld)
            return "OK już aktywny";

        List<int> ports = TcpConnections.GetEstablishedRemotePorts(processId);
        if (ports.Count == 0)
            return "ERR gra nie ma aktywnego połączenia z serwerem.";

        string filter = PvpInputPolicies.BuildLagSwitchFilter(ports);
        int error = 0;
        bool held;
        try
        {
            held = hold.TryAcquire(() =>
            {
                IntPtr handle = WinDivertNative.WinDivertOpen(filter, WinDivertNative.LayerNetwork, 0, 0);
                if (handle == WinDivertNative.InvalidHandle)
                {
                    error = Marshal.GetLastWin32Error();
                    return null;
                }
                // Deliberately no WinDivertRecv: matching packets wait in the
                // driver queue until the handle is closed.
                return () => _ = WinDivertNative.WinDivertClose(handle);
            }, TimeSpan.FromMilliseconds(Math.Clamp(maxMilliseconds, 1, 60_000)));
        }
        catch (DllNotFoundException)
        {
            return "ERR nie można wczytać WinDivert.dll (antywirus?).";
        }

        return held
            ? $"OK {ports.Count.ToString(CultureInfo.InvariantCulture)}"
            : $"ERR WinDivert nie uruchomił się (błąd {error.ToString(CultureInfo.InvariantCulture)}).";
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return Process.GetProcessById(processId);
        }
        catch
        {
            return null;
        }
    }
}

internal static class TcpConnections
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;
    private const int StateEstablished = 5;
    private const uint Loopback = 0x0100007F;
    private const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int addressFamily, int tableClass, int reserved);

    // Remote ports of the process's established IPv4 connections, without loopback.
    public static List<int> GetEstablishedRemotePorts(int processId)
    {
        var ports = new List<int>();
        int size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != ErrorInsufficientBuffer)
            return ports;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll, 0) != 0)
                return ports;

            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            IntPtr row = buffer + 4;
            for (int index = 0; index < count; index++, row += rowSize)
            {
                TcpRowOwnerPid entry = Marshal.PtrToStructure<TcpRowOwnerPid>(row);
                if (entry.State != StateEstablished || entry.LocalAddress == Loopback || entry.OwningPid != (uint)processId)
                    continue;
                ports.Add((ushort)IPAddress.NetworkToHostOrder((short)(entry.RemotePort & 0xFFFF)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return ports;
    }
}

internal static class WinDivertNative
{
    public const int LayerNetwork = 0;
    public static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);
}
