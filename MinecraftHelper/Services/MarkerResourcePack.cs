using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MinecraftHelper.Services;

// Paczka zasobów ze znacznikami dla Auto EQ, CobbleX, kontroli kilofa, złota i reconnectu.
// Kopacz buduje ją sam z gry użytkownika (1.8.8.jar), więc w repozytorium nie ma plików Mojanga.
// Paczka zawiera pełny domyślny wygląd 1.8.8 (tekstury, modele, stany bloków), bo detektory
// rozpoznają domyślny cobblestone, cyfry stacków, ramki EQ i skrzyni — inna paczka pod spodem
// nie może ich zmienić. Znaczniki muszą być identyczne z tym, czego szukają InventoryMarkerDetector i detektory GUI.
internal static class MarkerResourcePack
{
    public const string FileName = "MinecraftHelper_AutoEQ_CobbleX_1.8.8.zip";
    public const string Description = "MinecraftHelper AutoEQ + CobbleX (1.8.8)";
    // Zmień przy każdej zmianie znaczników albo zawartości — starsze paczki zostaną odbudowane.
    public const int GeneratorVersion = 1;
    public const string ManifestEntry = "minecraft-helper-markers.json";

    private const string TextureRoot = "assets/minecraft/textures/";
    private const int GuiWidth = 176;
    private const int GuiHeight = 166;
    private const int GuiTopLeftX = 1;
    private const int GuiTopLeftY = 1;
    private const int GuiBottomRightX = 171;
    private const int GuiBottomRightY = 162;
    private const int ItemMarkerX = 13;
    private const int ItemMarkerY = 0;

    private static readonly uint Magenta = Rgba(255, 0, 255);
    private static readonly uint Cyan = Rgba(0, 255, 255);
    private static readonly uint Yellow = Rgba(255, 255, 0);

    internal sealed record MarkedItem(string Id, string File, string Label, int MarkerCode);

    internal sealed record SolidBlock(string Id, string File, string Label, byte R, byte G, byte B);

    public static IReadOnlyList<MarkedItem> DiscardItems { get; } = new MarkedItem[]
    {
        new("diamond", "diamond.png", "diament", 0),
        new("gold_ingot", "gold_ingot.png", "sztabka zlota", 1),
        new("iron_ingot", "iron_ingot.png", "sztabka zelaza", 2),
        new("apple", "apple.png", "jablko", 4),
        new("gunpowder", "gunpowder.png", "proch", 6),
        new("emerald", "emerald.png", "emerald", 7),
        new("coal", "coal.png", "wegiel", 8),
        new("quartz", "quartz.png", "kwarc", 9),
        new("book", "book_normal.png", "zwykla ksiazka", 10),
        new("ender_pearl", "ender_pearl.png", "ender perla", 11),
        new("redstone", "redstone_dust.png", "redstone", 12)
    };

    // Znacznik obecności: kontrola kilofa, nigdy nie jest wyrzucany.
    public static IReadOnlyList<MarkedItem> PresenceItems { get; } = new MarkedItem[]
    {
        new("diamond_pickaxe", "diamond_pickaxe.png", "diamentowy kilof", 13)
    };

    public static IReadOnlyList<SolidBlock> SolidBlocks { get; } = new SolidBlock[]
    {
        new("obsidian", "obsidian.png", "obsydian", 60, 48, 86),
        new("sand", "sand.png", "piasek", 255, 201, 14),
        new("gold_block", "gold_block.png", "blok zlota", 255, 242, 0),
        new("iron_block", "iron_block.png", "blok zelaza", 232, 232, 232),
        new("emerald_block", "emerald_block.png", "blok emeraldu", 131, 237, 161)
    };

    // Chroniony cobblestone: jednolity róż, którego szuka ochrona cobble w Auto EQ
    // (InventoryMarkerDetector.LooksLikeCobblestone) — nigdy nie jest wyrzucany.
    public static IReadOnlyList<SolidBlock> ProtectedBlocks { get; } = new SolidBlock[]
    {
        new("cobblestone", "cobblestone.png", "cobblestone (chroniony)", 255, 174, 201)
    };

