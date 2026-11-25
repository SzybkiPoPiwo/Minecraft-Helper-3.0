using System.Text;
using System.Text.Json;
using MinecraftHelper.Services;

// Komunikaty od autora: parser pliku, filtry (program, wersja, daty), czyszczenie tekstu i stan „przeczytane”.
// Bez WPF, bez sieci i bez danych użytkownika: stan trafia do katalogu tymczasowego.
internal static class Program
{
    private static int _checks;
    private static readonly DateTime Now = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
    private static string Root = "";

    private static int Main()
    {
        try
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MinecraftHelper.slnx"))) dir = dir.Parent;
            Root = dir?.FullName ?? throw new DirectoryNotFoundException("Repository not found.");
            CheckBasics(); CheckAudience(); CheckVersions(); CheckDates(); CheckOrderAndLimits(); CheckSanitizing(); CheckRobustness(); CheckState(); CheckUpdateAnnouncement(); CheckBubble(); CheckWiring();
            Console.WriteLine($"PASS: {_checks} checks; announcement parser, audience/version/date filters, text sanitizing and read state. No network, windows or user data.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + description);
        _checks++;
    }

    private static IReadOnlyList<AnnouncementItem> Parse(string json, bool miner = true, string version = "3.0.3")
    {
        Check(AnnouncementParser.TryParse(json, miner, version, Now, out IReadOnlyList<AnnouncementItem> items), "valid file parses: " + json);
        return items;
    }

    private static string One(string fields) => "{\"announcements\":[{\"id\":\"a1\",\"text\":\"Treść\"" + (fields.Length > 0 ? "," + fields : "") + "}]}";

    private static void CheckBasics()
    {
        var item = Parse("{\"version\":1,\"announcements\":[{\"id\":\"2026-11-25-update\",\"title\":\"Nowa wersja\",\"text\":\"Jest 3.0.4.\",\"level\":\"warning\",\"published\":\"2026-11-24T08:30:00Z\",\"url\":\"https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases\",\"action\":\"Pobierz\"}]}").Single();
        Check(item.Id == "2026-11-25-update" && item.Title == "Nowa wersja" && item.Text == "Jest 3.0.4." && item.Level == AnnouncementLevel.Warning
            && item.PublishedUtc == new DateTime(2026, 11, 24, 8, 30, 0, DateTimeKind.Utc) && item.LinkText == "Pobierz" && item.Link?.Host == "github.com", "all fields are read");

        var minimal = Parse(One("")).Single();
        Check(minimal.Title == "Komunikat" && minimal.Level == AnnouncementLevel.Info && minimal.Link == null && minimal.LinkText == "Szczegóły" && minimal.PublishedUtc == null, "only id and text are required; the rest has defaults");

        Check(Parse(One("\"level\":\"alarm\"")).Single().Level == AnnouncementLevel.Alarm && Parse(One("\"level\":\"ALERT\"")).Single().Level == AnnouncementLevel.Alarm
            && Parse(One("\"level\":\"warn\"")).Single().Level == AnnouncementLevel.Warning && Parse(One("\"level\":\"???\"")).Single().Level == AnnouncementLevel.Info, "levels: alarm, warning, anything else is info");

        // Link: tylko https do github.com (i jego subdomen); reszta to komunikat bez linku, nie odrzucony komunikat.
        foreach (string good in new[] { "https://github.com/a/b", "https://docs.github.com/x", "HTTPS://GITHUB.COM/a" })
            Check(Parse(One($"\"url\":\"{good}\"")).Single().Link != null, "github link accepted: " + good);
        foreach (string bad in new[] { "http://github.com/a", "https://evil.example/github.com", "https://github.com.evil.example/a", "https://notgithub.com/a", "javascript:alert(1)", "file:///C:/x", "https://user:pw@github.com/a", "github.com/a", "" })
        {
            var parsed = Parse(One($"\"url\":\"{bad}\"")).Single();
            Check(parsed.Link == null && parsed.Text == "Treść", "unsafe link dropped, message kept: " + bad);
        }
    }

    private static void CheckAudience()
    {
        string both = One(""), miner = One("\"apps\":[\"miner\"]"), clicker = One("\"apps\":\"autoclicker\"");
        Check(Parse(both, miner: true).Count == 1 && Parse(both, miner: false).Count == 1, "no apps field = both programs");
        Check(Parse(miner, true).Count == 1 && Parse(miner, false).Count == 0, "miner only: shown in Kopacz, hidden in Auto Clicker");
        Check(Parse(clicker, false).Count == 1 && Parse(clicker, true).Count == 0, "autoclicker only (as a string): the reverse");
        Check(Parse(One("\"apps\":[\"Kopacz\",\" Clicker \"]"), true).Count == 1 && Parse(One("\"apps\":[\"Kopacz\",\" Clicker \"]"), false).Count == 1, "names are case/space insensitive, Polish alias works");
        Check(Parse(One("\"apps\":[\"all\"]"), true).Count == 1 && Parse(One("\"apps\":[\"both\"]"), false).Count == 1, "all/both");
        Check(Parse(One("\"apps\":[\"discord\"]"), true).Count == 0 && Parse(One("\"apps\":[]"), true).Count == 0 && Parse(One("\"apps\":5"), true).Count == 0,
            "unknown, empty or malformed audience hides the message from everyone (better silent than wrong audience)");
        Check(Parse(One("\"apps\":null"), false).Count == 1, "null apps = both");
    }

    private static void CheckVersions()
    {
        Check(Parse(One("\"minVersion\":\"3.0.0\",\"maxVersion\":\"3.0.3\""), true, "3.0.3").Count == 1, "range is inclusive at the top");
        Check(Parse(One("\"minVersion\":\"3.0.3\""), true, "3.0.3").Count == 1, "range is inclusive at the bottom");
        Check(Parse(One("\"maxVersion\":\"3.0.2\""), true, "3.0.3").Count == 0 && Parse(One("\"minVersion\":\"3.0.4\""), true, "3.0.3").Count == 0, "outside the range is hidden");
        Check(Parse(One("\"minVersion\":\"3.0.0\""), true, "3.0.3-miner").Count == 1 && Parse(One("\"maxVersion\":\"3.0.3\""), true, "v3.0.3").Count == 1, "suffix and leading v are ignored");
        Check(Parse(One("\"maxVersion\":\"3.0.3\""), true, "3.0.3.0").Count == 1 && Parse(One("\"maxVersion\":\"3.0.3.0\""), true, "3.0.3").Count == 1, "3.0.3 and 3.0.3.0 are the same version");
        Check(Parse(One("\"maxVersion\":\"3.1\""), true, "3.0.9").Count == 1 && Parse(One("\"minVersion\":\"3\""), true, "3.0.0").Count == 1, "short versions are padded");
        Check(Parse(One("\"minVersion\":\"abc\""), true, "3.0.3").Count == 0 && Parse(One("\"maxVersion\":\"\\u0000\""), true, "3.0.3").Count == 0, "unreadable limit hides the message");
        Check(Parse(One("\"minVersion\":\"9.0.0\""), true, "dev").Count == 1, "unknown own version cannot be judged, so the message is shown");
        Check(AnnouncementParser.TryParseVersion("v3.0.3-autoclicker", out Version version) && version == new Version(3, 0, 3, 0), "TryParseVersion normalises");
        Check(!AnnouncementParser.TryParseVersion("", out _) && !AnnouncementParser.TryParseVersion("x.y", out _) && !AnnouncementParser.TryParseVersion(null, out _), "TryParseVersion rejects junk");
    }

    private static void CheckDates()
    {
        Check(Parse(One("\"published\":\"2026-11-25T13:00:00Z\"")).Count == 0 && Parse(One("\"published\":\"2026-11-25T11:00:00Z\"")).Count == 1, "scheduled messages appear at their time");
        Check(Parse(One("\"published\":\"2026-11-26\"")).Count == 0 && Parse(One("\"published\":\"2026-11-25\"")).Count == 1, "date-only published starts at the beginning of that day");
        Check(Parse(One("\"expires\":\"2026-11-25T11:59:00Z\"")).Count == 0 && Parse(One("\"expires\":\"2026-11-25T12:01:00Z\"")).Count == 1, "expired messages disappear");
        Check(Parse(One("\"expires\":\"2026-11-25\"")).Count == 1 && Parse(One("\"expires\":\"2026-11-24\"")).Count == 0, "date-only expires lasts until the end of that day");
        Check(Parse(One("\"published\":\"jutro\"")).Count == 0 && Parse(One("\"expires\":\"2026-13-45\"")).Count == 0, "a wrong date hides the message instead of showing it forever");
        Check(Parse(One("\"published\":\"2026-11-24T10:00:00+02:00\"")).Single().PublishedUtc == new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc), "offsets are converted to UTC");
    }

