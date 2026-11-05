using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services;

internal enum ResourcePackState
{
    Current,
    Outdated,
    Missing,
    NoGame
}

internal sealed record ResourcePackStatus(
    ResourcePackState State,
    string? GameDirectory,
    string? PackPath,
    string? SourceJar,
    bool OldUnpackedFolder,
    string Message);

// Szuka gry, z której Kopacz weźmie tekstury (1.8.8.jar), i zapisuje paczkę znaczników
// do jej folderu resourcepacks. Działa bez sieci i bez plików Mojanga w repozytorium.
internal static class ResourcePackInstaller
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string DefaultGameDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    public static ResourcePackStatus GetStatus(string? gameDirectory = null)
    {
        string game = string.IsNullOrWhiteSpace(gameDirectory) ? DefaultGameDirectory : gameDirectory;
        string? jar = FindSourceJar(game);
        if (!Directory.Exists(game) || jar == null)
        {
            return new ResourcePackStatus(ResourcePackState.NoGame, Directory.Exists(game) ? game : null, null, null, false,
                $"Nie znaleziono gry 1.8.8 w „{game}”. Uruchom raz Minecrafta 1.8.8 z launchera — Kopacz bierze tekstury z jego pliku 1.8.8.jar.");
        }

        string packs = Path.Combine(game, "resourcepacks");
        string pack = Path.Combine(packs, MarkerResourcePack.FileName);
        bool oldFolder = Directory.Exists(Path.Combine(packs, Path.GetFileNameWithoutExtension(MarkerResourcePack.FileName)));
        if (!File.Exists(pack))
            return new ResourcePackStatus(ResourcePackState.Missing, game, pack, jar, oldFolder, "Brak paczki zasobów Minecraft Helper.");

        int? version;
        try
        {
            using FileStream stream = File.OpenRead(pack);
            version = MarkerResourcePack.ReadGeneratorVersion(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            version = null;
        }

        return version == MarkerResourcePack.GeneratorVersion
            ? new ResourcePackStatus(ResourcePackState.Current, game, pack, jar, oldFolder, "Paczka zasobów jest aktualna.")
            : new ResourcePackStatus(ResourcePackState.Outdated, game, pack, jar, oldFolder, "Paczka zasobów jest starsza niż ta wersja Kopacza.");
    }

    /// <summary>Tworzy paczkę z tekstur gry i zapisuje ją w resourcepacks (zastępując starszą wersję ZIP).</summary>
    public static async Task<ResourcePackStatus> InstallAsync(string? gameDirectory = null)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ResourcePackStatus status = GetStatus(gameDirectory);
            if (status.State == ResourcePackState.NoGame || status.SourceJar == null || status.PackPath == null)
                return status;

            byte[] zip = await Task.Run(() => BuildFromJar(status.SourceJar)).ConfigureAwait(false);
            string directory = Path.GetDirectoryName(status.PackPath)!;
            Directory.CreateDirectory(directory);
            // Zapis przez plik tymczasowy — gra czytająca paczkę nie trafi na połowę pliku.
            string temp = Path.Combine(directory, $".{MarkerResourcePack.FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temp, zip).ConfigureAwait(false);
                File.Move(temp, status.PackPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            AppLog.Info("Paczka", $"Utworzono paczkę zasobów z {Path.GetFileName(status.SourceJar)}: {status.PackPath}");
            return GetStatus(gameDirectory) with { Message = "Utworzono paczkę zasobów. Włącz ją w grze: Opcje → Paczki zasobów i ustaw NAD innymi paczkami." };
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Przy starcie Kopacza: tworzy paczkę, gdy jej brakuje albo jest nieaktualna. Nie rusza gry, gdy jej nie ma.</summary>
    public static async Task<ResourcePackStatus?> EnsureInstalledAsync()
    {
        try
        {
            ResourcePackStatus status = GetStatus();
            if (status.State is ResourcePackState.Current or ResourcePackState.NoGame)
                return status;
            return await InstallAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Paczka", "Nie udało się automatycznie utworzyć paczki zasobów: " + ex.GetBaseException().Message);
            return null;
        }
    }

    internal static byte[] BuildFromJar(string jarPath)
    {
        using ZipArchive jar = ZipFile.OpenRead(jarPath);
        static byte[] Read(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }

        // Domyślny wygląd 1.8.8 z gry użytkownika — kopiowany tylko do paczki na jego dysku.
        List<(string Path, byte[] Data)> baseFiles = jar.Entries
            .Where(entry => entry.Length > 0 && MarkerResourcePack.BaseDirectories.Any(directory => entry.FullName.StartsWith(directory, StringComparison.Ordinal)))
            .Select(entry => (entry.FullName, Read(entry)))
            .ToList();
        return MarkerResourcePack.Build(path =>
        {
            ZipArchiveEntry? entry = jar.GetEntry("assets/minecraft/textures/" + path);
            return entry == null ? null : Read(entry);
        }, baseFiles);
    }

    // Najpierw czysty 1.8.8.jar z launchera; potem inne wersje 1.8.8 (np. BlazingPack) z teksturami.
    private static string? FindSourceJar(string gameDirectory)
    {
        string versions = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versions))
            return null;

        string vanilla = Path.Combine(versions, "1.8.8", "1.8.8.jar");
        if (File.Exists(vanilla) && HasTextures(vanilla))
            return vanilla;

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(versions, "*1.8.8*")
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jar"))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return candidates.FirstOrDefault(HasTextures);
    }

    private static bool HasTextures(string jarPath)
    {
        try
        {
            using ZipArchive jar = ZipFile.OpenRead(jarPath);
            return jar.GetEntry("assets/minecraft/textures/gui/container/inventory.png") != null
                && jar.GetEntry("assets/minecraft/textures/items/diamond.png") != null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
