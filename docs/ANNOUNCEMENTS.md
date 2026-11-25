# Komunikaty od autora

Kopacz i Auto Clicker mają **dzwonek „Komunikaty”**. Pokazuje dwie rzeczy:

1. **Nową wersję programu** — zawsze, z listą zmian i linkiem „Pobierz” do strony wydania na GitHubie. To zastąpiło
   dawne okno aktualizacji: nic już nie wyskakuje ani nie blokuje programu, a „Pomiń tę wersję” nie jest potrzebne
   (wystarczy przeczytać komunikat — licznik znika, a komunikat czeka na liście do czasu aktualizacji).
2. **Krótkie wiadomości od autora** (zmiana na serwerze, ważne ostrzeżenie) bez wydawania nowej wersji. Są w jednym
   pliku, [`announcements.json`](../announcements.json), na głównej gałęzi repozytorium.

- Kopacz: wąski rząd „Komunikaty” u góry paska bocznego, z licznikiem nowych.
- Auto Clicker: sama ikona z licznikiem w nagłówku okna, obok chipa okna gry. Przycisk „Sprawdź” w Ustawieniach →
  Aktualizacje sprawdza od razu i pokazuje wynik w dzwonku.
- **Okienko powiadomienia.** Gdy przychodzi nowy komunikat, w prawym dolnym rogu ekranu wyskakuje małe okienko
  „Nowa wiadomość” (to samo, które program pokazuje po ukryciu do zasobnika). Wiadomość od autora i ostrzeżenie
  pokazują od razu swoją treść (do 240 znaków), a nowa wersja mówi krótko, że jest. Okienko:
  - pojawia się **niezależnie od tego, czy okno programu jest aktywne** (także w tle i w zasobniku), ale nie
    przejmuje fokusu ani klawiszy, więc nie wyrywa gry z pełnego ekranu,
  - ma ikonę programu, tag w kolorze poziomu (NOWA WIADOMOŚĆ / OSTRZEŻENIE / WAŻNE / NOWA WERSJA) i ramkę w tym kolorze,
  - znika samo po **3 s**, dla każdego poziomu (w grze nie da się go zamknąć inaczej niż kliknięciem, a ono
    wyrywa z gry, więc nawet alarm zostaje krótko); wskaźnik myszy nad okienkiem wstrzymuje znikanie,
    a pełna treść zostaje na liście w dzwonku,
  - kliknięte przywraca program i otwiera listę; przy kilku nowych komunikatach mówi o najpoważniejszym i podaje,
    ile ich jest,
  - o danym komunikacie odzywa się raz na sesję (do zamknięcia programu), dopóki go nie przeczytasz.
- Sam dzwonek niczego nie przesłania. Po kliknięciu pokazuje się lista; zamknięcie listy (albo
  „Oznacz jako przeczytane”) zdejmuje licznik.
- Dzwonka nie widać, dopóki lista jest pusta (nie ma nowej wersji ani komunikatów).
- Przełącznik „Komunikaty od autora” (Kopacz: Ustawienia; Auto Clicker: Ustawienia → Aktualizacje) wyłącza
  tylko wiadomości z `announcements.json`. Informacja o nowej wersji zostaje zawsze. To jedno ustawienie wspólne
  dla obu programów.

Dzwonek i wiadomości od autora są w programach od wersji **3.0.4**. Starsze wersje tego pliku nie czytają
i nadal pokazują stare okno aktualizacji.

## Jak dodać komunikat

1. Otwórz `announcements.json` na GitHubie (gałąź `main`) i kliknij ołówek.
2. Dopisz obiekt do listy `announcements` (przykłady niżej) i zatwierdź zmianę.
3. Programy pobiorą plik przy następnym starcie albo najpóźniej po 2 godzinach. GitHub może trzymać
   starą wersję pliku jeszcze kilka minut.

Żeby komunikat zniknął, usuń go z pliku albo ustaw `expires`.

## Format

```json
{
  "version": 1,
  "announcements": [
    {
      "id": "2026-12-01-przerwa-serwera",
      "title": "Przerwa na serwerze",
      "text": "Dziś o 20:00 serwer będzie niedostępny przez godzinę.",
      "level": "warning",
      "apps": ["miner", "autoclicker"],
      "minVersion": "3.0.4",
      "maxVersion": "3.0.9",
      "published": "2026-12-01",
      "expires": "2026-12-31",
      "url": "https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0/releases",
      "action": "Pobierz"
    }
  ]
}
```

