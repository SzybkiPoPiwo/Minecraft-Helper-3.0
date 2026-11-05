extern alias production;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using MinecraftHelper.Models;
using MinecraftHelper.Services;
using Drawing = System.Drawing;
using ProductionWindow = production::MinecraftHelper.MainWindow;

internal static class Program
{
    private static int _checks;
    private static readonly DateTime Start = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GoldDepositOptions Options = new("/bazar", "Bazar", 6, 11, 1000, true, true, true);
    private static string Root = "";

    [STAThread]
    private static int Main()
    {
        try
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MinecraftHelper.slnx"))) dir = dir.Parent;
            Root = dir?.FullName ?? throw new DirectoryNotFoundException("Repository not found.");
            CheckFlow(); CheckFailures(); CheckClickGuard(); CheckManualTestSession(); CheckDetector(); CheckCursorQuantities(); CheckRealChestTexture(); CheckSettings(); CheckResetAndIntegration(); CheckGuiResources();
            Console.WriteLine($"PASS: {_checks} checks; gold transfer stages, rejection/recovery, real TXT markers, GUI geometry, cancellation and settings. No windows, desktop input, screenshots or user data.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + description);
        _checks++;
    }

    private static GoldDepositObservation Screen(DetectedInventoryItem[] items, DetectedInventoryItem? cursor = null, bool empty = true, bool valid = true)
        => new(true, valid, empty, cursor, items, valid ? "" : "wrong menu");

    private static GoldDepositFlow Open(GoldDepositOptions? options = null)
    {
        var flow = new GoldDepositFlow(options ?? Options, Start);
        foreach (GoldDepositAction action in new[] { GoldDepositAction.CloseInitialGui, GoldDepositAction.OpenChat, GoldDepositAction.TypeCommand, GoldDepositAction.SubmitCommand })
        {
            DateTime due = flow.NextAt;
            Check(flow.Tick(due.AddTicks(-1)).Action == GoldDepositAction.None, "stage never runs before its deadline");
            Check(flow.Tick(due).Action == action, "command/open order: " + action);
        }
        Check(flow.NeedsScreen, "no item input before menu verification");
        return flow;
    }

    private static void CheckFlow()
    {
        foreach (int slot in new[] { 0, 8, 26, 27, 35 })
        foreach (string id in new[] { "gold_ingot", "gold_block" })
        foreach (int quantity in new[] { 1, 8, 64 })
        {
            var item = new DetectedInventoryItem(slot, id, quantity);
            GoldDepositFlow flow = Open();
            Check(flow.Tick(flow.NextAt, Screen(new[] { item })).Action == GoldDepositAction.PickSource, "recognized gold picked once");
            Check(flow.Tick(flow.NextAt, Screen(Array.Empty<DetectedInventoryItem>(), item, false)).Action == GoldDepositAction.Deposit, "deposit only with the matching carried stack and source reduction");
            Check(flow.Tick(flow.NextAt, Screen(Array.Empty<DetectedInventoryItem>())).Action == GoldDepositAction.None, "empty cursor and matching inventory delta confirm transfer");
            Check(flow.ConfirmedStacks == 1 && flow.ConfirmedItems == quantity, "confirmed transfer counts");
            Check(flow.Tick(flow.NextAt, Screen(Array.Empty<DetectedInventoryItem>())).Action == GoldDepositAction.CloseMenu, "close only after no selected gold remains");
            Check(flow.Tick(flow.NextAt, new(false, false, true, null, Array.Empty<DetectedInventoryItem>())).Action == GoldDepositAction.Complete, "resume only after GUI close confirmation");
        }
        var block = new DetectedInventoryItem(20, "gold_block", 3);
        var ingot = new DetectedInventoryItem(0, "gold_ingot", 64);
        var priority = Open();
        Check(priority.Tick(priority.NextAt, Screen(new[] { ingot, block })).SourceSlot == block.Slot, "blocks are deposited first");
        var ingotsOff = Open(Options with { Ingots = false });
        Check(ingotsOff.Tick(ingotsOff.NextAt, Screen(new[] { ingot })).Action == GoldDepositAction.CloseMenu, "disabled gold type is not deposited");
    }

    private static void CheckFailures()
    {
        Check(Options.ValidateMenuIdentity("Bazar & Aukcje! (1/93)", "gold_ingot") == "" && Options.ValidateMenuIdentity("BAZAR", "gold_block") == "", "decorated titles and both valid bank icons pass identity checks");
        Check(Options.ValidateMenuIdentity("Sklep", "gold_ingot").Contains("tytułu menu") && Options.ValidateMenuIdentity("", "gold_ingot").Contains("brak odczytu"), "title failure identifies the expected and actually read title");
        Check(Options.ValidateMenuIdentity("Bazar", null).Contains("Tytuł menu potwierdzony") && Options.ValidateMenuIdentity("Bazar", "iron_ingot").Contains("iron_ingot"), "bank icon failure does not blame the already confirmed title or grid");
        foreach (GoldDepositOptions invalid in new[]
        {
            Options with { Command = "" }, Options with { Command = "/bazar\n/other" }, Options with { MenuTitle = "!!!" },
            Options with { Rows = 0 }, Options with { Rows = 7 }, Options with { TargetSlot = 55 },
            Options with { Columns = 0 }, Options with { Columns = 10 }, Options with { Columns = 1, TargetSlot = 7 },
            Options with { OpenDelayMs = 499 }, Options with { Ingots = false, Blocks = false }
        })
            Check(new GoldDepositFlow(invalid, Start).Tick(Start).Action == GoldDepositAction.Stop, "invalid configuration fails before any input");

        var item = new DetectedInventoryItem(3, "gold_ingot", 64);
        GoldDepositObservation before = Screen(new[] { item });
        GoldDepositObservation carrying = Screen(Array.Empty<DetectedInventoryItem>(), item, false);
        var rejection = Open();
        rejection.Tick(rejection.NextAt, before);
        rejection.Tick(rejection.NextAt, carrying);
        Check(rejection.Tick(rejection.NextAt, carrying).Action == GoldDepositAction.None, "rejection waits for server latency, no second deposit click");
        Check(rejection.Tick(rejection.NextAt, carrying).Action == GoldDepositAction.None, "second rejection observation still sends no deposit click");
        Check(rejection.Tick(rejection.NextAt, carrying).Action == GoldDepositAction.ReturnRejected, "rejected gold returns to original source once");
        Check(rejection.Tick(rejection.NextAt, before).Action == GoldDepositAction.Stop && rejection.ConfirmedStacks == 0, "recovered rejection stops mining without a false success");

        foreach (string phase in new[] { "menu", "pickup", "deposit", "return" })
        {
            var flow = Open();
            if (phase != "menu") flow.Tick(flow.NextAt, before);
            if (phase is "deposit" or "return") flow.Tick(flow.NextAt, carrying);
            if (phase == "return") for (int i = 0; i < 3; i++) flow.Tick(flow.NextAt, carrying);
            GoldDepositStep step = default;
            for (int i = 0; i < 3 && !flow.Finished; i++) step = flow.Tick(flow.NextAt, Screen(Array.Empty<DetectedInventoryItem>(), valid: false));
            Check(step.Action == GoldDepositAction.Stop, "wrong GUI fails closed during " + phase);
        }
        var uncertain = Open();
        uncertain.Tick(uncertain.NextAt, before);
        uncertain.Tick(uncertain.NextAt, carrying);
        GoldDepositStep uncertainStep = default;
        for (int i = 0; i < 3; i++) uncertainStep = uncertain.Tick(uncertain.NextAt, Screen(Array.Empty<DetectedInventoryItem>(), empty: false));
        Check(uncertainStep.Action == GoldDepositAction.Stop && uncertain.ConfirmedStacks == 0, "unknown cursor is never a successful deposit");
        var wrongCount = Open();
        wrongCount.Tick(wrongCount.NextAt, before);
        wrongCount.Tick(wrongCount.NextAt, carrying);
        GoldDepositStep countStep = default;
        for (int i = 0; i < 3; i++) countStep = wrongCount.Tick(wrongCount.NextAt, Screen(new[] { item }, item, false));
        Check(countStep.Action == GoldDepositAction.Stop, "unexpected inventory quantities do not loop indefinitely");
        var timeout = Open();
        Check(timeout.Tick(Start.AddMinutes(4)).Action == GoldDepositAction.Stop, "global deadline bounds the procedure");
    }

    private static void CheckClickGuard()
    {
        var area = new Drawing.Rectangle(100, 80, 1280, 720);
        var point = new Drawing.Point(400, 300);
        foreach (int dx in new[] { -2, 0, 2 }) foreach (int dy in new[] { -2, 0, 2 })
            Check(GoldDepositClickGuard.ValidateGeometry(area, area, point, new Drawing.Point(point.X + dx, point.Y + dy), true, true) == "",
                "subpixel/physical rounding within two pixels stays inside the target slot");
        Check(GoldDepositClickGuard.ValidateGeometry(area, area, point, new Drawing.Point(403, 300), true, true).Contains("mysz"), "actual pointer movement stops before click");
        Check(GoldDepositClickGuard.ValidateGeometry(area, area with { X = 101 }, point, point, true, true).Contains("Okno"), "window move has a separate rejection reason");
        Check(GoldDepositClickGuard.ValidateGeometry(area, area, point, point, false, true).Contains("Menu zamknięto"), "closed GUI cannot receive a click");
        Check(GoldDepositClickGuard.ValidateGeometry(area, area, point, point, true, false).Contains("ramki"), "missing trusted frame cannot receive a click");
        foreach (string title in new[] { "Bazar & Aukcje (1/93)", "Bazar & Aukcje (1/94)", "Bazar & Aukcje (1/100)" })
            Check(Options.ValidateMenuTitle(title) == "", "live auction counters do not change the menu identity");
        Check(Options.ValidateMenuTitle("Sklep").Length > 0, "a different menu title still stops a click");

        var item = new DetectedInventoryItem(0, "gold_ingot", 16);
        GoldDepositFlow flow = Open();
        flow.Tick(flow.NextAt, Screen(new[] { item }));
        DateTime clicked = flow.NextAt.AddSeconds(1);
        flow.ClickExecuted(clicked);
        Check(flow.NextAt == clicked.AddMilliseconds(250), "server wait starts after the actual pickup click, including slow OCR validation");
        Check(flow.Tick(clicked.AddMilliseconds(249), Screen(Array.Empty<DetectedInventoryItem>(), item, false)).Action == GoldDepositAction.None, "pickup cannot be checked before the server wait ends");
        Check(flow.Tick(flow.NextAt, Screen(Array.Empty<DetectedInventoryItem>(), item, false)).Action == GoldDepositAction.Deposit, "confirmed pickup still produces one deposit action");
        clicked = flow.NextAt.AddSeconds(1);
        flow.ClickExecuted(clicked);
        Check(flow.NextAt == clicked.AddMilliseconds(350), "deposit verification also waits after its actual click");
        Check(flow.HasExpired(Start.AddMinutes(4)), "a slow click validation cannot bypass the overall deadline");
    }

    private static void CheckManualTestSession()
    {
        IntPtr window = new(101);
        var test = new GoldDepositManualTestSession(Options, window, Start);
        Check(test.ReadyAt == Start.AddSeconds(3) && test.Flow.NextAt == test.ReadyAt && test.Flow.Options == Options, "manual test snapshots options and waits three seconds before the same production flow");
        Check(test.RemainingSeconds(Start) == 3 && test.RemainingSeconds(Start.AddMilliseconds(2500)) == 1, "manual test countdown is explicit");
        Check(test.Tick(Start, window, false, false, false) == GoldDepositTestDecision.Countdown && !test.Started, "opening the test in Helper is allowed during countdown without game input");
        Check(test.Tick(test.ReadyAt.AddTicks(-1), window, true, false, false) == GoldDepositTestDecision.Countdown && test.Flow.Tick(Start).Action == GoldDepositAction.None, "neither focus nor timer polling can start input early");
        Check(test.Tick(test.ReadyAt, window, true, false, false) == GoldDepositTestDecision.Run && test.Started, "manual flow starts only at deadline with the selected window focused");
        Check(test.Flow.Tick(test.ReadyAt).Action == GoldDepositAction.CloseInitialGui, "manual mode uses the actual deposit flow rather than a separate click sequence");
        Check(test.Tick(test.ReadyAt.AddMilliseconds(1), window, false, false, false) == GoldDepositTestDecision.Cancelled, "focus loss immediately cancels an active test");
        Check(test.Tick(test.ReadyAt.AddSeconds(1), window, true, false, false) == GoldDepositTestDecision.Cancelled, "refocusing cannot silently resume a cancelled test");
        foreach (bool beforeStart in new[] { true, false })
        {
            var stop = new GoldDepositManualTestSession(Options, window, Start);
            DateTime when = beforeStart ? Start : stop.ReadyAt;
            Check(stop.Tick(when, window, true, true, false) == GoldDepositTestDecision.Cancelled && stop.StopReason.Contains("STOP"), "STOP cancels both countdown and running session");
        }
        Check(new GoldDepositManualTestSession(Options, window, Start).Tick(Start, new IntPtr(102), true, false, false) == GoldDepositTestDecision.Cancelled, "changing the selected window cancels the test before any input");
        Check(new GoldDepositManualTestSession(Options, window, Start).Tick(Start, window, true, false, true) == GoldDepositTestDecision.Cancelled, "the test cannot overlap mining or reconnect");
        Check(new GoldDepositManualTestSession(Options, IntPtr.Zero, Start).Tick(Start, IntPtr.Zero, true, false, false) == GoldDepositTestDecision.Cancelled, "missing target window never arms input");
    }

    private static ZipArchive? _pack;

    // Tekstury z paczki, którą buduje sam Kopacz (MarkerResourcePack) z 1.8.8.jar gry —
    // test sprawdza więc detektory na dokładnie tym, co trafia do resourcepacks.
    private static ZipArchive Pack()
    {
        if (_pack != null)
            return _pack;
        string jar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "versions", "1.8.8", "1.8.8.jar");
        // Repozytorium nie zawiera tekstur Minecrafta (EULA) — test buduje paczkę z gry na tym komputerze.
        if (File.Exists(jar))
        {
            Type installer = typeof(ProductionWindow).Assembly.GetType("MinecraftHelper.Services.ResourcePackInstaller")!;
            byte[] zip = (byte[])installer.GetMethod("BuildFromJar", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { jar })!;
            _pack = new ZipArchive(new MemoryStream(zip));
        }
        else
        {
            throw new FileNotFoundException("Test potrzebuje prawdziwych tekstur 1.8.8: uruchom raz Minecrafta 1.8.8 z launchera (versions\\1.8.8\\1.8.8.jar).");
        }
        return _pack;
    }

