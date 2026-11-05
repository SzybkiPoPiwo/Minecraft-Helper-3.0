using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftHelper.Services;

internal sealed record DiscordMessage(string Id, string ChannelId, string? GuildId, string AuthorId, bool AuthorIsBot, string Content);

// Połączenie Gateway Discorda (WebSocket) tylko do odbierania komend z wiadomości.
// Heartbeat, ponowne łączenie z narastającą przerwą; zły token albo brak uprawnienia
// „Message Content Intent” zatrzymują próby, bo ponawianie nic by nie dało.
internal sealed class DiscordGateway
{
    private const string GatewayUrl = "wss://gateway.discord.gg/?v=10&encoding=json";
    // GUILD_MESSAGES | DIRECT_MESSAGES | MESSAGE_CONTENT
    private const int Intents = (1 << 9) | (1 << 12) | (1 << 15);

    private readonly string _token;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private int? _sequence;
    private volatile bool _heartbeatAcked = true;

    public DiscordGateway(string token) => _token = token;

    public event Func<DiscordMessage, Task>? MessageReceived;

    // Stan dla GUI: (połączony, opis).
    public event Action<bool, string>? StatusChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay = TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                StatusChanged?.Invoke(false, "Łączenie z Discordem…");
                bool fatal = await RunSessionAsync(cancellationToken).ConfigureAwait(false);
                if (fatal)
                    return;
                delay = TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or JsonException or InvalidOperationException or OperationCanceledException)
            {
                StatusChanged?.Invoke(false, "Brak połączenia z Discordem — ponawiam: " + ex.GetBaseException().Message);
                AppLog.Warn("Discord", "Połączenie z Discordem przerwane: " + ex.GetBaseException().Message);
            }

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
        }
    }

    // Zwraca true przy błędzie, którego ponawianie nie naprawi (token, uprawnienia).
    private async Task<bool> RunSessionAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await socket.ConnectAsync(new Uri(GatewayUrl), session.Token).ConfigureAwait(false);
        Task? heartbeat = null;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                string? text = await ReceiveAsync(socket, session.Token).ConfigureAwait(false);
                if (text == null)
                    break;

                using JsonDocument json = JsonDocument.Parse(text);
                JsonElement root = json.RootElement;
                int op = root.GetProperty("op").GetInt32();
                if (root.TryGetProperty("s", out JsonElement seq) && seq.ValueKind == JsonValueKind.Number)
                    _sequence = seq.GetInt32();

                switch (op)
                {
                    case 10: // HELLO
                        int interval = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                        _heartbeatAcked = true;
                        heartbeat = HeartbeatLoopAsync(socket, interval, session);
                        await SendAsync(socket, new
                        {
                            op = 2,
                            d = new
                            {
                                token = _token,
                                intents = Intents,
                                properties = new { os = "windows", browser = "MinecraftHelper", device = "MinecraftHelper" }
                            }
                        }, session.Token).ConfigureAwait(false);
                        break;
                    case 11: // HEARTBEAT ACK
                        _heartbeatAcked = true;
                        break;
                    case 1: // serwer prosi o heartbeat od razu
                        await SendAsync(socket, new { op = 1, d = _sequence }, session.Token).ConfigureAwait(false);
                        break;
                    case 7: // RECONNECT
                    case 9: // INVALID SESSION
                        return false;
                    case 0:
                        HandleDispatch(root);
                        break;
                }
            }
        }
        finally
        {
            session.Cancel();
            if (heartbeat != null)
            {
                try { await heartbeat.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }

        int? closeCode = (int?)socket.CloseStatus;
        if (closeCode == 4004)
        {
            StatusChanged?.Invoke(false, "Discord odrzucił token bota. Wklej poprawny token i zapisz ustawienia.");
            AppLog.Error("Discord", "Zły token bota (4004).");
            return true;
        }
        if (closeCode == 4014)
        {
            StatusChanged?.Invoke(false, "Włącz „Message Content Intent” w Developer Portal (Bot → Privileged Gateway Intents).");
            AppLog.Error("Discord", "Brak uprawnienia Message Content Intent (4014).");
            return true;
        }
        return false;
    }

    private void HandleDispatch(JsonElement root)
    {
        string? type = root.GetProperty("t").GetString();
        JsonElement data = root.GetProperty("d");
        if (type == "READY")
        {
            string name = data.GetProperty("user").GetProperty("username").GetString() ?? "bot";
            StatusChanged?.Invoke(true, $"Połączony jako {name}.");
            AppLog.Info("Discord", $"Bot połączony jako {name}.");
            return;
        }
        if (type != "MESSAGE_CREATE")
            return;

        JsonElement author = data.GetProperty("author");
        var message = new DiscordMessage(
            data.GetProperty("id").GetString() ?? string.Empty,
            data.GetProperty("channel_id").GetString() ?? string.Empty,
            data.TryGetProperty("guild_id", out JsonElement guild) ? guild.GetString() : null,
            author.GetProperty("id").GetString() ?? string.Empty,
            author.TryGetProperty("bot", out JsonElement bot) && bot.ValueKind == JsonValueKind.True,
            data.TryGetProperty("content", out JsonElement content) ? content.GetString() ?? string.Empty : string.Empty);

        // Obsługa komendy (zrzuty, kanał kont) nie może wstrzymywać odbioru i heartbeatu.
        Func<DiscordMessage, Task>? handler = MessageReceived;
        if (handler != null)
            _ = Task.Run(async () =>
            {
                try
                {
                    await handler(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Discord", "Błąd obsługi komendy z Discorda", ex);
                }
            });
    }

    private async Task HeartbeatLoopAsync(ClientWebSocket socket, int intervalMs, CancellationTokenSource session)
    {
        // Pierwszy heartbeat po losowej części interwału, jak zaleca Discord.
        await Task.Delay((int)(intervalMs * Random.Shared.NextDouble()), session.Token).ConfigureAwait(false);
        while (!session.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            if (!_heartbeatAcked)
            {
                // Brak odpowiedzi na poprzedni heartbeat = martwe połączenie; zamknij i połącz od nowa.
                AppLog.Warn("Discord", "Discord nie odpowiada na heartbeat — łączę ponownie.");
                session.Cancel();
                return;
            }
            _heartbeatAcked = false;
            await SendAsync(socket, new { op = 1, d = _sequence }, session.Token).ConfigureAwait(false);
            await Task.Delay(intervalMs, session.Token).ConfigureAwait(false);
        }
    }

    private async Task SendAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private static async Task<string?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }
}
