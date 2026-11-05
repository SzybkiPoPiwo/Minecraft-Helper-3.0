using MinecraftHelper.Services;

namespace MinecraftHelper.Services
{
    internal enum MinerPreviewRegion
    {
        Full,
        Hotbar,
        Inventory
    }

    internal sealed record MinerLiveFrame(byte[] Jpeg, int Width, int Height, DateTimeOffset CapturedAt, string? Note = null);
}

namespace MultiAccountTests
{
    internal static partial class Program
    {
        private static int _checks;

        private static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--cursor-child")
            {
                RunBackgroundCursorChild(args[1]);
                return;
            }
            if (args.Length == 2 && args[0] == "--lease-child")
            {
                using var lease = MinerLayoutSlotLease.TryAcquire(args[1], 2);
                Console.WriteLine(lease?.Index ?? -1);
                Console.Out.Flush();
                Console.ReadLine();
                return;
            }
            if (args.Length == 2 && args[0] == "--hud-child")
            {
                RunHudChild(int.Parse(args[1]));
                return;
            }
            if (args.Contains("--layout-smoke"))
            {
                RunHudLayoutSmoke();
                Console.WriteLine($"PASS: {_checks} native HUD layout checks (isolated, offscreen test windows).");
                return;
            }
            Check(MinerProfileId.Normalize(null) == "default", "Pusty profil powinien wskazywać konto główne.");
            Check(MinerProfileId.Normalize(" ../Konto_01! ") == "konto_01", "Id profilu musi być bezpieczne dla ścieżki i mutexu.");

            var alpha = new FakeSession("alpha", "Alpha", running: true);
            var beta = new FakeSession("beta", "Beta", running: false);
            IMinerRemoteSession[] sessions = { alpha, beta };

            MinerRemoteCommandResult status = MinerRemoteCommandRouter.Execute("/status", sessions);
            Check(status.Success, "Status wszystkich kont powinien działać.");
            Check(status.Message.Contains("Alpha", StringComparison.Ordinal), "Status powinien zawierać Alpha.");
            Check(status.Message.Contains("Beta", StringComparison.Ordinal), "Status powinien zawierać Beta.");

            MinerRemoteCommandResult ambiguous = MinerRemoteCommandRouter.Execute("/screenshot", sessions);
            Check(!ambiguous.Success, "Zrzut wielu kont musi wymagać wskazania profilu.");

            MinerRemoteCommandResult screenshot = MinerRemoteCommandRouter.Execute("/screenshot alpha", sessions);
            Check(screenshot.Success, "Zrzut wskazanego konta powinien działać.");
            Check(screenshot.Attachment?.SequenceEqual(new byte[] { 1, 2, 3 }) == true, "Zrzut powinien przenosić dane obrazu.");
            Check(screenshot.AttachmentContentType == "image/jpeg", "Zrzut powinien mieć typ JPEG.");

            MinerRemoteCommandResult inventory = MinerRemoteCommandRouter.Execute("/ekwipunek Alpha", sessions);
            Check(inventory.Success && alpha.LastRegion == MinerPreviewRegion.Inventory, "Komenda ekwipunek powinna wybierać właściwy wycinek.");

            MinerRemoteCommandResult food = MinerRemoteCommandRouter.Execute("/jedzenie alpha", sessions);
            Check(food.Success && alpha.LastRegion == MinerPreviewRegion.Hotbar, "Komenda jedzenie powinna wybierać hotbar.");

            MinerRemoteCommandResult stop = MinerRemoteCommandRouter.Execute("/stop beta", sessions);
            Check(stop.Success && beta.StopCalls == 1, "STOP powinien trafić wyłącznie do wybranego konta.");
            Check(alpha.StopCalls == 0, "STOP innego konta nie może zatrzymać Alpha.");

            MinerRemoteCommandResult unknown = MinerRemoteCommandRouter.Execute("/shell calc.exe", sessions);
            Check(!unknown.Success, "Dowolne polecenia spoza białej listy muszą być odrzucane.");

