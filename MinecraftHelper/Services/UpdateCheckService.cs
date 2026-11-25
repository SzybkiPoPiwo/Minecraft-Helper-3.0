using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services
{
    /// <summary>Podsekcja „Co nowego” z opisu wydania, np. „Oba programy” albo „Kopacz”.</summary>
    internal sealed record UpdateNotesSection(string Title, IReadOnlyList<string> Lines);

    /// <summary>Zmiany jednego wydania nowszego niż zainstalowana wersja.</summary>
    internal sealed record UpdateReleaseNotes(string Version, IReadOnlyList<UpdateNotesSection> Sections);

    internal sealed record AppUpdateInfo(
        string CurrentVersion,
        string LatestVersion,
        string ReleaseName,
        Uri DownloadUri,
        DateTimeOffset? PublishedAt = null,
        IReadOnlyList<UpdateReleaseNotes>? Notes = null,
        Uri? ReleasePageUri = null);

    /// <summary>
    /// Wynik sprawdzenia GitHuba. Succeeded=false to błąd sieci albo odpowiedzi — wtedy nie wiemy,
    /// czy aktualizacja jest, i program zostawia to, co już wie. Succeeded=true, Update=null: wersja aktualna.
    /// </summary>
    internal readonly record struct UpdateCheckResult(bool Succeeded, AppUpdateInfo? Update);

    internal static class UpdateCheckService
    {
        // Lista wydań zamiast samego „latest”: po przeskoczeniu kilku wersji pokazujemy zmiany
        // z każdej. Wersje robocze i testowe (draft, prerelease) są pomijane.
        private const string ReleasesEndpoint =
            "https://api.github.com/repos/SzybkiPoPiwo/Minecraft-Helper-3.0/releases?per_page=10";

        // Ile wydań nowszych od zainstalowanego pokazać w oknie aktualizacji.
        private const int MaxReleasesInNotes = 5;

#if MINER_EDITION
        internal const bool IsMinerEdition = true;
#else
        internal const bool IsMinerEdition = false;
#endif

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        public static async Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.UserAgent.ParseAdd($"MinecraftHelper/{NormalizeVersion(currentVersion)}");

                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return new UpdateCheckResult(false, null);

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(
                    responseStream,
                    cancellationToken: cancellationToken);

                return new UpdateCheckResult(true, BuildUpdateInfo(document.RootElement, currentVersion, IsMinerEdition));
            }
            catch
            {
                // Sprawdzenie jest tylko dodatkiem: żaden błąd sieci ani odpowiedzi
                // nie może blokować uruchomienia aplikacji.
                return new UpdateCheckResult(false, null);
            }
        }

        /// <summary>
        /// Najnowsze wydanie z listy GitHuba i zmiany ze wszystkich wydań nowszych niż bieżąca
        /// wersja. Bez sieci — wydzielone, żeby dało się to sprawdzić testem.
        /// </summary>
        internal static AppUpdateInfo? BuildUpdateInfo(JsonElement releases, string currentVersion, bool minerEdition)
        {
            if (releases.ValueKind != JsonValueKind.Array)
                return null;

            string normalizedCurrentVersion = NormalizeVersion(currentVersion);
            var newer = new List<(string Version, JsonElement Release)>();
            foreach (JsonElement release in releases.EnumerateArray())
            {
                if (GetBool(release, "draft") || GetBool(release, "prerelease"))
                    continue;
                string version = NormalizeVersion(GetString(release, "tag_name"));
                if (version.Length > 0 && ReleaseNotesCatalog.CompareVersions(version, normalizedCurrentVersion) > 0)
                    newer.Add((version, release));
            }

            if (newer.Count == 0)
                return null;

            newer.Sort((left, right) => ReleaseNotesCatalog.CompareVersions(right.Version, left.Version));
            (string latestVersion, JsonElement latest) = newer[0];

            string releaseName = GetString(latest, "name");
            if (string.IsNullOrWhiteSpace(releaseName))
                releaseName = $"Minecraft Helper {latestVersion}";

            DateTimeOffset? publishedAt = DateTimeOffset.TryParse(GetString(latest, "published_at"), out DateTimeOffset published)
                ? published
                : null;

            List<UpdateReleaseNotes> notes = newer
                .Take(MaxReleasesInNotes)
                .Select(item => new UpdateReleaseNotes(item.Version, ReleaseBodyParser.ParseWhatsNew(GetString(item.Release, "body"), minerEdition)))
                .Where(item => item.Sections.Count > 0)
                .ToList();

            Uri? releaseUri = GetSafeGitHubUri(GetString(latest, "html_url"));
            Uri? installerUri = FindInstallerUri(latest);
            if (installerUri == null && releaseUri == null)
                return null;

            return new AppUpdateInfo(
                normalizedCurrentVersion,
                latestVersion,
                releaseName,
                installerUri ?? releaseUri!,
                publishedAt,
                notes,
                releaseUri);
        }

        private static Uri? FindInstallerUri(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out JsonElement assets)
                || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var candidates = new List<(string Name, string Url)>();
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = GetString(asset, "name");
                string url = GetString(asset, "browser_download_url");
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(url))
                {
                    candidates.Add((name, url));
                }
            }

            foreach ((string _, string url) in candidates
                         .OrderByDescending(candidate =>
                             candidate.Name.Contains("MinecraftHelper", StringComparison.OrdinalIgnoreCase)
                             && candidate.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                         .ThenByDescending(candidate =>
                             candidate.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase)))
            {
                Uri? uri = GetSafeGitHubUri(url);
                if (uri != null)
                    return uri;
            }

            return null;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement property)
                   && property.ValueKind == JsonValueKind.String
                ? property.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static bool GetBool(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.True;

        internal static string NormalizeVersion(string? version)
        {
            string normalized = version?.Trim() ?? string.Empty;
            return normalized.StartsWith('v') || normalized.StartsWith('V')
                ? normalized[1..]
                : normalized;
        }

        private static Uri? GetSafeGitHubUri(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
                ? uri
                : null;
        }
    }

    /// <summary>
    /// Sekcja „### Co nowego” z opisu wydania (Wydanie-X.Y.Z.md). Podsekcje to linie
    /// „**Oba programy**”, „**Kopacz**”, „**Auto Clicker**”, a zmiany — punkty „- …”.
    /// Każdy program pokazuje wszystko poza podsekcją drugiego programu.
    /// </summary>
    internal static class ReleaseBodyParser
    {
        private const string WhatsNewHeading = "Co nowego";
        private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        public static IReadOnlyList<UpdateNotesSection> ParseWhatsNew(string? body, bool minerEdition)
        {
            var sections = new List<(string Title, List<string> Lines)>();
            if (string.IsNullOrWhiteSpace(body))
                return Array.Empty<UpdateNotesSection>();

            string otherApp = minerEdition ? "Auto Clicker" : "Kopacz";
            bool inside = false;
            bool skipSection = false;
            string title = string.Empty;

            foreach (string rawLine in body.Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith('#'))
                {
                    // Następny nagłówek kończy sekcję „Co nowego”.
                    if (inside)
                        break;
                    inside = line.TrimStart('#').Trim().StartsWith(WhatsNewHeading, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inside || line.Length == 0)
                    continue;

                if (line.Length > 4 && line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith("**", StringComparison.Ordinal))
                {
                    title = Clean(line);
                    skipSection = title.StartsWith(otherApp, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!(line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal)))
                    continue; // zwykły akapit, np. link do README
                if (skipSection)
                    continue;

                string text = Clean(line[2..]);
                if (text.Length == 0)
                    continue;
                if (sections.Count == 0 || sections[^1].Title != title)
                    sections.Add((title, new List<string>()));
                sections[^1].Lines.Add(text);
            }

            return sections.Select(section => new UpdateNotesSection(section.Title, section.Lines)).ToList();
        }

        // Bez formatowania Markdowna: pogrubienia, kod i linki zostają samym tekstem.
        private static string Clean(string text)
        {
            text = MarkdownLink.Replace(text, "$1");
            text = text.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
            return Spaces.Replace(text, " ").Trim();
        }
    }
}