    private static void CheckOrderAndLimits()
    {
        var ordered = Parse("{\"announcements\":[{\"id\":\"old\",\"text\":\"t\",\"published\":\"2026-10-01\"},{\"id\":\"nodate1\",\"text\":\"t\"},{\"id\":\"new\",\"text\":\"t\",\"published\":\"2026-11-20\"},{\"id\":\"nodate2\",\"text\":\"t\"}]}");
        Check(ordered.Select(i => i.Id).SequenceEqual(new[] { "new", "old", "nodate2", "nodate1" }), "newest first by date; undated go under all dated ones, the later one in the file counts as newer");
        var shuffled = Parse("{\"announcements\":[{\"id\":\"c\",\"text\":\"t\",\"published\":\"2026-11-10\"},{\"id\":\"e\",\"text\":\"t\",\"published\":\"2026-11-24T18:00:00Z\"},{\"id\":\"a\",\"text\":\"t\",\"published\":\"2026-11-01\"},{\"id\":\"d\",\"text\":\"t\",\"published\":\"2026-11-24T09:00:00Z\"},{\"id\":\"b\",\"text\":\"t\",\"published\":\"2026-11-05\"}]}");
        Check(shuffled.Select(i => i.Id).SequenceEqual(new[] { "e", "d", "c", "b", "a" }), "the order in the file does not matter: dates decide, newest on top, oldest at the bottom");
        var sameDay = Parse("{\"announcements\":[{\"id\":\"first\",\"text\":\"t\",\"published\":\"2026-11-20\"},{\"id\":\"second\",\"text\":\"t\",\"published\":\"2026-11-20\"},{\"id\":\"third\",\"text\":\"t\",\"published\":\"2026-11-20\"}]}");
        Check(sameDay.Select(i => i.Id).SequenceEqual(new[] { "third", "second", "first" }), "same date: the entry added later (further down the file) is newer, so it goes first");
        var allUndated = Parse("{\"announcements\":[{\"id\":\"u1\",\"text\":\"t\"},{\"id\":\"u2\",\"text\":\"t\"},{\"id\":\"u3\",\"text\":\"t\"}]}");
        Check(allUndated.Select(i => i.Id).SequenceEqual(new[] { "u3", "u2", "u1" }), "without any dates the last entry in the file is the newest");

        // Bardzo długi plik: czytane jest ostatnie 40 wpisów (najnowsze dopisane), nie pierwsze.
        string longFile = "{\"announcements\":[" + string.Join(",", Enumerable.Range(1, 60).Select(n => $"{{\"id\":\"n{n}\",\"text\":\"t\"}}")) + "]}";
        Check(AnnouncementParser.TryParse(longFile, true, "3.0.3", Now, out var longList) && longList.Select(i => i.Id).SequenceEqual(Enumerable.Range(51, 10).Reverse().Select(n => "n" + n)),
            "a file with 60 entries: the last 40 are read, so the entries added most recently are never the ones dropped");
        Check(AnnouncementParser.TryParse(longFile.Replace("\"n1\"", "\"first-only\""), true, "3.0.3", Now, out var longList2) && !longList2.Any(i => i.Id == "first-only"), "entries beyond the last 40 are not read at all");

        // Lista w dzwonku = informacja o nowej wersji na samej górze, a pod nią komunikaty od najnowszego do najstarszego.
        AnnouncementItem upd = new("update-3.0.9", "Dostępna wersja 3.0.9", "Masz wersję 3.0.3.", AnnouncementLevel.Info, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, "Pobierz");
        var bellList = UpdateAnnouncements.Combine(upd, shuffled);
        Check(bellList.Select(i => i.Id).SequenceEqual(new[] { "update-3.0.9", "e", "d", "c", "b", "a" }), "the bell list: the update is always first, even though it is older than every message, then newest to oldest");
        Check(UpdateAnnouncements.Combine(null, shuffled).Select(i => i.Id).SequenceEqual(new[] { "e", "d", "c", "b", "a" }), "up to date: just the messages, newest first");

        var duplicates = Parse("{\"announcements\":[{\"id\":\"x\",\"text\":\"pierwszy\"},{\"id\":\"x\",\"text\":\"drugi\"}]}");
        Check(duplicates.Count == 1 && duplicates[0].Text == "pierwszy", "duplicate ids: the first one wins");

        string many = "{\"announcements\":[" + string.Join(",", Enumerable.Range(1, 15).Select(n => $"{{\"id\":\"m{n}\",\"text\":\"t\"}}")) + "]}";
        Check(Parse(many).Count == AnnouncementParser.MaxShown && AnnouncementParser.MaxShown == 10, "at most 10 messages are shown");

        string huge = "{\"announcements\":[" + string.Join(",", Enumerable.Range(1, 100).Select(n => $"{{\"id\":\"h{n}\",\"text\":\"t\"}}")) + "]}";
        Check(Parse(huge).Count == AnnouncementParser.MaxShown, "a huge list is cut before it is processed");
    }

