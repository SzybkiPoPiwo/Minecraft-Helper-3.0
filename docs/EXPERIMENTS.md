# Eksperymenty techniczne

## Sterowanie BlazingPackiem na drugim pulpicie Windows

Status: wycofane z aplikacji po teście zgodności (27.09.2026).

### Cel

Sprawdzenie, czy Minecraft 1.8.8 uruchomiony przez BlazingPack może odbierać
polecenia z Minecraft Helpera, gdy znajduje się na innym wirtualnym pulpicie
Windows i nie jest aktywnym oknem.

### Sprawdzony wariant

- Polecenia klawiatury i myszy były wysyłane przez `PostMessage` bezpośrednio
  do uchwytu okna procesu `java.exe`.
- Testy obejmowały LPM, klawisze A, S, D, otwarcie ekwipunku klawiszem E oraz
  wpisanie komendy na czacie.
- Samo wysłanie `WM_KEYDOWN`/`WM_KEYUP` nie powodowało reakcji gry, dopóki
  użytkownik nie kliknął okna Minecrafta.
- Po wysłaniu `WM_SETFOCUS` ruch i otwieranie ekwipunku zaczęły działać również
  na drugim pulpicie.

### Wynik

BlazingPack/LWJGL po otrzymaniu fokusu przejmuje mysz, centruje lub ogranicza
kursor i może zakłócać pracę w aktywnym programie. Próby zwalniania `ClipCursor`
i przechwycenia myszy oraz przywracania pozycji kursora ograniczały objaw, ale
nie zapewniły bezpiecznej, niewidocznej pracy w tle.

Wirtualne pulpity Windows nie są osobnymi sesjami wejścia. Współdzielą aktywny
fokus, klawiaturę i mysz, dlatego ten wariant nie powinien zostać włączony do
produkcyjnego Kopacza.

### Zachowane wnioski na przyszłość

- Wiadomości należy kierować do aktualnego głównego okna właściwego procesu
  `java.exe`, a nie polegać wyłącznie na zapisanym wcześniej PID.
- Helper i gra muszą działać z tym samym poziomem uprawnień.
- `WM_SETFOCUS` umożliwił reakcję na przyciski na drugim pulpicie, lecz wywołał
  globalne skutki uboczne dla kursora.
- Pełna izolacja wymaga osobnej sesji wejścia, np. maszyny wirtualnej, drugiego
  komputera albo rozwiązania oficjalnie obsługiwanego wewnątrz klienta.

### Druga próba: opcja „Kopanie w tle” (26.10.2026)

Status: opcjonalny checkbox w Kopaczu, domyślnie wyłączony; wymaga testu w grze.

Różnice względem pierwszej próby (`Services/BackgroundGameWindow.cs`,
`MainWindow.BackgroundMining.cs`):

- Tryb włącza się dopiero, gdy Minecraft traci fokus w trakcie pracy Kopacza,
  i kończy po powrocie do okna gry. Bindy w tym czasie są ignorowane.
- `WM_ACTIVATE` + `WM_SETFOCUS` są wysyłane jednorazowo przy przejściu w tło
  (i raz po 500 ms), a nie przy każdym poleceniu.
- `WM_MOUSEMOVE` trafia do gry tylko przy otwartym GUI (EQ, menu, skrzynia).
  W grze LPM/PPM idą z pozycją środka okna, bez ruchu kursora.
- Skok kursora do środka okna gry (przejęcie myszy przez LWJGL) jest cofany
  w następnym ticku, a `ClipCursor` ograniczony do okna gry jest zdejmowany.
- Tekst czatu idzie jako `WM_CHAR`, obraz przez `PrintWindow`
  (`PW_RENDERFULLCONTENT`), a otwarte GUI rozpoznają znaczniki texturepacka.
- Zminimalizowana gra jest sterowana „na ślepo”; akcje wymagające obrazu
  (EQ, drop, CobbleX, reconnect) czekają na przywrócenie okna.

### Kradzież fokusu przy najechaniu myszą (testy 27–28.10.2026)

