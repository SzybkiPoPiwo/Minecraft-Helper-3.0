using System.Drawing;
using MinecraftHelper.Services;

namespace MultiAccountTests;

internal static partial class Program
{
    private static void CheckBackgroundCursor()
    {
        Point user = new(3, 224);
        Point center = new(427, 240);
        BackgroundCursorSnapshot armed = BackgroundCursorPolicy.Arm(default, user, 100);
        Check(armed.Initialized && armed.Position == user, "Ochrona komendy zapamiętuje pozycję użytkownika przed wejściem gry.");
        Check(BackgroundCursorPolicy.RestoreTarget(armed, center, center, false, 120) == user,
            "Przeskok na środek gry przy komendzie wraca do pozycji 3,224.");
        Check(BackgroundCursorPolicy.RestoreTarget(armed, new Point(500, 180), center, false, 120) == user,
            "Krótka ochrona obejmuje też odtworzenie starej pozycji gry poza środkiem.");
        Check(BackgroundCursorPolicy.RestoreTarget(default, center, center, false, 120) == null,
            "Brak zapisanej pozycji nie może wygenerować ruchu kursora.");

        BackgroundCursorSnapshot peer = BackgroundCursorPolicy.Arm(armed, center, 130);
        Check(peer.Position == user && peer.GuardUntil == 930,
            "Drugie konto przedłuża ochronę bez zapamiętania przeskoku pierwszego konta.");

        Point warpedPacket = new(503, 242);
        BackgroundCursorSnapshot mixed = BackgroundCursorPolicy.ObserveMouse(peer, warpedPacket, 3, 2, false, 150);
        Check(mixed.Position == new Point(6, 226) && mixed.LastCorrectedTimestamp == 150,
            "Przeskok połączony z ruchem fizycznym ponad 2px zachowuje ruch użytkownika poza grą.");
        Check(BackgroundCursorPolicy.ObserveMouse(mixed, warpedPacket, 3, 2, false, 150) == mixed,
            "Ten sam pakiet Raw Input w drugim procesie nie może drugi raz doliczyć ruchu.");
        Check(BackgroundCursorPolicy.ObserveMouse(mixed, new Point(700, 600), 3, 2, false, 149) == mixed,
            "Opóźnione pakiety nie nadpisują nowszej pozycji użytkownika.");

        BackgroundCursorSnapshot moving = BackgroundCursorPolicy.ObserveMouse(mixed, new Point(10, 229), 4, 3, false, 160);
        Check(moving.Position == new Point(10, 229), "Użytkownik może normalnie poruszać myszą podczas ochrony komendy.");
        BackgroundCursorSnapshot diagonal = BackgroundCursorPolicy.ObserveMouse(moving, new Point(74, 293), 8, 8, false, 165);
        Check(diagonal.Position == new Point(74, 293), "Przyspieszony ruch po przekątnej nie może być uznany za skok gry.");
        Check(BackgroundCursorPolicy.RestoreTarget(moving, moving.Position, center, false, 170) == null,
            "Prawidłowa pozycja nie jest wielokrotnie odtwarzana.");
        BackgroundCursorSnapshot quick = BackgroundCursorPolicy.ObserveMouse(moving, new Point(600, 500), 150, 80, false, 180);
        Check(quick.Position == new Point(600, 500), "Duży rzeczywisty ruch urządzenia nie może zostać uznany za teleport gry.");
        BackgroundCursorSnapshot absolute = BackgroundCursorPolicy.ObserveMouse(moving, new Point(-1500, 800), 100, 100, true, 180);
        Check(absolute.Position == new Point(-1500, 800) && absolute.LastCorrectedTimestamp == moving.LastCorrectedTimestamp,
            "Bezwzględne urządzenie wskazujące zachowuje własną fizyczną pozycję.");

        Check(BackgroundCursorPolicy.RestoreTarget(moving, center, center, true, 180) == null,
            "Świadomy wybór Minecrafta wyłącza odtwarzanie kursora.");
        BackgroundCursorSnapshot manual = BackgroundCursorPolicy.ObserveMouse(moving, center, 2, 2, false, 180, userSelectedGame: true);
        Check(manual.Position == center && manual.GuardUntil == 180,
            "Ręczne sterowanie grą przyjmuje pozycję pakietu i kończy wcześniejszą ochronę.");
        Check(BackgroundCursorPolicy.RestoreTarget(manual, new Point(500, 400), center, false, 181) == null,
            "Ochrona konta w tle nie zamraża kursora po świadomym wejściu do gry.");

        Check(BackgroundCursorPolicy.RestoreTarget(armed, new Point(800, 800), center, false, 900) == null,
            "Ochrona komendy wygasa dokładnie w swoim terminie.");
        Check(BackgroundCursorPolicy.RestoreTarget(armed, new Point(428, 239), center, false, 1000) == user,
            "Ochrona środka zachowuje tolerancję dla odwróconej osi Y i zaokrągleń monitora.");
        BackgroundCursorSnapshot expired = BackgroundCursorPolicy.ObserveMouse(armed, new Point(800, 800), 2, 2, false, 1000);
        Check(expired.Position == new Point(800, 800), "Po wygaśnięciu komendy sprzęt nie jest ograniczany dawną ochroną.");
        BackgroundCursorSnapshot rearmed = BackgroundCursorPolicy.Arm(expired, new Point(40, 50), 1100);
        Check(rearmed.Position == new Point(40, 50), "Nowa komenda po przerwie pobiera aktualny punkt sprzed wysłania wejścia.");

        int beforeWrap = int.MaxValue - 100;
        int afterWrap = unchecked(beforeWrap + 200);
        BackgroundCursorSnapshot wrapped = BackgroundCursorPolicy.Arm(default, user, beforeWrap);
        Check(BackgroundCursorPolicy.RestoreTarget(wrapped, center, center, false, afterWrap) == user,
            "Przepełnienie TickCount nie wyłącza aktywnej ochrony.");
        wrapped = BackgroundCursorPolicy.ObserveMouse(wrapped, new Point(6, 227), 3, 3, false, afterWrap);
        Check(wrapped.Position == new Point(6, 227), "Pakiety po przepełnieniu zegara zachowują kolejność.");
        Check(BackgroundCursorPolicy.ObserveMouse(wrapped, new Point(700, 600), 1, 1, false, beforeWrap) == wrapped,
            "Stare pakiety sprzed przepełnienia zegara pozostają odrzucane.");
        Check(BackgroundCursorPolicy.RestoreTarget(wrapped, new Point(800, 800), center, false, unchecked(beforeWrap + 801)) == null,
            "Termin ochrony wygasa również po przepełnieniu zegara.");

        string name = @"Local\MinecraftHelper.CursorTest." + Guid.NewGuid().ToString("N");
        using (var first = new BackgroundCursorStateStore(name))
        using (var second = new BackgroundCursorStateStore(name))
        {
            first.Arm(user, 100);
            second.Arm(center, 130);
            Check(second.RestoreTarget(center, center, false, 140) == user,
                "Oddzielne uchwyty silników współdzielą kanoniczną pozycję użytkownika.");
            Check(second.ObserveMouse(warpedPacket, 3, 2, false, 150) == new Point(6, 226),
                "Wspólna pamięć zwraca korektę po ruchu fizycznym na tle przeskoku.");
            Check(first.ObserveMouse(warpedPacket, 3, 2, false, 150) == null,
                "Drugie dostarczenie tego samego pakietu nie zwraca kolejnego ruchu kursora.");
            Check(first.RestoreTarget(warpedPacket, center, false, 160) == new Point(6, 226),
                "Pozostałe konta widzą punkt po korekcie urządzenia.");
            first.SelectUserWindow(new IntPtr(12345));
            Check(second.UserWindow == new IntPtr(12345), "Wybór okna przez użytkownika jest współdzielony przez silniki.");
            second.SelectUserWindow(IntPtr.Zero);
            Check(first.UserWindow == IntPtr.Zero, "Zwolnienie wybranego okna również jest wspólne.");
            Check(first.ObserveMouse(center, 1, 1, false, 170, userSelectedGame: true) == null,
                "Jawnie wybrana gra nie otrzymuje korekty nawet z aktywną wspólną ochroną.");
            Check(second.RestoreTarget(new Point(500, 400), center, false, 171) == null,
                "Ręczne przejęcie gry kończy ochronę także dla innych uchwytów.");
            first.Dispose();
            second.Arm(user, 1000);
            Check(second.RestoreTarget(center, center, false, 1010) == user,
                "Zamknięcie jednego silnika nie usuwa stanu pozostałych kont.");
            Check(first.RestoreTarget(center, center, false, 1010) == null,
                "Zamknięty uchwyt nie rzuca wyjątku ani nie rusza myszy.");
        }
        using var fresh = new BackgroundCursorStateStore(name);
        Check(fresh.RestoreTarget(center, center, false, 1020) == null,
            "Zamknięcie wszystkich silników usuwa dawną pozycję bez plików na dysku.");

        string crossProcessName = @"Local\MinecraftHelper.CursorProcessTest." + Guid.NewGuid().ToString("N");
        using var parentState = new BackgroundCursorStateStore(crossProcessName);
        parentState.Arm(user, 100);
        using var child = StartChild("--cursor-child", crossProcessName);
        try
        {
            Check(ReadChildLine(child) == "3,224", "Osobny proces widzi bazę kursora i nie nadpisuje jej środkiem własnej gry.");
            Check(parentState.ObserveMouse(warpedPacket, 3, 2, false, 150) == new Point(6, 226),
                "Rodzic koryguje wspólny pakiet bez ruszania rzeczywistej myszy.");
            child.StandardInput.WriteLine("get");
            Check(ReadChildLine(child) == "6,226", "Inny proces natychmiast widzi punkt po korekcie fizycznego przesunięcia.");
            child.StandardInput.WriteLine("close");
            Check(child.WaitForExit(5000), "Testowy proces zwalnia mapowaną pamięć kursora przy zamknięciu.");
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    private static void RunBackgroundCursorChild(string name)
    {
        using var state = new BackgroundCursorStateStore(name);
        Point center = new(427, 240);
        state.Arm(center, 130);
        WritePoint(state.RestoreTarget(center, center, false, 140));
        while (Console.ReadLine() == "get")
            WritePoint(state.RestoreTarget(new Point(503, 242), center, false, 160));
    }

    private static void WritePoint(Point? point)
    {
        Console.WriteLine(point is { } value ? $"{value.X},{value.Y}" : "none");
        Console.Out.Flush();
    }
}
