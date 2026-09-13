using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Gallium2021.Classes;

// I love you so much Bentley.
// I love you more Izzy. <3
namespace Gallium2021.Hub;

public class HubWebSocketHandler
{
    private readonly HubState _state;
    private readonly SessionManager _sessions;

    public HubWebSocketHandler(HubState state, SessionManager sessions)
    {
        _state = state;
        _sessions = sessions;
    }

    public async Task HandleAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var authHeader = context.Request.Headers.Authorization.ToString();
        var queryToken = context.Request.Query["access_token"].FirstOrDefault() ?? context.Request.Query["token"].FirstOrDefault();
        var accountId = _sessions.ResolveAccountIdOrNull(authHeader, queryToken);

        var conn = new HubConnection { Socket = socket };
        var stopPing = new CancellationTokenSource();

        _state.OnConnected(conn);

        if (accountId.HasValue)
        {
            _state.RegisterAccountConnection(accountId.Value, conn);
            await SendFrameAsync(conn, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["type"] = 1, ["target"] = "OnConnect", ["arguments"] = Array.Empty<object>(),
            }) + "\x1e");

            var toFlush = _state.SubscribeToPlayers(conn, new[] { accountId.Value });
            foreach (var frame in toFlush) await SendFrameAsync(conn, frame);
        }

        var pingTask = PingLoopAsync(conn, stopPing.Token);

        try
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;

                var data = sb.ToString();
                sb.Clear();

                foreach (var rawFrame in data.Split('\x1e'))
                {
                    var trimmed = rawFrame.Trim();
                    if (trimmed.Length == 0) continue;

                    JsonDocument? doc;
                    try { doc = JsonDocument.Parse(trimmed); }
                    catch { continue; }
                    using (doc)
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("protocol", out _))
                        {
                            await SendFrameAsync(conn, "{}\x1e");
                        }
                        else if (root.TryGetProperty("type", out var typeEl) && typeEl.GetInt32() == 1)
                        {
                            var invocationId = root.TryGetProperty("invocationId", out var invEl) ? (object?)invEl.ToString() : null;
                            var target = root.TryGetProperty("target", out var targetEl) ? targetEl.GetString() : null;
                            object? result2 = null;

                            if (target == "SubscribeToPlayers")
                            {
                                var playerIds = new List<int>();
                                if (root.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array && argsEl.GetArrayLength() > 0)
                                {
                                    var raw = argsEl[0];
                                    JsonElement idsElement = default;
                                    var hasIds = false;
                                    if (raw.ValueKind == JsonValueKind.Object)
                                    {
                                        if (raw.TryGetProperty("PlayerIds", out idsElement) || raw.TryGetProperty("playerIds", out idsElement))
                                            hasIds = true;
                                    }
                                    else if (raw.ValueKind == JsonValueKind.Array)
                                    {
                                        idsElement = raw;
                                        hasIds = true;
                                    }
                                    if (hasIds && idsElement.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var idEl in idsElement.EnumerateArray())
                                        {
                                            if (idEl.TryGetInt32(out var idVal)) playerIds.Add(idVal);
                                        }
                                    }
                                }
                                var toFlush = _state.SubscribeToPlayers(conn, playerIds);
                                foreach (var frame in toFlush) await SendFrameAsync(conn, frame);
                            }
                            else if (target == "GetSubscriptions")
                            {
                                lock (_state.Lock) { result2 = _state.ConnectionSubscriptions.GetValueOrDefault(conn, new HashSet<int>()).ToList(); }
                            }

                            var completion = JsonSerializer.Serialize(new Dictionary<string, object?>
                            {
                                ["type"] = 3, ["invocationId"] = invocationId, ["result"] = result2,
                            }) + "\x1e";
                            await SendFrameAsync(conn, completion);
                        }
                        else if (root.TryGetProperty("type", out var typeEl2) && typeEl2.GetInt32() == 6)
                        {
                            await SendFrameAsync(conn, "{\"type\":6}\x1e");
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            stopPing.Cancel();
            _state.OnDisconnected(conn);
            if (accountId.HasValue) _state.UnregisterAccountConnection(accountId.Value, conn);
            if (socket.State == WebSocketState.Open)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch { }
            }
        }
    }

    private static async Task SendFrameAsync(HubConnection conn, string frame)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        await conn.SendLock.WaitAsync();
        try { await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { conn.SendLock.Release(); }
    }

    private static async Task PingLoopAsync(HubConnection conn, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
                if (conn.Socket.State != WebSocketState.Open) break;
                try { await SendFrameAsync(conn, "{\"type\":6}\x1e"); }
                catch { break; }
            }
        }
        catch (TaskCanceledException) { }
    }
}
