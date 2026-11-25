using System.Windows;

namespace MinecraftHelper;

// Kroki samouczka Auto Clickera. Zrzuty (Assets/Tutorial/view-*.png) i współrzędne pochodzą z tego samego
// przejścia po zakładkach. Okno jest wąskie, więc dymek leży pod kartą albo nad nią.
internal static class AutoClickerTutorial
{
    public static TutorialContent Content { get; } = new(
        StageWidth: 504,
        StageHeight: 753,
        CardWidth: 430,
        FullName: "Minecraft Helper Auto Clicker",
        IconName: "autoclicker",
        Nominative: "Auto Clicker",
        Genitive: "Auto Clickera",
        Locative: "Auto Clickerze",
        FirstStepHint: "zacznij od wybrania procesu gry w Ustawieniach",
        TabButtonHint: "Samouczek każdej zakładki otworzysz przyciskiem „Samouczek” u góry okna.",
        Steps: new TutorialStep[]
        {
            new("Ustawienia", "view-ustawienia-6", new Rect(12, 137, 470, 136), ArrowSide.Up, new Point(72, 283), 50, 294, 440,
                "Proces Minecrafta",
                "Uruchom Minecrafta, odśwież listę, wybierz proces gry (nie launcher) i kliknij Zapisz. Auto Clicker klika tylko w tej grze i tylko wtedy, gdy jest na wierzchu."),
            new("Ustawienia", "view-ustawienia-152", new Rect(12, 137, 470, 159), ArrowSide.Up, new Point(72, 306), 50, 317, 440,
                "Kapitulacja",
                "Awaryjne wyłączenie: osobny bind albo przycisk gasi naraz wszystkie działające makra, także przy otwartym ekwipunku i GUI. Ustaw klawisz, którego nie używasz na czacie."),
            new("Ustawienia", "view-ustawienia-321", new Rect(12, 137, 470, 234), ArrowSide.Up, new Point(72, 381), 50, 392, 440,
                "Bezpieczeństwo i HUD",
                "Blokada kursora zatrzymuje clickery i bindy w ekwipunku, czacie i menu, żeby nic nie kliknęło przypadkiem. Overlay pokazuje nad grą, co teraz działa."),
            new("Ustawienia", "view-ustawienia-396", new Rect(12, 209, 470, 158), ArrowSide.Up, new Point(72, 377), 50, 388, 440,
                "Wygląd",
                "Motyw jasny, ciemny albo jak w Windows i kolor akcentu. Zmiana działa od razu."),
            new("Ustawienia", "view-ustawienia-396", new Rect(12, 377, 470, 261), ArrowSide.Down, new Point(72, 355), 50, 0, 440,
                "Aktualizacje i komunikaty",
                "Nową wersję pokazuje dzwonek „Komunikaty” w nagłówku — zawsze, z listą zmian i linkiem do pobrania; „Sprawdź” odświeża go od razu. Przełącznik „Pokazuj komunikaty od autora” wyłącza tylko wiadomości od autora, nie informację o nowej wersji. „Pokaż samouczek” powtórzy ten przewodnik."),
            new("Clicker", "view-clicker-0-lpm", new Rect(8, 139, 488, 114), ArrowSide.Up, new Point(68, 263), 48, 274, 440,
                "Kafelki AUTO LPM i AUTO PPM",
                "Kliknij kafelek, żeby zobaczyć jego ustawienia poniżej. Przełącznik włącza moduł, a plakietka mówi, co robi teraz: KLIKA, GOTOWY, PAUZA albo OFF."),
            new("Clicker", "view-clicker-0-lpm", new Rect(12, 259, 480, 207), ArrowSide.Up, new Point(72, 476), 50, 487, 440,
                "Ustawienia AUTO LPM",
                "Kliknij klawisz, żeby zmienić bind, wybierz tryb i przesuń zakres CPS. Kliknięcie wartości CPS pozwala wpisać liczby. Tryb decyduje, czy bind przełącza, trzyma albo uzbraja klikanie."),
            new("Clicker", "view-clicker-0-lpm", new Rect(21, 453, 462, 52), ArrowSide.Down, new Point(81, 431), 50, 0, 440,
                "Więcej opcji",
                "Tryb DAB trzyma klawisz O podczas klikania, a pauza przy kucaniu wstrzymuje AUTO LPM, gdy trzymasz klawisz skradania. Znaczniki pokazują, co jest włączone."),
            new("Clicker", "view-clicker-0-ppm", new Rect(12, 259, 480, 228), ArrowSide.Up, new Point(72, 497), 50, 508, 440,
                "AUTO PPM",
                "Działa jak AUTO LPM, tylko prawym przyciskiem. W trybach z osobnym bindem trzymany PPM wstrzymuje klikanie, więc możesz zjeść, a po puszczeniu klikanie wraca samo."),
            new("Akcje", "view-akcje-6", new Rect(12, 137, 470, 329), ArrowSide.Up, new Point(72, 476), 50, 487, 440,
                "Sloty i bindy akcji",
                "Pasek pokazuje układ, a w wierszach ustawiasz slot i bind każdego przedmiotu. Przy działającym AUTO LPM bind wybiera przedmiot, używa go i wraca na miecz. Wybranie zajętego slotu zamienia przedmioty miejscami."),
            new("Akcje", "view-akcje-27", new Rect(12, 521, 470, 62), ArrowSide.Down, new Point(72, 499), 50, 0, 440,
                "Auto Guard",
                "Blockhit: bind włącza tapowanie PPM, które działa tylko wtedy, gdy AUTO LPM naprawdę klika. Kliknij opis, żeby ustawić odstęp."),
            new("Akcje", "view-akcje-27", new Rect(12, 593, 470, 62), ArrowSide.Down, new Point(72, 571), 50, 0, 440,
                "Zamrożenie okna",
                "Wstrzymuje wątek gry tylko na czas trzymania bindu. Po maksymalnym czasie albo utracie fokusu gra rusza sama. Kliknij opis, żeby ustawić limit."),
            new("Jabłka", "view-jablka-0", new Rect(12, 143, 480, 344), ArrowSide.Up, new Point(72, 497), 50, 508, 440,
                "Ścinanie jabłek",
                "Jeden bind uruchamia pętlę, ten sam zatrzymuje ją od razu: nożyce, cięcie LPM, liście i kładzenie PPM. Ustaw sloty i zakresy czasu."),
            new("Bindy", "view-bindy-0", new Rect(12, 143, 480, 344), ArrowSide.Up, new Point(72, 497), 50, 508, 440,
                "BINDY",
                "Klawisz w grze wpisuje zapisaną komendę na czacie. Ustaw klawisz czatu taki jak w grze i dodaj wpisy z nazwą, klawiszem i komendą."),
            new("Zbroja", "view-zbroja-0", new Rect(12, 143, 480, 344), ArrowSide.Up, new Point(72, 497), 50, 508, 440,
                "Auto zbroja",
                "Bind zamienia założony set na drugi z ekwipunku. Najpierw skalibruj 4 pola każdego setu: kliknij przycisk części, otwórz EQ, najedź na slot i naciśnij lewy Ctrl."),
        });
}
