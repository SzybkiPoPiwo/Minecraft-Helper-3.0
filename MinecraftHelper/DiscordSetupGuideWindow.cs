using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MinecraftHelper.Services;

namespace MinecraftHelper;

// Poradnik „Jak skonfigurować?” z karty Bot Discord: pełna instrukcja dla osoby,
// która nigdy nie tworzyła bota. Okno nie jest modalne — można je mieć obok przeglądarki.
internal sealed class DiscordSetupGuideWindow : Window
{
    private const string PortalUrl = "https://discord.com/developers/applications";

    public DiscordSetupGuideWindow()
    {
        Title = "Bot Discord — jak skonfigurować";
        Width = 900;
        Height = Math.Min(880, SystemParameters.WorkArea.Height - 40);
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        GlassChrome.Apply(this);

        var steps = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        steps.Children.Add(BuildOverview());
        int number = 1;

        steps.Children.Add(Step(number++, "Przygotuj własny serwer Discord",
            new[]
            {
                "Bot musi być na serwerze, na którym możesz zapraszać boty — najprościej na **własnym**. Jeśli już masz taki serwer, przejdź do kroku 2.",
                "1. W Discordzie kliknij **+** (Dodaj serwer) na samym dole listy serwerów po lewej.",
                "2. Wybierz **Stwórz własny** → **Dla mnie i moich znajomych**.",
                "3. Wpisz nazwę, np. „Kopacz”, i kliknij **Utwórz**."
            },
            tip: "Na cudzym serwerze potrzebujesz uprawnienia **Zarządzanie serwerem** — bez niego nie dodasz bota."));

        steps.Children.Add(Step(number++, "Utwórz aplikację w Discord Developer Portal",
            new[]
            {
                "1. Kliknij przycisk poniżej (albo wpisz w przeglądarce `discord.com/developers/applications`) i zaloguj się tym samym kontem Discord.",
                "2. W prawym górnym rogu kliknij **New Application** (Nowa aplikacja).",
                "3. Wpisz nazwę bota, np. „Kopacz”, zaznacz zgodę na regulamin i kliknij **Create** (Utwórz).",
                "4. Opcjonalnie w zakładce **General Information** ustaw ikonę — tak bot będzie wyglądał na Discordzie."
            },
            actions: new UIElement[] { LinkButton("Otwórz Developer Portal", PortalUrl, primary: true) }));

        steps.Children.Add(Step(number++, "Skopiuj token bota",
            new[]
            {
                "1. W menu po lewej wejdź w zakładkę **Bot**.",
                "2. Kliknij **Reset Token** (Resetuj token) i potwierdź. Discord może poprosić o hasło albo kod 2FA.",
                "3. Kliknij **Copy** (Kopiuj). Token to długi ciąg znaków z dwiema kropkami, np. `MTQ0…xYz.GaBc12.k9…`.",
                "4. Wklej go od razu do Kopacza (krok 8) albo tymczasowo do Notatnika — Discord pokazuje token **tylko raz**. Zgubisz go? Po prostu zresetuj jeszcze raz.",
                "5. Opcjonalnie na tej samej stronie wyłącz **Public Bot** — wtedy nikt inny nie doda Twojego bota na swój serwer."
            },
            warning: "Token działa jak hasło do bota. Nie wysyłaj go nikomu i nie wklejaj na Discordzie. Jeśli wycieknie — kliknij Reset Token, a stary przestanie działać. Kopacz trzyma token zaszyfrowany kluczem Twojego konta Windows."));

        steps.Children.Add(Step(number++, "Włącz „Message Content Intent” (najczęściej pomijany krok!)",
            new[]
            {
                "1. Zostań w zakładce **Bot** i przewiń w dół do sekcji **Privileged Gateway Intents** — są tam trzy przełączniki.",
                "2. Włącz trzeci: **Message Content Intent**. Pozostałe dwa (Presence, Server Members) mogą zostać wyłączone.",
                "3. Na dole pojawi się pasek — kliknij **Save Changes** (Zapisz zmiany). Bez tego przełącznik się nie zapisze."
            },
            warning: "Bez tego bot widzi wiadomości jako puste i nie odpowiada na komendy. W Kopaczu zobaczysz wtedy „Włącz Message Content Intent” (błąd 4014). Po włączeniu kliknij w Kopaczu jeszcze raz „Zapisz ustawienia Discord”.",
            tip: "Niżej na tej stronie jest kalkulator **Uprawnienia bota** z mnóstwem pól — możesz go pominąć, on tylko liczy kod uprawnień."));

        steps.Children.Add(Step(number++, "Zaproś bota na swój serwer",
            new[]
            {
                "**Sposób szybki:** zapisz najpierw token w Kopaczu (krok 8), wróć tutaj i kliknij **Skopiuj link zaproszenia** poniżej — link ma już ustawione właściwe uprawnienia. Wklej go w przeglądarce.",
                "**Sposób ręczny w Developer Portal:**",
                "1. W menu po lewej wejdź w **OAuth2** → **URL Generator** (Generator URL).",
                "2. W sekcji **Scopes** (Zakresy) zaznacz tylko **bot**.",
                "3. **Typ integracji** zostaw na **Instalacja w gildii** (Guild Install) — „gildia” to po prostu serwer.",
                "4. W **Bot Permissions** (Uprawnienia bota) zaznacz dokładnie 4 pola: **Wyświetlanie kanałów** (View Channels), **Wysyłanie wiadomości** (Send Messages), **Załączanie plików** (Attach Files), **Czytanie historii czatu** (Read Message History). Kod uprawnień: `101376`.",
                "5. Skopiuj **wygenerowany URL** z samego dołu strony i otwórz go w przeglądarce.",
                "**Dalej, w obu sposobach:** wybierz swój serwer z listy → **Kontynuuj** → **Autoryzuj** → potwierdź, że nie jesteś robotem.",
                "Bot pojawi się na liście członków serwera jako **offline** (szary) — to normalne. Zrobi się zielony dopiero, gdy włączysz go w Kopaczu."
            },
            warning: "Nie zaznaczaj „Administrator”. Bot Kopacza potrzebuje tylko tych 4 uprawnień.",
            actions: new UIElement[] { InviteButton() }));

        steps.Children.Add(Step(number++, "Przygotuj kanał na powiadomienia",
            new[]
            {
                "1. Na serwerze kliknij **+** obok napisu **Kanały tekstowe** i utwórz kanał, np. `#kopacz`.",
                "2. Jeśli kanał jest **prywatny**: kliknij zębatkę przy kanale → **Uprawnienia** → **Dodaj członków lub role** → wybierz bota i zaznacz mu: Wyświetlanie kanału, Wysyłanie wiadomości, Załączanie plików, Czytanie historii czatu.",
                "Kanał publiczny zwykle działa od razu, bo bot dostał uprawnienia przy zaproszeniu."
            }));

        steps.Children.Add(Step(number++, "Skopiuj ID kanału i swoje ID",
            new[]
            {
                "ID to długa liczba (17–20 cyfr), np. `123456789012345678`. Żeby je skopiować, trzeba raz włączyć tryb dewelopera:",
                "1. Kliknij zębatkę **Ustawienia użytkownika** obok swojego nicku na dole Discorda.",
                "2. Wejdź w **Zaawansowane** i włącz **Tryb dewelopera**. Zamknij ustawienia (Esc).",
                "3. **ID kanału:** kliknij kanał `#kopacz` prawym przyciskiem myszy → **Kopiuj ID kanału**.",
                "4. **Twoje ID:** kliknij prawym przyciskiem swój nick (na liście członków po prawej albo przy swojej wiadomości) → **Kopiuj ID użytkownika**.",
                "Na telefonie: przytrzymaj palec na kanale albo na swoim profilu → **Kopiuj ID**."
            },
            tip: "Łatwo pomylić ID — ID serwera albo wiadomości nie zadziała. Potrzebne jest ID **kanału** i ID **użytkownika**."));

        steps.Children.Add(Step(number++, "Wklej dane do Kopacza i wyślij test",
            new[]
            {
                "W Kopaczu wejdź w **Ustawienia** → karta **Bot Discord** (ta sama, w której jest przycisk „Jak skonfigurować?”):",
                "• **Token bota** — wklej token z kroku 3 i naciśnij **Enter** (albo kliknij Zapisz). Pole zrobi się puste, pod nim pojawi się „Token zapisany (zaszyfrowany)”, a przełącznik bota włączy się sam. To dobrze — token jest bezpiecznie schowany.",
                "• **ID kanału** — liczba z kroku 7.",
                "• **Twoje ID (właściciele)** — Twoja liczba z kroku 7. Kilka osób wpisz po przecinku. Tylko te osoby mogą wydawać komendy botowi.",
                "• **Prefiks** — znak przed komendą. Zostaw `!` (komendy będą wyglądać tak: `!status`).",
                "Kliknij **Wyślij test**. Na kanale powinna pojawić się wiadomość od bota z Twoim oznaczeniem — udany test z nowym tokenem od razu go zapisuje. Jeśli coś jest źle, pasek na dole okna Kopacza powie co — zajrzyj do sekcji „Coś nie działa” na końcu."
            }));

        steps.Children.Add(Step(number++, "Włącz bota i zapisz",
            new[]
            {
                "1. Sprawdź **przełącznik w prawym górnym rogu karty** Bot Discord (obok przycisku „Jak skonfigurować?”) — po zapisaniu nowego tokenu włącza się sam; jeśli jest wyłączony, włącz go.",
                "2. Kliknij **Zapisz ustawienia Discord**.",
                "3. Po kilku sekundach linia stanu w karcie pokaże **„Połączony jako …”** z zieloną kropką, a bot na serwerze zrobi się zielony (online).",
                "4. Na kanale wpisz `!pomoc` — bot odpowie listą komend."
            },
            warning: "Komendy działają tylko przy włączonym przełączniku i zapisanych ustawieniach. Jeśli wyłączysz przełącznik, bot zamilknie do ponownego włączenia i zapisu.",
            tip: "Bot działa, dopóki działa główne okno Kopacza — może być schowane w zasobniku obok zegara. Po zamknięciu Kopacza bot przechodzi w offline."));

        steps.Children.Add(Step(number++, "Wybierz powiadomienia",
            new[]
            {
                "• **Alarm ochrony** — alarm obrażeń i wyjście z serwera. Zawsze z oznaczeniem Ciebie, gdy włączone „Oznacz mnie”.",
                "• **Reconnect** — ponowne łączenie z serwerem; nieudany reconnect też Cię oznacza.",
                "• **Brak kilofa** — kilof się zepsuł albo zniknął.",
                "• **Start / koniec kopania** — informacja przy każdym uruchomieniu i zatrzymaniu.",
                "• **Błędy** — błędy programu (te same co w konsoli `>_`).",
                "• **Dołącz zrzut ekranu** — do alarmów dołączany jest zrzut z gry.",
                "• **Oznacz mnie przy alarmach** — ping przy ważnych zdarzeniach, żeby telefon zadzwonił.",
                "• **Zrzut co / Status co (min)** — cykliczny zrzut ekranu albo status wszystkich kont, np. co 30 minut. `0` = wyłączone.",
                "Po każdej zmianie kliknij **Zapisz ustawienia Discord**.",
                "Kliknij **Testuj powiadomienia** — na kanał przyjdzie przykład każdego włączonego powiadomienia, z oznaczeniem i zrzutem z gry, dokładnie tak jak przy prawdziwym zdarzeniu. W grze nic się nie dzieje."
            },
            tip: "Prawdziwy alarm sprawdzisz bez ryzyka: zakładka **Ochrona** → zaznaczony **Tryb testowy** → **Testuj nasłuch (15 s)** → daj się uderzyć w grze. Przyjdzie „TEST alarmu obrażeń” z oznaczeniem."));

        steps.Children.Add(Step(number++, "Komendy z telefonu",
            new[]
            {
                "Wpisuj je na ustawionym kanale albo w wiadomości prywatnej do bota. `[konto]` to nazwa z panelu Kont — można ją pominąć, gdy działa jedno konto.",
                "`!pomoc` — lista komend",
                "`!status [konto]` — co robi każde konto",
                "`!dane [konto]` — z dziennika: ostatnia sesja, EQ, CobbleX, wyrzucone przedmioty, co zostało, wyjścia z serwera",
                "`!stats [konto]` — liczniki z całej historii konta: sesje, EQ, CobbleX, wyrzucone",
                "`!ss [konto]` — zrzut ekranu gry",
                "`!jedzenie [konto]` — pasek życia i jedzenia",
                "`!eq [konto]` — ostatni skan ekwipunku",
                "`!start [konto] [5/3/3|6/3/3]` — uruchom kopanie (gdy gra nie jest na wierzchu — w tle); np. `!start Konto 2 5/3/3`, `!start wszystkie`. Bez trybu rusza kanał zaznaczony w profilu (przy obu — 6/3/3)",
                "`!stop [konto]` — zatrzymaj kopanie; samo `!stop` zatrzymuje wszystkie konta",
                "`!join [konto] [5/3/3|6/3/3]` — wejdź na serwer jak Auto Reconnect (lista serwerów, Direct Connect, /home, kontrola EQ) i zacznij kopać. Wymaga profilu powrotu w zakładce Ochrona; wynik przyjdzie od konta",
                "`!leave [konto]` — zatrzymaj kopanie i wyjdź z serwera (ESC → Disconnect)",
                "Każdą komendę (poza `!pomoc`) możesz wyłączyć przyciskiem **Włącz / wyłącz komendy** w karcie Bot Discord — bot odpowie wtedy, że jest wyłączona."
            }));

        steps.Children.Add(BuildTroubleshooting());

        var scroll = new ScrollViewer
        {
            Content = steps,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };

        var close = new Button { Content = "Zamknij", MinWidth = 110, IsCancel = true };
        close.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var footerNote = new TextBlock
        {
            Text = "Okno możesz zostawić otwarte obok przeglądarki i Discorda.",
            Style = GlassChrome.GetStyle("GlassSub"),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(footerNote, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(footerNote);
        footer.Children.Add(close);

        var layout = new DockPanel { Margin = new Thickness(26, 22, 18, 18) };
        UIElement header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(scroll);

        var root = new Grid();
        root.Children.Add(new GlassBlobBackground { IsAnimationEnabled = false });
        root.Children.Add(layout);
        Content = root;
    }

    private static UIElement BuildHeader()
    {
        var icon = new Border
        {
            Style = GlassChrome.GetStyle("GlassIconTile"),
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Viewbox
            {
                Width = 22,
                Height = 22,
                Child = new Grid
                {
                    Width = 24,
                    Height = 24,
                    Children =
                    {
                        new System.Windows.Shapes.Path
                        {
                            Style = GlassChrome.GetStyle("GlassIconPath"),
                            Data = Geometry.Parse("M4,6 H20 V16 H11 L7,20 V16 H4 Z M8,10 H16 M8,13 H13")
                        }
                    }
                }
            }
        };

        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = "Bot Discord — konfiguracja krok po kroku", FontSize = 22, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = "Zajmie około 10 minut. Nie trzeba nic programować: tworzysz własnego bota w Discordzie i wklejasz do Kopacza trzy rzeczy.",
            Style = GlassChrome.GetStyle("GlassLead"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        var header = new DockPanel { Margin = new Thickness(0, 0, 8, 16) };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(text);
        return header;
    }

    private static UIElement BuildOverview()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "CO BĘDZIE POTRZEBNE", Style = GlassChrome.GetStyle("GlassCaption"), Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(Rich("Konto Discord, **własny serwer** (krok 1), przeglądarka i działający Kopacz."));
        panel.Children.Add(new TextBlock { Text = "DO KOPACZA WKLEISZ", Style = GlassChrome.GetStyle("GlassCaption"), Margin = new Thickness(0, 10, 0, 8) });

        var pills = new WrapPanel();
        foreach (string item in new[] { "1 · Token bota (krok 3)", "2 · ID kanału (krok 7)", "3 · Twoje ID (krok 7)" })
        {
            pills.Children.Add(new Border
            {
                Style = GlassChrome.GetStyle("GlassPill"),
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 8, 6),
                Child = new TextBlock { Text = item, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            });
        }
        panel.Children.Add(pills);
        panel.Children.Add(Rich("Discord może mieć menu po polsku albo po angielsku — przy ważnych przyciskach podaję obie nazwy.", new Thickness(0, 6, 0, 0)));

        return new Border { Style = GlassChrome.GetStyle("GlassNote"), Padding = new Thickness(18, 14, 18, 14), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, 14), Child = panel };
    }

    private static UIElement BuildTroubleshooting()
    {
        (string Problem, string Fix)[] rows =
        {
            ("Test: „Discord odrzucił token”", "Token jest zły albo został zresetowany. Krok 3: Reset Token → wklej nowy → Zapisz."),
            ("Test: brak dostępu do kanału (403)", "Bot nie widzi kanału albo nie może pisać. Krok 6: dodaj botowi uprawnienia na kanale."),
            ("Test: nie znaleziono kanału (404)", "Złe ID kanału — może to ID serwera albo wiadomości. Krok 7: PPM na kanał → Kopiuj ID kanału."),
            ("Stan: „Włącz Message Content Intent”", "Krok 4. Potem w Kopaczu kliknij jeszcze raz „Zapisz ustawienia Discord”."),
            ("Test działa, ale bot nie odpowiada na !pomoc", "Przełącznik bota jest wyłączony. Krok 9: włącz przełącznik → Zapisz."),
            ("Bot odpowiada „Brak uprawnień”", "Twojego ID nie ma w polu właścicieli albo wpisano złe ID. Krok 7 i 8."),
            ("Bot milczy na innym kanale", "Komendy działają tylko na ustawionym kanale albo w wiadomości prywatnej do bota."),
            ("Bot jest offline", "Główne okno Kopacza musi działać (może być w zasobniku). Sprawdź linię stanu w karcie Bot Discord."),
            ("Dalej nie wiesz, co się dzieje", "Otwórz konsolę `>_` w lewym dolnym rogu Kopacza i poszukaj wpisów oznaczonych „Discord”.")
        };

        var list = new StackPanel();
        list.Children.Add(new TextBlock { Text = "Coś nie działa?", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        foreach ((string problem, string fix) in rows)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star) });
            TextBlock left = Rich("**" + problem + "**");
            left.Margin = new Thickness(0, 0, 14, 0);
            TextBlock right = Rich(fix);
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            list.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassInset"), Margin = new Thickness(0, 0, 0, 6), Child = row });
        }

