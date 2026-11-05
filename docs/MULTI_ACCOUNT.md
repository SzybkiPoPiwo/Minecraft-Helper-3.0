# Wiele kont, podgląd Live i sterowanie Kopaczem

## Założenia

Wszystkie konta obsługuje się z jednego widocznego okna i jednej ikony w zasobniku.
Panel `Konta` pozwala wybrać konto do konfiguracji, przypisać Minecrafta, uruchomić
kopanie i obserwować pozostałe sesje. Wybrane konto pokazuje pełną konfigurację
w tym samym miejscu; pozostałe dalej pracują w tle ze swoimi ustawieniami i logami.

Silniki kont pozostają w osobnych, ukrytych procesach, dzięki czemu stan kopania,
wejście i przechwytywanie obrazu są rozdzielone. Nie trzeba uruchamiać ich ręcznie.
Nie mają dodatkowych ikon zasobnika, splash screenów ani okien przy starcie.

## Uruchamianie

Otwórz sekcję `Konta` w pasku bocznym Kopacza. W panelu można:

- dodać i nazwać profil (nowa nazwa trafia od razu do działającego Kopacza);
- wybrać konto do edycji jego pełnej konfiguracji, bez dodatkowego okna;
- przypisać profilowi okno Minecrafta z listy (okna innych kont są oznaczone jako zajęte);
- uruchomić lub zatrzymać kopanie na wybranym koncie;
- uruchomić wszystkie bezczynne konta (co 1,5 s) albo zatrzymać wszystkie;
- obserwować stan, czas pracy, statystyki i podgląd Live (10, 20 albo 30 kl./s; powiększony podgląd do 60 kl./s);
- kliknąć podgląd, aby go powiększyć (cały ekran, hotbar z jedzeniem, ekwipunek);
- usunąć zatrzymany profil razem z jego ustawieniami i logami (po wybraniu innego konta).

START oraz „Uruchom wszystkie” przygotowują silniki kont automatycznie. Wybór konta
nie zatrzymuje pozostałych sesji ani nie kopiuje ich ustawień. Kliknięcie nazwy konta
w pasku bocznym wraca do listy kont. Przywrócenie z zasobnika i ponowne uruchomienie
EXE bez argumentów pokazuje ostatnio wybrane konto. X ukrywa panel, a „Zamknij Kopacza”
w menu jedynej ikony zasobnika zatrzymuje i zamyka wszystkie silniki.

Awaryjny powrót bez kilofa kończy wyłącznie pracę tego konta; inne konta pozostają
aktywne. Awaria procesu głównego powoduje zakończenie jego ukrytych silników.

## Jedno okno — jedno konto

Każdy Kopacz zajmuje okno gry, z którym pracuje (nazwany obiekt Windows przypisany do
PID gry, zwalniany razem z procesem Kopacza). Inny profil nie użyje zajętego okna:
nie dopasuje do niego fokusu, bindów ani automatycznego wyszukiwania okna.

Dopóki zapisany proces gry działa, fokus jest rozpoznawany wyłącznie po jego PID —
kilka klientów `javaw` nie jest już mylonych ze sobą. Dopasowanie po nazwie procesu
lub tytule działa tylko po restarcie gry (gdy zapisany PID wygasł) i pomija okna
innych kont.

Panel przypisuje wolne okna kontom, które nie mają żadnego: po otwarciu panelu,
po kliknięciu „Odśwież okna” oraz zaraz po otwarciu profilu z panelu.

## Start spoza gry

START w głównym oknie i panelu Konta wybiera **6/3/3**, jeśli ten kanał jest
włączony — również wtedy, gdy włączony jest także 5/3/3. Brak kierunku 6/3/3
blokuje start i wymaga uzupełnienia konfiguracji. Kanał **5/3/3** (kopanie
w miejscu, LPM + Shift) uruchamia się z panelu, gdy 6/3/3 jest wyłączony.
Podsumowanie w sekcji Kopacz i odliczanie pokazują uruchamiany kanał.
Oba kanały można nadal uruchamiać ich własnymi bindami w grze.

