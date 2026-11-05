# Zatrzymywanie Kopacza podczas akcji

Uruchom na Windows z katalogu repozytorium:

```powershell
dotnet run --project tests/MiningStop/MiningStop.csproj -c Release
dotnet run --project tests/MinerIsolation/MinerIsolation.csproj -c Release
```

Testy sprawdzają zatrzymanie stanu Kopacza 5/3/3 i 6/3/3 dla każdego etapu
Auto EQ i komend, anulowanie pozostałych celów, CX, jedzenia i wznowienia,
odrzucanie starych wyników skanowania po STOP/restart oraz pierwszeństwo
obsługi STOP przed blokadą komend i reconnectu. Sprawdzają też pojedyncze
naciśnięcie bindu i ignorowanie klawiszy trzymanych syntetycznie przez automat.
Wybór kanału START jest sprawdzany przy obu kanałach włączonych: 6/3/3 ma
pierwszeństwo dla każdego kierunku, brak kierunku blokuje start, a samo 5/3/3
zachowuje kopanie w miejscu. Wyłączonego kanału nie uruchamia zapisany kierunek.
Licznik komend obu Kopaczy jest sprawdzany pod kątem zatrzymania na cały czas
Auto EQ/CX/jedzenia i wznowienia z dokładnie zachowaną pozostałą wartością.
Test obejmuje również jasne statusy sesji EQ i poprawną polską odmianę liczby
pozostałych stosów oraz wykonanych przebiegów.

Opcja `Wpisuj komendę 3 razy` jest sprawdzana dla jednego i trzech wysłań:
powtórzenia pozostają przed wpłatą złota, jedzeniem i wznowieniem kopania,
mają termin następnego kroku, a STOP kasuje licznik i oczekujące wysłania.
Test obejmuje też zapis checkboxa oraz odrzucenie opóźnionego kroku po STOP.

Ochrona awaryjna jest sprawdzana w trybie tylko Disconnect i z reconnectem:
normalizacja oraz zapis/odczyt zachowują niezależne checkboxy, a wyłączony
reconnect blokuje też opcjonalną kontrolę Auto EQ. Opóźniony wynik Disconnect
nie może uruchomić powrotu po wyłączeniu checkboxa, STOP ani ponownym włączeniu
opcji po anulowaniu starej procedury.

Testy interfejsu bez okna sprawdzają też możliwość odznaczenia zapisanej opcji
przy nieaktywnych zależnościach. Rzeczywisty handler trybu testowego jest wywoływany
z kontrolkami testowymi: wybór nie jest cofany i nie pojawia się modalne potwierdzenie.
Zmieniane są wyłącznie obiekty w pamięci; timer zapisu jest zatrzymywany bez zapisu.

Testy nie uruchamiają GUI/Minecrafta, nie zapisują ustawień ani logów użytkownika
i nie wysyłają klawiszy do pulpitu. Test resetowania stanu zaczyna z puszczonymi
klawiszami; rzeczywiste puszczenie klawiszy w grze wymaga próby ręcznej.

Rozpoznawanie powrotu do gry sprawdza hover, kliknięcia w inne aplikacje,
krótkie kliknięcia w grę, Alt+Tab, zmianę pulpitu, stare zdarzenia oraz kolejność
dostarczenia Raw Input i aktywacji okna. Osobna próba rejestracji natywnej:

```powershell
dotnet run --project tests/MiningStop/MiningStop.csproj -c Release -- --focus-input-smoke
```

Ta opcja dwukrotnie tworzy i usuwa niewidoczne okno typu message-only oraz
rejestruje/wyrejestrowuje Raw Input na niezależnym wątku wejścia. Testowy HWND gry=0
wyłącza korekty kursora, także gdy podczas testu użytkownik porusza myszą.
Sprawdza też na natywnym, ukrytym oknie HUD,
że przypisanie obcego właściciela przed i po utworzeniu HWND jest odrzucane.
Nie pokazuje GUI i nie wysyła wejścia do gry.
Zachowanie fokusu i kursora BlazingPacka nadal wymaga próby ręcznej.

Testy bazowej pozycji myszy sprawdzają zachowanie współrzędnych LWJGL przy
niezmienionym i zmienionym rozmiarze okna, parzystych/nieparzystych wymiarach,
odwróconej osi Y oraz ujemnych współrzędnych w komunikacie WM_MOUSEMOVE.
Osobna diagnostyka `artifacts/diagnostics/MouseBaselineProbe.java` odtwarza
kumulowanie i anulowanie zaległego ruchu na zainstalowanych klasach LWJGL,
w niezależnym JVM, bez uruchamiania okna ani wysyłania wejścia do pulpitu.

Tekst w tle jest sprawdzany dla pełnego protokołu VK_PACKET down → WM_CHAR → up,
w tym początkowego `/`, spacji, wielkich liter, Unicode i częściowych błędów.
Natywny smoke test sprawdza też brak dodatkowego drukowalnego znaku generowanego
przez TranslateMessage. `artifacts/diagnostics/KeyboardTextProbe.java` odtwarza
stary błąd i nową sekwencję na rzeczywistym WindowsKeyboard z JAR klienta.

## Próba ręczna

Osobny test rzeczywistych HUD-ów WPF (okna poza widocznym pulpitem):

```powershell
dotnet run --project tests/MiningStop/MiningStop.csproj -c Release -- --hud-layout-smoke
```

Sprawdza różne szerokości/wysokości kart, zmianę treści, brak miejsca na monitorze,
natychmiastowe ukrycie także przy włączonych animacjach, ponowne pokazanie i brak
przejmowania fokusu/obcego ownera. Nie otwiera Minecrafta ani nie wysyła wejścia.

Gdy działający Kopacz blokuje pliki `bin/Release`, do komend testów dodaj
`-p:BaseOutputPath=bin_codex/focus-verify/` przed separatorem `--`.

Użyj bindu, który nie koliduje ze sterowaniem gry (np. Insert/Delete), i powtórz
dla obu Kopaczy. Podczas każdej próby naciśnij raz ten sam bind, którym włączasz
kopanie:

- podczas powrotu A/S do punktu startowego przed EQ;
- podczas otwierania i skanowania EQ oraz wyrzucania stosów;
- podczas wpisywania CX/komendy kopania i jedzenia po EQ;
- podczas kontroli EQ, oczekiwania i analizy ekranu reconnectu.

Po STOP nie mogą być wykonywane pozostałe akcje ani wznowienie kopania.
Klawisze trzymane przez program (ruch, Shift, Ctrl, wyrzucanie, LPM/PPM) mają
zostać puszczone. Przytrzymanie bindu po STOP nie może ponownie uruchomić Kopacza.
Sprawdź też ponowny start po puszczeniu i ponownym naciśnięciu bindu.

STOP nie cofa przedmiotów już wyrzuconych ani komend już wysłanych. Nie wysyła
dodatkowego Escape/E: otwarte EQ lub chat można zamknąć ręcznie. Wynik trwającego
skanu może zostać obliczony w tle, ale po anulowaniu jest ignorowany.