            // Local panel commands are not part of the remote (Discord) whitelist.
            foreach (string localOnly in new[] { "/start alpha", "/window 1234 alpha", "/preview alpha", "/show alpha", "/reload alpha", "/arrange alpha" })
                Check(!MinerRemoteCommandRouter.Execute(localOnly, sessions).Success, $"{localOnly} nie może działać zdalnie.");
            Check(alpha.StartCalls == 0 && alpha.AssignedProcessId == 0 && alpha.ArrangeCalls == 0, "Odrzucona komenda lokalna nie może zmienić stanu konta.");

            MinerRemoteCommandResult arrange = MinerRemoteCommandRouter.Execute("arrange beta", sessions, allowLocalCommands: true);
            Check(arrange.Success && beta.ArrangeCalls == 1 && alpha.ArrangeCalls == 0, "Kalibracja okna powinna trafić tylko do wybranego konta.");
            Check(!MinerRemoteCommandRouter.Execute("arrange", sessions, allowLocalCommands: true).Success, "Kalibracja okien wielu kont naraz musi być odrzucana.");

            MinerRemoteCommandResult clearLogs = MinerRemoteCommandRouter.Execute("clear-logs beta", sessions, allowLocalCommands: true);
            Check(clearLogs.Success && beta.ClearedLogs == 1 && alpha.ClearedLogs == 0, "Czyszczenie historii powinno trafić tylko do wybranego konta.");
            Check(!MinerRemoteCommandRouter.Execute("clear-logs", sessions, allowLocalCommands: true).Success, "Czyszczenie historii wielu kont naraz musi być odrzucane.");
            Check(!MinerRemoteCommandRouter.Execute("clear-logs beta", sessions).Success && beta.ClearedLogs == 1, "Czyszczenia historii nie wolno wywołać z zewnątrz (np. z Discorda).");

            MinerRemoteCommandResult start = MinerRemoteCommandRouter.Execute("start beta", sessions, allowLocalCommands: true);
            Check(start.Success && beta.StartCalls == 1 && alpha.StartCalls == 0, "Start lokalny powinien trafić tylko do wybranego konta.");
            Check(beta.LastStartMode == null, "Start bez trybu zostawia wybór kanału profilowi konta.");
            MinerRemoteCommandResult start533 = MinerRemoteCommandRouter.Execute("start beta 5/3/3", sessions, allowLocalCommands: true);
            Check(start533.Success && beta.StartCalls == 2 && beta.LastStartMode == "533" && alpha.StartCalls == 0, "Tryb 5/3/3 na końcu komendy start trafia do wybranego konta, nie do nazwy konta.");
            MinerRemoteCommandResult start633 = MinerRemoteCommandRouter.Execute("start 633", new IMinerRemoteSession[] { alpha }, allowLocalCommands: true);
            Check(start633.Success && alpha.LastStartMode == "633", "Sam tryb bez nazwy konta działa w kanale jednego konta.");

            // !join / !leave z Discorda: tylko wybrane konto, tryb na końcu jak przy start.
            MinerRemoteCommandResult join = MinerRemoteCommandRouter.Execute("join beta 5/3/3", sessions, allowLocalCommands: true);
            Check(join.Success && beta.JoinCalls == 1 && beta.LastJoinMode == "533" && alpha.JoinCalls == 0, "Join z trybem trafia do wybranego konta, tryb nie jest częścią nazwy.");
            Check(MinerRemoteCommandRouter.Execute("join beta", sessions, allowLocalCommands: true).Success && beta.LastJoinMode == null, "Join bez trybu zostawia wybór kanału profilowi.");
            Check(!MinerRemoteCommandRouter.Execute("join", sessions, allowLocalCommands: true).Success && alpha.JoinCalls == 0, "Join wielu kont naraz w jednym kanale musi być odrzucany.");
            MinerRemoteCommandResult leave = MinerRemoteCommandRouter.Execute("leave alpha", sessions, allowLocalCommands: true);
            Check(leave.Success && alpha.LeaveCalls == 1 && beta.LeaveCalls == 0, "Leave trafia tylko do wybranego konta.");
            Check(!MinerRemoteCommandRouter.Execute("leave alpha", sessions).Success && alpha.LeaveCalls == 1, "Leave i join wymagają zaufanego kanału lokalnego (tak jak start).");