| Pole | Wymagane | Znaczenie |
|---|---|---|
| `id` | tak | Unikalna nazwa komunikatu: litery ASCII, cyfry i znaki `- _ . :`, do 64 znaków. Po `id` program pamięta, że komunikat został przeczytany, więc **nowa treść = nowe `id`**. |
| `text` | tak | Treść, do 500 znaków; nowe linie są zachowane. To zwykły tekst: znaczniki HTML nie działają. |
| `title` | nie | Tytuł, do 80 znaków, jedna linia. Domyślnie „Komunikat”. |
| `level` | nie | `info` (domyślnie), `warning` albo `alarm`. Wpływa na kolor kropki i licznika. |
| `apps` | nie | Dla kogo: `"miner"` (Kopacz), `"autoclicker"`, `"all"`. Może być jedna nazwa albo lista. Brak pola = oba programy. Pole z nieznaną nazwą ukrywa komunikat przed wszystkimi. |
| `minVersion`, `maxVersion` | nie | Zakres wersji programu, **włącznie** z obu końców (np. „tylko dla 3.0.5 i starszych”). Nieczytelna wersja ukrywa komunikat. |
| `published` | nie | Od kiedy pokazywać (pozwala zaplanować komunikat) i jak sortować listę: najnowsze na górze, najstarsze na dole. Data (`2026-12-01`, od początku dnia UTC) albo z godziną (`2026-12-01T08:30:00Z`). Zalecane zawsze — bez niej komunikat ląduje pod wszystkimi datowanymi. |
| `expires` | nie | Do kiedy pokazywać. Sama data trwa do końca tego dnia (UTC). |
| `url` | nie | Link „Szczegóły”. Tylko `https://github.com/…`; każdy inny adres jest pomijany (komunikat zostaje, bez linku). |
| `action` | nie | Napis na linku, do 30 znaków. Domyślnie „Szczegóły”. |

Uwagi:

- Plik może mieć komentarze `//` i końcowe przecinki, bo edytujesz go ręcznie.
- **Kolejność na liście w dzwonku:** na samej górze zawsze informacja o nowej wersji (jeśli program jest
  nieaktualny), pod nią komunikaty od najnowszego (góra) do najstarszego (dół), według `published`. Kolejność
  wpisów w pliku nie ma znaczenia, dopóki mają daty. Przy tej samej dacie i dla komunikatów bez daty nowszy jest
  ten, który stoi **dalej w pliku**, więc nowe wpisy dopisuj na końcu listy.
- Pokazywane jest najwyżej 10 najnowszych komunikatów; z pliku czytane jest ostatnie 40 wpisów (te dopisane
  najpóźniej); plik większy niż 64 KB jest ignorowany.
- **Błąd w jednym polu ukrywa cały komunikat** (zła data, zła wersja, złe `id`), żeby nigdy nie pokazać go
  niewłaściwemu odbiorcy. Po edycji sprawdź plik w przeglądarce (surowa wersja:
  `https://raw.githubusercontent.com/SzybkiPoPiwo/Minecraft-Helper-3.0/main/announcements.json`).
- Plik z błędem składni jest ignorowany w całości: programy zostają przy ostatniej poprawnej liście.
  Brak pliku (404) oznacza „brak komunikatów”.
- Plik jest publiczny. Nie wpisuj w nim niczego, czego nie chcesz pokazać każdemu.

## Prywatność i bezpieczeństwo

- Program robi dwa zwykłe zapytania GET: do API GitHuba po listę wydań (nowa wersja — jak dotąd, także gdy
  komunikaty są wyłączone) i do `raw.githubusercontent.com` po `announcements.json` (tylko gdy komunikaty są
  włączone). W nagłówku jest tylko numer wersji (`MinecraftHelper/3.0.4`). Nie jest wysyłane nic o koncie,
  ustawieniach ani komputerze.
- Komunikat to wyłącznie tekst. Nie uruchamia niczego i nie pobiera plików. Link otwiera się dopiero po
  kliknięciu i tylko do github.com.
- Procesy-silniki kont Kopacza w ogóle nie pobierają pliku — robi to tylko okno główne.

## Gdzie to jest w kodzie

| Plik | Rola |
|---|---|
| `MinecraftHelper/Services/AnnouncementService.cs` | Parser pliku (filtry, daty, czyszczenie tekstu), pobieranie, stan „przeczytane” i `UpdateAnnouncements` (komunikat o nowej wersji z odpowiedzi GitHuba). Bez WPF. |
| `MinecraftHelper/Services/UpdateCheckService.cs` | Sprawdzanie wydań na GitHubie i czytanie sekcji „### Co nowego” z opisu wydania (`ReleaseBodyParser`). |
| `MinecraftHelper/AnnouncementBell.cs` | Kontrolka dzwonka i lista (wspólna dla obu programów, budowana w kodzie, z motywem). |
| `MinecraftHelper/AnnouncementController.cs` | Sprawdzanie przy starcie i co 2 godziny (nowa wersja zawsze, komunikaty gdy włączone), scalanie na listę. |
| `MinecraftHelper/MainWindow.Announcements.cs`, `MinecraftHelper.Pvp/MainWindow.Announcements.cs` | Podpięcie w oknie każdego programu. |
| `tests/Announcements` | Testy parsera, filtrów i stanu (`dotnet run --project tests\Announcements -c Release`). |

Stan „przeczytane” i przełącznik leżą we wspólnym pliku `%AppData%\Minecraft Helper\announcements-state.json`
(poza ustawieniami konta, żeby przełączenie konta nie przywracało przeczytanych komunikatów).
Adres pliku z komunikatami to stała `AnnouncementService.SourceUrl`.
