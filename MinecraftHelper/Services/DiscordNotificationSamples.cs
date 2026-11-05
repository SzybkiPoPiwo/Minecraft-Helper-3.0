using System.Collections.Generic;

namespace MinecraftHelper.Services;

internal sealed record DiscordAlertSample(
    DiscordAlertKind Kind,
    string Title,
    string Description,
    bool Urgent,
    DiscordEmbedField[] Fields,
    int? Color = null);

// Przykłady do przycisku „Testuj powiadomienia”: te same tytuły i pola co prawdziwe zdarzenia
// (MainWindow.Discord.cs), tylko z przykładowymi liczbami.
internal static class DiscordNotificationSamples
{
    public static IReadOnlyList<DiscordAlertSample> All { get; } = new[]
    {
        new DiscordAlertSample(DiscordAlertKind.MiningStartStop, "▶️ Kopanie uruchomione", string.Empty, false, new DiscordEmbedField[]
        {
            new("⛏️ Tryb", "Kopacz 6/3/3", true),
            new("🧭 Ruch", "na wprost, szerokość 5", true),
            new("🎒 Auto EQ", "wybrane przedmioty", true),
            new("🧱 CobbleX", "włączony", true),
            new("🍖 Jedzenie po EQ", "włączone", true)
        }),
        new DiscordAlertSample(DiscordAlertKind.MiningStartStop, "⏹️ Kopanie zakończone", "Zatrzymano bindem.", false, new DiscordEmbedField[]
        {
            new("⛏️ Tryb", "Kopacz 6/3/3", true),
            new("⏱️ Czas sesji", "01:57:10\nod 12:03", true),
            new("🎒 Otwarcia EQ", "14", true),
            new("🧱 CobbleX", "9", true),
            new("🗑️ Wyrzucono", "2 304 szt.\n36 stacków", true),
            new("🚪 Wyjścia z serwera", "brak ✅", true),
            new("🗑️ Wyrzucone przedmioty", "• Andezyt — **1 280**\n• Diorit — **640**\n• Granit — **384**")
        }, DiscordColors.Neutral),
        new DiscordAlertSample(DiscordAlertKind.Protection, "🚨 Alarm obrażeń — wyszedłem z serwera",
            "Kopacz usłyszał dźwięk obrażeń, zatrzymał kopanie i kliknął **Disconnect**.", true, new DiscordEmbedField[]
            {
                new("🔊 Wykryty dźwięk", "podobieństwo 87%, poziom -18 dB", true),
                new("⛏️ Kopanie", "zatrzymane", true),
                new("➡️ Co dalej", "reconnect za 30 s", true)
            }),
        new DiscordAlertSample(DiscordAlertKind.Reconnect, "🔄 Reconnect — łączę ponownie",
            "Uruchomiono automatyczny reconnect. Profil: Mój serwer; powrót: /home kopalnia.", false, System.Array.Empty<DiscordEmbedField>()),
        new DiscordAlertSample(DiscordAlertKind.Reconnect, "❌ Reconnect nieudany — konto poza serwerem",
            "Nie udało się połączyć z serwerem. Próby połączenia: 10; wznowienie kopania: nie.", true, System.Array.Empty<DiscordEmbedField>()),
        new DiscordAlertSample(DiscordAlertKind.MissingPickaxe, "⛏️ Brak kilofa — wracam na home",
            "Brak znacznika diamentowego kilofa. Wysyłam /home kopalnia; po powrocie kopanie pozostanie zakończone.", false, System.Array.Empty<DiscordEmbedField>()),
        new DiscordAlertSample(DiscordAlertKind.Error, "❗ Błąd: Auto EQ",
            "Nie udało się otworzyć ekwipunku po 3 próbach.", false, System.Array.Empty<DiscordEmbedField>()),
        new DiscordAlertSample(DiscordAlertKind.CommandResult, "🚪 Wyszedłem z serwera",
            "Komenda `!leave`: kliknięto Disconnect. Powrót na serwer z kopaniem: `!join`.", false, System.Array.Empty<DiscordEmbedField>())
    };
}
