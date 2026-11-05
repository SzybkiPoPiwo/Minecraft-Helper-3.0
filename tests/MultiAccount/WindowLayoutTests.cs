using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using MinecraftHelper.Services;

namespace MultiAccountTests;

internal static partial class Program
{
    private static void CheckWindowLayouts()
    {
        var frame = new Size(16, 39);
        foreach (Rectangle area in new[]
        {
            new Rectangle(0, 0, 1920, 1040), new Rectangle(0, 40, 1920, 1040),
            new Rectangle(-1280, -120, 1280, 680), new Rectangle(0, 0, 1024, 728),
            new Rectangle(200, -2160, 3840, 2120), new Rectangle(0, 0, 640, 440)
        })
        {
            var tiles = MinerWindowLayout.CreateGameTiles(new[] { area }, new Size(854, 480), frame);
            Check(tiles.Count > 0, "Każdy typowy ekran musi pomieścić okno gry.");
            foreach (var tile in tiles)
            {
                Check(area.Contains(tile.Bounds), "Ramka gry musi mieścić się w obszarze roboczym, także na monitorze o ujemnych współrzędnych.");
                Check(tile.ClientSize.Width is >= 320 and <= 854 && tile.ClientSize.Height is >= 240 and <= 480,
                    "Rozmiar klienta musi zachować granice obsługiwane przez kopanie.");
            }
            for (int i = 0; i < tiles.Count; i++)
                for (int j = i + 1; j < tiles.Count; j++)
                    Check(!tiles[i].Bounds.IntersectsWith(tiles[j].Bounds), "Okna różnych kont nie mogą się nakładać.");
        }

        var primary = new Rectangle(0, 0, 1920, 1040);
        var secondary = new Rectangle(-1280, 0, 1280, 680);
        var combined = MinerWindowLayout.CreateGameTiles(new[] { primary, secondary }, new Size(854, 480), frame);
        Check(combined.Count == 8 && combined.Take(4).All(tile => primary.Contains(tile.Bounds))
            && combined.Skip(4).All(tile => secondary.Contains(tile.Bounds)), "Po zajęciu głównego monitora okna trafiają na drugi.");
        var otherFrames = MinerWindowLayout.CreateGameTiles(new[] { primary, secondary }, new Size(854, 480), new Size(32, 78));
        Check(combined.Count == otherFrames.Count, "Rozmiar ramki klienta nie może zmieniać wspólnej liczby miejsc.");
        for (int i = 0; i < combined.Count; i++)
        {
            Check(combined[i].Bounds.Location == otherFrames[i].Bounds.Location, "Różne ramki/DPI muszą używać tych samych początków miejsc.");
            for (int j = 0; j < otherFrames.Count; j++)
                if (i != j)
                    Check(!combined[i].Bounds.IntersectsWith(otherFrames[j].Bounds), "Klienci z różnymi ramkami nie mogą się nakładać.");
        }
        Check(MinerWindowLayout.CreateGameTiles(new[] { new Rectangle(0, 0, 300, 200) }, new Size(854, 480), frame).Count == 0,
            "Zbyt mały ekran nie może tworzyć okien wychodzących poza jego granice.");

        foreach (bool left in new[] { true, false })
            foreach (bool top in new[] { true, false })
            {
                var peers = new List<Rectangle>();
                var area = new Rectangle(-1000, 70, 1000, 720);
                for (int i = 0; i < 6; i++)
                {
                    Rectangle? placement = MinerWindowLayout.FindOverlayBounds(area, new Size(300, 220), left, top, 16, 8, peers);
                    Check(placement.HasValue, "HUD-y powinny przejść do kolejnego rzędu po zapełnieniu pierwszego.");
                    Rectangle target = placement!.Value;
                    Check(Rectangle.Inflate(area, -16, -16).Contains(target), "HUD musi mieścić się na wybranym monitorze.");
                    Check(peers.All(peer => !Rectangle.Inflate(peer, 8, 8).IntersectsWith(target)), "HUD-y muszą zachować odstęp.");
                    if (i < 3)
                        Check(target.Y == (top ? area.Top + 16 : area.Bottom - 16 - 220), "Pierwsze konta muszą być obok siebie w jednym rzędzie.");
                    peers.Add(target);
                }
                Rectangle? firstHole = MinerWindowLayout.FindOverlayBounds(area, peers[0].Size, left, top, 16, 8, peers.Skip(1).ToArray());
                Check(firstHole == peers[0], "Zamknięcie konta powinno zwolnić jego miejsce.");
            }
        var mixed = new[] { new Rectangle(668, 16, 312, 170), new Rectangle(228, 16, 432, 300) };
        Rectangle? taller = MinerWindowLayout.FindOverlayBounds(new Rectangle(0, 0, 1000, 720), new Size(432, 360), false, true, 16, 8, mixed);
        Check(taller.HasValue && mixed.All(peer => !peer.IntersectsWith(taller.Value)), "HUD-y o różnych szerokościach i wysokościach też nie mogą się nakładać.");
        Check(MinerWindowLayout.FindOverlayBounds(primary, new Size(300, 200), true, true, 16, 8, new[] { primary }) == null,
            "Brak miejsca na HUD musi być jawnie zgłoszony zamiast nakładania.");
    }

