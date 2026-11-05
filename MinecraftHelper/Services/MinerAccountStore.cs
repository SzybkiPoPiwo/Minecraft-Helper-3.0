using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MinecraftHelper.Models;

namespace MinecraftHelper.Services;

internal sealed class MinerAccountStore
{
    private const string AccountsFileName = "accounts.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public string AccountsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Minecraft Helper",
        "Kopacz",
        AccountsFileName);

    public IReadOnlyList<MinerAccountProfile> Load()
    {
        try
        {
            if (File.Exists(AccountsFilePath))
            {
                string json = File.ReadAllText(AccountsFilePath);
                List<MinerAccountProfile>? profiles = JsonSerializer.Deserialize<List<MinerAccountProfile>>(json);
                return Normalize(profiles);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Konta", "Nie udało się odczytać accounts.json — używam samego konta głównego", ex);
        }

        return new[] { CreateDefaultProfile() };
    }

    // Zmiana listy kont na świeżo wczytanym pliku, pod blokadą wspólną dla wszystkich
    // procesów Kopacza — dwa konta zmieniające listę naraz nie nadpiszą sobie zmian.
    public IReadOnlyList<MinerAccountProfile> Update(Action<List<MinerAccountProfile>> change)
    {
        using var gate = AccountsFileGate.Acquire();
        List<MinerAccountProfile> profiles = Load().ToList();
        change(profiles);
        SaveUnlocked(profiles);
        return Normalize(profiles);
    }

    public void Save(IEnumerable<MinerAccountProfile> profiles)
    {
        using var gate = AccountsFileGate.Acquire();
        SaveUnlocked(profiles);
    }

    private void SaveUnlocked(IEnumerable<MinerAccountProfile> profiles)
    {
        IReadOnlyList<MinerAccountProfile> normalized = Normalize(profiles);
        string? directory = Path.GetDirectoryName(AccountsFilePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Brak katalogu profili kont.");

        Directory.CreateDirectory(directory);
        string json = JsonSerializer.Serialize(normalized, SerializerOptions);
        string tempPath = Path.Combine(directory, $".{AccountsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, AccountsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    // Removes settings and logs of an additional profile. The main profile keeps the
    // shared legacy files and is never deleted here.
    public static void DeleteProfileData(string? profileId)
    {
        string id = MinerProfileId.Normalize(profileId);
        if (id == MinerProfileId.Default)
            return;

        string directory = new SettingsService(id).SettingsDirectoryPath;
        string expectedSuffix = Path.Combine("Profiles", id);
        if (!directory.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Nieoczekiwana ścieżka danych profilu.");

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static IReadOnlyList<MinerAccountProfile> Normalize(IEnumerable<MinerAccountProfile>? profiles)
    {
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<MinerAccountProfile>();

        foreach (MinerAccountProfile profile in profiles ?? Array.Empty<MinerAccountProfile>())
        {
            string id = MinerProfileId.Normalize(profile.Id);
            if (!usedIds.Add(id))
                continue;

            result.Add(new MinerAccountProfile
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(profile.Name) ? $"Konto {result.Count + 1}" : profile.Name.Trim(),
                Enabled = profile.Enabled,
                CreatedAt = profile.CreatedAt == default ? DateTimeOffset.UtcNow : profile.CreatedAt
            });
        }

        if (result.Count == 0)
            result.Add(CreateDefaultProfile());

        return result;
    }

    private static MinerAccountProfile CreateDefaultProfile() => new()
    {
        Id = MinerProfileId.Default,
        Name = "Konto główne"
    };

    private sealed class AccountsFileGate : IDisposable
    {
        private const string MutexName = @"LocalMinecraftHelper.Kopacz.Accounts";
        private readonly Mutex? _mutex;

        private AccountsFileGate(Mutex? mutex) => _mutex = mutex;

        public static AccountsFileGate Acquire()
        {
            var mutex = new Mutex(false, MutexName);
            try
            {
                if (mutex.WaitOne(TimeSpan.FromSeconds(5)))
                    return new AccountsFileGate(mutex);
            }
            catch (AbandonedMutexException)
            {
                // Poprzedni właściciel zakończył się w trakcie zapisu; blokada jest teraz nasza.
                return new AccountsFileGate(mutex);
            }

            // Inny proces trzyma blokadę zbyt długo — zapisujemy mimo to, jak przed zmianą.
            AppLog.Warn("Konta", "Nie udało się zablokować accounts.json w 5 s — zapisuję bez blokady.");
            mutex.Dispose();
            return new AccountsFileGate(null);
        }

        public void Dispose()
        {
            if (_mutex == null)
                return;
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}

