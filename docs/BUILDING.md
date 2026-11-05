# Samodzielne sprawdzenie i budowanie Minecraft Helper

Ten poradnik pozwala zbudować aplikację bez uruchamiania gotowego instalatora. Wszystkie polecenia wykonuj w PowerShellu uruchomionym w katalogu głównym repozytorium.

## Co znajduje się w repozytorium

- `MinecraftHelper/` — kod aplikacji `Minecraft Helper Kopacz`,
- `MinecraftHelper.Pvp/` — kod lekkiej aplikacji `Minecraft Helper Auto Clicker`,
- `MinecraftHelper/Assets/` — ikona i zasoby programu,
- `Installer/` — definicja instalatora Inno Setup,
- `scripts/` — skrypty testowania, publikowania i budowania instalatora,
- `tests/` — testy regresji obu aplikacji.

Paczki zasobów do Auto EQ i CobbleX nie ma w repozytorium — Kopacz tworzy ją sam z plików gry użytkownika (`MinecraftHelper/Services/MarkerResourcePack.cs`).

## Wymagane narzędzia

Do zbudowania samej aplikacji potrzebujesz:

1. Windows 10 lub Windows 11.
2. [.NET SDK](https://dotnet.microsoft.com/download/dotnet) obsługujący rozwiązania `.slnx` (sprawdzone na SDK `10.0.402`). Obie aplikacje nadal mają docelowe środowisko `.NET 8`.
3. Edytora, np. [Visual Studio Code](https://code.visualstudio.com/) z rozszerzeniem C# Dev Kit, Visual Studio 2022 z pakietem `.NET desktop development` albo Ridera.
4. [Git](https://git-scm.com/download/win) — opcjonalnie, jeśli zamiast `git clone` pobierzesz repozytorium jako ZIP.

Dodatkowe narzędzia:

- [Inno Setup 6](https://jrsoftware.org/isdl.php) — tylko do utworzenia instalatora,
- Minecraft 1.8.8 zainstalowany z oficjalnego launchera (`%APPDATA%\.minecraft\versions\1.8.8\1.8.8.jar`) — do testu `GoldDeposit`, który sprawdza detektory na prawdziwych teksturach z paczki budowanej przez Kopacza.

Pobieraj narzędzia z ich oficjalnych stron, nie z przypadkowych serwisów z programami.

## 1. Pobranie źródeł

Za pomocą Git:

```powershell
git clone https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0.git
cd Minecraft-Helper-3.0
```

Możesz również pobrać ZIP z GitHuba, rozpakować go i otworzyć PowerShell w rozpakowanym katalogu.

## 2. Sprawdzenie środowiska i zależności

```powershell
dotnet --version
dotnet restore .\MinecraftHelper.slnx
dotnet list .\MinecraftHelper\MinecraftHelper.csproj package
dotnet list .\MinecraftHelper.Pvp\MinecraftHelper.Pvp.csproj package
dotnet list .\MinecraftHelper\MinecraftHelper.csproj package --vulnerable --include-transitive
dotnet list .\MinecraftHelper.Pvp\MinecraftHelper.Pvp.csproj package --vulnerable --include-transitive
```

Dwa ostatnie polecenia sprawdzają znane podatności pakietów NuGet i wymagają połączenia z internetem.

## 3. Kompilacja bez instalatora

```powershell
dotnet build .\MinecraftHelper.slnx -c Release
```

Gotowe aplikacje znajdą się w:

```text
MinecraftHelper.Pvp\bin\Release\net8.0-windows\MinecraftHelper.AutoClicker.exe
MinecraftHelper\bin\Release\net8.0-windows\MinecraftHelper.Miner.exe
```

Możesz je uruchomić bez instalowania:

```powershell
.\MinecraftHelper.Pvp\bin\Release\net8.0-windows\MinecraftHelper.AutoClicker.exe
.\MinecraftHelper\bin\Release\net8.0-windows\MinecraftHelper.Miner.exe
```

Możliwe jest też uruchomienie bezpośrednio przez SDK:

```powershell
dotnet run --project .\MinecraftHelper.Pvp\MinecraftHelper.Pvp.csproj -c Release
dotnet run --project .\MinecraftHelper\MinecraftHelper.csproj -c Release
```

Kopacza dla konkretnego profilu konta można uruchomić także bez panelu. Identyfikator
profilu musi odpowiadać wpisowi w `accounts.json`:

```powershell
dotnet run --project .\MinecraftHelper\MinecraftHelper.csproj -c Release -- --profile konto-2
```

## 4. Samodzielna publikacja wersji przenośnej

Poniższe polecenia tworzą obie aplikacje `win-x64` razem z wymaganym środowiskiem .NET 8:

```powershell
dotnet publish .\MinecraftHelper.Pvp\MinecraftHelper.Pvp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\publish\autoclicker\win-x64

dotnet publish .\MinecraftHelper\MinecraftHelper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\artifacts\publish\miner\win-x64
```

Uruchamiane pliki będą dostępne tutaj:

```text
artifacts\publish\autoclicker\win-x64\MinecraftHelper.AutoClicker.exe
artifacts\publish\miner\win-x64\MinecraftHelper.Miner.exe
```

Publikacja Kopacza `win-x64` pomija nieużywane biblioteki OCR `x86/`. Folder `x64/` i `tessdata/` muszą pozostać przy aplikacji — nie usuwaj ich z wersji przenośnej. Do publikacji użyj nowego katalogu albo skryptu instalatora, który usuwa pozostałości `x86/` po starszym publikowaniu.

## 5. Zbudowanie własnego instalatora

Zainstaluj Inno Setup 6, zamknij działające kopie Minecraft Helper i uruchom:

```powershell
.\scripts\build-installer.ps1 -Version 3.0 -Rid win-x64 -SelfContained $true -Clean
```

Jeżeli PowerShell blokuje lokalny skrypt, możesz zezwolić na jego wykonanie tylko w bieżącym oknie terminala:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

Następnie ponownie uruchom skrypt budowania. Wynik znajdzie się w:

```text
artifacts\installer\MinecraftHelper-Setup-3.0.exe
```

Jeden instalator zawiera dwa niezależne składniki. Użytkownik może wybrać Auto Clicker, Kopacza albo oba programy.

Parametr `-Clean` usuwa wyłącznie wcześniejsze wyniki z `artifacts/publish` i `artifacts/installer`, po czym buduje je od początku.

## 6. Sprawdzenie wyniku

Oblicz sumę SHA-256 własnego pliku:

```powershell
Get-FileHash .\artifacts\installer\MinecraftHelper-Setup-3.0.exe -Algorithm SHA256
```

Przed udostępnieniem programu warto również:

```powershell
git status --short
git diff --check
dotnet build .\MinecraftHelper.slnx -c Release
.\scripts\test.ps1
```

Sprawdź ręcznie uruchamianie obu aplikacji, wybór procesu Minecrafta, zapis ustawień, bindy, overlay oraz zamykanie programów. W instalatorze wykonaj po jednej próbie instalacji każdego składnika osobno i obu razem. Sam fakt, że kod się kompiluje, nie potwierdza jeszcze poprawności wszystkich funkcji.

Skrypt testów zatrzyma się na pierwszym błędzie. Parametr `-NoRestore` pomija przywracanie pakietów, jeśli wszystkie projekty testowe zostały już zbudowane. Testy nie wysyłają wejścia ani nie modyfikują ustawień użytkownika; `PvpMouseTracker` na krótko rejestruje prawdziwy hook Windows. `MinerIsolation` sprawdza brak hooków i starych usług clickera w skompilowanym Kopaczu oraz migrację ustawień. `PvpIdle` symuluje godzinę czuwania, testuje powrót do obsługi bindów i zbiorczy odczyt pikseli kursora bez otwierania okna ani przechwytywania ekranu. `MiningStop` sprawdza zatrzymywanie Kopacza, Auto Reconnect, bota Discord i paczkę zasobów, a `MultiAccount` — profile kont, komendy między procesami i układ okien.

Zestaw `GoldDeposit` (uruchamiany także przez `scripts/test.ps1`) sprawdza wpłatę złota: maszynę stanów, odrzucenie i odłożenie stosu, znaczniki obu typów złota z paczki budowanej przez Kopacza z `1.8.8.jar` (wymaga zainstalowanej gry 1.8.8), siatki menu, hotbar i ustawienia. Nie wykonuje wpłat na serwerze; [pierwszą próbę](../tests/GoldDeposit/README.md) przeprowadź ręcznie z małą ilością złota.

## 7. Paczka zasobów

Paczki nie buduje się ręcznie i nie trzyma w repozytorium. Kopacz tworzy ją sam:

- przy starcie (główny proces), gdy w `%APPDATA%\.minecraft\resourcepacks` brakuje paczki albo jest starsza niż `GeneratorVersion`,
- na żądanie: **Ustawienia → Paczka zasobów → Utwórz paczkę ponownie**.

Źródłem jest `versions\1.8.8\1.8.8.jar` gry użytkownika (a gdy go brak — inna wersja 1.8.8 z teksturami, np. BlazingPack). Generator (`MinecraftHelper/Services/MarkerResourcePack.cs`, instalacja: `ResourcePackInstaller.cs`):

1. kopiuje domyślne tekstury, modele, stany bloków i teksty 1.8.8 — detektory rozpoznają domyślne cyfry stacków, ramki ekwipunku i skrzyni, więc paczka innego wyglądu pod spodem nie może ich zmienić,
2. nakłada znaczniki 3×3 na 11 przedmiotów do wyrzucania i diamentowy kilof, znaczniki narożników na `inventory.png`, jednolite kolory pięciu bloków i różowy chroniony cobblestone,
3. dodaje etykiety `[MH]` (en_US, pl_PL), dźwięki obrażeń `hit1`–`hit4`, ikonę i manifest `minecraft-helper-markers.json` z numerem wersji,
4. sprawdza zakodowane znaczniki i zapisuje ZIP przez plik tymczasowy.

Po zmianie znaczników albo zawartości podnieś `GeneratorVersion` — Kopacz odbuduje paczki u użytkowników przy następnym starcie. Test `MiningStop` sprawdza generator na sztucznych teksturach, a `GoldDeposit` — detektory na paczce zbudowanej z prawdziwego `1.8.8.jar`.

## Korzystanie z asystenta AI

W Visual Studio Code możesz poprosić asystenta AI o wykonanie instrukcji krok po kroku, na przykład:

> Przeczytaj `docs/BUILDING.md`, sprawdź wymagane narzędzia i pomóż mi zbudować aplikację bez uruchamiania instalatora. Wyjaśnij każde polecenie przed jego wykonaniem.

Nie zatwierdzaj automatycznie poleceń usuwających pliki ani wyłączających zabezpieczenia systemu. Asystent AI może pomóc w analizie, ale nie zastępuje samodzielnego przeglądu kodu lub audytu bezpieczeństwa.
