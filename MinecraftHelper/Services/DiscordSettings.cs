using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MinecraftHelper.Services;

// Ustawienia bota Discord — wspólne dla wszystkich kont (jeden bot, jeden kanał).
internal sealed class DiscordSettings
{
    public bool Enabled { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    // ID użytkowników Discord, którym bot wykonuje polecenia; oddzielone przecinkami.
    public string OwnerUserIds { get; set; } = string.Empty;
    public string CommandPrefix { get; set; } = "!";
    public bool MentionOwnersOnAlerts { get; set; } = true;
    public bool AttachScreenshotToAlerts { get; set; } = true;
    public bool NotifyMiningStartStop { get; set; } = true;
    public bool NotifyProtection { get; set; } = true;
    public bool NotifyReconnect { get; set; } = true;
    public bool NotifyMissingPickaxe { get; set; } = true;
    public bool NotifyErrors { get; set; } = true;
    // 0 = wyłączone.
    public int ScreenshotIntervalMinutes { get; set; }
    public int StatusIntervalMinutes { get; set; }
    // Token zaszyfrowany DPAPI (tylko bieżący użytkownik Windows) — nigdy jawnym tekstem.
    public string TokenProtected { get; set; } = string.Empty;
    // Wyłączone komendy (ID z DiscordCommands). Lista wyłączonych, a nie włączonych,
    // żeby nowe komendy w kolejnych wersjach działały od razu.
    public List<string> DisabledCommands { get; set; } = new();

    public bool IsCommandEnabled(string commandId) =>
        DisabledCommands?.Contains(commandId, StringComparer.OrdinalIgnoreCase) != true;

    [JsonIgnore]
    public bool HasToken => TokenProtected.Length > 0;

    public IReadOnlyCollection<string> GetOwnerIds() => OwnerUserIds
        .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
        .Where(id => id.All(char.IsDigit))
        .ToHashSet(StringComparer.Ordinal);

    [JsonIgnore]
    public string Prefix => string.IsNullOrWhiteSpace(CommandPrefix) ? "!" : CommandPrefix.Trim();

    // Gotowe do działania: włączone, z tokenem i kanałem.
    [JsonIgnore]
    public bool IsConfigured => Enabled && HasToken && ChannelId.Length > 0 && ChannelId.All(char.IsDigit);
}

internal static class DiscordSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly object CacheGate = new();
    private static DiscordSettings? _cached;
    private static DateTime _cachedWriteUtc;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Minecraft Helper",
        "Kopacz",
        "discord.json");

    public static DateTime LastWriteUtc => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    // Ustawienia z krótką pamięcią podręczną — powiadomienia czytają je przy każdym zdarzeniu.
    public static DiscordSettings Load()
    {
        lock (CacheGate)
        {
            DateTime writeUtc = LastWriteUtc;
            if (_cached != null && writeUtc == _cachedWriteUtc)
                return _cached;

            DiscordSettings settings = new();
            try
            {
                if (File.Exists(FilePath))
                    settings = JsonSerializer.Deserialize<DiscordSettings>(File.ReadAllText(FilePath)) ?? new DiscordSettings();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLog.Warn("Discord", "Nie udało się odczytać ustawień bota: " + ex.Message);
            }

            _cached = settings;
            _cachedWriteUtc = writeUtc;
            return settings;
        }
    }

    public static void Save(DiscordSettings settings)
    {
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, $".discord.json.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }

        lock (CacheGate)
            _cached = null;
    }

    public static void SetToken(DiscordSettings settings, string token)
    {
        token = token.Trim();
        settings.TokenProtected = token.Length == 0
            ? string.Empty
            : Convert.ToBase64String(Dpapi.Protect(Encoding.UTF8.GetBytes(token)));
    }

    public static string? GetToken(DiscordSettings settings)
    {
        if (!settings.HasToken)
            return null;
        try
        {
            return Encoding.UTF8.GetString(Dpapi.Unprotect(Convert.FromBase64String(settings.TokenProtected)));
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            // Np. plik skopiowany z innego konta Windows — token trzeba wpisać ponownie.
            AppLog.Warn("Discord", "Nie udało się odszyfrować tokenu bota — wpisz go ponownie.");
            return null;
        }
    }

    // Wyświetlanie kanałów, Wysyłanie wiadomości, Załączanie plików, Czytanie historii czatu.
    public const long InvitePermissions = 1024 + 2048 + 32768 + 65536;

    // Link zaproszenia bota na serwer. Pierwsza część tokenu to ID bota (Base64),
    // a ID bota jest równe ID aplikacji — nie trzeba go szukać w Developer Portal.
    public static string? TryBuildInviteUrl(string? token)
    {
        string first = token?.Trim().Split('.')[0] ?? string.Empty;
        if (first.Length == 0)
            return null;
        string base64 = first.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            string id = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return id.Length is >= 15 and <= 22 && id.All(char.IsDigit)
                ? $"https://discord.com/oauth2/authorize?client_id={id}&scope=bot&permissions={InvitePermissions}"
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // DPAPI (crypt32): szyfrowanie kluczem bieżącego użytkownika Windows, bez dodatkowych pakietów.
    private static class Dpapi
    {
        private const int CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static byte[] Protect(byte[] data) => Transform(data, protect: true);

        public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

        private static byte[] Transform(byte[] data, bool protect)
        {
            var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
            try
            {
                Marshal.Copy(data, 0, input.Data, data.Length);
                bool ok = protect
                    ? CryptProtectData(ref input, "Minecraft Helper — token Discord", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out DataBlob output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
                if (!ok)
                    throw new InvalidOperationException($"DPAPI: błąd {Marshal.GetLastWin32Error()}.");
                try
                {
                    var result = new byte[output.Size];
                    Marshal.Copy(output.Data, result, 0, output.Size);
                    return result;
                }
                finally
                {
                    LocalFree(output.Data);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input.Data);
            }
        }
    }
}
