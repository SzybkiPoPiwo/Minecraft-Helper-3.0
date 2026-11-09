using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftHelper
{
    /// <summary>
    /// Nowość pokazywana w panelu „Nowe w …” po aktualizacji Kopacza. Tab i Targets to nazwy
    /// (x:Name) zakładki i karty z MainWindow.xaml: „Pokaż” przechodzi do zakładki i podświetla
    /// pierwszą widoczną kartę z listy (np. Powiadomienia, a gdy bot jest wyłączony — kartę bota).
    /// </summary>
    internal sealed record MinerWhatsNewItem(
        string Version,
        string Title,
        string Text,
        string Tab,
        string[] Targets,
        string Icon);

    /// <summary>Cele zbudowane w kodzie, a nie w MainWindow.xaml — rejestrowane w oknie przez RegisterName.</summary>
    internal static class MinerWhatsNewTargets
    {
        public const string AccountsLogsButton = "BtnAccountsLogs";
    }

    internal static class MinerWhatsNew
    {
        // Przy kolejnej wersji dopisz jej nowości na początku listy (kolejność = kolejność kroków).
        private static readonly MinerWhatsNewItem[] Items =
        {
            new("3.0.3", "Discord ma własną zakładkę",
                "Bot, powiadomienia i komendy są teraz tutaj, a nie w Ustawieniach. Ustawienia bota są wspólne dla wszystkich kont.",
                "TabDiscord", new[] { "BorderDiscordBotCard" }, "IcoChat"),
            new("3.0.3", "Wyłączone opcje są zwinięte",
                "Wyłączona opcja chowa swoje ustawienia — tak jak HUD tutaj. Przy wyłączonym bocie Discord widać tylko jego przełącznik.",
                "TabSettings", new[] { "BorderOverlayHudSection" }, "IcoCollapse"),
            new("3.0.3", "Status i zrzut co jakiś czas",
                "W karcie Powiadomienia włączasz przełącznik i wpisujesz, co ile minut — bez wpisywania 0. Karta pojawia się po włączeniu bota.",
                "TabDiscord", new[] { "BorderDiscordNotifyCard", "BorderDiscordBotCard" }, "IcoBell"),
            new("3.0.3", "Uporządkowane Ustawienia",
                "Kopia ustawień (Exportuj / Importuj) jest w karcie Profil konta, a samouczek, menu developera i „O programie” — w jednej karcie na dole.",
                "TabSettings", new[] { "BorderProfileCard" }, "IcoSliders"),
            new("3.0.3", "Nowy wygląd zakładki Kopanie",
                "Kafelek wybiera tryb, a jego ustawienia są poniżej. 5/3/3 kopie w miejscu, więc ma tylko bind i komendy; kierunek i obszar ustawiasz w 6/3/3.",
                "TabKopacz", new[] { "GridKopaczModeTiles" }, "IcoPickaxe"),
            new("3.0.3", "Kontrola kilofa przed kopaniem",
                "Przy każdym starcie i powrocie na serwer Kopacz otwiera EQ i sprawdza, czy diamentowy kilof leży na slocie 1. Bez niego kopanie się zatrzyma.",
                "TabKopacz", new[] { "BorderAccountBar" }, "IcoShield"),
            new("3.0.3", "Logi są teraz w Kontach",
                "Pozycja zniknęła z paska bocznego. Przycisk Logi otwiera historię kopania w osobnym oknie: lista jak w konsoli, szczegóły wpisu i filtr „Tylko problemy”.",
                "TabAccounts", new[] { MinerWhatsNewTargets.AccountsLogsButton, "HostAccounts" }, "IcoLogs"),
            new("3.0.3", "Uporządkowany Ekwipunek",
                "Ustawienia gry zwinięte do jednego wiersza („Jak ustawić?”), kolejność kroków w jednym pasku, a CobbleX i jedzenie w karcie Kroki dodatkowe. Przy wyłączonym Auto EQ widać tylko przełącznik.",
                "TabInventory", new[] { "BorderInventoryCleanupSection" }, "IcoGrid"),
            new("3.0.3", "Auto EQ wyrzuca też z paska na dole",
                "W Skanowanych slotach możesz włączyć sloty paska. Odznaczone zostają nietknięte, a kilof na H1 i jedzenie na H2 są chronione.",
                "TabInventory", new[] { "BorderInventorySlotsCard", "BorderInventoryCleanupSection" }, "IcoBox"),
            new("3.0.3", "Wpłata złota ma własną zakładkę",
                "Ustawienia wpłaty są teraz osobno, w pasku bocznym. Sama wpłata dalej odbywa się w cyklu Auto EQ.",
                "TabGoldDeposit", new[] { "BorderGoldDepositSection" }, "IcoGold"),
            new("3.0.3", "Mapa działania",
                "Otwierasz ją tym przyciskiem w osobnym oknie. Pokazuje, co Kopacz zrobi przy obecnych ustawieniach, i podświetla krok, który wykonuje teraz.",
                "TabSettings", new[] { "BorderActionMapCard" }, "IcoMap"),
        };

        /// <summary>
        /// Nowości wersji nowszych niż <paramref name="previousVersion"/> i nie nowszych niż
        /// bieżąca — po przeskoczeniu kilku wersji pokazujemy wszystkie, od najstarszej.
        /// Brak poprzedniej wersji (stary zapis bez numeru) = tylko bieżąca wersja.
        /// </summary>
        public static IReadOnlyList<MinerWhatsNewItem> Since(string? previousVersion, string currentVersion)
        {
            bool knownPrevious = !string.IsNullOrWhiteSpace(previousVersion);
            // Ta sama albo nowsza wersja (powrót do starszego programu) — nic nowego.
            if (knownPrevious && ReleaseNotesCatalog.CompareVersions(previousVersion, currentVersion) >= 0)
                return Array.Empty<MinerWhatsNewItem>();
            return Items
                .Where(item => ReleaseNotesCatalog.CompareVersions(item.Version, currentVersion) <= 0
                    && (knownPrevious
                        ? ReleaseNotesCatalog.CompareVersions(item.Version, previousVersion) > 0
                        : ReleaseNotesCatalog.CompareVersions(item.Version, currentVersion) == 0))
                .Select((item, index) => (item, index))
                .OrderBy(pair => pair.item.Version, Comparer<string>.Create(ReleaseNotesCatalog.CompareVersions))
                .ThenBy(pair => pair.index)
                .Select(pair => pair.item)
                .ToList();
        }
    }
}