    private static void CheckLayoutReservations()
    {
        string group = "Test-" + Guid.NewGuid().ToString("N");
        using var first = MinerLayoutSlotLease.TryAcquire(group, 2);
        Check(first?.Index == 0, "Pierwsze konto rezerwuje pierwszy slot.");
        using var child = StartChild("--lease-child", group);
        try
        {
            Check(ReadChildLine(child) == "1", "Osobny proces musi wybrać inne miejsce.");
            using (var full = MinerLayoutSlotLease.TryAcquire(group, 2))
                Check(full == null, "Zapełniony układ nie może ponownie przydzielić zajętego miejsca.");
            child.Kill();
            Check(child.WaitForExit(5000), "Proces testowy powinien się zakończyć.");
            using var recovered = MinerLayoutSlotLease.TryAcquire(group, 2);
            Check(recovered?.Index == 1, "Awaria procesu musi automatycznie zwolnić jego slot.");
            first!.Dispose();
            using var replacement = MinerLayoutSlotLease.TryAcquire(group, 2);
            Check(replacement?.Index == 0 && recovered?.Index == 1, "Nowe konto zajmuje lukę bez przestawiania istniejących okien.");
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    private static Process StartChild(string mode, string value)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(value);
        return Process.Start(start)!;
    }

    private static string? ReadChildLine(Process process) => process.StandardOutput.ReadLineAsync()
        .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

    // Own popup fixtures, far outside the desktop. No activation, injected input
    // or changes to any real Minecraft/Kopacz window.
    private static readonly Rectangle HudTestArea = new(-30000, -30000, 1000, 720);

    private static void RunHudChild(int height)
    {
        IntPtr window = CreateHudFixture(312, height);
        using var layout = new MinerHudLayout();
        try
        {
            Console.WriteLine(layout.TryPosition(window, HudTestArea, false, false, 16, 8) ? BoundsText(window) : "FAILED");
            Console.Out.Flush();
            Console.ReadLine();
        }
        finally { DestroyWindow(window); }
    }

    private static void RunHudLayoutSmoke()
    {
        IntPtr foreground = GetForegroundWindow();
        IntPtr first = CreateHudFixture(312, 180);
        IntPtr second = CreateHudFixture(432, 290);
        using var layoutA = new MinerHudLayout();
        using var layoutB = new MinerHudLayout();
        try
        {
            Check(layoutA.TryPosition(first, HudTestArea, false, false, 16, 8), "Pierwszy natywny HUD powinien się zmieścić.");
            using var child = StartChild("--hud-child", "220");
            try
            {
                string? childBounds = ReadChildLine(child);
                Check(childBounds != null && childBounds != "FAILED", "HUD w osobnym procesie powinien się zmieścić.");
                int[] parts = childBounds!.Split(',').Select(int.Parse).ToArray();
                var peer = new Rectangle(parts[0], parts[1], parts[2], parts[3]);
                Check(!GetBounds(first).IntersectsWith(peer) && peer.Bottom == GetBounds(first).Bottom,
                    "HUD-y z osobnych procesów powinny być obok siebie w jednym rzędzie.");
                Check(layoutB.TryPosition(second, HudTestArea, false, false, 16, 8), "Szerszy trzeci HUD powinien przejść do kolejnego rzędu.");
                Check(!GetBounds(second).IntersectsWith(peer) && !GetBounds(second).IntersectsWith(GetBounds(first)),
                    "Różne rozmiary natywnych HUD-ów nie mogą powodować kolizji.");
                Check(GetWindow(first, 4) == IntPtr.Zero && GetWindow(second, 4) == IntPtr.Zero,
                    "Układ nie może ustawiać właściciela i łączyć kolejek wejścia.");
                Check(GetForegroundWindow() == foreground, "Rozmieszczanie HUD-ów nie może zmieniać fokusu.");
                child.StandardInput.WriteLine("close");
                Check(child.WaitForExit(5000), "Proces HUD testowego powinien zwolnić okno.");
                Check(!layoutB.TryPosition(second, new Rectangle(-30000, -30000, 450, 350), true, true, 16, 8)
                    && layoutB.NoSpace, "Zbyt mały monitor musi zostać rozpoznany.");
                Check(layoutB.TryPosition(second, HudTestArea, true, true, 16, 8) && !layoutB.NoSpace,
                    "HUD musi wrócić po wybraniu większego monitora.");
            }
            finally { if (!child.HasExited) child.Kill(); }
        }
        finally { DestroyWindow(second); DestroyWindow(first); }
    }

    private static IntPtr CreateHudFixture(int width, int height)
    {
        IntPtr window = CreateWindowEx(0x08000080, "STATIC", "Miner layout test", 0x90000000,
            HudTestArea.X, HudTestArea.Y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed");
        return window;
    }
    private static Rectangle GetBounds(IntPtr window)
    {
        if (!GetWindowRect(window, out NativeRect value)) throw new InvalidOperationException("GetWindowRect failed");
        return Rectangle.FromLTRB(value.Left, value.Top, value.Right, value.Bottom);
    }
    private static string BoundsText(IntPtr window)
    {
        Rectangle bounds = GetBounds(window);
        return $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}";
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
}
