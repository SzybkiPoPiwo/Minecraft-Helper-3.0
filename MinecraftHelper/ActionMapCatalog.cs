using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftHelper
{
    /// <summary>Bieżące ustawienia wybranego konta potrzebne mapie działania.</summary>
    internal sealed record ActionMapSnapshot
    {
        public string MiningMode { get; init; } = "";
        public bool Uses633 { get; init; }
        public string Bind { get; init; } = "";
        public int CommandCount { get; init; }
        public bool AutoEq { get; init; }
        public int IntervalSeconds { get; init; }
        public string SlotSummary { get; init; } = "";
        public string DropSummary { get; init; } = "";
        public bool CobbleX { get; init; }
        public string CobbleXThreshold { get; init; } = "";
        public bool CobbleXCommandMissing { get; init; }
        public bool Enderchest { get; init; }
        public string EnderchestCommand { get; init; } = "";
        public bool EnderchestFull { get; init; }
        public string EnderchestIssue { get; init; } = "";
        public bool Gold { get; init; }
        public string GoldCommand { get; init; } = "";
        /// <summary>Wpłata komendą (komenda i jedno kliknięcie) zamiast ręcznego przenoszenia stosów.</summary>
        public bool GoldByCommand { get; init; }
        public string GoldIssue { get; init; } = "";
        public bool Food { get; init; }
        public bool Protection { get; init; }
        public bool ReconnectAfterAlarm { get; init; }
        public string WaitSeconds { get; init; } = "";
        public bool Hud { get; init; }
        public bool Discord { get; init; }
        public bool DiscordConnected { get; init; }

        /// <summary>Obecne ustawienia pokazywane po kliknięciu kafelka (id kafelka → wiersze).</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ActionMapDetail>>? Details { get; init; }
    }

    internal sealed record ActionMapDetail(string Label, string Value);

    /// <summary>Kafelek mapy. Tab i Targets to x:Name zakładki i karty; Tab == null = działa samo.</summary>
    internal sealed record ActionMapTile(
        string Id, string Title, string Value, string Description, string Where,
        string? Requires, string? Tab, string[] Targets, bool Alarm, bool Enabled, string Warning = "")
    {
        public IReadOnlyList<ActionMapDetail> Details { get; init; } = Array.Empty<ActionMapDetail>();
    }

    internal enum ActionMapWire { Main, AutoEq, Alarm }

    internal readonly record struct ActionMapSegment(int X1, int Y1, int X2, int Y2, ActionMapWire Wire, bool Dashed, bool Arrow);

    /// <summary>Treść i układ mapy (bez WPF). Nowy krok programu = nowy kafelek tutaj i w GetActionMapCurrentStep.</summary>
    internal static class ActionMapCatalog
    {
        public const int Width = 900;
        public const int Height = 492;

        // x, y, szerokość, wysokość
        public static readonly IReadOnlyDictionary<string, (int X, int Y, int W, int H)> Layout = new Dictionary<string, (int, int, int, int)>
        {
            ["start"] = (16, 62, 118, 58), ["pick"] = (166, 62, 118, 58), ["mine"] = (316, 56, 170, 70), ["cmd"] = (530, 0, 130, 54), ["back"] = (736, 62, 130, 58),
            ["open"] = (16, 190, 104, 60), ["scan"] = (138, 190, 104, 60), ["drop"] = (260, 190, 104, 60), ["cobble"] = (382, 190, 104, 60), ["ender"] = (504, 190, 104, 60), ["gold"] = (626, 190, 104, 60), ["food"] = (748, 190, 104, 60),
            ["alarm"] = (16, 340, 104, 60), ["exit"] = (138, 340, 104, 60), ["wait"] = (260, 340, 104, 60), ["join"] = (382, 340, 104, 60), ["rcheck"] = (504, 340, 104, 60), ["home"] = (626, 340, 104, 60), ["resume"] = (748, 340, 104, 60),
            ["hud"] = (140, 438, 150, 46), ["discord"] = (300, 438, 150, 46), ["logs"] = (460, 438, 150, 46),
        };

        public static readonly IReadOnlyList<ActionMapSegment> Wires = new ActionMapSegment[]
        {
            new(134, 91, 166, 91, ActionMapWire.Main, false, true), new(284, 91, 316, 91, ActionMapWire.Main, false, true),
            new(736, 91, 486, 91, ActionMapWire.AutoEq, false, true),
            new(420, 56, 420, 27, ActionMapWire.Main, true, false), new(420, 27, 530, 27, ActionMapWire.Main, true, true),
            new(401, 126, 401, 140, ActionMapWire.AutoEq, false, false), new(401, 140, 68, 140, ActionMapWire.AutoEq, false, false), new(68, 140, 68, 190, ActionMapWire.AutoEq, false, true),
            new(120, 220, 138, 220, ActionMapWire.AutoEq, false, true), new(242, 220, 260, 220, ActionMapWire.AutoEq, false, true), new(364, 220, 382, 220, ActionMapWire.AutoEq, false, true),
            new(486, 220, 504, 220, ActionMapWire.AutoEq, false, true), new(608, 220, 626, 220, ActionMapWire.AutoEq, false, true), new(730, 220, 748, 220, ActionMapWire.AutoEq, false, true),
            new(800, 190, 800, 120, ActionMapWire.AutoEq, false, true),
            new(373, 126, 373, 290, ActionMapWire.Alarm, true, false), new(373, 290, 68, 290, ActionMapWire.Alarm, true, false), new(68, 290, 68, 340, ActionMapWire.Alarm, true, true),
            new(120, 370, 138, 370, ActionMapWire.Alarm, false, true), new(242, 370, 260, 370, ActionMapWire.Alarm, false, true), new(364, 370, 382, 370, ActionMapWire.Alarm, false, true),
            new(486, 370, 504, 370, ActionMapWire.Alarm, false, true), new(608, 370, 626, 370, ActionMapWire.Alarm, false, true), new(730, 370, 748, 370, ActionMapWire.Alarm, false, true),
            new(852, 370, 880, 370, ActionMapWire.Alarm, false, false), new(880, 370, 880, 91, ActionMapWire.Alarm, false, false), new(880, 91, 866, 91, ActionMapWire.Alarm, false, true),
        };

        public static IReadOnlyList<ActionMapTile> Build(ActionMapSnapshot s) =>
            BuildTiles(s).Select(tile => s.Details != null && s.Details.TryGetValue(tile.Id, out IReadOnlyList<ActionMapDetail>? details)
                ? tile with { Details = details }
                : tile).ToList();

        private static ActionMapTile[] BuildTiles(ActionMapSnapshot s)
        {
            string[] miner = { s.Uses633 ? "RbKopacz633Tile" : "RbKopacz533Tile" };
            string[] autoEq = { "BorderInventoryCleanupSection" };
            string[] extras = { "BorderInventoryExtrasCard", "BorderInventoryCleanupSection" };
            string[] protection = { "BorderEmergencyDamageSoundSection" };
            string[] reconnect = { "BorderEmergencyReconnectSection", "BorderEmergencyDamageSoundSection" };
            const string Pack = "Paczka zasobów Kopacza włączona w grze";
            const string Off = "wyłączone";
            bool returns = s.Protection && s.ReconnectAfterAlarm;
            string afterAlarm = s.Protection ? "tylko wyjście" : Off;

            return new[]
            {
                new ActionMapTile("start", "Start", s.Bind.Length > 0 ? "bind: " + s.Bind : "przycisk START",
                    "Bindem w grze, przyciskiem START albo komendą !start na Discordzie. Ten sam bind zatrzymuje kopanie w każdej chwili.",
                    "Kopanie → Bind start / stop", null, "TabKopacz", miner, false, true),
                new ActionMapTile("pick", "Kontrola kilofa", "H1 i klawisz 1",
                    "Otwiera EQ i sprawdza diamentowy kilof na H1. Gdy go tam nie ma, kopanie się zatrzymuje. Potem naciska 1.",
                    "Działa samo", Pack, null, Array.Empty<string>(), false, true),
                new ActionMapTile("mine", "Kopanie", s.MiningMode.Length > 0 ? s.MiningMode : "brak trybu",
                    "5/3/3 trzyma LPM i Shift w miejscu. 6/3/3 chodzi po prostokącie, na wprost albo do góry.",
                    "Kopanie", null, "TabKopacz", miner, false, true, s.MiningMode.Length > 0 ? "" : "Włącz tryb kopania (5/3/3 albo 6/3/3)."),
                new ActionMapTile("cmd", "Komendy w pętli", s.CommandCount > 0 ? $"liczba: {s.CommandCount}" : "brak komend",
                    "Każda komenda czeka swój czas od poprzedniej, a po ostatniej lista wraca na początek. Podczas Auto EQ odliczanie stoi.",
                    "Kopanie → Komendy w pętli", null, "TabKopacz", miner, false, s.CommandCount > 0),
                new ActionMapTile("open", "Otwarcie EQ", s.AutoEq ? $"co {s.IntervalSeconds} s" : Off,
                    "Rusza co czas ze „Skanuj co”. Kopacz 6/3/3 najpierw wraca na początek trasy.",
                    "Ekwipunek → Auto EQ", "GUI Scale: Large, ekwipunek pod E", "TabInventory", autoEq, false, s.AutoEq),
                new ActionMapTile("scan", "Skan", s.AutoEq ? s.SlotSummary : Off,
                    "Rozpoznaje przedmioty po znacznikach z paczki i sprawdza, czy kilof dalej leży na H1.",
                    "Ekwipunek → Skanowane sloty", Pack, "TabInventory", new[] { "BorderInventorySlotsCard", "BorderInventoryCleanupSection" }, false, s.AutoEq),
                new ActionMapTile("drop", "Wyrzucanie", s.AutoEq ? s.DropSummary : Off,
                    "Ctrl + Q na wybranych przedmiotach z zaznaczonych slotów. Cobblestone, kilof i odznaczone sloty paska zostają.",
                    "Ekwipunek → Co wyrzucać", null, "TabInventory", new[] { "BorderInventoryDropCard", "BorderInventoryCleanupSection" }, false, s.AutoEq),
                new ActionMapTile("cobble", "CobbleX", s.AutoEq && s.CobbleX ? $"próg: {s.CobbleXThreshold}" : Off,
                    "Po zamknięciu EQ wpisuje komendę, gdy jest dość pełnych stacków cobblestone.",
                    "Ekwipunek → Kroki dodatkowe", null, "TabInventory", extras, false, s.AutoEq && s.CobbleX,
                    s.AutoEq && s.CobbleX && s.CobbleXCommandMissing ? "Wpisz komendę CobbleX." : ""),
                new ActionMapTile("ender", "Enderchest", !(s.AutoEq && s.Enderchest) ? Off : s.EnderchestFull ? "pełny" : s.EnderchestCommand,
                    "Komenda, potem Shift + LPM na wybranych przedmiotach i zamknięcie. Gdy jest pełny, komenda czeka do następnego startu kopania.",
                    "Enderchest", null, "TabEnderchest", new[] { "BorderEnderchestSection" }, false, s.AutoEq && s.Enderchest,
                    !(s.AutoEq && s.Enderchest) ? "" : s.EnderchestFull ? "Enderchest jest pełny. Następna próba przy kolejnym starcie kopania." : s.EnderchestIssue),
                new ActionMapTile("gold", "Wpłata złota", s.AutoEq && s.Gold ? s.GoldCommand : Off,
                    s.GoldByCommand
                        ? "Wpisuje komendę wpłaty, sprawdza tytuł menu i raz klika wskazane pole. Potem sprawdza, czy złota ubyło z EQ."
                        : "Otwiera menu komendą, sprawdza jego tytuł i przenosi pełne stacki złota do pola wpłaty.",
                    "Wpłata złota", null, "TabGoldDeposit", new[] { "BorderGoldDepositSection" }, false, s.AutoEq && s.Gold,
                    s.AutoEq && s.Gold ? s.GoldIssue : ""),
                new ActionMapTile("food", "Jedzenie", s.AutoEq && s.Food ? "slot 2, 4 s" : Off,
                    "Slot 2, PPM przez 4 sekundy, powrót na slot 1.",
                    "Ekwipunek → Kroki dodatkowe", null, "TabInventory", extras, false, s.AutoEq && s.Food),
                new ActionMapTile("back", "Powrót", "slot 1",
                    "Wybiera slot 1 i kopie dalej. Komendy liczą od miejsca, w którym stanęły.",
                    "Działa samo", null, null, Array.Empty<string>(), false, true),
                new ActionMapTile("alarm", "Alarm", s.Protection ? "dźwięk obrażeń" : Off,
                    "Słyszy dźwięk obrażeń i od razu zatrzymuje kopanie.",
                    "Ochrona", "Paczka TXT Kopacza włączona w grze", "TabProtection", protection, true, s.Protection),
                new ActionMapTile("exit", "Wyjście", s.Protection ? "Disconnect" : Off,
                    "ESC i Disconnect. Można ustawić, żeby na tym się skończyło.",
                    "Ochrona → Po alarmie", null, "TabProtection", protection, true, s.Protection),
                new ActionMapTile("wait", "Odczekanie", returns ? $"{s.WaitSeconds} s" : afterAlarm,
                    "Czeka czas ustawiony w ochronie, zanim wróci na serwer.",
                    "Ochrona → Po alarmie", null, "TabProtection", reconnect, true, returns),
                new ActionMapTile("join", "Wejście", returns ? "lista serwerów" : afterAlarm,
                    "Menu główne, lista serwerów, Direct Connect i wejście na serwer.",
                    "Ochrona → Profil powrotu", null, "TabProtection", reconnect, true, returns),
                new ActionMapTile("rcheck", "Kilof w EQ", returns ? "diamentowy" : afterAlarm,
                    "Szuka diamentowego kilofa. Bez niego wraca na awaryjny home i kończy pracę konta.",
                    "Ochrona → Profil powrotu", Pack, "TabProtection", reconnect, true, returns),
                new ActionMapTile("home", "/home", returns ? "z profilu" : afterAlarm,
                    "Komenda home, w razie potrzeby wybór w menu, i odczekanie teleportu.",
                    "Ochrona → Profil powrotu", null, "TabProtection", reconnect, true, returns),
                new ActionMapTile("resume", "Wznowienie", returns ? "ten sam tryb" : afterAlarm,
                    "Rusza tym samym trybem, który kopał, znów z kontrolą kilofa na H1.",
                    "Działa samo", null, null, Array.Empty<string>(), true, returns),
                new ActionMapTile("hud", "HUD nad grą", s.Hud ? "włączony" : Off,
                    "Kafelki z tym, co teraz się dzieje: kopanie, komendy, Auto EQ, ochrona.",
                    "Ustawienia → HUD nad grą", null, "TabSettings", new[] { "BorderOverlayHudSection" }, false, s.Hud),
                new ActionMapTile("discord", "Bot Discord", !s.Discord ? Off : s.DiscordConnected ? "połączony" : "rozłączony",
                    "Powiadomienia o alarmach, reconnekcie, braku kilofa, starcie i końcu kopania oraz komendy z telefonu.",
                    "Discord", null, "TabDiscord", new[] { "BorderDiscordBotCard" }, false, s.Discord,
                    s.Discord && !s.DiscordConnected ? "Bot jest włączony, ale nie ma połączenia." : ""),
                new ActionMapTile("logs", "Logi", "historia",
                    "Historia sesji kopania i cykli Auto EQ.",
                    "Konta → przycisk Logi", null, "TabAccounts", Array.Empty<string>(), false, true),
            };
        }
    }
}