            MinerRemoteCommandResult window = MinerRemoteCommandRouter.Execute("window 4321 alpha", sessions, allowLocalCommands: true);
            Check(window.Success && alpha.AssignedProcessId == 4321 && beta.AssignedProcessId == 0, "Przypisanie okna powinno trafić tylko do wybranego konta.");
            Check(!MinerRemoteCommandRouter.Execute("window abc alpha", sessions, allowLocalCommands: true).Success, "Nieprawidłowy PID okna musi być odrzucany.");
            Check(!MinerRemoteCommandRouter.Execute("window 4321", sessions, allowLocalCommands: true).Success, "Okno wielu kont naraz musi być odrzucane.");

            MinerRemoteCommandResult preview = MinerRemoteCommandRouter.Execute("preview alpha", sessions, allowLocalCommands: true);
            Check(preview.Success && alpha.LastWidth == MinerRemoteCommandRouter.PreviewWidth, "Podgląd Live powinien używać mniejszego obrazu.");
            MinerRemoteCommandRouter.Execute("/screenshot alpha", sessions);
            Check(alpha.LastWidth == MinerRemoteCommandRouter.ScreenshotWidth, "Zrzut powinien używać pełnej szerokości.");

            MinerRemoteCommandResult single = MinerRemoteCommandRouter.Execute("/status alpha", sessions);
            Check(single.Status?.ProfileId == "alpha", "Status jednego konta powinien zwracać dane dla panelu.");
            MinerRemoteCommandResult stats = MinerRemoteCommandRouter.Execute("/stats beta", sessions);
            Check(stats.Success && stats.Message.Contains("wyrzucono 64", StringComparison.Ordinal) && stats.Message != MinerRemoteCommandRouter.Execute("/status beta", sessions).Message,
                "Stats powinien różnić się od statusu i zawierać statystyki.");

            var owned = new MinerRemoteStatus("x", "X", false, "—", "MC", 0, 0, 0, 0) { WindowOwnedByAnotherProfile = true };
            Check(MinerRemoteCommandRouter.DescribeState(owned).Contains("inne konto", StringComparison.Ordinal), "Konflikt okna powinien być widoczny w statusie.");

            // One game window belongs to one profile (named claim per game PID).
            int gameProcessId = Environment.ProcessId;
            using (var first = new MinerGameWindowClaim())
            using (var second = new MinerGameWindowClaim())
            {
                Check(first.TryClaim(gameProcessId), "Pierwszy profil powinien zająć wolne okno.");
                Check(first.TryClaim(gameProcessId), "Ponowne zajęcie własnego okna musi działać.");
                Check(!first.IsClaimedByAnotherProfile(gameProcessId), "Własne okno nie jest zajęte przez inne konto.");
                Check(second.IsClaimedByAnotherProfile(gameProcessId), "Drugi profil musi widzieć okno jako zajęte.");
                Check(!second.TryClaim(gameProcessId) && second.ProcessId == 0, "Drugi profil nie może zająć cudzego okna.");
                first.Release();
                Check(!second.IsClaimedByAnotherProfile(gameProcessId), "Zwolnione okno powinno być wolne.");
                Check(second.TryClaim(gameProcessId), "Po zwolnieniu okno może przejąć inny profil.");
            }

            Check(MinerGameWindowClaim.IsProcessRunning(Environment.ProcessId), "Bieżący proces powinien być wykrywany jako działający.");
            Check(!MinerGameWindowClaim.IsProcessRunning(0), "PID 0 nie jest procesem gry.");

