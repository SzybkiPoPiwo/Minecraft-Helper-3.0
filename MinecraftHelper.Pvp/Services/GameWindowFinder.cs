using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MinecraftHelper.Services;

internal enum GameWindowMatch
{
    None,
    // Okno procesu Javy, które nie jest oknem Swing/AWT (np. klient z własną klasą okna).
    Likely,
    // Klasa okna LWJGL 2 (1.8.x i klienty na nim) albo GLFW (LWJGL 3).
    Certain
}

internal sealed record GameWindowInfo(IntPtr Handle, int ProcessId, string ProcessName, string Title, GameWindowMatch Match, bool Visible = true)
{
    public string DisplayName => $"{ProcessName} [{ProcessId}] — {Title}";
}

// Wyszukuje okna gry po klasie okna zamiast po tytule. Dopasowanie po słowie
// „Minecraft” w tytule łapało też sam Auto Clicker, Kopacza, VS Code czy kartę
// przeglądarki — a gdy takie okno zostało wybrane, kliknięcia szły w złe miejsce.
internal static class GameWindowFinder
{
    public static GameWindowMatch Classify(string className, string processName, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return GameWindowMatch.None;
        if (processName.StartsWith("MinecraftHelper", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("launcher", StringComparison.OrdinalIgnoreCase)
            || title.Contains("launcher", StringComparison.OrdinalIgnoreCase))
        {
            return GameWindowMatch.None;
        }

        if (className.Equals("LWJGL", StringComparison.Ordinal)
            || className.StartsWith("GLFW", StringComparison.Ordinal))
        {
            return GameWindowMatch.Certain;
        }

        bool javaProcess = processName.Equals("javaw", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("java", StringComparison.OrdinalIgnoreCase);
        // SunAwt* to okna Swing/AWT (konsola serwera, narzędzia), a nie okno gry.
        return javaProcess && !className.StartsWith("SunAwt", StringComparison.Ordinal)
            ? GameWindowMatch.Likely
            : GameWindowMatch.None;
    }

    // Wybór procesu gry po odświeżeniu listy: obecny cel, zapisany proces, zapisany
    // tytuł (po ponownym uruchomieniu gry PID jest inny), a na końcu jedyna gra.
    public static GameWindowInfo? PickTarget(
        IReadOnlyList<GameWindowInfo> candidates,
        int currentProcessId,
        int savedProcessId,
        string? savedTitle)
    {
        foreach (int processId in new[] { currentProcessId, savedProcessId })
        {
            if (processId <= 0)
                continue;
            GameWindowInfo? byProcess = candidates.FirstOrDefault(candidate => candidate.ProcessId == processId);
            if (byProcess != null)
                return byProcess;
        }
        if (!string.IsNullOrWhiteSpace(savedTitle))
        {
            GameWindowInfo? byTitle = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Title, savedTitle.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byTitle != null)
                return byTitle;
        }

        List<GameWindowInfo> certain = candidates.Where(candidate => candidate.Match == GameWindowMatch.Certain).ToList();
        if (certain.Count == 1)
            return certain[0];
        return candidates.Count == 1 ? candidates[0] : null;
    }

    // Jedna pozycja na proces gry, z jego najlepszym oknem.
    public static List<GameWindowInfo> FindAll()
    {
        return EnumerateGameWindows(0)
            .GroupBy(candidate => candidate.ProcessId)
            .Select(PickBestWindow)
            .OrderByDescending(candidate => candidate.Match)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // Bieżące okno wskazanego procesu gry — także ukryte, np. pełny ekran po Alt+Tab.
    public static IntPtr FindProcessWindow(int processId)
    {
        if (processId <= 0)
            return IntPtr.Zero;
        List<GameWindowInfo> windows = EnumerateGameWindows((uint)processId);
        return windows.Count == 0 ? IntPtr.Zero : PickBestWindow(windows).Handle;
    }

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

    public static int GetWindowProcessId(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return 0;
        _ = GetWindowThreadProcessId(window, out uint processId);
        return (int)processId;
    }

    // Fokus gry = na wierzchu jest dowolne okno jej procesu. Porównanie uchwytów
    // zawodziło przy pełnym ekranie, bo LWJGL 2 tworzy wtedy nowe okno.
    public static bool IsProcessForeground(int processId)
    {
        return processId > 0 && GetWindowProcessId(GetForegroundWindow()) == processId;
    }

    private static GameWindowInfo PickBestWindow(IEnumerable<GameWindowInfo> windows)
    {
        return windows
            .OrderByDescending(candidate => candidate.Visible)
            .ThenByDescending(candidate => candidate.Match)
            .First();
    }

    private static List<GameWindowInfo> EnumerateGameWindows(uint onlyProcessId)
    {
        uint ownProcessId = (uint)Environment.ProcessId;
        var processNames = new Dictionary<uint, string>();
        var results = new List<GameWindowInfo>();
        EnumWindows((window, state) =>
        {
            try
            {
                if (GetWindow(window, GwOwner) != IntPtr.Zero)
                    return true;
                _ = GetWindowThreadProcessId(window, out uint processId);
                if (processId == 0 || processId == ownProcessId || (onlyProcessId != 0 && processId != onlyProcessId))
                    return true;

                string title = ReadText(window, GetWindowTextLength(window), GetWindowText).Trim();
                if (title.Length == 0)
                    return true;
                bool visible = IsWindowVisible(window);
                string className = ReadText(window, 256, GetClassName);
                string processName = GetProcessName(processNames, processId);
                GameWindowMatch match = Classify(className, processName, title);
                // Ukryte okno liczy się tylko z klasą gry: Java ma też ukryte okna pomocnicze.
                if (match == GameWindowMatch.Certain || (match == GameWindowMatch.Likely && visible))
                    results.Add(new GameWindowInfo(window, (int)processId, processName, title, match, visible));
            }
            catch
            {
                // Proces mógł się zakończyć w trakcie wyliczania — pomijamy okno.
            }
            return true;
        }, IntPtr.Zero);
        return results;
    }

    private static string GetProcessName(Dictionary<uint, string> cache, uint processId)
    {
        if (cache.TryGetValue(processId, out string? cached))
            return cached;
        // Process.ProcessName robi migawkę wszystkich procesów przy każdym oknie;
        // QueryFullProcessImageName czyta tylko ścieżkę jednego procesu.
        string name = string.Empty;
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process != IntPtr.Zero)
        {
            try
            {
                var path = new StringBuilder(1024);
                int length = path.Capacity;
                if (QueryFullProcessImageName(process, 0, path, ref length))
                    name = Path.GetFileNameWithoutExtension(path.ToString());
            }
            finally
            {
                CloseHandle(process);
            }
        }
        cache[processId] = name;
        return name;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private delegate int TextReader(IntPtr window, StringBuilder buffer, int maximumCount);

    private static string ReadText(IntPtr window, int length, TextReader reader)
    {
        if (length <= 0)
            return string.Empty;
        var buffer = new StringBuilder(length + 1);
        return reader(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private const uint GwOwner = 4;

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
}
