# Wpłata złota — testy i pierwsza próba

```powershell
dotnet run --project .\tests\GoldDeposit\GoldDeposit.csproj -c Release
```

Zestaw linkuje produkcyjną maszynę stanów, detektor znaczników i modele ustawień.
Korzysta z prawdziwych tekstur złota z dołączonej paczki ZIP, ale wyłącznie w
syntetycznych bitmapach. Nie otwiera okien, nie przechwytuje ekranu, nie wysyła
wejścia i nie czyta/zapisuje AppData ani logów użytkownika.

Sprawdza polecenia i opóźnienia, oba rodzaje złota, różne ilości oraz sloty EQ i
hotbara, kolejność bloków/sztabek, odrzuconą wpłatę i jedną próbę odłożenia do
źródła, nieznane GUI/kursor, niezgodne ilości, limity czasu i zapis ustawień.
Siatki skrzyni są testowane dla 1/3/6 rzędów i skali 1–4. Kontrola resetu używa
niezainicjalizowanego obiektu prawdziwej aplikacji — bez konstruktora okna.
Osobne próby korzystają z rzeczywistej tekstury `generic_54.png` z ZIP-a dla
3/5/6 rzędów, z zaokrąglaniem wymiarów takim jak w Minecraft i przesunięciem
menu względem środka obrazu. Sprawdzają ramkę nagłówka, automatyczny odczyt
wysokości niezależny od siatki wyboru oraz rozróżnienie błędu siatki, odczytu
tytułu i znacznika ikony wpłaty.
Osobna macierz rysuje stosy 1–64 z prawdziwymi teksturami złota i czcionki
`ascii.png` na tle slotu i jasnego nagłówka, w skalach 1–3. Kontrola przed
kliknięciem sprawdza ruch myszy, zmianę okna i zamknięcie GUI; dopuszcza zmianę
licznika aukcji w tytule i podpowiedź zasłaniającą sloty. Test czasu serwera
uwzględnia opóźnienie walidacji OCR przed faktycznym kliknięciem.
Panel wpłaty jest też ładowany przez WPF z prawdziwego XAML-a i jego stylów,
bez uruchamiania aplikacji i otwierania okna. Ten test wykrywa brakujące zasoby,
których nie sprawdza sama kompilacja.
Wybór pola na siatce korzysta z produkcyjnej obsługi kliknięć; sprawdzane są
zapis celu i szerokości, podświetlenie, położenie przycisków oraz ograniczenie
wyboru po zmianie liczby rzędów lub kolumn. Węższe siatki używają takiej samej
geometrii wyśrodkowania jak menu home; EQ gracza nadal ma dziewięć kolumn.
Sesja ręcznego testu sprawdza 3-sekundowe odliczanie, niezmienny zestaw ustawień,
fokus, STOP, zmianę okna i konflikt z inną automatyzacją. Test panelu sprawdza
blokadę ustawień oraz obu przycisków startu i dostępność STOP. Nie uruchamia
rzeczywistej wpłaty ani nie wysyła komendy na serwer.
Sprawdzana jest także kolejność etapów w obu normalnych kanałach Kopacza:
wykryte złoto → wpłata → opcjonalne jedzenie → wznowienie aktywnego kanału.

## Pierwsza próba na serwerze

1. Uruchom Kopacza w wersji 3.0 lub nowszej (wcześniejsze instalatory testowe nie mają tej funkcji).
2. Włącz aktualną paczkę Minecraft Helper i Auto EQ. Otwórz zwykłe EQ i sprawdź
   rozpoznawanie tekstur. Wpłata wymaga potwierdzonych znaczników GUI tej paczki.
3. W Auto EQ → Wpłata złota wpisz właściwą komendę i fragment tytułu menu.
   Pole wpłaty wybierz na siatce: po prawej `+ Kolumna` / `− Kolumna` zmieniają
   szerokość, a na dole `+ Rząd` / `− Rząd` wysokość. Dla pokazanego bazaru:
   `Bazar`, 6 rzędów, 9 kolumn, pole 11
   (drugi rząd, druga kolumna). Pole wpłaty musi mieć rozpoznaną ikonę złota.
4. Zatrzymaj kopanie, otwórz menu ręcznie i odsuń kursor poza GUI. Kliknij test
   odczytu menu, wróć do Minecrafta w ciągu 3 s. Test nie wpłaca ani nie klika.
5. Próbę automatyczną wykonaj z małą ilością złota. Na pokazanym serwerze użytkownik
   potwierdził przyjmowanie bloków i sztabek przez ten sam przycisk; oba typy są
   domyślnie zaznaczone. Na innym serwerze sprawdź je osobno. Opcja hotbara dotyczy
   wyłącznie złota.
6. Przy wyłączonym Kopaczu i innych automatach kliknij `Test wpłaty złota` i
   przejdź do Minecrafta w ciągu 3 s. Ten przycisk wykona rzeczywistą wpłatę:
   komenda → menu → przeniesienie złota → weryfikacja → zamknięcie GUI.
   Po teście Kopacz pozostaje wyłączony. `STOP testu` lub bind Kopacza przerywa
   odliczanie i procedurę; przy przerwaniu sprawdź kursor. Zmiana fokusu po starcie
   również zatrzymuje test. Wynik znajdziesz w panelu, HUD i logach.
7. Włącz Kopacza i poczekaj na zwykły skan Auto EQ. Czasy komend mają pozostać
   zamrożone do końca wpłaty i jedzenia. Nie poruszaj myszą podczas procedury.
8. Sprawdź rzeczywiste saldo serwera i EQ oraz HUD/logi. Program potwierdza zmianę
   ikony kursora i ubytek z EQ, nie odczytuje salda banku ani pakietów serwera.

Wybrany bind Kopacza pozostaje przyciskiem STOP. Przy odrzuceniu automat próbuje
odłożyć stos do źródłowego miejsca i zatrzymuje kopanie. Przy nieznanym GUI lub
utracie fokusu nie wykonuje dalszych kliknięć i nie zamyka niepewnego menu;
sprawdź ręcznie kursor. Użytkownik potwierdził poprawne wykonanie ręcznego testu
całej wpłaty na serwerze. Pełny cykl ze skanem i wznowieniem kopania należy
sprawdzić osobno; testy automatyczne nie uruchamiają gry.