    private static void CheckSanitizing()
    {
        Check(Parse("{\"announcements\":[{\"id\":\"a\",\"text\":\"ma\\u0000ły\\u0007 tekst\\u200B z\\u202E ukrytymi\"}]}").Single().Text == "mały tekst z ukrytymi", "control and invisible formatting characters are removed");
        var multi = Parse("{\"announcements\":[{\"id\":\"a\",\"title\":\"Dwie\\nlinie\\ttytułu\",\"text\":\"a\\r\\nb\\n\\n\\n\\n\\nc\"}]}").Single();
        Check(multi.Title == "Dwie linie tytułu", "a title is one line");
        Check(multi.Text == "a\nb\n\nc", "text keeps line breaks (CRLF normalised, long gaps collapsed)");
        Check(Parse(One("\"text\":\"" + new string('x', 900) + "\"")).Single().Text.Length == AnnouncementParser.MaxTextLength && Parse(One("\"text\":\"" + new string('x', 900) + "\"")).Single().Text.EndsWith('…'), "text is cut to 500 with an ellipsis");
        Check(Parse(One("\"title\":\"" + new string('t', 300) + "\"")).Single().Title.Length == AnnouncementParser.MaxTitleLength, "title is cut to 80");
        Check(Parse(One("\"action\":\"" + new string('a', 100) + "\"")).Single().LinkText.Length == AnnouncementParser.MaxLinkTextLength, "link text is cut to 30");
        Check(Parse("{\"announcements\":[{\"id\":\"a\",\"text\":\"<b>pogrubione</b> &amp; <script>x</script>\"}]}").Single().Text == "<b>pogrubione</b> &amp; <script>x</script>", "markup is not interpreted: it stays literal text");

        // Identyfikatory: krótkie, ASCII, bez spacji.
        Check(Parse("{\"announcements\":[{\"id\":\"ok-1_2.3:4\",\"text\":\"t\"}]}").Count == 1, "id characters: letters, digits, - _ . :");
        foreach (string bad in new[] { "", " ", "z spacją", "zażółć", new string('i', 65), "a/b" })
            Check(Parse("{\"announcements\":[{\"id\":" + JsonSerializer.Serialize(bad) + ",\"text\":\"t\"}]}").Count == 0, "bad id rejected: [" + bad + "]");
        Check(Parse("{\"announcements\":[{\"id\":\"" + new string('i', 64) + "\",\"text\":\"t\"}]}").Count == 1, "64-character id is fine");
        Check(Parse("{\"announcements\":[{\"id\":\"a\"},{\"id\":\"b\",\"text\":\"   \"},{\"text\":\"bez id\"}]}").Count == 0, "no text or no id: rejected");
    }

