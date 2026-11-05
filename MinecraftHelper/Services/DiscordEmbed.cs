using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace MinecraftHelper.Services;

// Kolory paska embeda — to samo znaczenie co kolory statusów w GUI.
internal static class DiscordColors
{
    public const int Alarm = 0xE5484D;
    public const int Error = 0xF76B15;
    public const int Warn = 0xFFB224;
    public const int Info = 0x3E63DD;
    public const int Ok = 0x30A46C;
    public const int Neutral = 0x8B8D98;
    public const int Accent = 0x6E56CF;
}

internal readonly record struct DiscordEmbedField(string Name, string Value, bool Inline = false);

// Karta wiadomości Discorda (embed): kolorowy pasek, tytuł, opis, pola, zrzut i stopka.
// Limity Discorda (tytuł 256, opis 4096, pole 1024, 25 pól) pilnuje ToPayload.
internal sealed class DiscordEmbed
{
    public const string DefaultFooter = "Minecraft Helper • Kopacz";

    public DiscordEmbed(string title, int color)
    {
        Title = title;
        Color = color;
    }

    public string Title { get; }

    public int Color { get; }

    public string? Author { get; set; }

    public string? Description { get; set; }

    public List<DiscordEmbedField> Fields { get; } = new();

    public string Footer { get; set; } = DefaultFooter;

    public DateTimeOffset? Timestamp { get; set; } = DateTimeOffset.Now;

    // Puste wartości pomijamy — Discord odrzuca pole bez treści.
    public DiscordEmbed Field(string name, string? value, bool inline = false)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Fields.Add(new DiscordEmbedField(name, value, inline));
        return this;
    }

    public DiscordEmbed AddFields(IEnumerable<DiscordEmbedField>? fields)
    {
        if (fields != null)
            foreach (DiscordEmbedField field in fields)
                Field(field.Name, field.Value, field.Inline);
        return this;
    }

    public JsonObject ToPayload(string? imageName = null)
    {
        var embed = new JsonObject
        {
            ["title"] = Cut(Title, 256),
            ["color"] = Color,
            ["footer"] = new JsonObject { ["text"] = Cut(Footer, 2048) }
        };
        if (!string.IsNullOrWhiteSpace(Author))
            embed["author"] = new JsonObject { ["name"] = Cut(Author, 256) };
        if (!string.IsNullOrWhiteSpace(Description))
            embed["description"] = Cut(Description, 4096);
        if (Timestamp != null)
            embed["timestamp"] = Timestamp.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (Fields.Count > 0)
            embed["fields"] = new JsonArray(LayoutFields().Take(25).Select(field => (JsonNode)new JsonObject
            {
                ["name"] = Cut(field.Name, 256),
                ["value"] = Cut(field.Value, 1024),
                ["inline"] = field.Inline
            }).ToArray());
        if (imageName != null)
            embed["image"] = new JsonObject { ["url"] = "attachment://" + imageName };
        return embed;
    }

    // Wersja tekstowa — do logów i testów.
    public string ToPlainText()
    {
        var text = new StringBuilder(Title);
        if (!string.IsNullOrWhiteSpace(Author))
            text.Append(" [").Append(Author).Append(']');
        if (!string.IsNullOrWhiteSpace(Description))
            text.AppendLine().Append(Description);
        foreach (DiscordEmbedField field in Fields)
            text.AppendLine().Append(field.Name).Append(": ").Append(field.Value);
        return text.ToString();
    }

    // Pola w kolumnach zlewały się: wartości stały obok siebie zwykłym tekstem. Krótkie
    // wartości dostają szare tło (kod), a po każdych dwóch polach w wierszu stoi puste pole,
    // więc Discord układa je w 2 kolumny z odstępem zamiast 3 ściśniętych.
    private IEnumerable<DiscordEmbedField> LayoutFields()
    {
        int inRow = 0;
        for (int index = 0; index < Fields.Count; index++)
        {
            DiscordEmbedField field = Fields[index];
            if (!field.Inline)
            {
                inRow = 0;
                yield return field;
                continue;
            }

            yield return field with { Value = AsChips(field.Value) };
            if (++inRow == 2 && index + 1 < Fields.Count && Fields[index + 1].Inline)
            {
                yield return new DiscordEmbedField(Blank, Blank, true);
                inRow = 0;
            }
        }
    }

    private const string Blank = "​";

    // Wartości z własnym formatowaniem (pogrubienie, listy, kod) zostają bez zmian.
    private static string AsChips(string value) =>
        value.IndexOfAny(new[] { '*', '`', '•', '[', '_' }) >= 0
            ? value
            : string.Join("\n", value.Split('\n').Select(line => line.Trim().Length == 0 ? line : $"`{line.Trim()}`"));

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