W próbach w grze kopanie działało podczas korzystania z innych aplikacji, ale
najechanie na grę lub Kopacza powodowało przejęcie pierwszego planu przez Minecrafta.
Ochrona dodana w Claude sprawdzała `GetAsyncKeyState` dla LPM/PPM/Alt/Win.
W ostatnim logu (`00:57:49.446` i `00:58:15.356`) LPM miał stan DOWN, więc kod
uznał przejęcie fokusu za świadome i zakończył sesję tła (`restore=True`).
Nie ustalono jeszcze, skąd pochodził ten stan LPM.

Poprawka z 28.10 zastępuje ten warunek niedawnym zdarzeniem Raw Input:
kliknięciem w grę/pasek zadań albo skrótem przełączania okien/pulpitów.
Ruch myszy, sam trzymany przycisk i kliknięcia w inne aplikacje nie dają zgody
na przejście do zwykłego sterowania. Zapamiętywane jest również okno Kopacza.
Nieudane przywrócenie fokusu utrzymuje sesję tła; udane ponownie inicjuje wejście
LWJGL, które otrzymuje `WM_KILLFOCUS` przy oddaniu pierwszego planu.
Gdy aktywny jest sam Kopacz, `LockSetForegroundWindow` chroni jego pierwszy plan
podczas wysyłania fałszywego fokusu. Rejestracja Raw Input trwa tylko przez sesję tła
i nie blokuje wejścia innych aplikacji ani nie instaluje globalnego hooka.

Do sprawdzenia w grze po tej poprawce: hover nad oboma oknami bez klikania,
klikanie i przeciąganie w innych aplikacjach, celowy klik w grę/jej ikonę,
Alt+Tab, powrót po oddaniu fokusu oraz dalszy ruch/kopanie.
Automatyczne testy rozpoznawania intencji nie potwierdzają zachowania BlazingPacka.

Kolejna próba wykazała, że pierwsza poprawka nie wystarczała. Dziennik o 01:14:00
pokazał cykl: oddanie fokusu → ponowny `WM_SETFOCUS` → ponowne przejęcie przez grę.
Odczyt tokenów działających procesów potwierdził `elevated=False` i integralność
Medium zarówno dla Kopacza (PID 31464), jak i Minecrafta (PID 31832).
Systemowe aktywowanie okien przez hover (`SPI_GETACTIVEWINDOWTRACKING`) było wyłączone.

Odczyt natywnych okien potwierdził natomiast, że ukryty HUD Kopacza (`0x410C60`,
wątek 5396 — ten sam co główne okno) miał właściciela Minecrafta (`0x1C09C6`,
wątek 31864). Relacja owner/owned pomiędzy wątkami automatycznie łączy ich kolejki
wejścia, więc taki HUD narusza izolację fokusu całego głównego wątku Kopacza.
Samo ukrycie HUD nie usuwa tej relacji. W edycji Kopacza usunięto powiązanie
z grą i zablokowano je również w setterze HUD (`MINER_EDITION`); wariant PvP
zachowuje wcześniejsze zachowanie. Pozycję i kolejność HUD ustawia istniejący
kod `SetWindowPos` z `SWP_NOACTIVATE`. Natywny test sprawdza właściciela HWND
przed i po próbie jego zmiany. Użytkownik potwierdził poprawę fokusu po tej zmianie.

### Jednorazowy obrót kamery przy START (28.10.2026)

Po poprawce wyboru kanału START użytkownik potwierdził chodzenie i kopanie,
ale zgłosił lekkie obrócenie kamery w prawo od razu po pięciosekundowym odliczaniu,
bez poruszania myszą. Log potwierdził kanał 6/3/3, kierunek 3 i wysyłanie D/S/A/W;
w tym teście nie było przejęcia pierwszego planu przez grę.

Sprawdzono używaną bibliotekę `lwjgl-2.9.4-nightly-20150209.jar`: `WindowsMouse`
sumuje różnice pozycji, a `Mouse.poll()` dodaje je do DX/DY aż do odczytu przez
grę. To możliwe źródło skoku przy pierwszym fałszywym fokusie. Próba w osobnym,
bezokienkowym JVM na rzeczywistych klasach LWJGL odtworzyła zaległe DX/DY=13,-7.
Wyrównanie wirtualnej pozycji do poprzedniego środka kamery wyzerowało je dla
czterech konfiguracji rozmiaru okna (8 prób łącznie). To nie jest pomiar kąta
w działającym BlazingPacku: podgląd okien computer-use był niedostępny.