    private static void CheckRobustness()
    {
        foreach (string bad in new[] { "", "   ", "nie json", "{", "{\"announcements\":5}", "{\"foo\":[]}", "\"tekst\"", "42", "null" })
            Check(!AnnouncementParser.TryParse(bad, true, "3.0.3", Now, out IReadOnlyList<AnnouncementItem> items) && items.Count == 0, "not a messages file → false (the app keeps its old list): [" + bad + "]");
        Check(!AnnouncementParser.TryParse(null, true, "3.0.3", Now, out _), "null → false");
        Check(Parse("{\"announcements\":[]}").Count == 0 && Parse("[]").Count == 0, "an empty list is a valid file");
        Check(Parse("[{\"id\":\"a\",\"text\":\"t\"}]").Count == 1, "a bare array is accepted");
        Check(Parse("{\n // komentarz\n \"announcements\": [ {\"id\":\"a\", \"text\":\"t\",}, ], }").Count == 1, "comments and trailing commas (hand-edited file) are accepted");
        Check(Parse("{\"announcements\":[1,\"x\",null,[],{\"id\":\"a\",\"text\":\"t\"}]}").Count == 1, "non-object entries are skipped");
        Check(Parse("{\"announcements\":[{\"id\":5,\"text\":\"t\"},{\"id\":\"b\",\"text\":7}]}").Count == 0, "wrong value types are treated as missing");
        string deep = new string('[', 40) + new string(']', 40);
        Check(!AnnouncementParser.TryParse(deep, true, "3.0.3", Now, out _), "absurdly nested JSON is refused, not crashed on");
        Check(AnnouncementService.MaxBodyBytes == 64 * 1024 && AnnouncementService.SourceUrl.StartsWith("https://raw.githubusercontent.com/SzybkiPoPiwo/", StringComparison.Ordinal), "source is the author's repository over https and the size limit is 64 KB");
    }

