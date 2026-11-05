using System;
using System.Collections.Generic;
using System.Linq;

namespace MinecraftHelper.Services;

internal enum DiscordCommandGroup
{
    Preview,
    Images,
    Control,
    Server
}

// Kind: zwykła komenda, sterowanie (zielona) albo zatrzymanie/wyjście (czerwona) — kolor pigułki w GUI.
internal enum DiscordCommandKind
{
    Normal,
    Start,
    Stop
}

internal sealed record DiscordCommandInfo(
    string Id,
    IReadOnlyList<string> Aliases,
    string Usage,
    string Description,
    DiscordCommandGroup Group,
    DiscordCommandKind Kind = DiscordCommandKind.Normal);

// Jedno źródło prawdy o komendach bota: rozpoznawanie aliasów, lista w !pomoc,
// pigułki w karcie Bot Discord i okno włączania/wyłączania komend.
internal static class DiscordCommands
{
    public const string Help = "pomoc";

    public static IReadOnlyList<DiscordCommandInfo> All { get; } = new[]
    {
        new DiscordCommandInfo("status", new[] { "konta" }, "status [konto]", "co robi każde konto", DiscordCommandGroup.Preview),
        new DiscordCommandInfo("dane", new[] { "logi", "raport" }, "dane [konto]", "ostatnia sesja z dziennika: EQ, CobbleX, wyrzucone, co zostało, wyjścia z serwera", DiscordCommandGroup.Preview),
        new DiscordCommandInfo("stats", new[] { "statystyki" }, "stats [konto]", "liczniki z całej historii konta: sesje, EQ, CobbleX, wyrzucone", DiscordCommandGroup.Preview),
        new DiscordCommandInfo("ss", new[] { "zrzut", "screenshot" }, "ss [konto]", "zrzut ekranu gry", DiscordCommandGroup.Images),
        new DiscordCommandInfo("jedzenie", new[] { "hp", "food" }, "jedzenie [konto]", "pasek życia i jedzenia", DiscordCommandGroup.Images),
        new DiscordCommandInfo("eq", new[] { "ekwipunek", "inventory" }, "eq [konto]", "ostatni skan ekwipunku", DiscordCommandGroup.Images),
        new DiscordCommandInfo("start", Array.Empty<string>(), "start [konto] [5/3/3|6/3/3]", "uruchom kopanie (gdy gra nie jest na wierzchu — w tle)", DiscordCommandGroup.Control, DiscordCommandKind.Start),
        new DiscordCommandInfo("stop", Array.Empty<string>(), "stop [konto]", "zatrzymaj kopanie; samo stop zatrzymuje wszystkie konta", DiscordCommandGroup.Control, DiscordCommandKind.Stop),
        new DiscordCommandInfo("join", new[] { "wejdz", "wejdź" }, "join [konto] [5/3/3|6/3/3]", "wejdź na serwer jak Auto Reconnect: lista serwerów, Direct Connect, /home, kontrola EQ — i zacznij kopać", DiscordCommandGroup.Server, DiscordCommandKind.Start),
        new DiscordCommandInfo("leave", new[] { "wyjdz", "wyjdź" }, "leave [konto]", "zatrzymaj kopanie i wyjdź z serwera (ESC → Disconnect)", DiscordCommandGroup.Server, DiscordCommandKind.Stop)
    };

    // Nazwa wpisana na Discordzie → komenda (także alias). null — nieznana albo !pomoc.
    public static DiscordCommandInfo? Find(string typed)
    {
        string name = typed.Trim().ToLowerInvariant();
        return All.FirstOrDefault(command => command.Id == name || command.Aliases.Contains(name, StringComparer.Ordinal));
    }

    public static bool IsHelp(string typed) => typed.Trim().ToLowerInvariant() is Help or "help";

    public static string GroupTitle(DiscordCommandGroup group) => group switch
    {
        DiscordCommandGroup.Preview => "📊 Podgląd",
        DiscordCommandGroup.Images => "📸 Obrazy z gry",
        DiscordCommandGroup.Control => "🎮 Sterowanie kopaniem",
        _ => "🌐 Serwer"
    };
}