    private static Drawing.Bitmap Texture(string path)
    {
        ZipArchiveEntry entry = Pack().GetEntry(path) ?? throw new FileNotFoundException(path);
        using Stream stream = entry.Open();
        using var original = new Drawing.Bitmap(stream);
        return new Drawing.Bitmap(original);
    }

    private static void DrawItem(Drawing.Bitmap frame, Drawing.Bitmap item, Drawing.Point center, int scale)
    {
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            Drawing.Color color = item.GetPixel(x, y);
            if (color.A < 128) continue;
            for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++)
                frame.SetPixel(center.X - 8 * scale + x * scale + sx, center.Y - 8 * scale + y * scale + sy, color);
        }
    }

    private static void CheckDetector()
    {
        using Drawing.Bitmap ingot = Texture("assets/minecraft/textures/items/gold_ingot.png");
        using Drawing.Bitmap block = Texture("assets/minecraft/textures/blocks/gold_block.png");
        foreach (int scale in new[] { 1, 2, 3, 4 })
        foreach (int rows in new[] { 1, 3, 6 })
        {
            int width = 176 * scale + 160, height = (114 + rows * 18) * scale + 100;
            using var frame = new Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var expected = new ChestMarkerLayout(80, 50, scale, rows);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(frame))
            {
                graphics.Clear(Drawing.Color.DarkGreen);
                graphics.FillRectangle(Drawing.Brushes.Black, expected.Left + 3 * scale, expected.Top, 170 * scale, scale);
                graphics.FillRectangle(Drawing.Brushes.White, expected.Left + 3 * scale, expected.Top + scale, 170 * scale, scale);
                for (int slot = 0; slot < 36; slot++)
                {
                    Drawing.Point center = expected.PlayerInventory.GetSlotCenter(slot);
                    int x = center.X - 8 * scale, y = center.Y - 8 * scale;
                    using var interior = new Drawing.SolidBrush(Drawing.Color.FromArgb(139, 139, 139));
                    using var edge = new Drawing.SolidBrush(Drawing.Color.FromArgb(55, 55, 55));
                    using var highlight = new Drawing.SolidBrush(Drawing.Color.White);
                    graphics.FillRectangle(interior, x, y, 16 * scale, 16 * scale);
                    graphics.FillRectangle(edge, x - scale, y - scale, 18 * scale, scale);
                    graphics.FillRectangle(edge, x - scale, y - scale, scale, 18 * scale);
                    graphics.FillRectangle(highlight, x + 16 * scale, y - scale, scale, 18 * scale);
                    graphics.FillRectangle(highlight, x - scale, y + 16 * scale, 18 * scale, scale);
                }
            }
            DrawItem(frame, ingot, expected.PlayerInventory.GetSlotCenter(0), scale);
            DrawItem(frame, block, expected.PlayerInventory.GetSlotCenter(26), scale);
            DrawItem(frame, ingot, expected.PlayerInventory.GetSlotCenter(27), scale);
            DrawItem(frame, block, expected.PlayerInventory.GetSlotCenter(35), scale);
            Check(InventoryMarkerDetector.TryDetectChest(frame, rows, out ChestMarkerLayout found) && found.Scale == scale && found.Rows == rows
                && Math.Abs(found.Left - expected.Left) < scale && Math.Abs(found.Top - expected.Top) < scale, "chest/player grid geometry at scale " + scale + " rows " + rows);
            var items = InventoryMarkerDetector.ReadMarkedItems(frame, found.PlayerInventory, true);
            Check(items.Count(i => i.ItemId == "gold_ingot") == 2 && items.Count(i => i.ItemId == "gold_block") == 2, "actual TXT gold textures detected in EQ and hotbar");
            Check(InventoryMarkerDetector.ReadMarkedItems(frame, found.PlayerInventory, false).Count == 2, "hotbar exclusion leaves main inventory");
            Check(found.GetMenuSlotCenter(11) == new Drawing.Point(found.Left + 34 * scale, found.Top + 44 * scale), "bank slot 11 is row two, column two");
            Check(InventoryMarkerDetector.TryDetectChest(frame, rows, out ChestMarkerLayout compact, columns: 3)
                && compact.Columns == 3 && compact.PlayerInventory == found.PlayerInventory, "compact menu width leaves player inventory geometry unchanged");
            Check(compact.GetMenuSlotCenter(2) == new Drawing.Point(compact.Left + 87 * scale, compact.Top + 26 * scale), "three-column menu centre matches the home menu geometry");
            if (rows >= 3)
                Check(compact.GetMenuSlotCenter(5) == new Drawing.Point(compact.Left + 87 * scale, compact.Top + 44 * scale), "compact slot numbering uses the configured width when moving to the next row");
        }
        using var world = new Drawing.Bitmap(640, 480);
        using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(world)) graphics.Clear(Drawing.Color.Gray);
        Check(!InventoryMarkerDetector.TryDetectChest(world, 6, out _), "flat/world image cannot authorize container clicks");
        using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(world)) graphics.Clear(Drawing.Color.FromArgb(198, 198, 198));
        Check(!InventoryMarkerDetector.TryDetectChest(world, 6, out _), "flat light-gray GUI background is not a slot grid");
        using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(world)) graphics.Clear(Drawing.Color.FromArgb(55, 55, 55));
        Check(!InventoryMarkerDetector.TryDetectChest(world, 6, out _), "flat dark-gray background is not a slot grid");
        Check(!InventoryMarkerDetector.TryDetectChest(world, 0, out _), "invalid chest rows rejected");
        Check(!InventoryMarkerDetector.TryDetectChest(world, 6, out _, columns: 0) && !InventoryMarkerDetector.TryDetectChest(world, 6, out _, columns: 10), "invalid chest columns rejected");
    }

    private static void CheckCursorQuantities()
    {
        using Drawing.Bitmap item = Texture("assets/minecraft/textures/items/gold_ingot.png");
        using Drawing.Bitmap font = Texture("assets/minecraft/textures/font/ascii.png");
        foreach (int scale in new[] { 1, 2, 3 })
        foreach (int background in new[] { 139, 198 })
        foreach (int quantity in Enumerable.Range(1, 64))
        {
            using var frame = new Drawing.Bitmap(64 * scale, 64 * scale);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(frame)) graphics.Clear(Drawing.Color.FromArgb(background, background, background));
            Drawing.Point center = new(32 * scale, 32 * scale);
            DrawItem(frame, item, center, scale);
            if (quantity > 1)
            {
                string text = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
                int startX = 24 + 17 - text.Length * 6;
                for (int shadow = 1; shadow >= 0; shadow--)
                for (int index = 0; index < text.Length; index++)
                for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                {
                    int code = text[index];
                    if (font.GetPixel(code % 16 * 8 + x, code / 16 * 8 + y).A < 128) continue;
                    for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++)
                        frame.SetPixel((startX + index * 6 + x + shadow) * scale + dx, (24 + 9 + y + shadow) * scale + dy,
                            shadow == 1 ? Drawing.Color.FromArgb(63, 63, 63) : Drawing.Color.White);
                }
            }
            DetectedInventoryItem? found = InventoryMarkerDetector.ReadMarkedItem(frame, new Drawing.Point(24 * scale, 24 * scale), scale);
            Check(found is { ItemId: "gold_ingot" } && found.Value.Quantity == quantity,
                $"actual gold/font quantity {quantity}, scale {scale}, background {background}; read {found?.Quantity}");
        }
    }

    private static void CheckRealChestTexture()
    {
        using Drawing.Bitmap gui = Texture("assets/minecraft/textures/gui/container/generic_54.png");
        foreach (int scale in new[] { 1, 2, 3, 4 })
        foreach (int rows in new[] { 3, 5, 6 })
        foreach (bool shifted in new[] { false, true })
        {
            int width = 176 * scale + 163, height = (114 + rows * 18) * scale + 103;
            using var frame = new Drawing.Bitmap(width, height);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(frame)) graphics.Clear(Drawing.Color.DarkGreen);
            int left = (((width + scale - 1) / scale - 176) / 2) * scale + (shifted ? 3 * scale : 0);
            int top = (((height + scale - 1) / scale - (114 + rows * 18)) / 2) * scale - (shifted ? scale : 0);
            for (int y = 0; y < 114 + rows * 18; y++)
            for (int x = 0; x < 176; x++)
            {
                int sourceY = y < rows * 18 + 17 ? y : 126 + y - (rows * 18 + 17);
                Drawing.Color color = gui.GetPixel(x, sourceY);
                for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++)
                    frame.SetPixel(left + x * scale + dx, top + y * scale + dy, color);
            }
            bool detected = InventoryMarkerDetector.TryDetectChest(frame, rows, out ChestMarkerLayout found);
            Check(detected && found.Scale == scale && Math.Abs(found.Left - left) < scale && Math.Abs(found.Top - top) < scale,
                $"real bundled chest texture and Minecraft scaled-resolution rounding: rows {rows}, scale {scale}");
            for (int otherRows = 1; otherRows <= 6; otherRows++)
                if (otherRows != rows)
                    Check(!InventoryMarkerDetector.TryDetectChest(frame, otherRows, out _), $"real {rows}-row menu does not match {otherRows} rows at scale {scale}");
            Check(InventoryMarkerDetector.TryDetectChestMenu(frame, 9, 2, out ChestMarkerLayout automatic) && automatic.Rows == rows,
                "actual menu height is detected independently from the slot picker height");
            Check(!InventoryMarkerDetector.TryDetectChestMenu(frame, 9, rows + 1, out _), "a selected field below the detected menu is rejected");
            Type optionType = typeof(ProductionWindow).Assembly.GetType("MinecraftHelper.Services.GoldDepositOptions")!;
            object configured = Activator.CreateInstance(optionType, new object[] { "/bazar", "Bazar", 5, 11, 1000, true, true, true, 9 })!;
            object?[] arguments = { frame, configured, null };
            bool hostFound = (bool)typeof(ProductionWindow).GetMethod("TryDetectGoldMenuLayout", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
            Check(hostFound && (int)arguments[2]!.GetType().GetProperty("Rows")!.GetValue(arguments[2])! == rows,
                "production adapter accepts slot 11 in a five-row picker while using the actual menu height");
            using var tooltip = new Drawing.Bitmap(frame);
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(tooltip))
            {
                // Moving over an item can cover the inventory borders with a
                // tooltip, while the trusted container itself remains open.
                Drawing.Point first = automatic.PlayerInventory.GetSlotCenter(0);
                graphics.FillRectangle(Drawing.Brushes.Black, first.X - 9 * scale, first.Y - 9 * scale, 162 * scale, 77 * scale);
            }
            Check(!InventoryMarkerDetector.TryDetectChest(tooltip, rows, out _) && InventoryMarkerDetector.IsChestHeaderVisible(tooltip, automatic),
                "hover tooltip may hide the grid without invalidating the already verified frame before click");
            using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(tooltip))
                graphics.FillRectangle(Drawing.Brushes.Red, automatic.Left, automatic.Top, 176 * scale, 2 * scale);
            Check(!InventoryMarkerDetector.IsChestHeaderVisible(tooltip, automatic), "replacement of the trusted header remains a hard stop");
        }
    }

    private static void CheckSettings()
    {
        var settings = new AppSettings();
        Check(!settings.GoldDepositEnabled && settings.GoldDepositCommand == "" && settings.GoldDepositColumns == 9, "old/fresh settings use nine columns and do not enable transfers automatically");
        settings.GoldDepositEnabled = true; settings.GoldDepositCommand = "/bank";
        settings.GoldDepositRows = 6; settings.GoldDepositColumns = 5; settings.GoldDepositTargetSlot = 11; settings.GoldDepositIncludeHotbar = false;
        AppSettings saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Check(saved.GoldDepositEnabled && saved.GoldDepositCommand == "/bank" && saved.GoldDepositColumns == 5 && saved.GoldDepositTargetSlot == 11 && !saved.GoldDepositIncludeHotbar, "gold options round-trip without user files");
        AppSettings legacy = JsonSerializer.Deserialize<AppSettings>("{\"GoldDepositRows\":6,\"GoldDepositTargetSlot\":11}")!;
        Check(legacy.GoldDepositColumns == 9 && legacy.GoldDepositTargetSlot == 11, "settings from before width selection retain their original bank slot");
    }

    private static void CheckResetAndIntegration()
    {
        Type type = typeof(ProductionWindow);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object host = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_goldDepositPending", flags)!.SetValue(host, true);
        type.GetField("_goldDepositBusy", flags)!.SetValue(host, true);
        type.GetField("_goldDepositEmptyCursor", flags)!.SetValue(host, new byte[] { 1 });
        FieldInfo click = type.GetField("_goldDepositClickAction", flags)!;
        click.SetValue(host, Enum.Parse(click.FieldType, "Deposit"));
        type.GetMethod("ResetGoldDepositState", flags)!.Invoke(host, null);
        Check(!(bool)type.GetField("_goldDepositPending", flags)!.GetValue(host)! && Convert.ToInt32(click.GetValue(host)) == 0, "STOP cancels pending transfer and deferred click");
        Check(type.GetField("_goldDepositEmptyCursor", flags)!.GetValue(host) == null && (bool)type.GetField("_goldDepositBusy", flags)!.GetValue(host)!, "reset discards cursor reference but does not unlock a still-running OCR task");
        PropertyInfo active = type.GetProperty("IsGoldDepositActive", flags)!;
        FieldInfo cleanupStage = type.GetField("_inventoryCleanupStage", flags)!;
        cleanupStage.SetValue(host, Enum.Parse(cleanupStage.FieldType, "DepositGold"));
        Check((bool)active.GetValue(host)!, "a scheduled deposit is active even before the first OCR task");
        foreach (string miner in new[] { "533", "633" })
        {
            object miningHost = RuntimeHelpers.GetUninitializedObject(type);
            foreach (string name in new[] { "_inventoryCleanupTargets", "_inventoryCleanupInitialItemTypeCounts", "_inventoryCleanupInitialItemTypeStackCounts", "_inventoryCleanupItemTypeCounts", "_inventoryCleanupItemTypeStackCounts" })
            {
                FieldInfo field = type.GetField(name, flags)!;
                field.SetValue(miningHost, Activator.CreateInstance(field.FieldType));
            }
            FieldInfo owner = type.GetField("_inventoryCleanupOwner", flags)!;
            object ownerValue = Enum.Parse(owner.FieldType, "Kopacz" + miner);
            owner.SetValue(miningHost, ownerValue);
            cleanupStage.SetValue(miningHost, Enum.Parse(cleanupStage.FieldType, "DepositGold"));
            type.GetField("_kopacz" + miner + "RuntimeEnabled", flags)!.SetValue(miningHost, true);
            type.GetField("_inventoryCleanupGeneration", flags)!.SetValue(miningHost, 10);
            type.GetField("_goldDepositPending", flags)!.SetValue(miningHost, true);
            type.GetField("_goldDepositBusy", flags)!.SetValue(miningHost, true);
            click.SetValue(miningHost, Enum.Parse(click.FieldType, "Deposit"));
            type.GetMethod("StopMiningRuntime", flags)!.Invoke(miningHost, new[] { ownerValue });
            Check(!(bool)type.GetField("_kopacz" + miner + "RuntimeEnabled", flags)!.GetValue(miningHost)!, miner + " stops during a deferred gold transfer");
            Check(!(bool)active.GetValue(miningHost)! && Convert.ToInt32(click.GetValue(miningHost)) == 0, miner + " cannot resume or send the deferred deposit click after STOP");
            Check((int)type.GetField("_inventoryCleanupGeneration", flags)!.GetValue(miningHost)! > 10 && (bool)type.GetField("_goldDepositBusy", flags)!.GetValue(miningHost)!, miner + " invalidates delayed results without unlocking OCR early");
        }
        var matches = type.GetMethod("GoldPatchMatches", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((bool)matches.Invoke(null, new object[] { new byte[256], new byte[256] })!, "identical cursor background is empty");
        Check(!(bool)matches.Invoke(null, new object[] { new byte[256], Enumerable.Repeat((byte)255, 256).ToArray() })!, "carried/different cursor image is not empty");
        string main = File.ReadAllText(Path.Combine(Root, "MinecraftHelper/MainWindow.xaml.cs"));
        string adapter = File.ReadAllText(Path.Combine(Root, "MinecraftHelper/MainWindow.GoldDeposit.cs"));
        Check(main.Contains("!GoldDepositOptions.IsGold(item.ItemId) && item.ItemId != \"other\""), "gold and unknown items are protected from drops");
        Check(main.Contains("if (_goldDepositPending && ChkGoldDepositEnabled.IsChecked == true) return InventoryCleanupStage.DepositGold;"), "gold runs after CX and before food/resume, only while enabled");
        Check(adapter.Contains("generation != _inventoryCleanupGeneration") && adapter.Contains("!ReferenceEquals(flow, _goldDepositFlow)"), "late scan cannot act after STOP or a new flow");
        Check(adapter.Contains("IsGameInputAvailableFor(_targetGameWindowHandle)") && adapter.Contains("!GoldInputAllowed() || !SendMouseClick"), "focus (or background mining) is rechecked at input boundary");
        int focusGuard = main.IndexOf("if (!backgroundInput && (!_isMinecraftFocused || !targetWindowFocusedNow))", StringComparison.Ordinal);
        int focusStop = main.IndexOf("if (IsGoldDepositActive)", focusGuard, StringComparison.Ordinal);
        int returnGuard = main.IndexOf("else if (_inventoryCleanupStage == InventoryCleanupStage.ReturnToMiningStart)", focusGuard, StringComparison.Ordinal);
        Check(focusStop > focusGuard && focusStop < returnGuard, "outer focus guard stops the gold transaction before pausing other mining stages");
        string eqToggle = main[main.IndexOf("private void ChkInventoryCleanupEnabled_Changed", StringComparison.Ordinal)..main.IndexOf("private void ChkCobbleXEnabled_Changed", StringComparison.Ordinal)];
        Check(eqToggle.Contains("if (IsGoldDepositActive)") && eqToggle.Contains("StopGoldDeposit("), "disabling Auto EQ stops mining during a gold transfer");
    }

    private static void CheckGuiResources()
    {
        // Load the actual panel and styles through WPF. A build alone does not
        // resolve StaticResource names. Event handlers are omitted so this test
        // creates controls without running the application or touching AppData.
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement source = XElement.Load(Path.Combine(Root, "MinecraftHelper/MainWindow.xaml"));
        XElement panel = source.Descendants().Single(e => (string?)e.Attribute("Name") == "BorderGoldDepositSection");
        XElement markup = new(wpf + "Grid", new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName),
            new XElement(wpf + "Grid.Resources", source.Element(wpf + "Window.Resources")!.Elements().Select(e => new XElement(e))),
            new XElement(panel));
        foreach (XAttribute attribute in markup.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "Checked" or "Unchecked" or "TextChanged" or "Click").ToArray()) attribute.Remove();
        var view = (System.Windows.FrameworkElement)System.Windows.Markup.XamlReader.Parse(markup.ToString());
        var settings = (System.Windows.FrameworkElement)view.FindName("PanelGoldDepositSettings");
        Type type = typeof(ProductionWindow);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object host = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_isLoadingUi", flags)!.SetValue(host, true);
        foreach (string name in new[] { "ChkGoldDepositEnabled", "PanelGoldDepositSettings", "PanelGoldDepositConfiguration", "TxtGoldDepositCommand", "TxtGoldDepositTitle", "TxtGoldDepositDelay", "ChkGoldDepositIngots", "ChkGoldDepositBlocks", "ChkGoldDepositHotbar", "BorderGoldDepositSlots", "PanelGoldDepositSlots", "TxtGoldDepositSelectedSlot", "TxtGoldDepositGridSize", "BtnGoldDepositAddRow", "BtnGoldDepositRemoveRow", "BtnGoldDepositAddColumn", "BtnGoldDepositRemoveColumn", "BtnGoldDepositReadTest", "BtnGoldDepositRunTest", "BtnGoldDepositStopTest", "TxtGoldDepositStatus" })
            type.GetField(name, flags)!.SetValue(host, view.FindName(name));
        type.GetField("ChkInventoryCleanupEnabled", flags)!.SetValue(host, new System.Windows.Controls.CheckBox { IsChecked = true });
        FieldInfo modelField = type.GetField("_settings", flags)!;
        object model = Activator.CreateInstance(modelField.FieldType)!;
        model.GetType().GetProperty("GoldDepositEnabled")!.SetValue(model, true);
        modelField.SetValue(host, model);
        type.GetMethod("LoadGoldDepositSettings", flags)!.Invoke(host, null);
        var slots = (System.Windows.Controls.Primitives.UniformGrid)view.FindName("PanelGoldDepositSlots");
        var selected = (System.Windows.Controls.TextBlock)view.FindName("TxtGoldDepositSelectedSlot");
        var add = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositAddRow");
        var remove = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositRemoveRow");
        Check(slots.Rows == 6 && slots.Columns == 9 && slots.Children.Count == 54 && selected.Text == "11 (rząd 2, kolumna 2)", "saved bank slot is loaded and described in the visual menu grid");
        ((System.Windows.Controls.Button)slots.Children[53]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        object options = type.GetMethod("GoldOptions", flags)!.Invoke(host, null)!;
        Check((int)options.GetType().GetProperty("TargetSlot")!.GetValue(options)! == 54 && selected.Text.Contains("rząd 6, kolumna 9"), "clicking a visual slot updates the real transfer target");
        Check(slots.Children.Cast<System.Windows.Controls.Button>().Count(b => b.BorderThickness.Left == 2) == 1, "exactly one bank slot is highlighted");
        type.GetMethod("SaveGoldDepositSettings", flags)!.Invoke(host, null);
        Check((int)model.GetType().GetProperty("GoldDepositTargetSlot")!.GetValue(model)! == 54, "clicked target is saved to the settings model");
        for (int i = 0; i < 8; i++) type.GetMethod("BtnGoldDepositRemoveRow_Click", flags)!.Invoke(host, new object[] { remove, new System.Windows.RoutedEventArgs() });
        type.GetMethod("SaveGoldDepositSettings", flags)!.Invoke(host, null);
        Check(slots.Rows == 1 && slots.Children.Count == 9 && !remove.IsEnabled, "row removal stops at one row");
        Check((int)model.GetType().GetProperty("GoldDepositTargetSlot")!.GetValue(model)! == 9 && (int)model.GetType().GetProperty("GoldDepositRows")!.GetValue(model)! == 1, "removing the selected row keeps the saved target inside the grid");
        for (int i = 0; i < 8; i++) type.GetMethod("BtnGoldDepositAddRow_Click", flags)!.Invoke(host, new object[] { add, new System.Windows.RoutedEventArgs() });
        Check(slots.Rows == 6 && slots.Children.Count == 54 && !add.IsEnabled && remove.IsEnabled, "row addition stops at six rows");
        model.GetType().GetProperty("GoldDepositRows")!.SetValue(model, 3);
        model.GetType().GetProperty("GoldDepositTargetSlot")!.SetValue(model, 54);
        type.GetMethod("LoadGoldDepositSettings", flags)!.Invoke(host, null);
        Check(slots.Rows == 3 && slots.Children.Count == 27 && selected.Text == "27 (rząd 3, kolumna 9)", "older invalid row/target combinations load within bounds");
        var addColumn = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositAddColumn");
        var removeColumn = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositRemoveColumn");
        var border = (System.Windows.Controls.Border)view.FindName("BorderGoldDepositSlots");
        type.GetMethod("BtnGoldDepositRemoveColumn_Click", flags)!.Invoke(host, new object[] { removeColumn, new System.Windows.RoutedEventArgs() });
        type.GetMethod("SaveGoldDepositSettings", flags)!.Invoke(host, null);
        options = type.GetMethod("GoldOptions", flags)!.Invoke(host, null)!;
        Check(slots.Columns == 8 && slots.Children.Count == 24 && border.Width == 332 && selected.Text == "24 (rząd 3, kolumna 8)", "right-hand minus changes grid width and keeps selection within bounds");
        Check((int)model.GetType().GetProperty("GoldDepositColumns")!.GetValue(model)! == 8 && (int)model.GetType().GetProperty("GoldDepositTargetSlot")!.GetValue(model)! == 24
            && (int)options.GetType().GetProperty("Columns")!.GetValue(options)! == 8, "changed width is saved and forwarded to the real deposit action");
        for (int i = 0; i < 12; i++) type.GetMethod("BtnGoldDepositRemoveColumn_Click", flags)!.Invoke(host, new object[] { removeColumn, new System.Windows.RoutedEventArgs() });
        Check(slots.Columns == 1 && slots.Children.Count == 3 && !removeColumn.IsEnabled && addColumn.IsEnabled, "column removal stops at one column");
        for (int i = 0; i < 12; i++) type.GetMethod("BtnGoldDepositAddColumn_Click", flags)!.Invoke(host, new object[] { addColumn, new System.Windows.RoutedEventArgs() });
        Check(slots.Columns == 9 && slots.Children.Count == 27 && !addColumn.IsEnabled && removeColumn.IsEnabled && border.Width == 372, "column addition stops at nine columns");
        settings.Visibility = System.Windows.Visibility.Visible;
        view.Measure(new System.Windows.Size(900, double.PositiveInfinity));
        view.Arrange(new System.Windows.Rect(0, 0, 900, view.DesiredSize.Height));
        Check(addColumn.TranslatePoint(new System.Windows.Point(), view).X >= border.TranslatePoint(new System.Windows.Point(), view).X + border.ActualWidth
            && removeColumn.TranslatePoint(new System.Windows.Point(), view).X >= border.TranslatePoint(new System.Windows.Point(), view).X + border.ActualWidth,
            "width controls are laid out to the right of the menu grid");
        Check(view.FindName("ChkGoldDepositEnabled") is System.Windows.Controls.CheckBox, "gold panel loads with its real WPF styles and tooltip resources");
        Check(view.FindName("TxtGoldDepositCommand") is System.Windows.Controls.TextBox && view.DesiredSize.Height > 0, "expanded deposit settings can be laid out without startup errors");
        var readTest = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositReadTest");
        var runTest = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositRunTest");
        var stopTest = (System.Windows.Controls.Button)view.FindName("BtnGoldDepositStopTest");
        Check(readTest.IsEnabled && runTest.IsEnabled && !stopTest.IsEnabled, "read-only and real-flow test buttons are available while idle");
        FieldInfo manualField = type.GetField("_goldDepositManualTest", flags)!;
        options = type.GetMethod("GoldOptions", flags)!.Invoke(host, null)!;
        object session = Activator.CreateInstance(manualField.FieldType, new object[] { options, new IntPtr(101), Start })!;
        manualField.SetValue(host, session);
        type.GetField("_goldDepositFlow", flags)!.SetValue(host, session.GetType().GetProperty("Flow")!.GetValue(session));
        type.GetMethod("UpdateGoldDepositTestButtons", flags)!.Invoke(host, null);
        Check(!readTest.IsEnabled && !runTest.IsEnabled && stopTest.IsEnabled && !((System.Windows.FrameworkElement)view.FindName("PanelGoldDepositConfiguration")).IsEnabled, "real-flow test locks settings and competing tests while keeping STOP enabled");
        type.GetMethod("ResetGoldDepositState", flags)!.Invoke(host, null);
        type.GetMethod("UpdateGoldDepositTestButtons", flags)!.Invoke(host, null);
        Check(manualField.GetValue(host) == null && readTest.IsEnabled && runTest.IsEnabled && !stopTest.IsEnabled, "reset cancels manual test and restores idle controls without starting mining");
        type.GetField("_goldDepositReadTestActive", flags)!.SetValue(host, true);
        type.GetField("_goldDepositReadTestGeneration", flags)!.SetValue(host, 10);
        type.GetMethod("CancelGoldDepositReadTest", flags)!.Invoke(host, null);
        Check(!(bool)type.GetField("_goldDepositReadTestActive", flags)!.GetValue(host)! && (int)type.GetField("_goldDepositReadTestGeneration", flags)!.GetValue(host)! == 11,
            "STOP invalidates late read-only results so they cannot overwrite a newer real-flow test");
        foreach (string miner in new[] { "533", "633" })
        {
            FieldInfo owner = type.GetField("_inventoryCleanupOwner", flags)!;
            owner.SetValue(host, Enum.Parse(owner.FieldType, "Kopacz" + miner));
            type.GetField("_kopacz" + miner + "RuntimeEnabled", flags)!.SetValue(host, true);
            type.GetField("_goldDepositPending", flags)!.SetValue(host, true);
            type.GetField("_inventoryCleanupEatAfterCleanupPending", flags)!.SetValue(host, true);
            var chooseStage = type.GetMethod("GetInventoryCleanupPostCommandStage", flags)!;
            Check(chooseStage.Invoke(host, null)!.ToString() == "DepositGold", miner + " runs detected gold after cleanup/CX and before eating");
            type.GetField("_goldDepositPending", flags)!.SetValue(host, false);
            Check(chooseStage.Invoke(host, null)!.ToString() == "SelectFoodSlot", miner + " proceeds to eating after confirmed gold deposit");
            type.GetField("_inventoryCleanupEatAfterCleanupPending", flags)!.SetValue(host, false);
            Check(chooseStage.Invoke(host, null)!.ToString() == "ResumeMining" && (bool)type.GetField("_kopacz" + miner + "RuntimeEnabled", flags)!.GetValue(host)!, miner + " keeps its original channel active for resumption after deposit");
            ((System.Windows.Controls.CheckBox)view.FindName("ChkGoldDepositEnabled")).IsChecked = false;
            type.GetField("_goldDepositPending", flags)!.SetValue(host, true);
            Check(chooseStage.Invoke(host, null)!.ToString() == "ResumeMining", miner + " skips pending deposit when the gold option is disabled");
            ((System.Windows.Controls.CheckBox)view.FindName("ChkGoldDepositEnabled")).IsChecked = true;
            type.GetField("_kopacz" + miner + "RuntimeEnabled", flags)!.SetValue(host, false);
        }
    }
}