    // Stałe etykiety ekranów łączenia/rozłączenia — reconnect rozpoznaje je bez zgadywania języka.
    private static readonly (string Key, string Value)[] LanguageOverrides =
    {
        ("connect.authorizing", "[MH] AUTHORIZING"),
        ("connect.connecting", "[MH] CONNECTING"),
        ("connect.failed", "[MH] CONNECTION FAILED"),
        ("disconnect.closed", "[MH] CONNECTION LOST"),
        ("disconnect.disconnected", "[MH] CONNECTION LOST"),
        ("disconnect.endOfStream", "[MH] CONNECTION LOST"),
        ("disconnect.kicked", "[MH] KICKED"),
        ("disconnect.loginFailed", "[MH] LOGIN FAILED"),
        ("disconnect.lost", "[MH] CONNECTION LOST"),
        ("disconnect.overflow", "[MH] CONNECTION LOST"),
        ("disconnect.spam", "[MH] KICKED"),
        ("disconnect.timeout", "[MH] CONNECTION LOST"),
        ("gui.toMenu", "[MH] SERVER LIST"),
        ("deathScreen.leaveServer", "[MH] LEAVE SERVER"),
        ("deathScreen.respawn", "[MH] RESPAWN"),
        ("deathScreen.title", "[MH] PLAYER DEAD"),
        ("deathScreen.title.hardcore", "[MH] GAME OVER"),
        ("deathScreen.titleScreen", "[MH] LEAVE SERVER"),
        ("selectServer.direct", "[MH] DIRECT CONNECT"),
        ("selectServer.select", "[MH] JOIN SERVER"),
        ("selectServer.title", "[MH] SERVER LIST"),
        ("addServer.enterIp", "[MH] SERVER ADDRESS")
    };

    private static readonly string[] DamageSounds = { "hit1.ogg", "hit2.ogg", "hit3.ogg", "hit4.ogg" };

    private static uint[,] GuiTopLeftMarker => new[,]
    {
        { Magenta, Cyan, Yellow, Magenta },
        { Cyan, Yellow, Magenta, Cyan },
        { Yellow, Magenta, Cyan, Yellow }
    };

    private static uint[,] GuiBottomRightMarker => new[,]
    {
        { Yellow, Cyan, Magenta, Yellow },
        { Magenta, Yellow, Cyan, Magenta },
        { Cyan, Magenta, Yellow, Cyan }
    };

    // Wspólny lokalizator 2×2 w lewym górnym rogu znacznika, pozostałe pięć pól to kod typu
    // w systemie trójkowym (magenta, cyjan, żółty).
    internal static uint[,] BuildItemMarker(int markerCode)
    {
        uint[,] marker =
        {
            { Magenta, Cyan, Magenta },
            { Cyan, Yellow, Magenta },
            { Magenta, Magenta, Magenta }
        };
        uint[] codeColors = { Magenta, Cyan, Yellow };
        foreach ((int row, int column) in new[] { (0, 2), (1, 2), (2, 0), (2, 1), (2, 2) })
        {
            marker[row, column] = codeColors[markerCode % codeColors.Length];
            markerCode /= codeColors.Length;
        }
        return marker;
    }

    // Katalogi domyślnej paczki 1.8.8 kopiowane bez zmian (jak w poprzedniej, gotowej paczce).
    public static readonly string[] BaseDirectories =
    {
        "assets/minecraft/textures/", "assets/minecraft/models/", "assets/minecraft/blockstates/", "assets/minecraft/texts/"
    };

