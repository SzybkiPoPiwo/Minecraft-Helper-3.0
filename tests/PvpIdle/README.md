# Auto Clicker — spoczynek i analiza kursora

```powershell
dotnet run --project .\tests\PvpIdle\PvpIdle.csproj -c Release
```

Test linkuje produkcyjną bramkę spoczynku oraz analizator pikseli kursora.
Nie tworzy okien, nie rejestruje hooków, nie wysyła wejścia, nie przechwytuje
ekranu i nie czyta ustawień użytkownika.

- Symuluje godzinę spoczynku przy zaznaczonych LPM, PPM, Auto zbroi, BINDY i HUD:
  240 000 kroków po 15 ms, bez ciągłego zatrzymywania harmonogramu i bez pracy HUD.
- Osobno symuluje godzinę spoczynku w zasobniku przed sprawdzaniem okna/fokusu.
  Sprawdza wyjście do normalnych kontroli po bindzie i podczas aktywnej sekwencji,
  oznaczenie fokusu jako nieznanego i zachowanie szybkości odczytu bindów.
- Sprawdza wyjście ze spoczynku dla bindów, obsługę puszczenia i drugiego naciśnięcia,
  świeży odczyt kursora przed aktywacją oraz zachowanie wszystkich aktywnych etapów.
- Weryfikuje, że ukryte/minimalizowane okno nie odświeża kontrolek, a poprzedni HUD
  może zostać schowany bez powtarzania operacji dla pustego panelu.
- Porównuje zbiorczy odczyt bitmap z poprzednim algorytmem pikselowym, w tym
  pojedyncze widoczne piksele, przezroczystość, krawędzie i rozmiary 1–128 px.

To testy pracy w spoczynku i zachowania ochrony GUI, nie odtworzenie skoku kamery
w Minecraft. Przyczynę zgłoszonego freeze'u trzeba potwierdzić dłuższą próbą w grze.
Do porównania zamknij poprzednią instancję w zasobniku i uruchom poprawiony projekt;
uruchomienie drugiej kopii tylko przywróci już działającą instancję.