    private static void CheckState()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mh-announcements-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AnnouncementStateStore(folder);
            AnnouncementState fresh = store.Load();
            Check(fresh.Enabled && fresh.Read.Count == 0 && !File.Exists(store.FilePath), "fresh state: enabled, nothing read, no file created by reading");

            store.MarkRead(new[] { "a", "b", "a", "", " " });
            Check(store.Load().Read.SequenceEqual(new[] { "a", "b" }), "marking read adds ids once and ignores blanks");
            Check(new AnnouncementStateStore(folder).Load().Read.SequenceEqual(new[] { "a", "b" }), "another instance (the other app) sees the same state");

            Check(store.SetEnabled(false) && !new AnnouncementStateStore(folder).Load().Enabled && new AnnouncementStateStore(folder).Load().Read.Count == 2, "disabling is shared and keeps the read ids");
            store.SetEnabled(true);
            Check(store.Load().Enabled, "enabling again");

            store.MarkRead(Enumerable.Range(1, 260).Select(n => "id" + n));
            AnnouncementState capped = store.Load();
            Check(capped.Read.Count == 200 && capped.Read[^1] == "id260" && capped.Read[0] == "id61" && !capped.Read.Contains("a"),
                "the read list is capped at 200, newest kept (it is shared by both programs, so it is never trimmed by one program's own list)");

            Check(!Directory.GetFiles(folder, "*.tmp").Any(), "no temporary file is left behind");
            string text = File.ReadAllText(store.FilePath);
            Check(!text.StartsWith('\uFEFF') && JsonDocument.Parse(text).RootElement.GetProperty("enabled").GetBoolean(), "state file is plain UTF-8 JSON");

            File.WriteAllText(store.FilePath, "to nie jest json {{{");
            AnnouncementState recovered = store.Load();
            Check(recovered.Enabled && recovered.Read.Count == 0, "a corrupted state file behaves like a fresh install");
            store.MarkRead(new[] { "z" });
            Check(store.Load().Read.SequenceEqual(new[] { "z" }), "and is repaired by the next write");

