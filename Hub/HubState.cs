using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Mocha2021.Hub;

public class HubConnection
{
    public required WebSocket Socket { get; init; }
    public readonly SemaphoreSlim SendLock = new(1, 1);
    public HashSet<int> Subscriptions = new();
}

public class HubState
{
    public readonly Dictionary<int, HashSet<HubConnection>> AccountConnections = new();
    public readonly Dictionary<HubConnection, HashSet<int>> ConnectionSubscriptions = new();
    public readonly Dictionary<int, HashSet<HubConnection>> PlayerConnections = new();
    public readonly Dictionary<int, List<string>> PendingNotifications = new();
    public readonly object Lock = new();

    public void OnConnected(HubConnection conn)
    {
        lock (Lock) { ConnectionSubscriptions[conn] = new HashSet<int>(); }
    }

    public void OnDisconnected(HubConnection conn)
    {
        lock (Lock)
        {
            if (ConnectionSubscriptions.Remove(conn, out var ids))
            {
                foreach (var pid in ids)
                {
                    if (PlayerConnections.TryGetValue(pid, out var set))
                    {
                        set.Remove(conn);
                        if (set.Count == 0) PlayerConnections.Remove(pid);
                    }
                }
            }
        }
    }

    public List<string> SubscribeToPlayers(HubConnection conn, IEnumerable<int> playerIds)
    {
        var newIds = playerIds.ToHashSet();
        var toFlush = new List<string>();
        lock (Lock)
        {
            var oldIds = ConnectionSubscriptions.GetValueOrDefault(conn, new HashSet<int>());
            ConnectionSubscriptions[conn] = newIds;
            foreach (var pid in newIds)
            {
                if (!PlayerConnections.TryGetValue(pid, out var set))
                {
                    set = new HashSet<HubConnection>();
                    PlayerConnections[pid] = set;
                }
                set.Add(conn);
            }
            foreach (var pid in oldIds.Except(newIds))
            {
                if (PlayerConnections.TryGetValue(pid, out var set)) set.Remove(conn);
            }
            foreach (var pid in newIds)
            {
                if (PendingNotifications.Remove(pid, out var queued)) toFlush.AddRange(queued);
            }
        }
        return toFlush;
    }

    public void RegisterAccountConnection(int accountId, HubConnection conn)
    {
        lock (Lock)
        {
            if (!AccountConnections.TryGetValue(accountId, out var set))
            {
                set = new HashSet<HubConnection>();
                AccountConnections[accountId] = set;
            }
            set.Add(conn);
        }
    }

    public void UnregisterAccountConnection(int accountId, HubConnection conn)
    {
        lock (Lock)
        {
            if (AccountConnections.TryGetValue(accountId, out var set)) set.Remove(conn);
        }
    }

    public async Task<bool> PushToAccountAsync(int accountId, string notifId, params object?[] args)
    {
        var frame = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = 1,
            ["target"] = notifId,
            ["arguments"] = args,
        }) + "\x1e";

        List<HubConnection> sockets;
        lock (Lock)
        {
            sockets = PlayerConnections.TryGetValue(accountId, out var s1) && s1.Count > 0
                ? s1.ToList()
                : AccountConnections.TryGetValue(accountId, out var s2) ? s2.ToList() : new List<HubConnection>();
        }

        var sent = false;
        var bytes = Encoding.UTF8.GetBytes(frame);
        var dead = new List<HubConnection>();
        foreach (var conn in sockets)
        {
            if (conn.Socket.State != WebSocketState.Open) { dead.Add(conn); continue; }
            try
            {
                await conn.SendLock.WaitAsync();
                try
                {
                    await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                    sent = true;
                }
                finally { conn.SendLock.Release(); }
            }
            catch { dead.Add(conn); }
        }
        if (dead.Count > 0)
        {
            lock (Lock)
            {
                foreach (var conn in dead)
                {
                    AccountConnections.GetValueOrDefault(accountId)?.Remove(conn);
                    PlayerConnections.GetValueOrDefault(accountId)?.Remove(conn);
                }
            }
        }
        if (!sent)
        {
            lock (Lock)
            {
                if (!PendingNotifications.TryGetValue(accountId, out var list))
                {
                    list = new List<string>();
                    PendingNotifications[accountId] = list;
                }
                list.Add(frame);
            }
        }
        return sent;
    }

    public async Task<int> BroadcastAsync(string target, object payload)
    {
        var frame = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = 1,
            ["target"] = target,
            ["arguments"] = new[] { payload },
        }) + "\x1e";
        var bytes = Encoding.UTF8.GetBytes(frame);

        List<HubConnection> all;
        lock (Lock) { all = AccountConnections.Values.SelectMany(s => s).Distinct().ToList(); }

        var sent = 0;
        foreach (var conn in all)
        {
            try
            {
                await conn.SendLock.WaitAsync();
                try
                {
                    await conn.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                    sent++;
                }
                finally { conn.SendLock.Release(); }
            }
            catch { }
        }
        return sent;
    }
}