START z panelu działa od razu, gdy okno gry profilu jest na wierzchu. W innym wypadku
wymaga włączonego „Kopania w tle” w tym profilu — Kopacz rozpoczyna wtedy sesję w tle
przed pierwszym wciśnięciem klawisza, więc nic nie trafia do aplikacji, której
używasz. Okno gry nie może być zminimalizowane (przenieś je na inny pulpit, Win+Tab).

STOP z panelu działa tak samo jak STOP z bindu: zwalnia klawisze, przerywa Auto EQ,
reconnect i oczekujące wznowienie oraz zamyka wpis w logach. Nie wysyła ESC.

Konto można też wybrać przy uruchamianiu programu (przełącza wspólny panel):

```powershell
MinecraftHelper.Miner.exe --profile identyfikator-profilu
```

## Rozmieszczenie okien i HUD-ów

Przy rozpoczęciu kopania w tle każde konto dostaje osobne miejsce na okno gry.
Okna układają się obok siebie, potem w kolejnych rzędach i na następnych monitorach.
Rozmiar obszaru gry wynosi maksymalnie 854×480; na mniejszych ekranach jest
zmniejszany tak, żeby ramki mieściły się w obszarze roboczym. Typowy monitor Full HD
mieści cztery okna. Pełny ekran F11 pozostaje bez zmian — do układania użyj trybu okna.

Miejsce pozostaje przypisane przez pauzę/STOP i powrót do gry. Zwolnienie następuje
po zmianie przypisanego okna, zamknięciu gry lub procesu profilu (również po awarii).
Nowe konto zajmuje wolne miejsce bez przesuwania już pracujących gier. Gdy wszystkie
miejsca są zajęte, START zgłasza brak miejsca i nie uruchamia wejścia do gry.

HUD-y kont uwzględniają swoje rzeczywiste rozmiary i ustawiony monitor/narożnik:
zajmują najpierw kolejne kolumny, potem rzędy, z odstępem między oknami.
Tytuł karty zawiera nazwę konta. Przy zmianie treści/rozmiaru układ jest sprawdzany
ponownie. Jeśli cały HUD nie mieści się na wybranym monitorze, jest ukrywany,
a Kopacz zgłasza możliwość wyboru innego monitora w ustawieniach. Po zwolnieniu
miejsca wraca automatycznie. Rozmieszczanie nie aktywuje gry ani HUD-u i nie ustawia
właściciela HUD-u na Minecrafta, więc nie łączy ich kolejek wejścia.

Przy przejściu ze starszej wersji trzeba zamknąć **wszystkie stare profile**, potem
uruchomić nowy EXE jeden raz. Silniki pozostałych kont będą uruchamiane automatycznie.

## Dane profili

Lista kont znajduje się w:

```text
%APPDATA%\Minecraft Helper\Kopacz\accounts.json
```

Profil główny dla zgodności używa dotychczasowych plików. Profile dodatkowe zapisują
dane osobno:

```text
%APPDATA%\Minecraft Helper\Kopacz\Profiles\<id>\settings.json
%APPDATA%\Minecraft Helper\Kopacz\Profiles\<id>\mining-logs.json
```

Przy pierwszym uruchomieniu nowy profil otrzymuje kopię konfiguracji profilu głównego,
ale bez zapisanego okna gry. Każdemu profilowi trzeba więc wskazać właściwy proces
Minecrafta.

## Podgląd Live

Podgląd korzysta z lokalnego przechwycenia obrazu wybranego okna Minecrafta. Nie
przejmuje myszy, nie aktywuje gry i nie wysyła obrazu do internetu. Przechwycenie oraz
kodowanie JPEG odbywa się poza wątkiem interfejsu Kopacza. Wyłączenie przełącznika
Live usuwa podglądy i pozostawia samo lekkie sprawdzanie statusu.