            File.WriteAllText(store.FilePath, "{\"enabled\":false,\"read\":[\"x\",5,null,\"" + new string('q', 100) + "\",\"y\",\"x\"]}");
            AnnouncementState odd = store.Load();
            Check(!odd.Enabled && odd.Read.SequenceEqual(new[] { "x", "y" }), "odd entries in a hand-edited state file are ignored");
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { }
        }
    }

    // Nowa wersja programu jest komunikatem w dzwonku (zastąpiła okno aktualizacji): powstaje z odpowiedzi GitHuba.
    private static AppUpdateInfo? Update(string current, params string[] tags)
    {
        string Release(string tag, string body) =>
            "{\"tag_name\":\"" + tag + "\",\"name\":\"Minecraft Helper " + tag + "\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-11-24T10:00:00Z\"," +
            "\"html_url\":\"https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases/tag/" + tag + "\",\"body\":" + JsonSerializer.Serialize(body) + ",\"assets\":[]}";
        string Body(params string[] lines) => "### Co nowego\n\n**Oba programy**\n" + string.Join("\n", lines.Select(l => "- " + l)) + "\n";
        var releases = new List<string>();
        foreach (string tag in tags) releases.Add(Release(tag, Body($"Zmiana z {tag.TrimStart('v')} (pierwsza)", $"Zmiana z {tag.TrimStart('v')} (druga)")));
        using JsonDocument document = JsonDocument.Parse("[" + string.Join(",", releases) + "]");
        return UpdateCheckService.BuildUpdateInfo(document.RootElement, current, minerEdition: false);
    }

    private static void CheckUpdateAnnouncement()
    {
        AppUpdateInfo info = Update("3.0.3", "v3.0.4")!;
        AnnouncementItem item = UpdateAnnouncements.FromUpdate(info);
        Check(item.Id == "update-3.0.4" && item.Title == "Dostępna wersja 3.0.4" && item.Level == AnnouncementLevel.Info && item.LinkText == "Pobierz", "an update becomes an info message with a stable id per version");
        Check(item.Text.StartsWith("Masz wersję 3.0.3.\nCo nowego:\n• ", StringComparison.Ordinal) && item.Text.Contains("• Zmiana z 3.0.4 (pierwsza)") && item.Text.Contains("• Zmiana z 3.0.4 (druga)"),
            "the message names the installed version and lists what is new");
        Check(item.Link?.AbsoluteUri == "https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases/tag/v3.0.4" && item.PublishedUtc == new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc), "it links to the release page (notes, checksum) and carries the publication time");

        // Po przeskoczeniu kilku wersji: zmiany z wszystkich, najnowsze pierwsze, z limitem i licznikiem reszty.
        AnnouncementItem skipped = UpdateAnnouncements.FromUpdate(Update("3.0.1", "v3.0.4", "v3.0.3", "v3.0.2", "v3.0.5")!);
        string[] bullets = skipped.Text.Split('\n').Where(l => l.StartsWith("• ", StringComparison.Ordinal)).ToArray();
        Check(skipped.Id == "update-3.0.5" && bullets.Length == 6 && bullets[0].Contains("3.0.5") && skipped.Text.EndsWith("…i jeszcze 2 zmiany.", StringComparison.Ordinal), "several skipped versions: newest changes first, at most 6, the rest is counted");

        // Odmiana „zmiana / zmiany / zmian” (6 widocznych, reszta liczona).
        string Tail(int extra) => UpdateAnnouncements.FromUpdate(new AppUpdateInfo("3.0.0", "3.0.9", "x", new Uri("https://github.com/a/b"), null,
            new[] { new UpdateReleaseNotes("3.0.9", new[] { new UpdateNotesSection("Oba programy", Enumerable.Range(1, 6 + extra).Select(n => "zmiana " + n).ToArray()) }) }, null)).Text.Split('\n').Last();
        Check(Tail(1) == "…i jeszcze 1 zmiana." && Tail(2) == "…i jeszcze 2 zmiany." && Tail(4) == "…i jeszcze 4 zmiany." && Tail(5) == "…i jeszcze 5 zmian." && Tail(12) == "…i jeszcze 12 zmian." && Tail(22) == "…i jeszcze 22 zmiany." && Tail(0).StartsWith("• zmiana 6", StringComparison.Ordinal),
            "Polish plural of „zmiana”");

        // Brak opisu zmian, brak linku do strony wydania (został sam instalator), dziwne znaczniki w wersji.
        var bare = UpdateAnnouncements.FromUpdate(new AppUpdateInfo("3.0.3", "3.0.4", "x", new Uri("https://github.com/a/b/releases/download/v3.0.4/Setup.exe")));
        Check(bare.Text == "Masz wersję 3.0.3. Pełny opis zmian jest na stronie wydania." && bare.Link?.AbsoluteUri.EndsWith("Setup.exe", StringComparison.Ordinal) == true && bare.PublishedUtc == null, "no notes: a short fallback text; without a release page the installer link is used");
        Check(UpdateAnnouncements.FromUpdate(new AppUpdateInfo("3.0.3", "3.0.4 beta/x", "x", new Uri("https://github.com/a/b"))).Id == "update-3.0.4_beta_x", "version text never breaks the id rules");
        Check(UpdateAnnouncements.FromUpdate(new AppUpdateInfo("3.0.3", new string('9', 200), "x", new Uri("https://github.com/a/b"))).Id.Length <= AnnouncementParser.MaxIdLength, "a very long version stays within the id limit");
        Check(UpdateAnnouncements.FromUpdate(new AppUpdateInfo("3.0.3", "3.0.4", "x", new Uri("https://github.com/a/b"), null,
            new[] { new UpdateReleaseNotes("3.0.4", new[] { new UpdateNotesSection("Oba programy", new[] { new string('z', 300) + "‮", "  " }) }) }, null)).Text.Contains("• " + new string('z', 109) + "…", StringComparison.Ordinal), "long or blank change lines are cut or dropped like any other text");

        // Scalanie: aktualizacja zawsze pierwsza, bez powtórzeń.
        AnnouncementItem a = new("a", "A", "tekst", AnnouncementLevel.Info, null, null, "Szczegóły"), b = a with { Id = "b" }, same = a with { Id = item.Id };
        Check(UpdateAnnouncements.Combine(null, new[] { a, b }).Select(i => i.Id).SequenceEqual(new[] { "a", "b" }), "no update: the list is untouched");
        Check(UpdateAnnouncements.Combine(item, new[] { a, same, b }).Select(i => i.Id).SequenceEqual(new[] { item.Id, "a", "b" }), "the update goes first and an announcement with the same id is not repeated");
        Check(UpdateAnnouncements.Combine(item, Array.Empty<AnnouncementItem>()).Count == 1, "an update alone still makes the bell visible (announcements switched off)");
    }

    // Dymek „jest nowa wiadomość”: co ogłasza, jak długo i w jakiej kolejności.
    private static void CheckBubble()
    {
        AnnouncementItem Item(string id, AnnouncementLevel level, string text = "Treść wiadomości.") => new(id, "Tytuł " + id, text, level, null, null, "Szczegóły");
        AnnouncementItem info = Item("m-info", AnnouncementLevel.Info), warn = Item("m-warn", AnnouncementLevel.Warning), alarm = Item("m-alarm", AnnouncementLevel.Alarm);
        AnnouncementItem update = Item("update-3.0.4", AnnouncementLevel.Info, "Masz wersję 3.0.3.\nCo nowego:\n• coś");
        var empty = new HashSet<string>();

        var all = new[] { update, info, warn, alarm };
        Check(AnnouncementBubble.Pending(all, empty, empty).Count == 4, "everything new is pending at the start");
        Check(AnnouncementBubble.Pending(all, new HashSet<string> { "m-info" }, empty).Select(i => i.Id).SequenceEqual(new[] { "update-3.0.4", "m-warn", "m-alarm" }), "read messages never get a bubble");
        Check(AnnouncementBubble.Pending(all, empty, new HashSet<string> { "update-3.0.4", "m-warn" }).Select(i => i.Id).SequenceEqual(new[] { "m-info", "m-alarm" }), "a message already announced this session is not announced again");
        Check(AnnouncementBubble.Pending(Array.Empty<AnnouncementItem>(), empty, empty).Count == 0 && AnnouncementBubble.Pending(all, new HashSet<string>(all.Select(i => i.Id)), empty).Count == 0, "nothing new = no bubble");

        Check(AnnouncementBubble.Headline(new[] { update, info }) == info, "an author message goes before the update notice when both are new and equally important");
        Check(AnnouncementBubble.Headline(new[] { update, info, warn }) == warn && AnnouncementBubble.Headline(new[] { info, warn, alarm }) == alarm && AnnouncementBubble.Headline(new[] { alarm, warn }) == alarm, "the most serious message speaks first");
        Check(AnnouncementBubble.Headline(new[] { update }) == update && AnnouncementBubble.Headline(new[] { info, Item("m-info2", AnnouncementLevel.Info) }) == info, "a single message, or equal ones: the first in the list");
        Check(AnnouncementBubble.Headline(new[] { update, Item("m-info0", AnnouncementLevel.Info) }).Id == "m-info0" && AnnouncementBubble.Headline(new[] { Item("w1", AnnouncementLevel.Warning), update }).Id == "w1", "level beats kind, kind beats position");

        Check(AnnouncementBubble.Tag(update) == "Nowa wersja" && AnnouncementBubble.Tag(info) == "Nowa wiadomość" && AnnouncementBubble.Tag(warn) == "Ostrzeżenie" && AnnouncementBubble.Tag(alarm) == "Ważne", "tags tell what kind of message it is");
        Check(AnnouncementBubble.Tag(Item("update-3.0.9", AnnouncementLevel.Warning)) == "Nowa wersja", "an update is an update whatever its level");

        // Wiadomość od autora i ostrzeżenie: cała treść od razu; aktualizacja: krótko.
        string longText = "Pierwsza linia.\nDruga linia.\n\n\n\nTrzecia. " + new string('x', 400);
        string body = AnnouncementBubble.Body(Item("m-long", AnnouncementLevel.Warning, longText));
        Check(body.StartsWith("Pierwsza linia.\nDruga linia.\n\nTrzecia. xxx", StringComparison.Ordinal) && body.Length == AnnouncementBubble.MaxBodyLength && body.EndsWith('…') && AnnouncementBubble.MaxBodyLength == 240,
            "a warning or author message shows its whole text (long ones cut at 240) so nothing has to be opened to read it");
        Check(AnnouncementBubble.Body(info) == "Treść wiadomości.", "a short message is shown as it is");
        Check(AnnouncementBubble.Body(update) == "Masz wersję 3.0.3. Kliknij, aby zobaczyć, co nowego.", "an update says only that it exists; the changes are in the list");
        Check(AnnouncementBubble.Body(Item("update-x", AnnouncementLevel.Info, "")) == "Kliknij, aby zobaczyć, co nowego.", "an update without text still invites to open the list");

        Check(AnnouncementBubble.VisibleFor == TimeSpan.FromSeconds(3),
            "the toast stays 3 seconds for every level: a player cannot dismiss it without a click that pulls them out of the game, so even an alarm leaves quickly (the full text stays in the list)");
        Check(AnnouncementBubble.IsUpdate(update) && !AnnouncementBubble.IsUpdate(info) && !AnnouncementBubble.IsUpdate(Item("my-update-1", AnnouncementLevel.Info)), "only the app's own update message counts as an update (by id prefix)");
    }

    private static void CheckWiring()
    {
        // Oba programy dołączają te same pliki; Auto Clicker nie może mieć niczego z bota Discord ani ze znaczników Kopacza.
        string pvp = File.ReadAllText(Path.Combine(Root, "MinecraftHelper.Pvp", "MinecraftHelper.Pvp.csproj"));
        foreach (string file in new[] { "Services\\AnnouncementService.cs", "AnnouncementBell.cs", "AnnouncementController.cs" })
            Check(pvp.Contains("..\\MinecraftHelper\\" + file, StringComparison.Ordinal), "Auto Clicker project includes the shared " + file);
        foreach (string file in new[] { "AnnouncementService.cs", "AnnouncementBell.cs", "AnnouncementController.cs" })
        {
            string source = File.ReadAllText(Path.Combine(Root, "MinecraftHelper", file.StartsWith("AnnouncementService") ? "Services" : "", file));
            Check(!source.Contains("Discord", StringComparison.OrdinalIgnoreCase) && !source.Contains("AppSettings", StringComparison.Ordinal), "shared " + file + " stays free of Discord and per-account settings");
        }
        // Dawne okno aktualizacji i „Pomiń tę wersję” zastąpił dzwonek — nic po nich nie zostało.
        Check(!File.Exists(Path.Combine(Root, "MinecraftHelper", "UpdateAvailableWindow.cs")) && !pvp.Contains("UpdateAvailableWindow", StringComparison.Ordinal), "the old update window is gone from both programs");
        foreach (string path in new[] { Path.Combine("MinecraftHelper", "Models", "AppSettings.cs"), Path.Combine("MinecraftHelper.Pvp", "Models", "PvpSettings.cs"),
                     Path.Combine("MinecraftHelper", "MainWindow.xaml.cs"), Path.Combine("MinecraftHelper.Pvp", "MainWindow.xaml.cs") })
            Check(!File.ReadAllText(Path.Combine(Root, path)).Contains("SkippedUpdateVersion", StringComparison.Ordinal) && !File.ReadAllText(Path.Combine(Root, path)).Contains("UpdateAvailableWindow", StringComparison.Ordinal),
                "no skipped-version setting or update dialog left in " + path);
        // Nowa wiadomość = okienko w rogu ekranu (TrayToastWindow, bez przejmowania fokusu), a nie coś w oknie programu,
        // które znikałoby, gdy program jest w tle. Każdy program podaje własną ikonę i sposób przywracania okna.
        string bell = File.ReadAllText(Path.Combine(Root, "MinecraftHelper", "AnnouncementBell.cs"));
        string tray = File.ReadAllText(Path.Combine(Root, "MinecraftHelper", "GlassTray.cs"));
        Check(bell.Contains("new TrayToastWindow(", StringComparison.Ordinal) && !bell.Contains("IsActive", StringComparison.Ordinal), "a new message pops up as a toast window whatever the state of the program window");
        Check(tray.Contains("WsExNoActivate", StringComparison.Ordinal) && tray.Contains("ShowActivated = false", StringComparison.Ordinal), "the toast window never takes focus (it must not pull the game out of full screen)");
        Check(File.ReadAllText(Path.Combine(Root, "MinecraftHelper", "MainWindow.Announcements.cs")).Contains("ToastIconResource = \"Assets/miner.ico\"", StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(Root, "MinecraftHelper.Pvp", "MainWindow.Announcements.cs")).Contains("ToastIconResource = \"Assets/autoclicker.ico\"", StringComparison.Ordinal), "each program gives the toast its own icon");
        foreach (string path in new[] { Path.Combine("MinecraftHelper", "MainWindow.Announcements.cs"), Path.Combine("MinecraftHelper.Pvp", "MainWindow.Announcements.cs") })
            Check(File.ReadAllText(Path.Combine(Root, path)).Contains("RestoreWindow = RestoreFromTray", StringComparison.Ordinal), "clicking the toast restores the window the same way as the tray toast: " + path);
        string miner = File.ReadAllText(Path.Combine(Root, "MinecraftHelper", "MainWindow.Announcements.cs"));
        Check(miner.Contains("_workspaceWorker", StringComparison.Ordinal), "worker processes of the miner never create the bell or fetch");
        string minerXaml = File.ReadAllText(Path.Combine(Root, "MinecraftHelper", "MainWindow.xaml"));
        string pvpXaml = File.ReadAllText(Path.Combine(Root, "MinecraftHelper.Pvp", "MainWindow.xaml"));
        foreach (string xaml in new[] { minerXaml, pvpXaml })
            Check(xaml.Contains("HostAnnouncementBell", StringComparison.Ordinal) && xaml.Contains("ChkAnnouncementsEnabled", StringComparison.Ordinal), "both windows have the bell host and the settings switch");
    }
}
