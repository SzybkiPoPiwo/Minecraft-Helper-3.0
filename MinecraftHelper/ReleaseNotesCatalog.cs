using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftHelper
{
    internal sealed record AppReleaseNotes(
        string Version,
        string Title,
        IReadOnlyList<string> Changes);

    internal static class ReleaseNotesCatalog
    {
        // Przy kolejnej wersji dodaj nowy wpis na początku listy.
        private static readonly AppReleaseNotes[] Releases =
        {
            new AppReleaseNotes(
                "3.0.4",
                "Dzwonek Komunikaty i wpłata złota komendą",
                new[]
                {
                    "Oba programy: dzwonek „Komunikaty” pokazuje nową wersję z listą zmian i linkiem do pobrania oraz krótkie wiadomości od autora. Zastąpił okno aktualizacji — nic już nie blokuje programu, a „Pomiń tę wersję” nie jest potrzebne.",
                    "Oba programy: nowy komunikat wyskakuje na 3 s w rogu ekranu, także gdy program jest w tle, ale nie przejmuje fokusu, więc nie wyrywa gry z pełnego ekranu.",
                    "Oba programy: przełącznik „Komunikaty od autora” w Ustawieniach wyłącza tylko wiadomości od autora; informacja o nowej wersji zostaje zawsze.",
                    "Oba programy: instalator bez białych błysków przy przechodzeniu między stronami i podczas kopiowania plików.",
                    "Oba programy: samouczki pokazują nowy wygląd Ustawień.",
                    "Kopacz: Wpłata złota ma dwa sposoby — „Ręczne przenoszenie” (jak dotąd) albo „Komenda”: program wpisuje komendę wpłaty z serwera i raz klika wskazane pole w menu. Każdy sposób pamięta własną komendę, tytuł i pole.",
                    "Kopacz: Enderchest ma plakietkę BETA i ostrzeżenie — funkcja jest w przygotowaniu i na razie nie działa.",
                    "Kopacz: mapa działania opisuje wybrany sposób wpłaty złota.",
                    "Auto Clicker: lista procesów Minecrafta w Ustawieniach odświeża się przy każdym otwarciu — gra uruchomiona po Auto Clickerze pojawia się od razu, bez klikania „Odśwież”.",
                    "Auto Clicker: przycisk „Sprawdź” w Ustawieniach pokazuje wynik w dzwonku."
                }),
            new AppReleaseNotes(
                "3.0.3",
                "Nowy wygląd, Kapitulacja i samouczek w obu programach",
                new[]
                {
                    "Kopacz: bot Discord ma własną zakładkę w pasku bocznym. Przy wyłączonym bocie widać tylko jego przełącznik — karty Połączenie, Powiadomienia i Komendy rozwijają się po włączeniu.",
                    "Kopacz: status i zrzut ekranu co jakiś czas włączasz przełącznikiem i wpisujesz, co ile minut — bez wpisywania 0.",
                    "Kopacz: uporządkowane Ustawienia — kopia ustawień jest w karcie Profil konta, a samouczek, menu developera i informacje o programie w jednej karcie.",
                    "Kopacz: uporządkowany Ekwipunek — ustawienia gry zwinięte do jednego wiersza, kolejność kroków w jednym pasku, a CobbleX i jedzenie w karcie Kroki dodatkowe.",
                    "Kopacz: Auto EQ może wyrzucać także z paska na dole. Sloty wybierasz pojedynczo, a kilof na H1 i jedzenie na H2 są chronione.",
                    "Kopacz: tryb „Wszystko poza cobblestone” nigdy nie wyrzuca diamentowego kilofa.",
                    "Kopacz: każdy start kopania i powrót na serwer sprawdza, czy diamentowy kilof leży na slocie 1. Jeśli nie, kopanie się zatrzymuje. Potem wybiera slot 1 i zaczyna kopać.",
                    "Kopacz: Wpłata złota ma własną zakładkę w pasku bocznym.",
                    "Kopacz: nowy wygląd zakładki Kopanie. Kafelek wybiera tryb, a jego ustawienia są poniżej; 5/3/3 kopie w miejscu, więc ma tylko bind i komendy.",
                    "Kopacz: Logi przeniesione do zakładki Konta, w osobnym oknie: lista jak w konsoli, szczegóły wpisu i filtr „Tylko problemy”.",
                    "Kopacz: mapa działania (przycisk w Ustawieniach, osobne okno) pokazuje kolejne kroki przy obecnych ustawieniach i podświetla ten wykonywany teraz.",
                    "Kopacz: po aktualizacji panel „Nowe w …” w rogu okna pokazuje nowości, a „Pokaż” przenosi prosto do nich i podświetla właściwą kartę.",
                    "Kopacz: samouczek pokazuje obecny wygląd programu i obejmuje też zakładki Discord, Wpłata złota i Enderchest — każda ma swój przycisk „Samouczek”.",
                    "Oba programy: okno aktualizacji pokazuje, co zmieniło się w nowej wersji, i pozwala pominąć wersję.",
                    "Auto Clicker: przytrzymanie PPM wstrzymuje AUTO PPM (np. żeby zjeść), a po puszczeniu klikanie wraca samo.",
                    "Auto Clicker: Kapitulacja w Ustawieniach — osobny bind wyłącza naraz wszystkie działające makra, także przy otwartym ekwipunku, czacie albo GUI.",
                    "Auto Clicker: nowy wygląd zakładek Clicker i Akcje. AUTO LPM i AUTO PPM to kafelki z jednym panelem ustawień, a slot i bind każdego przedmiotu są w jednym wierszu.",
                    "Auto Clicker: samouczek w trybie ćwiczeń, jak w Kopaczu. Przycisk „Samouczek” u góry okna pokazuje bieżącą zakładkę, a cały przewodnik powtórzysz w Ustawieniach.",
                    "Oba programy: nowa instalacja startuje w jasnym motywie; zapisany wybór motywu zostaje."
                }),
            new AppReleaseNotes(
                "3.0.2",
                "Tryb ciemny i nowy HUD nad grą",
                new[]
                {
                    "Oba programy: tryb ciemny. W Ustawieniach → Wygląd wybierzesz Jasny, Ciemny albo Jak w Windows (domyślnie) — wtedy program przełącza się razem z trybem aplikacji w Windows. Ciemnieją też okna dialogowe, menu zasobnika, HUD i pasek tytułu.",
                    "Oba programy: nowy HUD nad grą. Każdy kafelek ma ikonę, plakietkę stanu (ON, PAUZA, TEST, ALARM), dużą wartość i płynnie przesuwający się pasek odliczania.",
                    "Kopacz: komendy w pętli mają w HUD własny kafelek — następna komenda z odliczaniem, kolejne w kolejce i pasek do wysłania; podczas Auto EQ kafelek pokazuje pauzę.",
                    "Kopacz: ekran wczytywania w wybranym motywie i kolorze akcentu.",
                    "Kopacz: na karcie konta stan nie nachodzi już na nazwę konta."
                }),
            new AppReleaseNotes(
                "3.0.1",
                "Samouczek Kopacza i nowy instalator",
                new[]
                {
                    "Kopacz: samouczek w trybie ćwiczeń. Przy pierwszym uruchomieniu prowadzi po wszystkich sekcjach programu (36 kroków) na makiecie z prawdziwych zrzutów — kliknięcia niczego nie zmieniają, a po zakończeniu otwiera się prawdziwy Kopacz.",
                    "Kopacz: przycisk „Samouczek” obok tytułu każdej zakładki pokazuje tylko jej sekcje; cały samouczek powtórzysz w Ustawieniach → Pokaż samouczek.",
                    "Kopacz: samouczek przypomina, że ochrona i reconnect wymagają włączonej w grze paczki TXT Kopacza.",
                    "Kopacz: poprawiono tło — na szerokich oknach plamy koloru nie urywają się już twardą poziomą krawędzią.",
                    "Instalator: nowy wygląd w stylu szkła, krótsze opisy programów i pole „Pokaż samouczek przy pierwszym uruchomieniu” (domyślnie zaznaczone)."
                }),
            new AppReleaseNotes(
                "3.0",
                "Nowy Minecraft Helper: osobny Auto Clicker i Kopacz",
                new[]
                {
                    "Minecraft Helper to teraz dwie niezależne aplikacje — Auto Clicker i Kopacz — z własnymi ikonami, ustawieniami i nowym, szklanym wyglądem. Instalator pozwala wybrać jedną albo obie.",
                    "Auto Clicker: AUTO LPM i AUTO PPM z trybami przełączania, trzymania binda i uzbrajania bindem z klikaniem tylko przy wciśniętym przycisku myszy. Blokada przy widocznym kursorze chroni EQ, czat i menu.",
                    "Auto Clicker: szybkie akcje PVP (blok, śnieżka, perła, wędka) z powrotem na miecz, Auto Guard, zamrożenie okna, BINDY z komendami, Auto zbroja i automatyczne ścinanie jabłek.",
                    "Auto Clicker wybiera proces gry, a nie pojedyncze okno: działa na pełnym ekranie, po F11 i Alt+Tab, nie gubi wybranej gry po utracie fokusu i nigdy nie wybiera własnego okna, Kopacza ani innych programów z „Minecraft” w tytule.",
                    "Kopacz: kopanie 5/3/3 i 6/3/3 z edytorem obszaru (jedna kratka = jeden blok), kopanie w tle (beta), Auto EQ z wyrzucaniem wybranych przedmiotów, CobbleX i jedzenie.",
                    "Kopacz: ochrona po dźwięku obrażeń, kontrola kilofa, Auto Reconnect (menu główne, ekran rozłączenia, lista serwerów, Direct Connect), powrót na /home i wznowienie kopania.",
                    "Kopacz: wiele kont naraz, bot Discord z powiadomieniami i komendami (!start, !stop, !join, !leave, !status, !dane, !zrzut) — każdą można wyłączyć w oknie „Komendy bota”; logi kopania z czyszczeniem oraz osobne okno menu developera z testami każdego etapu ochrony.",
                    "Kopacz sam tworzy paczkę zasobów Auto EQ z plików gry 1.8.8 i wrzuca ją do resourcepacks — wystarczy ją włączyć w Minecraft.",
                    "Ustawienia z wcześniejszych wersji zostają zachowane. Przed instalacją zamknij obie aplikacje z menu zasobnika — przycisk X tylko chowa okno."
                }),
            new AppReleaseNotes(
                "1.1.8",
                "AutoWater, Auto zbroja i rozbudowana ochrona Kopacza",
                new[]
                {
                    "Dodano Auto zbroję, która pod bindem zamienia założony komplet z drugim setem przygotowanym w ekwipunku.",
                    "Dodano eksperymentalny AutoWater z własnym bindem, wyborem slotu wody i slotu powrotnego oraz regulowanym czasem prób PPM i zebrania wody.",
                    "Awaryjna ochrona Kopacza łączy wykrywanie dźwięku obrażeń, wyjście z serwera, reconnect, kontrolę kilofa i bezpieczny powrót do odpowiedniego home.",
                    "Rozbudowano komunikaty, HUD oraz logi przebiegu ochrony awaryjnej i Auto Reconnect."
                }),
            new AppReleaseNotes(
                "1.1.7",
                "Płynniejsze działanie i pełna kontrola nad Kopaczem",
                new[]
                {
                    "Globalna obsługa myszy jest aktywna tylko podczas pracy makr, dzięki czemu samo uruchomienie Minecraft Helper nie powoduje przycięć gry.",
                    "Bind Kopacza natychmiast zatrzymuje pracę także podczas Auto EQ, wyrzucania, CobbleX, jedzenia, wpisywania komend i Auto Reconnect.",
                    "Wyniki skanowania EQ i rozpoznawania ekranów wykonywane w tle są bezpiecznie odrzucane po zatrzymaniu albo ponownym uruchomieniu automatyzacji.",
                    "Odliczanie komend Kopacza zatrzymuje się na czas całej sekwencji Auto EQ, CobbleX i jedzenia, a następnie wraca z zachowanym czasem.",
                    "Logi Auto EQ pokazują teraz bezpośredni wynik czyszczenia, liczbę pozostałych stosów oraz jasny powód zakończenia lub przerwania operacji."
                }),
            new AppReleaseNotes(
                "1.1.6",
                "Szybsza reakcja clickerów i bezpieczniejszy AUTO PPM",
                new[]
                {
                    "AUTO PPM w trybie Trzymanie bindu wymaga teraz osobnego klawisza i nie może współdzielić go z AUTO LPM.",
                    "Puszczenie bindu zatrzymuje AUTO LPM lub AUTO PPM bez oczekiwania na kolejną iterację interfejsu.",
                    "Harmonogram kliknięć dodatkowo kontroluje stan trzymanego klawisza na własnym wątku, dzięki czemu zajęte GUI nie opóźnia zatrzymania.",
                    "Pierwsze kliknięcie jest wykonywane natychmiast po aktywacji, a kolejne od początku zachowują pełny ustawiony rytm CPS.",
                    "Starsza konfiguracja ze wspólnym bindem w trybie Trzymanie bindu AUTO PPM jest bezpiecznie rozdzielana przy wczytywaniu ustawień."
                }),
            new AppReleaseNotes(
                "1.1.5",
                "Wspólne bindy, czytelniejsze logi i aktualizacje",
                new[]
                {
                    "AUTO LPM i AUTO PPM mogą korzystać z jednego wspólnego bindu w trybie kombinacji albo Trzymanie bindu.",
                    "Dodano czytelne okno konfliktu, które wyjaśnia, jak bezpiecznie rozdzielić wspólny bind przed przejściem do trybu klasycznego.",
                    "Dopracowano obramowania sesji, otwarć EQ i zdarzeń automatyzacji w logach kopania, aby rozwinięte poziomy nie zlewały się ze sobą.",
                    "Przy uruchomieniu program sprawdza najnowsze publiczne wydanie GitHub i informuje o dostępnej aktualizacji wraz z linkiem do oficjalnego instalatora.",
                    "Sprawdzanie aktualizacji działa w tle, nie pobiera ani nie uruchamia plików automatycznie i nie blokuje programu przy braku internetu."
                }),
            new AppReleaseNotes(
                "1.1.4",
                "Nowe tryby clickerów i bezpieczniejsze automatyzacje",
                new[]
                {
                    "AUTO LPM i AUTO PPM otrzymały tryb Trzymanie bindu, w którym clicker działa wyłącznie podczas fizycznego trzymania przypisanego klawisza.",
                    "Dodano ustawienia klawisza otwierania chatu i wyrzucania przedmiotu, dzięki czemu automatyzacje współpracują z własnym sterowaniem Minecrafta.",
                    "Moduł BINDY nie uruchamia komend podczas pisania, gdy Minecraft pokazuje kursor czatu, ekwipunku lub innego GUI.",
                    "Kopacz 6/3/3 przed zaplanowanym Auto EQ i CobbleX wraca do pozycji startowej: pełne A przy kopaniu na wprost albo A i S przy kopaniu do góry.",
                    "Poprawiono utrzymywanie HUD nad oknem gry na Windows 10 i odtwarzanie panelu po zmianach trybu okna.",
                    "Usunięto nieużywany timer i starą ścieżkę analizy encji F3 oraz ograniczono zbędne operacje podczas zapisu ustawień i logów."
                }),
            new AppReleaseNotes(
                "1.1.3",
                "Nowy wygląd i rozszerzone Auto EQ",
                new[]
                {
                    "Dodano lekkie, animowane tło z gwiazdami oraz przełącznik pozwalający wyłączyć animację w ustawieniach.",
                    "Ujednolicono wygląd i działanie pasków przewijania we wszystkich oknach; przewijanie nie przeskakuje już od razu na samą górę lub dół.",
                    "Dopracowano układ zakładki Kopacz, obramowania kart i przyciski wyboru kanału 5/3/3 oraz 6/3/3.",
                    "Komunikaty modułu BINDY znikają automatycznie po kilku sekundach zamiast pozostawać na ekranie.",
                    "Auto EQ rozpoznaje i może osobno wyrzucać bloki złota, żelaza oraz emeraldu.",
                    "Zaktualizowano paczkę zasobów i detektor jednolitych kolorów bloków, zachowując ochronę cobblestone.",
                    "Odświeżono instrukcję oraz zrzuty ekranu programu."
                }),
            new AppReleaseNotes(
                "1.1.2",
                "Auto Reconnect, rozbudowany Kopacz i nowe logi",
                new[]
                {
                    "Dodano konfigurowalne profile Auto Reconnect z adresem Direct Connect, komendą powrotu, opcjonalnym GUI /home, wyborem pola oraz czasami oczekiwania.",
                    "Po odzyskaniu połączenia program wznawia ten sam tryb Kopacza 5/3/3 lub 6/3/3, który działał przed rozłączeniem.",
                    "Kontrola kilofa działa podczas zaplanowanego Auto EQ i może uruchomić oddzielnie skonfigurowany powrót do home po wykryciu problemu.",
                    "Rozbudowano Auto EQ o niezależny tryb wyrzucania całej zawartości, wybór typów i slotów oraz opcjonalne jedzenie mięsa ze slotu 2.",
                    "Przebudowano logi kopania: jedno uruchomienie Kopacza tworzy nadrzędną sesję, a kolejne skany EQ i zdarzenia automatyzacji są dostępne w rozwijanych szczegółach.",
                    "Logi zapisują tryb wyrzucania, wyniki przedmiotów i stosów, CobbleX, jedzenie oraz przebieg Auto Reconnect; raporty CSV i TXT zawierają nowe dane sesji.",
                    "Uporządkowano sekcję Kopacza, dodano czytelne obramowania kart i zakładek, zmniejszono domyślne okno oraz wyśrodkowano je przy uruchomieniu.",
                    "Dodano blokadę uruchomienia drugiej kopii Minecraft Helper i poprawiono obsługę wejścia, aby działające GUI nie powodowało przycięć podczas gwałtownych ruchów myszy.",
                    "AUTO LPM i AUTO PPM nie przechwytują bindów przy widocznym kursorze Minecrafta; ograniczenie nie zatrzymuje pozostałych modułów."
                }),
            new AppReleaseNotes(
                "1.1.1",
                "Logi kopania i dokładniejsze Auto EQ",
                new[]
                {
                    "Dodano trwałe logi kopania z liczbą utworzonych CobbleXów oraz wyrzuconych sztuk i stosów.",
                    "Dodano wyszukiwanie w historii oraz eksport aktualnie widocznych wpisów do raportu CSV lub TXT.",
                    "Poprawiono liczenie wyrzuconych przedmiotów — wynik jest potwierdzany ponownym skanem ekwipunku i uwzględnia liczebność stosów.",
                    "Auto EQ odsuwa kursor poza ekwipunek i wykonuje do trzech przebiegów, aby tooltip nie zasłaniał przedmiotów.",
                    "Ujednolicono wygląd komunikatu usuwania historii z ciemnym motywem aplikacji.",
                    "Przycisk minimalizacji pozostawia aplikację na pasku zadań, a zamknięcie przyciskiem X przenosi ją do zasobnika i wyświetla powiadomienie.",
                    "Każde otwarcie EQ tworzy osobną, rozwijaną sesję logu z kolorowym statusem i listą wyrzuconych przedmiotów.",
                    "AUTO PPM oraz HOLD PPM nie mogą zostać uruchomione, gdy Minecraft pokazuje kursor ekwipunku, chatu lub innego GUI.",
                    "HUD pokazuje czas pracy, historię skanów EQ i wyrzucania podczas kopania oraz czas bieżącej sesji łowienia.",
                    "Poprawiono precyzję AUTO LPM, AUTO PPM i HOLD PPM — ustawione 20 CPS nie traci już szybkości przez opóźnienia timera.",
                    "Poprawiono rozpoznawanie kursora w trybie pełnoekranowym — przezroczysty kursor rozgrywki nie blokuje makra, a otwarte GUI nadal je zatrzymuje."
                }),
            new AppReleaseNotes(
                "1.1.0",
                "Auto EQ, CobbleX i poprawki interfejsu",
                new[]
                {
                    "Dodano Auto EQ z wyborem typów przedmiotów i slotów 1–27 oraz wyrzucaniem całych stosów przez lewy Ctrl + Q.",
                    "Dodano wykrywanie pełnych stacków cobblestone i automatyczne wykonywanie komendy CobbleX.",
                    "Rozszerzono HUD o stan Auto EQ, licznik cobblestone i wynik ostatniego skanu.",
                    "Poprawiono stabilność clickerów, obsługę wejścia oraz niekontrolowane przesunięcia kursora.",
                    "Uproszczono zakładki PVP i Experimental oraz dodano animowane, rozwijane sekcje z pomocą pod przyciskami ?.",
                    "Dodano komunikat pierwszego uruchomienia i informacje o zmianach pomiędzy wersjami."
                })
        };

        public static string CurrentVersion
        {
            get
            {
                Version? version = typeof(ReleaseNotesCatalog).Assembly.GetName().Version;
                if (version == null)
                    return "3.0";

                int build = Math.Max(0, version.Build);
                return build == 0
                    ? $"{version.Major}.{version.Minor}"
                    : $"{version.Major}.{version.Minor}.{build}";
            }
        }

        public static IReadOnlyList<AppReleaseNotes> GetNotesForStartup(
            string? previousVersion,
            string currentVersion,
            bool isFirstRun)
        {
            Version? previous = ParseVersion(previousVersion);
            Version? current = ParseVersion(currentVersion);

            IEnumerable<AppReleaseNotes> notes = Releases;
            if (current != null)
            {
                notes = notes.Where(note =>
                {
                    Version? noteVersion = ParseVersion(note.Version);
                    return noteVersion != null && noteVersion <= current;
                });
            }

            if (!isFirstRun && previous != null && current != null && previous < current)
            {
                notes = notes.Where(note =>
                {
                    Version? noteVersion = ParseVersion(note.Version);
                    return noteVersion != null && noteVersion > previous;
                });
            }
            else
            {
                notes = notes.Where(note =>
                    string.Equals(note.Version, currentVersion, StringComparison.OrdinalIgnoreCase));
            }

            List<AppReleaseNotes> result = notes
                .OrderByDescending(note => ParseVersion(note.Version))
                .ToList();

            if (result.Count == 0)
            {
                result.Add(new AppReleaseNotes(
                    currentVersion,
                    "Zmiany w aplikacji",
                    new[]
                    {
                        "Ta wersja zawiera zmiany programu. Zapoznaj się z opisami funkcji dostępnymi pod przyciskami ?."
                    }));
            }

            return result;
        }

        public static int CompareVersions(string? left, string? right)
        {
            Version? leftVersion = ParseVersion(left);
            Version? rightVersion = ParseVersion(right);

            if (leftVersion == null && rightVersion == null)
                return 0;
            if (leftVersion == null)
                return -1;
            if (rightVersion == null)
                return 1;

            return leftVersion.CompareTo(rightVersion);
        }

        private static Version? ParseVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            int suffixIndex = normalized.IndexOfAny(new[] { '+', '-' });
            if (suffixIndex >= 0)
                normalized = normalized[..suffixIndex];

            return Version.TryParse(normalized, out Version? version) ? version : null;
        }
    }
}