## Lokalne polecenia

Każdy proces udostępnia nazwany kanał Windows dostępny tylko dla bieżącego użytkownika:

```text
MinecraftHelper.Miner.<id-profilu>
```

Kanał przyjmuje wyłącznie polecenia z białej listy. Podstawowe tylko odczytują stan, zwracają obraz albo zatrzymują pracę:

| Polecenie | Działanie |
|---|---|
| `status` | stan profilu, tryb kopania, czas pracy i okno gry |
| `stats` | statystyki z logów: kopania, EQ, CobbleX, wyrzucone przedmioty |
| `screenshot` | pełny podgląd okna Minecrafta |
| `food` / `jedzenie` | hotbar z paskami życia i jedzenia (według skali GUI) |
| `inventory` / `ekwipunek` | wykryte GUI ekwipunku na żywo; gdy jest zamknięte — ostatni skan EQ z jego wiekiem (bez żadnego skanu: cały ekran z adnotacją) |
| `stop` | natychmiastowe zatrzymanie wskazanego Kopacza |

Polecenia sterujące: `start [tryb]`, `join [tryb]` (wejście na serwer jak Auto Reconnect, potem
kopanie), `leave` (wyjście z serwera), `window <pid>`, `preview` (mały obraz do kart), `show`,
`reload`, `arrange` (kalibracja okna) i `clear-logs`. Kanał jest dostępny tylko dla
bieżącego użytkownika Windows, więc korzystają z niego wyłącznie lokalne procesy: panel kont
i bot Discord działający w głównym procesie Kopacza. Bot wysyła tylko polecenia odpowiadające
jego komendom (`status`, `screenshot`, `jedzenie`, `ekwipunek`, `start`, `stop`,
`join`, `leave`); `window`, `arrange` czy `clear-logs` są z Discorda niedostępne.

Wspólny panel koordynuje wybór i zamykanie kont przez dodatkowy kanał
`MinecraftHelper.Miner.Workspace`, dostępny tylko bieżącemu użytkownikowi Windows.
Polecenia `workspace-hide`, `workspace-show` i `workspace-close` są obsługiwane
wyłącznie przez lokalne kanały silników, poza publiczną listą komend.

Kanał nie obsługuje poleceń powłoki, uruchamiania programów ani dowolnych komend gry.
Klient przerywa rozmowę po 5 s, więc zawieszony Kopacz nie blokuje panelu.

## Bot Discord

Model: **bot lokalny** — użytkownik tworzy własną aplikację Discord i wkleja jej token
w Ustawieniach → Bot Discord. Nie ma serwera pośredniego. Przycisk **Jak skonfigurować?**
w karcie otwiera poradnik krok po kroku (`DiscordSetupGuideWindow`) z linkiem do Developer
Portal i przyciskiem kopiującym link zaproszenia: ID aplikacji pochodzi z zapisanego tokenu,
uprawnienia to `101376` (wyświetlanie kanałów, wysyłanie wiadomości, pliki, historia czatu).

- Ustawienia są wspólne dla wszystkich kont: `%APPDATA%\Minecraft Helper\Kopacz\discord.json`.
  Token jest zaszyfrowany DPAPI (tylko bieżący użytkownik Windows) i nie trafia do
  `settings.json`, logów ani repozytorium.
- **Komendy** odbiera jeden bot (Gateway, `DiscordBotHost`) w głównym procesie Kopacza
  i przekazuje je kontom przez kanały `MinecraftHelper.Miner.<id-profilu>`. Wymaga
  włączonego „Message Content Intent”. Komendy przyjmowane są tylko od wpisanych
  właścicieli, na wybranym kanale albo w wiadomości prywatnej.