            var snapshot = new MinerInventorySnapshot();
            DateTimeOffset scannedAt = new(2026, 10, 26, 15, 14, 2, TimeSpan.Zero);
            Check(!snapshot.TryGetFallback(scannedAt, out _), "Bez skanu EQ nie ma zapisanego ekwipunku.");
            snapshot.Remember(new MinerLiveFrame(new byte[] { 7, 8 }, 640, 620, scannedAt));
            Check(snapshot.TryGetFallback(scannedAt.AddMinutes(2), out MinerLiveFrame? lastInventory) && lastInventory != null,
                "Zamknięty ekwipunek powinien zwracać ostatni skan.");
            Check(lastInventory!.Jpeg.SequenceEqual(new byte[] { 7, 8 }) && lastInventory.CapturedAt == scannedAt,
                "Ostatni skan musi zachować obraz i godzinę skanu.");
            Check(lastInventory.Note?.Contains("2 min temu", StringComparison.Ordinal) == true, "Ostatni skan powinien podawać swój wiek.");
            Check(MinerInventorySnapshot.DescribeAge(TimeSpan.FromSeconds(30)) == "przed chwilą", "Świeży skan powinien być opisany jako przed chwilą.");
            Check(MinerInventorySnapshot.DescribeAge(TimeSpan.FromMinutes(75)) == "1 h 15 min temu", "Starszy skan powinien podawać godziny i minuty.");

            CheckWindowLayouts();
            CheckLayoutReservations();
            CheckWorkspaceSelection();
            CheckWorkspacePipe();
            CheckBackgroundCursor();
            Console.WriteLine($"PASS: {_checks} checks; profiles, commands, inventory, window/HUD layout and process slot isolation.");
        }

        private static void Check(bool condition, string message)
        {
            _checks++;
            if (!condition)
                throw new InvalidOperationException($"FAIL #{_checks}: {message}");
        }

        private sealed class FakeSession : IMinerRemoteSession
        {
            private readonly bool _running;

            public FakeSession(string profileId, string displayName, bool running)
            {
                ProfileId = profileId;
                DisplayName = displayName;
                _running = running;
            }

            public string ProfileId { get; }

            public string DisplayName { get; }

            public int StopCalls { get; private set; }

            public MinerPreviewRegion LastRegion { get; private set; }

            public int LastWidth { get; private set; }

            public int StartCalls { get; private set; }

            public int AssignedProcessId { get; private set; }

            public int ArrangeCalls { get; private set; }

            public MinerRemoteStatus GetRemoteStatus() => new(
                ProfileId,
                DisplayName,
                _running,
                _running ? "6/3/3" : "—",
                "Minecraft",
                2,
                3,
                1,
                64);

            public bool TryCaptureRemoteFrame(MinerPreviewRegion region, int maximumWidth, out MinerLiveFrame? frame)
            {
                LastRegion = region;
                LastWidth = maximumWidth;
                frame = new MinerLiveFrame(new byte[] { 1, 2, 3 }, 320, 180, DateTimeOffset.UtcNow);
                return true;
            }

            public void StopRemoteMining()
            {
                StopCalls++;
            }

            public string? LastStartMode { get; private set; }

            public bool TryStartRemoteMining(string? mode, out string message)
            {
                StartCalls++;
                LastStartMode = mode;
                message = "uruchomiono.";
                return true;
            }

            public int JoinCalls { get; private set; }
            public string? LastJoinMode { get; private set; }
            public int LeaveCalls { get; private set; }

            public bool TryJoinServer(string? mode, out string message)
            {
                JoinCalls++;
                LastJoinMode = mode;
                message = "wchodzę na serwer.";
                return true;
            }

            public bool TryLeaveServer(out string message)
            {
                LeaveCalls++;
                message = "wychodzę z serwera…";
                return true;
            }

            public bool TryAssignGameWindow(int processId, out string message)
            {
                AssignedProcessId = processId;
                message = "przypisano okno.";
                return true;
            }

            public bool TryArrangeGameWindow(out string message)
            {
                ArrangeCalls++;
                message = "okno ustawione.";
                return true;
            }

            public void ShowFromRemote()
            {
            }

            public void ReloadProfile()
            {
            }

            public int ClearedLogs { get; private set; }

            public bool TryClearMiningLogs(out string message)
            {
                ClearedLogs++;
                message = "wyczyszczono historię kopania.";
                return true;
            }
        }
    }
}

