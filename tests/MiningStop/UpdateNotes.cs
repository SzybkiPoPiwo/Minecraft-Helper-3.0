using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MinecraftHelper;

// Okno aktualizacji i panel „Nowe w …”: odczyt „Co nowego” z opisu wydania na GitHubie (bez sieci)
// oraz lista nowości Kopacza — każde „Pokaż” musi prowadzić do istniejącej zakładki i karty.
internal static partial class Program
{
    private const string SampleReleaseBody = """
        ## Minecraft Helper 3.0.4

        Krótki opis wydania. Ustawienia zostają zachowane.

        ### Pobieranie

        - To nie jest zmiana — lista z innej sekcji.

        ### Co nowego

        **Oba programy**
        - Tryb ciemny: **Ustawienia → Wygląd → Motyw** i `Jak w Windows`.
        - Zobacz [README](https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0#readme) po szczegóły.

        **Kopacz**
        - Discord ma własną zakładkę.

        **Auto Clicker**
        - Nowy tryb klikania.

        Pełny opis funkcji: [README](https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0#readme).

        ### Znane problemy
        - Też nie jest zmianą.
        """;

    // Każdy start i powrót do kopania: najpierw slot 1 z kilofem, potem LPM.
    private static void VerifyMiningToolSlot()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryDirectory().FullName, "MinecraftHelper", "MainWindow.xaml.cs"));
        int Section(string name) => source.IndexOf(name, StringComparison.Ordinal);
        string Body(string name)
        {
            int start = Section(name);
            int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
            return start < 0 || end < 0 ? string.Empty : source[start..end];
        }

        foreach ((string tick, string hold) in new[] { ("private void RunKopacz533Tick(DateTime now)", "SetKopacz533MiningHold(true);"), ("private void RunKopacz633Tick(DateTime now)", "SetKopacz633AttackHold(true);") })
        {
            string body = Body(tick);
            int wait = body.IndexOf("WaitForMiningToolSlot(", StringComparison.Ordinal);
            Check(wait >= 0 && wait < body.IndexOf(hold, StringComparison.Ordinal), "the mining loop selects the pickaxe slot before holding the mouse button");
        }
        foreach (string entry in new[] { "private void StartKopacz533Runtime(DateTime now)", "private void StartKopacz633Runtime(DateTime now)", "private void StopAutoReconnect(string message, bool resumeMining, bool warning)" })
            Check(Body(entry).Contains("RequestMiningToolSlot(", StringComparison.Ordinal), "every start and server return requests the pickaxe slot: " + entry);
        Check(!Body("private void StartKopacz633Runtime(DateTime now)").Contains("SetKopacz633AttackHold(true);", StringComparison.Ordinal),
            "6/3/3 does not swing before the pickaxe slot is selected");
        string wait2 = Body("private bool WaitForMiningToolSlot(InventoryCleanupOwner owner, DateTime now)");
        Check(wait2.IndexOf("IsInventoryCursorVisible()", StringComparison.Ordinal) >= 0
                && wait2.IndexOf("IsInventoryCursorVisible()", StringComparison.Ordinal) < wait2.IndexOf("SendKeyTap(VK_1);", StringComparison.Ordinal),
            "key 1 is never pressed into an open screen, where it would swap the hovered item");
    }

    private static void VerifyUpdateNotes()
    {
        Assembly assembly = typeof(MainWindow).Assembly;
        MethodInfo parse = assembly.GetType("MinecraftHelper.Services.ReleaseBodyParser")!.GetMethod("ParseWhatsNew", BindingFlags.Static | BindingFlags.Public)!;

        (string Title, string[] Lines)[] Sections(bool miner) => ((IEnumerable)parse.Invoke(null, new object?[] { SampleReleaseBody, miner })!)
            .Cast<object>()
            .Select(section => ((string)section.GetType().GetProperty("Title")!.GetValue(section)!,
                ((IEnumerable)section.GetType().GetProperty("Lines")!.GetValue(section)!).Cast<string>().ToArray()))
            .ToArray();

        var miner = Sections(miner: true);
        Check(miner.Select(section => section.Title).SequenceEqual(new[] { "Oba programy", "Kopacz" }),
            "Kopacz shows „Oba programy” and its own section, never Auto Clicker's — and nothing outside „Co nowego”");
        Check(miner[0].Lines.SequenceEqual(new[] { "Tryb ciemny: Ustawienia → Wygląd → Motyw i Jak w Windows.", "Zobacz README po szczegóły." }),
            "release notes lose Markdown bold, code and link syntax but keep the text");
        var clicker = Sections(miner: false);
        Check(clicker.Select(section => section.Title).SequenceEqual(new[] { "Oba programy", "Auto Clicker" }),
            "Auto Clicker shows „Oba programy” and its own section, never Kopacz's");
        Check(((IEnumerable)parse.Invoke(null, new object?[] { "## Wydanie\n- bez sekcji Co nowego", true })!).Cast<object>().Count() == 0,
            "a release body without „Co nowego” yields no notes (the window links to GitHub instead)");

        // Lista wydań z GitHuba: najnowsze stabilne, zmiany z każdego nowszego niż zainstalowane.
        string Release(string tag, bool prerelease = false) => $$"""
            {"tag_name":"{{tag}}","name":"Minecraft Helper {{tag}}","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},
             "published_at":"2026-11-08T10:00:00Z","html_url":"https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases/tag/{{tag}}",
             "body":{{JsonSerializer.Serialize(SampleReleaseBody.Replace("3.0.4", tag))}},
             "assets":[{"name":"MinecraftHelper-Setup-{{tag}}.exe","browser_download_url":"https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases/download/{{tag}}/MinecraftHelper-Setup-{{tag}}.exe"}]}
            """;
        using JsonDocument releases = JsonDocument.Parse("[" + string.Join(",", Release("v3.0.6", prerelease: true), Release("v3.0.5"), Release("v3.0.4"), Release("v3.0.2")) + "]");
        MethodInfo build = assembly.GetType("MinecraftHelper.Services.UpdateCheckService")!.GetMethod("BuildUpdateInfo", Static)!;
        object info = build.Invoke(null, new object[] { releases.RootElement, "3.0.3", true })!;
        object? Prop(object target, string name) => target.GetType().GetProperty(name)!.GetValue(target);
        Check((string)Prop(info, "LatestVersion")! == "3.0.5" && (bool)Prop(info, "IsDirectInstaller")!,
            "the update is the newest stable release (prereleases skipped) with its installer");
        string[] noteVersions = ((IEnumerable)Prop(info, "Notes")!).Cast<object>().Select(note => (string)Prop(note, "Version")!).ToArray();
        Check(noteVersions.SequenceEqual(new[] { "3.0.5", "3.0.4" }),
            "after skipping versions the window lists every newer release, newest first, and not older ones");
        Check(build.Invoke(null, new object[] { releases.RootElement, "3.0.5", true }) == null,
            "no update window when the installed version is the newest stable one");

        // Nowości Kopacza: zakresy wersji i miejsca docelowe „Pokaż”.
        Type whatsNew = assembly.GetType("MinecraftHelper.MinerWhatsNew")!;
        MethodInfo since = whatsNew.GetMethod("Since", BindingFlags.Static | BindingFlags.Public)!;
        object[] Items(string? previous, string current) => ((IEnumerable)since.Invoke(null, new object?[] { previous, current })!).Cast<object>().ToArray();
        Check(Items("3.0.2", "3.0.3").Length == 11 && Items("3.0.3", "3.0.3").Length == 0 && Items("", "3.0.3").Length == 11 && Items("3.0.2", "3.0.2").Length == 0,
            "the „Nowe w …” panel lists only versions after the acknowledged one, up to the running version");

        string root = FindRepositoryDirectory().FullName;
        string xaml = File.ReadAllText(Path.Combine(root, "MinecraftHelper", "MainWindow.xaml"));
        foreach (object item in Items("0.0", "999.0"))
        {
            string tab = (string)Prop(item, "Tab")!;
            string[] targets = (string[])Prop(item, "Targets")!;
            string icon = (string)Prop(item, "Icon")!;
            Check(xaml.Contains($"<TabItem Name=\"{tab}\"", StringComparison.Ordinal)
                    && targets.All(target => target == "BtnAccountsLogs" || xaml.Contains($"Name=\"{target}\"", StringComparison.Ordinal))
                    && xaml.Contains($"x:Key=\"{icon}\"", StringComparison.Ordinal),
                $"„Pokaż” for „{Prop(item, "Title")}” leads to an existing tab, card and icon");
        }

        // Przycisk Logi powstaje w kodzie panelu Konta, więc okno rejestruje jego nazwę samo.
        Check(File.ReadAllText(Path.Combine(root, "MinecraftHelper", "MainWindow.GlassUi.cs")).Contains("RegisterName(MinerWhatsNewTargets.AccountsLogsButton", StringComparison.Ordinal),
            "the Logi button is findable by the „Pokaż” spotlight");
        string whatsNewCode = File.ReadAllText(Path.Combine(root, "MinecraftHelper", "MainWindow.WhatsNew.cs"));
        Check(whatsNewCode.Contains("StartSpotlight(index, single: true)", StringComparison.Ordinal)
                && whatsNewCode.Contains("StartSpotlight(0, single: false)", StringComparison.Ordinal)
                && whatsNewCode.Contains("if (_spotlightSingle || _spotlightIndex >=", StringComparison.Ordinal),
            "„Pokaż” opens one item and only the tour walks through all of them");

        string startup = File.ReadAllText(Path.Combine(root, "MinecraftHelper", "MainWindow.xaml.cs"));
        Check(startup.Contains("if (!_isFirstRun)", StringComparison.Ordinal) && startup.Contains("ShowWhatsNewAfterUpdate();", StringComparison.Ordinal),
            "after an update Kopacz shows the corner panel instead of the blocking version window");
    }
}
