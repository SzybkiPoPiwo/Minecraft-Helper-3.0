using System;
using System.IO;
using System.Text.Json;
using MinecraftHelper.Models;

namespace MinecraftHelper.Services
{
    public class SettingsService
    {
        private const string RootSettingsFolderName = "Minecraft Helper";
        private const string ProductSettingsFolderName = "Kopacz";
        private const string SettingsFileName = "settings.json";
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string _profileId;

        public SettingsService(string? profileId = null)
        {
            _profileId = MinerProfileId.Normalize(profileId);
        }

        public string ProfileId => _profileId;

        public string SettingsDirectoryPath => _profileId == MinerProfileId.Default
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                RootSettingsFolderName,
                ProductSettingsFolderName)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                RootSettingsFolderName,
                ProductSettingsFolderName,
                "Profiles",
                _profileId);

        public string SettingsFilePath => Path.Combine(SettingsDirectoryPath, SettingsFileName);

        private string LegacySettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            RootSettingsFolderName,
            SettingsFileName);

        public bool SettingsFileExists =>
            File.Exists(SettingsFilePath)
            || (_profileId == MinerProfileId.Default && File.Exists(LegacySettingsFilePath));

        private void EnsureSettingsDirectoryExists()
        {
            Directory.CreateDirectory(SettingsDirectoryPath);
        }

        public void Save(AppSettings settings)
        {
            EnsureSettingsDirectoryExists();
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            WriteAllTextAtomically(SettingsFilePath, json);
        }

        public AppSettings Load()
        {
            try
            {
                string sourcePath = File.Exists(SettingsFilePath)
                    ? SettingsFilePath
                    : _profileId == MinerProfileId.Default ? LegacySettingsFilePath : SettingsFilePath;
                if (File.Exists(sourcePath))
                {
                    string json = File.ReadAllText(sourcePath);
                    AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    if (!string.Equals(sourcePath, SettingsFilePath, StringComparison.OrdinalIgnoreCase))
                        TryMigrateLegacySettings(settings);
                    return settings;
                }

                // First run: create a default settings file in AppData.
                AppSettings defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }
            catch (Exception ex)
            {
                AppLog.Error("Ustawienia", "Nie udało się odczytać ustawień — używam domyślnych", ex);
            }
            return new AppSettings();
        }

        private void TryMigrateLegacySettings(AppSettings settings)
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                    Save(settings);
            }
            catch (Exception ex)
            {
                AppLog.Error("Ustawienia", "Nie udało się przenieść starych ustawień Kopacza", ex);
            }
        }

        public void ExportToFile(AppSettings settings, string filePath)
        {
            try
            {
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(settings, SerializerOptions);
                WriteAllTextAtomically(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Błąd eksportu ustawień.", ex);
            }
        }

        public AppSettings ImportFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("Plik nie znaleziony");

                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Błąd importu ustawień.", ex);
            }
        }

        private static void WriteAllTextAtomically(string filePath, string contents)
        {
            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(directory))
                directory = Directory.GetCurrentDirectory();

            string tempPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempPath, contents);
                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
