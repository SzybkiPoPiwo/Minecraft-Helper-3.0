# Minecraft Helper Auto Clicker

Lekka, niezależna aplikacja zawierająca wyłącznie funkcje używane poza kopaniem:

- AUTO LPM i AUTO PPM: przełączanie bindem, działanie podczas trzymania, tryb kombinacji oraz trwałe uzbrojenie osobnym bindem, w którym clicker pracuje tylko podczas fizycznego trzymania LPM albo PPM. Wspólny bind działa dla LPM w trybie trzymania/kombinacji i PPM w trybie kombinacji; trwałe uzbrojenie LPM i PPM wymaga różnych klawiszy,
- szybkie akcje PVP: osobne bindy Auto bloku, śnieżki, perły i wędki, własne sloty hotbara oraz regulowany czas trzymania PPM dla wędki,
- ścinanie jabłek: osobny bind, slot nożyc i liści, losowane zakresy czasu przełączania, cięcia i kładzenia, PPM 40 CPS oraz automatyczne `/repair` co ustawioną liczbę cykli,
- BINDY z własnymi nazwami, klawiszami i komendami,
- Auto zbroja z ręczną kalibracją pól,
- opcjonalny, mały overlay aktywnych funkcji z jednolitym rozmiarem wszystkich komunikatów.

Projekt nie uruchamia Kopacza, Auto EQ kopacza, OCR, Auto Reconnect ani nasłuchu dźwięku. Kliknięcia wykonuje lekki harmonogram uruchamiany tylko wtedy, gdy makro faktycznie pracuje. W trybach zależnych od fizycznego LPM/PPM minimalny tracker działa tylko podczas trzymania przycisku, ignoruje wejście aplikacji i nie przetwarza ruchów myszy. Ustawienia są zapisywane niezależnie w:

`%APPDATA%\Minecraft Helper\AutoClicker\settings.json`

W bieżących źródłach tryb czuwania odczytuje bindy, ale nie analizuje kursora,
nie odświeża ukrytych kontrolek ani pustego HUD-u. Przed aktywacją następuje świeży
odczyt kursora; ochrona przed kliknięciem w EQ/chat pozostaje włączona. Bitmapy
kursora są odczytywane zbiorczo, a rozpoznane nazwy klawiszy zapamiętywane.
W spoczynku program pomija też odtwarzanie okna, sprawdzanie uchwytu i fokusu,
gdy program jest w zasobniku i niczego nie wykonuje. Po użyciu binda oraz podczas
aktywnych sekwencji kontrole okna i fokusu wracają przed wysłaniem wejścia.
Przed instalacją zamknij starą instancję w zasobniku; po instalacji sprawdź
numer 3.0 w tytule programu. [Testy spoczynku](../tests/PvpIdle/README.md)
nie zastępują dłuższej próby w Minecraft i nie potwierdzają przyczyny skoku kamery.

Szybka akcja działa tylko podczas aktywnego AUTO LPM; samo AUTO PPM jej nie uruchamia. Jedno naciśnięcie odpowiedniego binda wybiera skonfigurowany slot, wykonuje jedno użycie PPM i przywraca ustawiony slot miecza. AUTO LPM oraz DAB działają podczas całej sekwencji bez przerwy, natomiast AUTO PPM jest chwilowo pauzowane, aby nie dublowało użycia przedmiotu. Auto blok wybiera ustawiony slot bloku i stawia jeden blok dokładnie tak jak akcje śnieżki i perły, a dla wędki można ustawić czas trzymania PPM od 20 do 1000 ms. Utrata fokusu, otwarte GUI lub wyłączenie AUTO LPM przerywają sekwencję i zwalniają PPM.

Auto blok, śnieżka i perła używają pojedynczego pełnego kliknięcia PPM, bez przytrzymywania przycisku między etapami. Akcja aktualizuje wyłącznie lewy kanał harmonogramu i nie zmienia jego cyklu, jeśli ustawienia LPM pozostają takie same. Włączenie, wykonanie i zakończenie akcji nie resetują CPS ani terminu następnego kliknięcia. W trybach trzymania puszczenie wymaganego bindu lub LPM nadal zatrzymuje klikanie.

Zakładka `Jabłka` uruchamia niezależną pętlę jednym bindem: wybiera nożyce, przytrzymuje LPM, przełącza się na liście, wysyła PPM z szybkością 40 CPS i wraca na nożyce. Każdy z trzech etapów ma własny zakres `od–do` w milisekundach. Domyślnie po 40 pełnych cyklach, już na slocie nożyc, program wpisuje `/repair`. Ten sam bind zatrzymuje automat natychmiast; utrata fokusu lub nieoczekiwane otwarcie GUI także przerywa działanie i zwalnia trzymany LPM.

## Ustawienia i aktualizacje

Jeżeli istnieją ustawienia wcześniejszego prototypu z brancha `projekt-2-apki`, program wczyta je ze starej lokalizacji i przy najbliższym zapisie zapisze konfigurację także w nowym pliku.

Przy pierwszym uruchomieniu oraz po zmianie wersji Auto Clicker pokazuje krótkie informacje o konfiguracji i listę nowości. Przy każdym starcie sprawdza też najnowsze publiczne wydanie na GitHubie; znalezioną aktualizację tylko zgłasza i podaje oficjalny link, bez automatycznego pobierania lub instalowania pliku.

## Uruchomienie ze źródeł

Uruchom PowerShell w głównym katalogu repozytorium, obok `MinecraftHelper.slnx`. Wymagania SDK: [instrukcja budowania](../docs/BUILDING.md#wymagane-narzędzia).

```powershell
dotnet run --project ".\MinecraftHelper.Pvp\MinecraftHelper.Pvp.csproj" -c Release
```

Po uruchomieniu wybierz proces gry z listy i kliknij `Zapisz`. Clicker trzyma się procesu, więc pełny ekran, `F11` i `Alt+Tab` nie kasują wyboru. Funkcje wejścia działają tylko wtedy, gdy gra jest na wierzchu.

Program ma interfejs okienkowy, nie konsolowy. Jeśli już działa i nie widzisz okna, sprawdź ikonę w zasobniku obok zegara. Przycisk `X` i minimalizacja ukrywają okno; całkowite zamknięcie jest dostępne w menu ikony zasobnika.