Poprawka wysyła jeden `WM_MOUSEMOVE` **przed pierwszym** `WM_SETFOCUS` sesji,
bez `SetCursorPos` i bez przesuwania systemowej myszy. Uwzględnia stary rozmiar
okna sprzed `PrepareWindow` oraz odwróconą oś Y LWJGL. Nie ponawia korekty po
rozpoczęciu pracy i pomija ją przy rozpoznanym otwartym EQ/skrzyni. Nieudane
wysłanie fokusu jest ponawiane zamiast zużywać licznik prób.

Diagnostyka: ignorowany `artifacts/diagnostics/MouseBaselineProbe.java`.
Do potwierdzenia w grze po restarcie Kopacza: zachowanie kąta kamery przy START,
chodzenie i kopanie, brak skoku kursora oraz dalsze zachowanie fokusu.

### Niewpisane komendy w tle (28.10.2026)

Log z 01:52:36 pokazał T down/up, wszystkie znaki `/repair` jako WM_CHAR
i Enter. Wysyłanie do kolejki nie było jednak dowodem, że GUI przyjęło tekst.
Bezokienkowy test na używanym JAR LWJGL 2.9.4 odtworzył przypięcie początkowego
`/` do poprzedniego T key-up (po odfiltrowaniu puszczeń zostawało `repair`).
Po opróżnieniu kolejki samo WM_CHAR zachowywało tekst, ale zdarzenia znaków
miały stan UP, co nie odpowiada zwykłemu wpisywaniu z klawiatury.

`Services/BackgroundTextInput.cs` wysyła każdy znak jako
WM_KEYDOWN(VK_PACKET) → WM_CHAR(UTF-16) → WM_KEYUP(VK_PACKET). LWJGL mapuje
VK_PACKET na KEY_NONE, więc nie używamy fizycznych liter, Shift/AltGr ani schowka.
Puszczenie nośnika jest wykonywane również po błędzie wysłania znaku.
Ścieżka jest wspólna dla komend Kopacza, CobbleX, wpłaty złota i reconnectu.
Wejście na pierwszym planie i dotychczasowe czasy etapów pozostawiono bez zmian.

Diagnostyka `artifacts/diagnostics/KeyboardTextProbe.java` sprawdza faktyczny
dekoder LWJGL: 10 prób obejmuje `/repair`, `/bloki all`, `/cx` i tekst Unicode,
z opróżnioną i nieopróżnioną kolejką po otwarciu czatu. Natywna próba Windows
sprawdza, że TranslateMessage dla nośnika nie generuje dodatkowej drukowalnej litery.
Użytkownik nie zaobserwował dokładnego stanu czatu przy błędzie; działanie
pełnej komendy w BlazingPacku nadal wymaga próby po restarcie Kopacza.

### Skok systemowego kursora przy komendzie i kilku kontach (28.10.2026)

Użytkownik potwierdził, że przesuwa się sam kursor; aktywne okno pozostaje bez zmian.
Po przeskoku nad Minecraftem ruch myszy sterował kamerą. W logu z 02:55:01.118
wysłano T do gry, a o 02:55:01.165 stary strażnik odtworzył zapisany punkt 3,224.
Między tymi zdarzeniami nie zarejestrowano przejęcia pierwszego planu.

Stara ochrona rozpoznawała wyłącznie środek własnego okna (±2 px), była wywoływana
przed dalszymi akcjami ticka i zapamiętywała każdą inną pozycję jako ruch użytkownika.
Asynchroniczny skok po otwarciu/zamknięciu czatu mógł zostać zapisany jako nowa baza,
zwłaszcza po małym fizycznym ruchu myszy albo przez proces innego konta.
Zainstalowany LWJGL 2.9.4 ma natywne centrowanie przy grab i odtwarzanie grab_x/grab_y
przy ungrab. Dokładny punkt reakcji BlazingPacka pozostaje do potwierdzenia w grze.

Poprawka uzbraja krótką (800 ms) ochronę przed otwieraniem czatu, Enter/Escape/E,
wirtualnym ruchem/kliknięciem GUI i fałszywym fokusem. Pamięć użytkowej pozycji jest
wspólna dla silników (MMF + mutex, bez plików). Rzeczywiste pakiety Raw Input mogą ją
aktualizować; próbkowanie samej pozycji Windows podczas aktywnej ochrony jej nie nadpisuje.
Duży skok połączony z małym ruchem urządzenia jest rozpoznawany heurystycznie i zachowuje
przesunięcie fizyczne. Ruch bezwzględnego urządzenia i świadomy wybór gry są respektowane.

