# MultiAccount

Sprawdza fundament wielu kont i zdalnego sterowania:

- bezpieczne identyfikatory profili,
- status wielu sesji,
- wybór pojedynczego konta,
- zrzuty pełnego okna, hotbara i ekwipunku,
- ostatni skan EQ zwracany przy zamkniętym ekwipunku (z wiekiem skanu),
- zdalny STOP,
- odrzucanie komend spoza zamkniętej białej listy,
- układ okien na małych/dużych i kilku monitorach, z różnymi ramkami klientów,
- rozmieszczenie HUD-ów w każdym narożniku, z różnymi rozmiarami i zawijaniem rzędów,
- rezerwacje miejsc między procesami oraz automatyczne zwolnienie po awarii,
- wybór konta w jednym widocznym interfejsie, zachowanie ustawień i geometrii okna,
- przygotowanie ukrytego silnika bez zmiany wybranego konta,
- odtworzenie poprzedniego widoku po błędzie/anulowaniu przełączenia,
- wspólne zatrzymanie i zamknięcie silników,
- rzeczywisty lokalny kanał IPC hide/show/close i przekazywanie położenia w JSON.
- wspólną ochronę kursora przy komendzie/EQ: skok z ruchem fizycznym, przyspieszony
  ruch po przekątnej, ręczny wybór gry i rzeczywisty drugi proces ze wspólną pamięcią.

Uruchomienie:

```powershell
dotnet run --project .\tests\MultiAccount\MultiAccount.csproj -c Debug
```

Osobny test natywny HWND między procesami:

```powershell
dotnet run --project .\tests\MultiAccount\MultiAccount.csproj -c Release -- --layout-smoke
```

Tworzy wyłącznie własne okna testowe poza widocznym pulpitem. Sprawdza brak
kolizji, brak aktywacji/ownerów oraz powrót po zwolnieniu miejsca. Nie przesuwa
okien Minecrafta ani Kopacza i nie wysyła wejścia do pulpitu.