        return new Border { Style = GlassChrome.GetStyle("GlassCard"), Padding = new Thickness(18, 16, 18, 12), CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 4, 0, 6), Child = list };
    }

    private static Border Step(int number, string title, string[] lines, string? warning = null, string? tip = null, UIElement[]? actions = null)
    {
        var body = new StackPanel { Margin = new Thickness(14, 1, 0, 0) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) });
        foreach (string line in lines)
            body.Children.Add(Rich(line, new Thickness(0, 0, 0, 6)));
        if (warning != null)
            body.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassWarnNote"), Margin = new Thickness(0, 6, 0, 2), Child = Rich("⚠  " + warning) });
        if (tip != null)
            body.Children.Add(new Border { Style = GlassChrome.GetStyle("GlassNote"), Margin = new Thickness(0, 6, 0, 2), Child = Rich("💡  " + tip) });
        if (actions != null)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (UIElement action in actions)
                row.Children.Add(action);
            body.Children.Add(row);
        }

        var badge = new Border
        {
            Style = GlassChrome.GetStyle("GlassStepNo"),
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Child = new TextBlock { Text = number.ToString(), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        grid.Children.Add(badge);
        grid.Children.Add(body);

        return new Border { Style = GlassChrome.GetStyle("GlassCard"), Padding = new Thickness(18, 16, 18, 14), CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 0, 0, 12), Child = grid };
    }

    // Prosty zapis: **pogrubienie** i `kod` (czcionka stała na jasnym tle).
    private static TextBlock Rich(string text, Thickness margin = default)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21, Margin = margin };
        bool bold = false;
        int index = 0;
        while (index < text.Length)
        {
            int nextBold = text.IndexOf("**", index, StringComparison.Ordinal);
            int nextCode = text.IndexOf('`', index);
            int next = nextBold < 0 ? nextCode : nextCode < 0 ? nextBold : Math.Min(nextBold, nextCode);
            if (next < 0)
            {
                block.Inlines.Add(Plain(text[index..], bold));
                break;
            }
            if (next > index)
                block.Inlines.Add(Plain(text[index..next], bold));

            if (next == nextBold)
            {
                bold = !bold;
                index = next + 2;
                continue;
            }

            int end = text.IndexOf('`', next + 1);
            if (end < 0)
            {
                block.Inlines.Add(new Run(text[next..]));
                break;
            }
            var code = new Run(text[(next + 1)..end]) { Background = GlassPalette.NeutralSoft };
            code.SetResourceReference(TextElement.FontFamilyProperty, "GlassMonoFont");
            block.Inlines.Add(code);
            index = end + 1;
        }
        return block;
    }

    // Zwykły tekst dziedziczy grubość po TextBlocku; pogrubienie tylko między **.
    private static Run Plain(string text, bool bold) =>
        bold ? new Run(text) { FontWeight = FontWeights.SemiBold } : new Run(text);

    private static Button LinkButton(string text, string url, bool primary = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), ToolTip = url };
        if (primary)
            button.Style = GlassChrome.GetStyle("GlassPrimaryButton");
        button.Click += (_, _) => OpenUrl(url);
        return button;
    }

    // Link z ID aplikacji odczytanym z zapisanego tokenu i właściwymi uprawnieniami.
    private static Button InviteButton()
    {
        var button = new Button
        {
            Content = "Skopiuj link zaproszenia",
            Style = GlassChrome.GetStyle("GlassPrimaryButton"),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Działa po zapisaniu tokenu w Kopaczu (krok 8). Link ma ustawione 4 potrzebne uprawnienia."
        };
        button.Click += async (_, _) =>
        {
            string? url = DiscordSettingsStore.TryBuildInviteUrl(DiscordSettingsStore.GetToken(DiscordSettingsStore.Load()));
            if (url == null)
            {
                GlassMessageDialog.Show(Window.GetWindow(button), "Brak zapisanego tokenu",
                    "Najpierw wklej token bota w karcie Bot Discord i kliknij „Zapisz ustawienia Discord” (kroki 3 i 8). Potem wróć tutaj — link zbuduje się sam.\n\nMożesz też użyć sposobu ręcznego opisanego w tym kroku.");
                return;
            }
            if (!TryCopy(url))
            {
                OpenUrl(url);
                return;
            }
            button.Content = "Skopiowano ✓ — wklej w przeglądarce";
            await Task.Delay(2500);
            button.Content = "Skopiuj link zaproszenia";
        };
        return button;
    }

    private static bool TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            // Schowek bywa chwilowo zajęty przez inny program.
            return false;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn("Discord", "Nie udało się otworzyć przeglądarki: " + ex.Message);
        }
    }
}