    /// <summary>Buduje ZIP paczki. readTexture zwraca PNG o ścieżce względnej do assets/minecraft/textures/
    /// (np. "items/diamond.png") z tekstur gry albo null, gdy go nie ma. baseFiles to domyślne pliki gry
    /// kopiowane bez zmian (pliki ze znacznikami je zastępują).</summary>
    public static byte[] Build(Func<string, byte[]?> readTexture, IEnumerable<(string Path, byte[] Data)>? baseFiles = null)
    {
        var files = new List<(string Path, byte[] Data)>();

        foreach (MarkedItem item in DiscardItems.Concat(PresenceItems))
        {
            Image image = Decode(Require(readTexture, "items/" + item.File));
            Draw(image, ItemMarkerX, ItemMarkerY, BuildItemMarker(item.MarkerCode));
            files.Add((TextureRoot + "items/" + item.File, Encode(image)));
        }

        foreach (SolidBlock block in SolidBlocks.Concat(ProtectedBlocks))
        {
            var image = new Image(16, 16);
            Array.Fill(image.Pixels, Rgba(block.R, block.G, block.B));
            files.Add((TextureRoot + "blocks/" + block.File, Encode(image)));
        }

        Image inventory = Decode(Require(readTexture, "gui/container/inventory.png"));
        if (inventory.Width < GuiWidth || inventory.Height < GuiHeight)
            throw new InvalidOperationException($"inventory.png ma nieoczekiwany rozmiar {inventory.Width}×{inventory.Height} — potrzebna jest tekstura 1.8.8 w rozdzielczości 16x.");
        Draw(inventory, GuiTopLeftX, GuiTopLeftY, GuiTopLeftMarker);
        Draw(inventory, GuiBottomRightX, GuiBottomRightY, GuiBottomRightMarker);
        files.Add((TextureRoot + "gui/container/inventory.png", Encode(inventory)));

        string language = string.Concat(LanguageOverrides.Select(entry => $"{entry.Key}={entry.Value}\n"));
        foreach (string code in new[] { "en_US", "pl_PL" })
            files.Add(($"assets/minecraft/lang/{code}.lang", Encoding.UTF8.GetBytes(language)));

        foreach (string sound in DamageSounds)
            files.Add(("assets/minecraft/sounds/damage/" + sound, ReadEmbedded(sound)));

        files.Add(("pack.mcmeta", Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { pack = new { pack_format = 1, description = Description } }, new JsonSerializerOptions { WriteIndented = true }) + "\n")));
        files.Add(("pack.png", ReadEmbedded("pack.png")));
        files.Add((ManifestEntry, BuildManifest()));
        files.Add(("MINECRAFT_HELPER.txt", BuildReadme()));

        Verify(files);

        var marked = new HashSet<string>(files.Select(file => file.Path), StringComparer.Ordinal);
        IEnumerable<(string Path, byte[] Data)> all = (baseFiles ?? Enumerable.Empty<(string, byte[])>())
            .Where(file => !marked.Contains(file.Path))
            .Concat(files);

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, byte[] data) in all)
            {
                ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using Stream stream = entry.Open();
                stream.Write(data, 0, data.Length);
            }
        }
        return output.ToArray();
    }

    /// <summary>Wersja generatora zapisana w paczce; null, gdy to nie paczka Minecraft Helper.</summary>
    public static int? ReadGeneratorVersion(Stream zipStream)
    {
        try
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            ZipArchiveEntry? manifest = zip.GetEntry(ManifestEntry);
            if (manifest == null)
                return null;
            using Stream stream = manifest.Open();
            using JsonDocument json = JsonDocument.Parse(stream);
            return json.RootElement.TryGetProperty("generatorVersion", out JsonElement version) && version.TryGetInt32(out int value) ? value : 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return null;
        }
    }

    private static byte[] Require(Func<string, byte[]?> readTexture, string path) =>
        readTexture(path) ?? throw new FileNotFoundException($"W plikach gry brakuje tekstury {path}.");

    private static byte[] ReadEmbedded(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MinecraftHelper.ResourcePack." + name)
            ?? throw new FileNotFoundException("W programie brakuje pliku paczki: " + name);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static byte[] BuildManifest()
    {
        var manifest = new
        {
            format = 1,
            generatorVersion = GeneratorVersion,
            minecraftVersion = "1.8.8",
            gui = new
            {
                width = GuiWidth,
                height = GuiHeight,
                topLeftMarker = new { x = GuiTopLeftX, y = GuiTopLeftY },
                bottomRightMarker = new { x = GuiBottomRightX, y = GuiBottomRightY }
            },
            automationLanguages = new { languages = new[] { "en_US", "pl_PL" }, labelPrefix = "[MH]" },
            itemMarker = new { version = 2, x = ItemMarkerX, y = ItemMarkerY, width = 3, height = 3 },
            discardItems = DiscardItems.Select(item => (object)new { id = item.Id, label = item.Label, markerCode = item.MarkerCode, texture = "items/" + item.File })
                .Concat(SolidBlocks.Select(block => (object)new { id = block.Id, label = block.Label, recognition = "solidBlockColor", rgb = new int[] { block.R, block.G, block.B }, texture = "blocks/" + block.File })),
            presenceItems = PresenceItems.Select(item => new { id = item.Id, label = item.Label, markerCode = item.MarkerCode, texture = "items/" + item.File, purpose = "missing-item safety check" }),
            protectedItems = ProtectedBlocks.Select(block => new { id = block.Id, label = block.Label, recognition = "solidBlockColor", rgb = new int[] { block.R, block.G, block.B }, texture = "blocks/" + block.File })
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static byte[] BuildReadme()
    {
        var lines = new List<string>
        {
            "MinecraftHelper AutoEQ + CobbleX - paczka dla 1.8.8",
            "",
            "Paczke tworzy Kopacz (Ustawienia -> Paczka zasobow) z plikow Twojej gry 1.8.8.",
            "Zawiera domyslny wyglad 1.8.8 ze znacznikami - detektory Kopacza rozpoznaja domyslny",
            "cobblestone, cyfry stackow i ramki ekwipunku. Ustaw ja NAD innymi paczkami.",
            "",
            "Oznaczone przedmioty:"
        };
        lines.AddRange(DiscardItems.Select(item => "- " + item.Label));
        lines.AddRange(SolidBlocks.Select(block => $"- {block.Label} (jednolity kolor bloku, bez znacznika)"));
        lines.Add("");
        lines.Add("Znacznik kontrolny (nigdy nie jest wyrzucany przez Auto EQ):");
        lines.AddRange(PresenceItems.Select(item => "- " + item.Label));
        lines.AddRange(ProtectedBlocks.Select(block => $"- {block.Label} (jednolity rozowy kolor)"));
        lines.Add("");
        lines.Add("Dla jezyka polskiego i angielskiego paczka ustawia stale etykiety [MH] na ekranach laczenia, rozlaczenia, smierci i wyboru serwera.");
        return Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    // Kontrola po zakodowaniu: znaczniki muszą wyjść z PNG dokładnie takie, jak zostały narysowane.
    private static void Verify(IReadOnlyList<(string Path, byte[] Data)> files)
    {
        byte[] Find(string path) => files.First(file => file.Path == TextureRoot + path).Data;
        foreach (MarkedItem item in DiscardItems.Concat(PresenceItems))
            Expect(Decode(Find("items/" + item.File)), ItemMarkerX, ItemMarkerY, BuildItemMarker(item.MarkerCode), item.File);
        Image inventory = Decode(Find("gui/container/inventory.png"));
        Expect(inventory, GuiTopLeftX, GuiTopLeftY, GuiTopLeftMarker, "inventory (lewy górny)");
        Expect(inventory, GuiBottomRightX, GuiBottomRightY, GuiBottomRightMarker, "inventory (prawy dolny)");
    }

    private static void Expect(Image image, int startX, int startY, uint[,] pattern, string name)
    {
        for (int y = 0; y < pattern.GetLength(0); y++)
            for (int x = 0; x < pattern.GetLength(1); x++)
                if (image.Pixels[(startY + y) * image.Width + startX + x] != pattern[y, x])
                    throw new InvalidOperationException($"Niepoprawny znacznik w {name}, piksel {x},{y}.");
    }

    private static void Draw(Image image, int startX, int startY, uint[,] pattern)
    {
        for (int y = 0; y < pattern.GetLength(0); y++)
        {
            for (int x = 0; x < pattern.GetLength(1); x++)
            {
                int targetX = startX + x;
                int targetY = startY + y;
                if (targetX >= image.Width || targetY >= image.Height)
                    throw new InvalidOperationException($"Znacznik wychodzi poza obraz {image.Width}×{image.Height} — potrzebna jest tekstura 16x.");
                image.Pixels[targetY * image.Width + targetX] = pattern[y, x];
            }
        }
    }

    // Piksele jako BGRA (Bgra32, bez mnożenia przez alfę) — dokładnie te wartości trafiają do PNG.
    private sealed class Image
    {
        public Image(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new uint[width * height];
        }

        public int Width { get; }
        public int Height { get; }
        public uint[] Pixels { get; }
    }

    private static uint Rgba(byte r, byte g, byte b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    private static Image Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Bgra32)
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var image = new Image(source.PixelWidth, source.PixelHeight);
        source.CopyPixels(image.Pixels, image.Width * 4, 0);
        return image;
    }

    private static byte[] Encode(Image image)
    {
        BitmapSource source = BitmapSource.Create(image.Width, image.Height, 72, 72, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