- **Powiadomienia** (`DiscordNotifier`) wysyła każdy proces konta samodzielnie przez REST:
  alarm ochrony, reconnect, brak kilofa, start/koniec kopania, błędy — opcjonalnie ze
  zrzutem ekranu i oznaczeniem właścicieli. Ten sam komunikat najwyżej raz na 30 s.
- Wszystkie wiadomości (powiadomienia i odpowiedzi na komendy) to karty Discorda
  (`DiscordEmbed`): kolor paska według wagi zdarzenia, tytuł z ikoną, konto w nagłówku,
  pola w kolumnach, zrzut wewnątrz karty, stopka z godziną. Oznaczenie trafia do tekstu
  nad kartą — tylko ono budzi telefon.
- **Alarm obrażeń** wysyła jedno powiadomienie z oznaczeniem po próbie Disconnect:
  „wyszedłem z serwera” (z informacją o reconnekcie albo jego braku) lub „nie udało się
  wyjść”. Zdarzenia dziennika z tej chwili nie dublują go. Test alarmu (tryb testowy)
  wysyła wiadomość „TEST alarmu obrażeń” — służy do sprawdzenia oznaczenia. Nieudany
  reconnect także oznacza właścicieli.

| Komenda | Działanie |
|---|---|
| `!pomoc` | lista komend |
| `!status [konto]` | stan kont (bez nazwy: wszystkie) |
| `!stats [konto]` | liczniki z całej historii konta (sesje, EQ, CobbleX, wyrzucone) |
| `!ss [konto]` | zrzut ekranu gry |
| `!jedzenie [konto]` | hotbar z życiem i jedzeniem |
| `!eq [konto]` | ekwipunek (ostatni skan EQ) |
| `!dane [konto]` | z dziennika konta: ostatnia sesja (czas, EQ, CobbleX, wyrzucone przedmioty, stan ostatniego EQ, wyjścia z serwera i reconnecty) oraz suma z dzisiaj |
| `!start [konto] [tryb]` (tryb: `5/3/3` albo `6/3/3`) | uruchomienie kopania (gdy gra nie jest na wierzchu — tylko w tle); bez nazwy — jedyne gotowe konto; `!start wszystkie`. Tryb na końcu wybiera kanał (odznaczony zostanie zaznaczony w profilu); bez trybu — kanał z profilu, przy obu 6/3/3 |
| `!stop [konto]` | zatrzymanie kopania; bez nazwy — wszystkie konta |
| `!join [konto] [tryb]` | wejście na serwer przebiegiem Auto Reconnect (lista serwerów, Direct Connect, `/home`, kontrola EQ), potem start kopania jak `!start`. Wymaga profilu powrotu i — gdy gra nie jest na wierzchu — kopania w tle. Bez nazwy: jedyne pasujące konto; `!join wszystkie`. Wynik (start albo powód przerwania) przychodzi od konta |
| `!leave [konto]` | zatrzymanie kopania i wyjście z serwera (ESC → Disconnect); przerywa też trwający reconnect. Wynik przychodzi od konta |

Każdą komendę poza `!pomoc` można wyłączyć w `Ustawienia` → `Bot Discord` → `Włącz / wyłącz komendy`. Lista wyłączonych trafia do `discord.json` (`DisabledCommands`); bot odpowiada na nie „Komenda wyłączona” i pomija je w `!pomoc`.

Wpisywanie komend gry z Discorda jest celowo niedostępne.

## Test regresji

```powershell
dotnet run --project .\tests\MultiAccount\MultiAccount.csproj -c Release
```

Test sprawdza izolację identyfikatorów profili, wybór konta, komendy obrazów, STOP
jednego profilu, odrzucanie poleceń spoza białej listy, niedostępność poleceń
lokalnych z zewnątrz oraz zajmowanie okna gry przez jeden profil. Obejmuje też
przełączanie jednego widocznego okna, zachowanie ustawień i położenia, anulowanie,
odzyskanie starego widoku po błędzie i zatrzymanie wszystkich sesji przy wyjściu.
