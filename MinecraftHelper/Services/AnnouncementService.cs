using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services
{
    internal enum AnnouncementLevel { Info, Warning, Alarm }

    /// <summary>Jeden komunikat od autora, już sprawdzony i oczyszczony (tylko tekst).</summary>
    internal sealed record AnnouncementItem(
        string Id,
        string Title,
        string Text,
        AnnouncementLevel Level,
        DateTime? PublishedUtc,
        Uri? Link,
        string LinkText);

    /// <summary>
    /// Czyta plik komunikatów (announcements.json w repozytorium). Czysta logika, bez WPF i sieci.
    /// Plik pisze człowiek, więc parser jest wyrozumiały dla przecinków i komentarzy, ale ostry
    /// wobec treści: komunikat jest tylko tekstem, link tylko do github.com, a pole z błędem
    /// pomija cały komunikat (lepiej nie pokazać, niż pokazać nie temu odbiorcy).
    /// </summary>
    internal static class AnnouncementParser
    {
        public const int MaxShown = 10;
        public const int MaxIdLength = 64;
        public const int MaxTitleLength = 80;
        public const int MaxTextLength = 500;
        public const int MaxLinkTextLength = 30;
        private const int MaxParsed = 40;

        /// <returns>false, gdy to nie jest poprawny plik komunikatów (wtedy program zostawia poprzednią listę).</returns>
        public static bool TryParse(string? json, bool minerEdition, string currentVersion, DateTime nowUtc, out IReadOnlyList<AnnouncementItem> items)
        {
            items = Array.Empty<AnnouncementItem>();
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 16
                });

                JsonElement list;
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    list = document.RootElement;
                else if (document.RootElement.ValueKind == JsonValueKind.Object
                         && document.RootElement.TryGetProperty("announcements", out JsonElement array)
                         && array.ValueKind == JsonValueKind.Array)
                    list = array;
                else
                    return false;

                Version? current = TryParseVersion(currentVersion, out Version parsedCurrent) ? parsedCurrent : null;
                var found = new List<(AnnouncementItem Item, int Index)>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                // Nowe wpisy dopisuje się na końcu pliku, więc przy bardzo długiej liście czytamy ostatnie
                // MaxParsed (najnowsze), a nie pierwsze.
                JsonElement[] entries = list.EnumerateArray().ToArray();
                for (int position = Math.Max(0, entries.Length - MaxParsed); position < entries.Length; position++)
                {
                    JsonElement element = entries[position];
                    if (element.ValueKind != JsonValueKind.Object)
                        continue;
                    AnnouncementItem? item = ParseItem(element, minerEdition, current, nowUtc);
                    if (item != null && ids.Add(item.Id))
                        found.Add((item, position));
                }

                // Najnowsze na górze, najstarsze na dole (po `published`). Przy tej samej dacie i dla komunikatów bez
                // daty (idą pod wszystkie datowane) nowszy jest ten, który stoi w pliku dalej — wpisy dopisuje się na końcu.
                // Informację o nowej wersji dokłada na samą górę UpdateAnnouncements.Combine.
                items = found
                    .OrderByDescending(entry => entry.Item.PublishedUtc ?? DateTime.MinValue)
                    .ThenByDescending(entry => entry.Index)
                    .Take(MaxShown)
                    .Select(entry => entry.Item)
                    .ToList();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static AnnouncementItem? ParseItem(JsonElement element, bool minerEdition, Version? current, DateTime nowUtc)
        {
            string id = GetString(element, "id").Trim();
            if (id.Length == 0 || id.Length > MaxIdLength || !id.All(IsIdChar))
                return null;

            string text = Clean(GetString(element, "text"), MaxTextLength, singleLine: false);
            if (text.Length == 0)
                return null;
            string title = Clean(GetString(element, "title"), MaxTitleLength, singleLine: true);
            if (title.Length == 0)
                title = "Komunikat";

            if (!AppliesToEdition(element, minerEdition))
                return null;
            if (!VersionInRange(element, current))
                return null;

            if (!TryReadDate(element, "published", endOfDay: false, out DateTime? published)
                || !TryReadDate(element, "expires", endOfDay: true, out DateTime? expires))
                return null;
            // Zaplanowany na później albo już nieaktualny.
            if (published > nowUtc || (expires != null && expires <= nowUtc))
                return null;

            string linkText = Clean(GetString(element, "action"), MaxLinkTextLength, singleLine: true);
            return new AnnouncementItem(
                id,
                title,
                text,
                ParseLevel(GetString(element, "level")),
                published,
                SafeLink(GetString(element, "url")),
                linkText.Length == 0 ? "Szczegóły" : linkText);
        }

        private static bool IsIdChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':';

        private static AnnouncementLevel ParseLevel(string value) => value.Trim().ToLowerInvariant() switch
        {
            "alarm" or "alert" => AnnouncementLevel.Alarm,
            "warning" or "warn" or "ostrzezenie" or "ostrzeżenie" => AnnouncementLevel.Warning,
            _ => AnnouncementLevel.Info
        };

        /// <summary>Brak pola „apps” = oba programy. Pole z samymi nieznanymi nazwami = nikt.</summary>
        private static bool AppliesToEdition(JsonElement element, bool minerEdition)
        {
            if (!element.TryGetProperty("apps", out JsonElement apps) || apps.ValueKind == JsonValueKind.Null)
                return true;

            var names = new List<string>();
            if (apps.ValueKind == JsonValueKind.String)
                names.Add(apps.GetString() ?? string.Empty);
            else if (apps.ValueKind == JsonValueKind.Array)
                names.AddRange(apps.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString() ?? string.Empty));
            else
                return false;

            bool miner = false, clicker = false;
            foreach (string name in names.Select(n => n.Trim().ToLowerInvariant()))
            {
                switch (name)
                {
                    case "all" or "both" or "oba":
                        miner = clicker = true;
                        break;
                    case "miner" or "kopacz":
                        miner = true;
                        break;
                    case "autoclicker" or "auto-clicker" or "clicker" or "pvp":
                        clicker = true;
                        break;
                }
            }

            return minerEdition ? miner : clicker;
        }

        private static bool VersionInRange(JsonElement element, Version? current)
        {
            string min = GetString(element, "minVersion").Trim();
            string max = GetString(element, "maxVersion").Trim();
            Version? minVersion = null, maxVersion = null;
            if (min.Length > 0)
            {
                if (!TryParseVersion(min, out Version parsedMin))
                    return false;
                minVersion = parsedMin;
            }
            if (max.Length > 0)
            {
                if (!TryParseVersion(max, out Version parsedMax))
                    return false;
                maxVersion = parsedMax;
            }

            // Nie znamy własnej wersji (np. wersja robocza): nie da się ocenić zakresu, więc pokazujemy.
            if (current == null)
                return true;
            return (minVersion == null || current >= minVersion) && (maxVersion == null || current <= maxVersion);
        }

        /// <summary>„3.0.3”, „v3.0.3”, „3.0.3-miner” → 3.0.3.0. Porównywalne ze sobą niezależnie od liczby składników.</summary>
        internal static bool TryParseVersion(string? value, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            string text = UpdateCheckService.NormalizeVersion(value);
            int cut = text.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0)
                text = text[..cut];
            if (text.Length == 0)
                return false;
            if (!text.Contains('.'))
                text += ".0";
            if (!Version.TryParse(text, out Version? parsed))
                return false;
            version = new Version(Math.Max(parsed.Major, 0), Math.Max(parsed.Minor, 0), Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
            return true;
        }

        /// <returns>false, gdy pole jest wpisane, ale to nie data. Data bez godziny: początek dnia (UTC), a dla „expires” koniec dnia.</returns>
        private static bool TryReadDate(JsonElement element, string name, bool endOfDay, out DateTime? value)
        {
            value = null;
            string text = GetString(element, name).Trim();
            if (text.Length == 0)
                return true;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return false;
            DateTime utc = parsed.UtcDateTime;
            value = endOfDay && text.Length <= 10 ? utc.AddDays(1) : utc;
            return true;
        }

        private static Uri? SafeLink(string value)
        {
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(uri.UserInfo))
                return null;
            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
                ? uri
                : null;
        }

        private static string GetString(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;

        /// <summary>Zwykły tekst: bez znaków sterujących i niewidocznych znaków formatujących (podszywanie się), z ograniczoną długością.</summary>
        internal static string Clean(string? value, int maxLength, bool singleLine)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(Math.Min(value.Length, maxLength + 1));
            foreach (char c in value.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                if (c == '\n')
                    builder.Append(singleLine ? ' ' : '\n');
                else if (c == '\t')
                    builder.Append(' ');
                else if (!char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format)
                    builder.Append(c);
            }

            string text = builder.ToString().Trim();
            while (text.Contains("\n\n\n", StringComparison.Ordinal))
                text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
            if (text.Length > maxLength)
                text = text[..(maxLength - 1)].TrimEnd() + "…";
            return text;
        }
    }

    /// <summary>
    /// Pobiera plik komunikatów z GitHuba. Zapytanie jest takie samo jak przy sprawdzaniu
    /// aktualizacji: zwykły GET, w nagłówku tylko numer wersji programu — nic więcej nie jest wysyłane.
    /// </summary>
    internal static class AnnouncementService
    {
        /// <summary>Plik na głównej gałęzi repozytorium wydań. Zmiana treści komunikatów = zmiana tego pliku.</summary>
        internal const string SourceUrl =
            "https://raw.githubusercontent.com/SzybkiPoPiwo/Minecraft-Helper-3.0/main/announcements.json";

        internal const int MaxBodyBytes = 64 * 1024;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        /// <returns>
        /// Lista komunikatów dla tego programu i tej wersji. Pusta lista, gdy pliku nie ma (404) albo nic nie pasuje.
        /// null, gdy nie udało się pobrać albo plik jest uszkodzony — wtedy program zostawia to, co ma.
        /// </returns>
        public static async Task<IReadOnlyList<AnnouncementItem>?> FetchAsync(
            string currentVersion,
            bool minerEdition,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
                request.Headers.UserAgent.ParseAdd($"MinecraftHelper/{UpdateCheckService.NormalizeVersion(currentVersion)}");

                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return Array.Empty<AnnouncementItem>();
                if (!response.IsSuccessStatusCode)
                    return null;
                if (response.Content.Headers.ContentLength is long length && length > MaxBodyBytes)
                    return null;

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + read > MaxBodyBytes)
                        return null;
                    buffer.Write(chunk, 0, read);
                }

                string json = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                return AnnouncementParser.TryParse(json, minerEdition, currentVersion, DateTime.UtcNow, out IReadOnlyList<AnnouncementItem> items)
                    ? items
                    : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                // Brak sieci, DNS, TLS itp. — komunikaty są dodatkiem, więc cisza.
                return null;
            }
        }
    }

    /// <summary>
    /// Informacja o nowej wersji programu jako komunikat w dzwonku. Powstaje z odpowiedzi GitHuba
    /// (a nie z announcements.json), więc jest niezależna od przełącznika komunikatów od autora
    /// i zawsze stoi na liście pierwsza. Zastępuje dawne okno aktualizacji.
    /// </summary>
    internal static class UpdateAnnouncements
    {
        private const int MaxChanges = 6;
        private const int MaxChangeLength = 110;
        private const int MaxTextLength = 900;

        public static AnnouncementItem FromUpdate(AppUpdateInfo update)
        {
            List<string> changes = (update.Notes ?? Array.Empty<UpdateReleaseNotes>())
                .SelectMany(notes => notes.Sections)
                .SelectMany(section => section.Lines)
                .Select(line => AnnouncementParser.Clean(line, MaxChangeLength, singleLine: true))
                .Where(line => line.Length > 0)
                .ToList();

            var text = new StringBuilder($"Masz wersję {update.CurrentVersion}.");
            if (changes.Count > 0)
            {
                text.Append("\nCo nowego:");
                foreach (string change in changes.Take(MaxChanges))
                    text.Append("\n• ").Append(change);
                if (changes.Count > MaxChanges)
                {
                    int rest = changes.Count - MaxChanges;
                    text.Append($"\n…i jeszcze {rest} {ChangesWord(rest)}.");
                }
            }
            else
            {
                text.Append(" Pełny opis zmian jest na stronie wydania.");
            }

            return new AnnouncementItem(
                "update-" + SafeId(update.LatestVersion),
                $"Dostępna wersja {update.LatestVersion}",
                AnnouncementParser.Clean(text.ToString(), MaxTextLength, singleLine: false),
                AnnouncementLevel.Info,
                update.PublishedAt?.UtcDateTime,
                // Strona wydania ma opis, sumę kontrolną i uwagę o SmartScreen — lepsza niż goły plik instalatora.
                update.ReleasePageUri ?? update.DownloadUri,
                "Pobierz");
        }

        /// <summary>Aktualizacja (jeśli jest) na początku, potem komunikaty od autora. Ten sam identyfikator nie powtarza się.</summary>
        public static IReadOnlyList<AnnouncementItem> Combine(AnnouncementItem? update, IReadOnlyList<AnnouncementItem> announcements)
        {
            if (update == null)
                return announcements;
            var combined = new List<AnnouncementItem>(announcements.Count + 1) { update };
            combined.AddRange(announcements.Where(item => !string.Equals(item.Id, update.Id, StringComparison.Ordinal)));
            return combined;
        }

        private static string SafeId(string version)
        {
            string id = new string(version.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
            return id.Length > AnnouncementParser.MaxIdLength - 7 ? id[..(AnnouncementParser.MaxIdLength - 7)] : id;
        }

        private static string ChangesWord(int count) =>
            count == 1 ? "zmiana"
            : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "zmiany"
            : "zmian";
    }

    /// <summary>
    /// Dymek, który wyskakuje nad dzwonkiem, gdy przychodzi nowy komunikat. Tu tylko logika (co pokazać
    /// i jak długo) — samo okienko jest w AnnouncementBell. Wiadomość od autora i ostrzeżenie pokazują
    /// od razu swoją treść; informacja o nowej wersji mówi krótko, że jest, a zmiany są na liście.
    /// </summary>
    internal static class AnnouncementBubble
    {
        public const int MaxBodyLength = 240;

        public static bool IsUpdate(AnnouncementItem item) => item.Id.StartsWith("update-", StringComparison.Ordinal);

        /// <summary>Nowe (nieprzeczytane) komunikaty, o których dymek jeszcze nie mówił w tej sesji.</summary>
        public static IReadOnlyList<AnnouncementItem> Pending(IReadOnlyList<AnnouncementItem> items, IReadOnlySet<string> read, IReadOnlySet<string> announced) =>
            items.Where(item => !read.Contains(item.Id) && !announced.Contains(item.Id)).ToList();

        /// <summary>
        /// O którym komunikacie mówi dymek, gdy nowych jest kilka: o najpoważniejszym; przy równej wadze
        /// wiadomość od autora wyprzedza informację o aktualizacji, a dalej decyduje kolejność na liście.
        /// </summary>
        public static AnnouncementItem Headline(IReadOnlyList<AnnouncementItem> pending) =>
            pending.Select((item, index) => (Item: item, Index: index))
                .OrderByDescending(entry => entry.Item.Level)
                .ThenBy(entry => IsUpdate(entry.Item) ? 1 : 0)
                .ThenBy(entry => entry.Index)
                .First().Item;

        public static string Tag(AnnouncementItem item) =>
            IsUpdate(item) ? "Nowa wersja"
            : item.Level switch
            {
                AnnouncementLevel.Alarm => "Ważne",
                AnnouncementLevel.Warning => "Ostrzeżenie",
                _ => "Nowa wiadomość"
            };

        /// <summary>
        /// Aktualizacja: pierwsza linijka („Masz wersję …”) i zachęta do otwarcia listy. Wiadomość od autora
        /// i ostrzeżenie: cała treść (do 240 znaków), żeby nie trzeba było nic otwierać, by ją przeczytać.
        /// </summary>
        public static string Body(AnnouncementItem item)
        {
            if (!IsUpdate(item))
                return AnnouncementParser.Clean(item.Text, MaxBodyLength, singleLine: false);

            string first = item.Text.Split('\n')[0].Trim();
            return (first.Length > 0 ? first + " " : string.Empty) + "Kliknij, aby zobaczyć, co nowego.";
        }

        /// <summary>
        /// Jak długo okienko jest widoczne: 3 s dla każdego poziomu. Okienko nie przejmuje fokusu, więc w grze
        /// nie da się go zamknąć inaczej niż kliknięciem (które przywraca program i wyrywa z gry) — dlatego ma
        /// znikać szybko, nawet alarm. Pełna treść zostaje na liście w dzwonku.
        /// </summary>
        public static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(3);
    }

    internal sealed record AnnouncementState(bool Enabled, IReadOnlyList<string> Read);

    /// <summary>
    /// Wspólny dla obu programów i wszystkich kont stan komunikatów: %AppData%\Minecraft Helper\announcements-state.json
    /// ({ "enabled": true, "read": [ "id", … ] }). Poza ustawieniami konta, żeby przełączenie konta
    /// nie przywracało już przeczytanych komunikatów. Błędy odczytu i zapisu nie przerywają programu.
    /// </summary>
    internal sealed class AnnouncementStateStore
    {
        private const int MaxRead = 200;
        private static readonly object Gate = new();
        private readonly string _folder;
        private readonly string _path;

        public AnnouncementStateStore(string? folder = null)
        {
            _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Helper");
            _path = Path.Combine(_folder, "announcements-state.json");
        }

        public string FilePath => _path;

        public AnnouncementState Load()
        {
            lock (Gate)
                return LoadUnsafe();
        }

        public bool SetEnabled(bool enabled)
        {
            lock (Gate)
            {
                AnnouncementState state = LoadUnsafe();
                return Save(state with { Enabled = enabled });
            }
        }

        public AnnouncementState MarkRead(IEnumerable<string> ids)
        {
            lock (Gate)
            {
                AnnouncementState state = LoadUnsafe();
                var read = state.Read.ToList();
                foreach (string id in ids)
                {
                    if (!string.IsNullOrWhiteSpace(id) && !read.Contains(id, StringComparer.Ordinal))
                        read.Add(id);
                }
                if (read.Count > MaxRead)
                    read = read.Skip(read.Count - MaxRead).ToList();
                state = state with { Read = read };
                Save(state);
                return state;
            }
        }

        private AnnouncementState LoadUnsafe()
        {
            try
            {
                if (!File.Exists(_path))
                    return new AnnouncementState(true, Array.Empty<string>());

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_path));
                JsonElement root = document.RootElement;
                bool enabled = !(root.TryGetProperty("enabled", out JsonElement flag) && flag.ValueKind == JsonValueKind.False);
                var read = new List<string>();
                if (root.TryGetProperty("read", out JsonElement array) && array.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        string? id = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(id) && id.Length <= AnnouncementParser.MaxIdLength && !read.Contains(id, StringComparer.Ordinal))
                            read.Add(id);
                    }
                }
                return new AnnouncementState(enabled, read.Count > MaxRead ? read.Skip(read.Count - MaxRead).ToList() : read);
            }
            catch (Exception)
            {
                // Uszkodzony plik = jak świeża instalacja.
                return new AnnouncementState(true, Array.Empty<string>());
            }
        }

        private bool Save(AnnouncementState state)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                string json = JsonSerializer.Serialize(
                    new { enabled = state.Enabled, read = state.Read },
                    new JsonSerializerOptions { WriteIndented = true });
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                File.Move(temporary, _path, overwrite: true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