Dotychczasowy obserwator Raw Input działa teraz na osobnym wątku z własną pętlą
komunikatów i timerem 5 ms, niezależnie od OCR/PrintWindow głównego wątku. To nadal
jedna rejestracja myszy/klawiatury na proces. Właściciel rejestracji zapobiega jej
usunięciu przez spóźnione zamknięcie starszej sesji. Logowanie korekt jest ograniczone
do jednej na sekundę i wykonywane poza wątkiem wejścia.

Testy obejmują starą pozycję inną niż środek, skok z ruchem >2 px, dwa konta,
zdublowane/stare pakiety, zegar TickCount, rzeczywisty drugi proces ze wspólnym MMF
oraz świadome przejęcie gry. Nie wysyłają wejścia ani nie przestawiają kursora pulpitu.
Dokumentacja formatu pozycji komunikatu: [GetMessagePos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getmessagepos).
Pełna próba po restarcie: dwa konta z komendą, Auto EQ/CX oraz jednoczesne korzystanie
z przeglądarki; brak skoku kursora i dalsze działanie ręcznego kliknięcia w grę.

## Auto reconnect i powrót do kopania

Status: aktywny prototyp w zakładce `Experimental` (27.09.2026).

Mechanizm działa wyłącznie przy widocznym, aktywnym oknie Minecrafta i nie
otwiera już samodzielnie EQ w osobnym interwale. Kontrola została podpięta pod
zaplanowany cykl Auto EQ. Jeżeli podczas tego cyklu gra trzykrotnie nie otworzy
ekwipunku albo nie uda się rozpoznać jego ekranu, uruchamiana jest maszyna
stanów reconnectu: rozpoznanie ekranu, powrót do listy serwerów, Direct Connect,
komenda home i końcowe potwierdzenie EQ przed wznowieniem kopania.

Auto EQ sprawdza również osobny znacznik diamentowego kilofa w głównym EQ i na
hotbarze. Jego brak nie uruchamia Direct Connect. To osobny flow bezpieczeństwa:
program zamyka EQ, wykonuje home zgodnie z wybranym profilem (z GUI albo bez)
i kończy tylko ten kanał Kopacza, który rozpoczął skan.

Konfiguracja korzysta z osobnych profili serwerów. Profil zapisuje nazwę,
adres Direct Connect, pełną komendę home, czasy oraz limity prób. Jeśli home
nie otwiera GUI, automat wysyła wyłącznie zapisaną komendę, np.
`/home kopalnia`. Jeśli serwer pokazuje GUI, profil przechowuje czas
oczekiwania, liczbę rzędów i kolumn oraz wybrane pole. Edytor pozwala dodawać
kolumny z prawej strony i rzędy od dołu, więc układ nie jest ograniczony do
3 × 9. Po zakończeniu ustawionego czasu teleportacji automat czeka dodatkową
sekundę przed otwarciem kontrolnego EQ.

Po wykryciu ekranu rozłączenia automat odczekuje 7 sekund przed użyciem
`Back to Server List` albo `Reconnect`. BlazingPack potrafi blokować przycisk
powrotu przez około 5 sekund i ogranicza kolejne połączenie przez około
6 sekund; dodatkowa sekunda zapasu zapobiega klikaniu na granicy timeoutu.

Rozpoznawane etykiety `[MH]` są identyczne w `en_US.lang` i `pl_PL.lang`.
Oryginalna przyczyna rozłączenia pozostaje widoczna. Wykrycie bana albo
nieznanego ekranu zatrzymuje procedurę bez dalszego klikania.

Zalecana kolejność testów:

1. `Rozpoznaj ekran` na każdym ekranie rozłączenia, liście serwerów i Direct
   Connect.
2. `Sprawdź EQ` w grze z aktywną paczką, diamentowym kilofem i `GUI Scale: Large`.
3. `Test /home` z wybranym profilem i właściwym polem jego układu GUI.
4. `Pełny reconnect` dopiero po zaliczeniu wcześniejszych etapów.

