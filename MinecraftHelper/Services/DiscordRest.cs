using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services;

// Minimalny klient REST API Discorda: wysyłanie wiadomości (z obrazem) i sprawdzenie tokenu.
// Działa w każdym procesie Kopacza — powiadomienia nie potrzebują połączenia Gateway.
internal static class DiscordRest
{
    private const int MaxMessageLength = 2000;
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri("https://discord.com/api/v10/"), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/SzybkiPoPiwo/Minecraft-Helper-3.0, 2) MinecraftHelperKopacz");
        return client;
    }

    // Zwraca nazwę bota — przy okazji sprawdza, czy token jest poprawny.
    public static async Task<string> GetBotNameAsync(string token, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendWithRetryAsync(token, () => new HttpRequestMessage(HttpMethod.Get, "users/@me"), cancellationToken).ConfigureAwait(false);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return json.RootElement.TryGetProperty("username", out JsonElement name) ? name.GetString() ?? "bot" : "bot";
    }

    public static Task SendMessageAsync(string token, string channelId, string content, byte[]? image = null,
        string? imageName = null, CancellationToken cancellationToken = default) =>
        PostAsync(token, channelId, content, null, image, imageName, cancellationToken);

    // Wiadomość jako karta (embed). content to krótki tekst nad kartą — tam trafiają oznaczenia,
    // bo tylko on budzi telefon. Zrzut ekranu jest pokazywany wewnątrz karty.
    public static Task SendEmbedAsync(string token, string channelId, DiscordEmbed embed, string? content = null,
        byte[]? image = null, CancellationToken cancellationToken = default) =>
        PostAsync(token, channelId, content, embed, image, image == null ? null : "zrzut.jpg", cancellationToken);

    private static async Task PostAsync(string token, string channelId, string? content, DiscordEmbed? embed,
        byte[]? image, string? imageName, CancellationToken cancellationToken)
    {
        if (content?.Length > MaxMessageLength)
            content = content[..(MaxMessageLength - 1)] + "…";
        imageName = image == null ? null : string.IsNullOrWhiteSpace(imageName) ? "zrzut.jpg" : imageName;

        // Pingi tylko do użytkowników wpisanych w treść (<@id>) — nigdy @everyone ani ról.
        var body = new JsonObject
        {
            ["allowed_mentions"] = new JsonObject { ["parse"] = new JsonArray("users") }
        };
        if (!string.IsNullOrEmpty(content))
            body["content"] = content;
        if (embed != null)
            body["embeds"] = new JsonArray(embed.ToPayload(imageName));
        string payload = body.ToJsonString();

        HttpRequestMessage Build()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"channels/{channelId}/messages");
            if (image == null)
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                return request;
            }

            var form = new MultipartFormDataContent();
            form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "files[0]", imageName!);
            request.Content = form;
            return request;
        }

        using HttpResponseMessage response = await SendWithRetryAsync(token, Build, cancellationToken).ConfigureAwait(false);
    }

    // Limity Discorda (429) czekają podany czas i ponawiają; inne błędy zgłaszają czytelny komunikat.
    private static async Task<HttpResponseMessage> SendWithRetryAsync(string token, Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", token);
            HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return response;

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == (HttpStatusCode)429 && attempt < 3)
            {
                double seconds = 1;
                try
                {
                    using JsonDocument json = JsonDocument.Parse(body);
                    if (json.RootElement.TryGetProperty("retry_after", out JsonElement retry))
                        seconds = retry.GetDouble();
                }
                catch (JsonException)
                {
                }
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 0.2, 30)), cancellationToken).ConfigureAwait(false);
                continue;
            }

            response.Dispose();
            throw new InvalidOperationException(DescribeError(response.StatusCode, body));
        }
    }

    private static string DescribeError(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Unauthorized => "Discord odrzucił token bota (401). Skopiuj token ponownie z Developer Portal.",
        HttpStatusCode.Forbidden => "Bot nie ma dostępu do kanału (403). Dodaj bota na serwer i nadaj mu prawo pisania oraz załączania plików na tym kanale.",
        HttpStatusCode.NotFound => "Nie znaleziono kanału (404). Sprawdź ID kanału (PPM na kanał → Kopiuj ID kanału).",
        _ => $"Discord zwrócił błąd {(int)status}: {(body.Length > 200 ? body[..200] : body)}"
    };
}
