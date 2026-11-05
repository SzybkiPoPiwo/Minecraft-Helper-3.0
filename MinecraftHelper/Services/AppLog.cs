using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinecraftHelper.Services;

internal enum AppLogLevel { Info, Warning, Error }

internal sealed record AppLogEntry(DateTime Time, AppLogLevel Level, string Source, string Message);

// Dziennik zdarzeń programu: ostatnie wpisy w pamięci (konsola w GUI) i plik
// app.log w folderze profilu. Bezpieczny wątkowo; zdarzenie EntryAdded może przyjść
// z dowolnego wątku, więc odbiorca sam przełącza się na wątek UI.
internal static class AppLog
{
    public const int MaxEntries = 2000;
    private const long MaxFileBytes = 1_000_000;
    // Ochrona przed zalaniem dziennika, np. komunikatem odświeżanym w każdym ticku.
    private const int MaxEntriesPerSourcePerSecond = 20;

    private static readonly object Sync = new();
    private static readonly Queue<AppLogEntry> Entries = new();
    private static readonly Dictionary<string, (long Second, int Count)> Rates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AppLogEntry> LastBySource = new(StringComparer.Ordinal);
    private static string? _filePath;

    public static event Action<AppLogEntry>? EntryAdded;

    public static string? FilePath => _filePath;

    public static void Initialize(string directory)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _filePath = Path.Combine(directory, "app.log");
                // Jeden poprzedni plik wystarczy do diagnozy; większe dzienniki tylko przeszkadzają.
                if (File.Exists(_filePath) && new FileInfo(_filePath).Length > MaxFileBytes)
                    File.Move(_filePath, Path.Combine(directory, "app.old.log"), overwrite: true);
            }
            catch (IOException)
            {
                // Dziennik w pamięci nadal działa; plik jest tylko kopią dla diagnozy.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static void Info(string source, string message) => Write(AppLogLevel.Info, source, message);

    public static void Warn(string source, string message) => Write(AppLogLevel.Warning, source, message);

    public static void Error(string source, string message, Exception? exception = null)
    {
        string text = exception == null ? message : $"{message}: {exception.GetBaseException().Message}";
        Write(AppLogLevel.Error, source, text);
        if (exception != null)
            AppendToFile($"    {exception.GetType().Name}: {exception}{Environment.NewLine}");
    }

    public static IReadOnlyList<AppLogEntry> Snapshot()
    {
        lock (Sync)
            return Entries.ToArray();
    }

    private static void Write(AppLogLevel level, string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var entry = new AppLogEntry(DateTime.Now, level, source, message.Trim());
        lock (Sync)
        {
            // Ten sam komunikat z tego samego źródła (np. stan odświeżany co tick) zapisujemy raz.
            if (LastBySource.TryGetValue(source, out AppLogEntry? last) && last.Level == entry.Level && last.Message == entry.Message)
                return;
            if (!TryTakeRate(source, entry.Time))
                return;

            LastBySource[source] = entry;
            Entries.Enqueue(entry);
            while (Entries.Count > MaxEntries)
                Entries.Dequeue();
        }

        AppendToFile(Format(entry) + Environment.NewLine);
        EntryAdded?.Invoke(entry);
    }

    private static bool TryTakeRate(string source, DateTime time)
    {
        long second = time.Ticks / TimeSpan.TicksPerSecond;
        Rates.TryGetValue(source, out (long Second, int Count) rate);
        rate = rate.Second == second ? (second, rate.Count + 1) : (second, 1);
        Rates[source] = rate;
        return rate.Count <= MaxEntriesPerSourcePerSecond;
    }

    public static string Format(AppLogEntry entry)
    {
        string level = entry.Level switch
        {
            AppLogLevel.Error => "BŁĄD ",
            AppLogLevel.Warning => "UWAGA",
            _ => "INFO "
        };
        return $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff} {level} [{entry.Source}] {entry.Message}";
    }

    private static void AppendToFile(string text)
    {
        string? path = _filePath;
        if (path == null)
            return;

        lock (Sync)
        {
            try
            {
                File.AppendAllText(path, text, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Np. plik otwarty w edytorze z blokadą — wpis zostaje w konsoli.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
