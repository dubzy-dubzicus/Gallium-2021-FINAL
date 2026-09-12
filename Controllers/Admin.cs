using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;
using Mocha2021.Hub;

namespace Mocha2021.Controllers;

[Controller]
public class Admin : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly RoomDB _roomDb;
    private readonly SessionManager _sessions;
    private readonly RoomInstanceManager _instances;
    private readonly HubState _hub;
    private readonly ServerState _state;
    private readonly IWebHostEnvironment _env;

    public Admin(PlayerDB playerDb, RoomDB roomDb, SessionManager sessions, RoomInstanceManager instances, HubState hub, ServerState state, IWebHostEnvironment env)
    {
        _playerDb = playerDb;
        _roomDb = roomDb;
        _sessions = sessions;
        _instances = instances;
        _hub = hub;
        _state = state;
        _env = env;
    }

    private int CurrentAccountId() => _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());

    private Dictionary<string, object?>? ReadJsonBody()
    {
        try { Request.Body.Position = 0; } catch { }
        using var reader = new StreamReader(Request.Body);
        var text = reader.ReadToEndAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(text); }
        catch { return null; }
    }

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpGet("/admin/")]
    [HttpGet("/admin")]
    public IActionResult AdminIndex()
    {
        var path = Path.Combine(_env.ContentRootPath, "index.html");
        if (System.IO.File.Exists(path)) return PhysicalFile(path, "text/html");
        return Content("<html><body><h1>Mocha2021 Admin</h1><p>No admin UI has been built yet.</p></body></html>", "text/html");
    }

    [HttpGet("/admin/api/accounts")]
    public IActionResult AdminAccounts()
    {
        var accounts = _playerDb.GetAllAccountsForAdmin().Select(r => new Dictionary<string, object?>
        {
            ["account_id"] = r.AccountId, ["username"] = r.Username, ["display_name"] = r.DisplayName,
            ["is_junior"] = r.IsJunior ?? false, ["is_online"] = r.IsOnline,
            ["last_heartbeat"] = r.LastHeartbeat?.ToString("o"), ["created_at"] = r.CreatedAt.ToString("o"),
        }).ToList();
        return new OkObjectResult(new Dictionary<string, object?> { ["accounts"] = accounts });
    }

    [HttpDelete("/admin/api/accounts/{accountId:int}")]
    public IActionResult AdminDeleteAccount(int accountId)
    {
        _playerDb.DeleteAccount(accountId);
        return Ok2(new() { ["deleted"] = accountId });
    }

    private async Task<Dictionary<string, object?>> ForceAccountToDormAsync(int accountId)
    {
        var targetRoomId = 1_000_000 + accountId;
        var instance = new RoomInstance
        {
            RoomInstanceId = Random.Shared.Next(10000, 99999999),
            RoomId = targetRoomId,
            SubRoomId = targetRoomId,
            Location = RoomInstanceManager.DefaultRoomLocation,
            DataBlob = "",
            PhotonRoomId = Guid.NewGuid().ToString(),
            Name = "^DormRoom",
            MaxCapacity = 10,
            IsPrivate = true,
        };
        _instances.SetAccountRoomInstance(accountId, instance);
        var json = instance.ToJson();
        await _hub.PushToAccountAsync(accountId, "ForceRoomChange", new Dictionary<string, object?> { ["errorCode"] = 0, ["roomInstance"] = json });
        return json;
    }

    [HttpPost("/admin/api/accounts/{accountId:int}/ban")]
    public async Task<IActionResult> AdminBanPlayer(int accountId)
    {
        var data = ReadJsonBody() ?? new();
        var reason = data.GetValueOrDefault("reason")?.ToString() ?? "";
        var hoursRaw = data.GetValueOrDefault("hours");
        double? hours = hoursRaw != null && double.TryParse(hoursRaw.ToString(), out var h) ? h : null;
        var reportCategoryRaw = data.GetValueOrDefault("reportCategory");
        var reportCategory = reportCategoryRaw != null ? int.Parse(reportCategoryRaw.ToString()!) : 0;
        var isHostKick = data.GetValueOrDefault("isHostKick")?.ToString()?.ToLowerInvariant() is "true" or "1";
        var moderatorId = CurrentAccountId();

        _playerDb.CreateModerationAction(accountId, moderatorId, reportCategory, isBan: true, isHostKick, reason, hours);
        await ForceAccountToDormAsync(accountId);
        return Ok2(new() { ["banned"] = accountId });
    }

    [HttpPost("/admin/api/accounts/{accountId:int}/gift")]
    public IActionResult AdminGiftAccount(int accountId)
    {
        var data = ReadJsonBody() ?? new();
        var moderatorId = CurrentAccountId();
        if (moderatorId == 0) moderatorId = 1;

        object? Pick(params string[] keys)
        {
            foreach (var key in keys)
                if (data.TryGetValue(key, out var v) && v != null && v.ToString() != "") return v;
            return null;
        }

        var currency = Pick("currency", "Currency") is { } cRaw ? int.Parse(cRaw.ToString()!) : 0;
        var xp = Pick("xp", "Xp", "XP") is { } xRaw ? int.Parse(xRaw.ToString()!) : 0;
        var avatarItemDesc = Pick("avatarItemDesc", "AvatarItemDesc")?.ToString() ?? "";
        var friendlyName = Pick("friendlyName", "friendly_name", "FriendlyName")?.ToString() ?? "";
        var tooltip = Pick("tooltip", "Tooltip")?.ToString() ?? "";
        var message = Pick("message", "Message")?.ToString() ?? "";
        var avatarItemType = Pick("avatarItemType", "AvatarItemType") is { } atRaw ? int.Parse(atRaw.ToString()!) : 0;
        var rarity = Pick("giftRarity", "GiftRarity", "Rarity") is { } rRaw ? int.Parse(rRaw.ToString()!) : -1;
        var giftContext = Pick("giftContext", "GiftContext") is { } gcRaw ? int.Parse(gcRaw.ToString()!) : 110100;

        if (currency == 0 && xp == 0 && string.IsNullOrEmpty(avatarItemDesc))
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid_request", ["error_description"] = "gift needs at least a currency, xp, or avatarItemDesc value" }) { StatusCode = 400 };

        var acct = _playerDb.GetAccount(accountId);
        if (acct == null)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid_request", ["error_description"] = "no account with that id" }) { StatusCode = 400 };

        var giftId = _playerDb.CreateGift(accountId, fromPlayerId: moderatorId, avatarItemDesc: avatarItemDesc,
            currencyType: currency != 0 ? 2 : -1, currency: currency, xp: xp, message: message,
            giftContext: giftContext, giftRarity: rarity, avatarItemType: avatarItemType,
            friendlyName: friendlyName, tooltip: tooltip);

        return Ok2(new() { ["giftId"] = giftId, ["accountId"] = accountId });
    }

    [HttpGet("/admin/api/rooms")]
    public IActionResult AdminRooms()
    {
        var rows = _roomDb.GetAllRoomsForAdmin();
        var rooms = rows.Select(r => new Dictionary<string, object?>
        {
            ["room_id"] = r.RoomId, ["name"] = r.Name, ["unity_scene_id"] = r.UnitySceneId,
            ["visits"] = r.Visits, ["cheers"] = r.Cheers, ["is_rro"] = r.IsRRO, ["is_dorm"] = r.IsDorm,
            ["platforms"] = r.Platforms ?? "",
        }).ToList();
        return new OkObjectResult(new Dictionary<string, object?> { ["rooms"] = rooms });
    }

    [HttpPatch("/admin/api/rooms/{roomId:int}")]
    public IActionResult AdminEditRoom(int roomId)
    {
        var data = ReadJsonBody() ?? new();
        var room = _roomDb.GetRoom(roomId);
        if (room != null)
        {
            if (data.TryGetValue("visits", out var visits) && visits != null) room.Visits = int.Parse(visits.ToString()!);
            if (data.TryGetValue("cheers", out var cheers) && cheers != null) room.Cheers = int.Parse(cheers.ToString()!);
            if (data.TryGetValue("platforms", out var platforms))
            {
                room.Platforms = platforms is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? string.Join(",", el.EnumerateArray().Select(e => e.GetString()))
                    : platforms?.ToString();
            }
            _roomDb.SaveRoom(room);
            _instances.InvalidateActiveRoom(roomId);
        }
        return Ok2(new() { ["updated"] = roomId });
    }

    [HttpDelete("/admin/api/rooms/{roomId:int}")]
    public IActionResult AdminDeleteRoom(int roomId)
    {
        _roomDb.DeleteRoom(roomId);
        return Ok2(new() { ["deleted"] = roomId });
    }

    [HttpGet("/admin/api/maintenance")]
    public IActionResult AdminGetMaintenance() => new OkObjectResult(new Dictionary<string, object?> { ["starts_in_minutes"] = _state.MaintenanceMinutesRemaining() });

    [HttpPost("/admin/api/maintenance")]
    public IActionResult AdminSetMaintenance()
    {
        var data = ReadJsonBody() ?? new();
        var minsRaw = data.GetValueOrDefault("starts_in_minutes");
        var mins = minsRaw != null ? int.Parse(minsRaw.ToString()!) : 0;
        _state.MaintenanceDeadline = mins > 0 ? DateTime.UtcNow.AddMinutes(mins) : null;
        return Ok2(new() { ["starts_in_minutes"] = mins });
    }

    [HttpGet("/admin/api/log/stream")]
    public async Task AdminLogStream()
    {
        var channel = System.Threading.Channels.Channel.CreateBounded<string>(200);
        lock (_state.LogLock) { _state.LogSubscribers.Add(channel); }

        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.ContentType = "text/event-stream";

        try
        {
            while (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var entry = await channel.Reader.ReadAsync(cts.Token);
                    await Response.WriteAsync($"data: {entry}\n\n");
                }
                catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
                {
                    await Response.WriteAsync(": keepalive\n\n");
                }
                await Response.Body.FlushAsync();
            }
        }
        finally
        {
            lock (_state.LogLock) { _state.LogSubscribers.Remove(channel); }
        }
    }
}
