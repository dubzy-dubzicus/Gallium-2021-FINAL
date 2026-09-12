using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;
using Mocha2021.Hub;
using Mocha2021.Models;

namespace Mocha2021.Controllers;

[Controller]
public class Social : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly HubState _hub;

    public Social(PlayerDB playerDb, SessionManager sessions, HubState hub)
    {
        _playerDb = playerDb;
        _sessions = sessions;
        _hub = hub;
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

    private static readonly Dictionary<string, int> RelationshipTypeCodes = new() { ["pending"] = 1, ["incoming"] = 2, ["friends"] = 3 };

    private static Dictionary<string, object?> RelationshipObj(RelationshipRow row, int accountId)
    {
        var rel = row.Relationship;
        if (row.AccountId != accountId && rel == "pending") rel = "incoming";
        var otherId = row.AccountId == accountId ? row.OtherAccountId : row.AccountId;
        return new Dictionary<string, object?>
        {
            ["Id"] = otherId, ["PlayerID"] = otherId,
            ["RelationshipType"] = RelationshipTypeCodes.GetValueOrDefault(rel, 0),
            ["Favorited"] = 0, ["Muted"] = 0, ["Ignored"] = 0,
            ["AccountId"] = accountId, ["OtherAccountId"] = otherId, ["Relationship"] = rel,
            ["CreatedAt"] = row.CreatedAt.ToString("o"),
        };
    }

    private static readonly Dictionary<int, string> RelationshipTypeNames = new() { [0] = "none", [1] = "pending", [2] = "incoming", [3] = "friends" };

    private static Dictionary<string, object?> RelationshipResponse(int ownerAccountId, int otherId, int relationshipType) => new()
    {
        ["Id"] = otherId, ["PlayerID"] = otherId, ["RelationshipType"] = relationshipType,
        ["Favorited"] = 0, ["Muted"] = 0, ["Ignored"] = 0,
        ["AccountId"] = ownerAccountId, ["OtherAccountId"] = otherId,
        ["Relationship"] = RelationshipTypeNames.GetValueOrDefault(relationshipType, "none"),
        ["CreatedAt"] = DateTime.UtcNow.ToString("o"),
    };

    [HttpGet("/api/relationships/v2/get")]
    public IActionResult RelationshipsGet()
    {
        var accountId = CurrentAccountId();
        var rows = _playerDb.GetRelationshipRows(accountId).OrderBy(r => r.OtherAccountId).ThenBy(r => r.AccountId).ToList();
        var seen = new HashSet<int>();
        var result = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var obj = RelationshipObj(row, accountId);
            var other = (int)obj["OtherAccountId"]!;
            if (!seen.Add(other)) continue;
            result.Add(obj);
        }
        return new OkObjectResult(result);
    }

    [HttpGet("/api/relationships/v2/sendfriendrequest")]
    public async Task<IActionResult> SendFriendRequest([FromQuery] int? id)
    {
        var accountId = CurrentAccountId();
        if (id == null || id == accountId) return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid target account" }) { StatusCode = 400 };

        _playerDb.UpsertRelationship(accountId, id.Value, "pending");
        await _hub.PushToAccountAsync(id.Value, "ReceiveFriendRequest", RelationshipResponse(id.Value, accountId, 2));
        return new OkObjectResult(RelationshipResponse(accountId, id.Value, 1));
    }

    [HttpGet("/api/relationships/v2/acceptfriendrequest")]
    [HttpGet("/api/relationships/v2/addfriend")]
    public async Task<IActionResult> AcceptFriendRequest([FromQuery] int? id)
    {
        var accountId = CurrentAccountId();
        if (id == null || id == accountId) return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid target account" }) { StatusCode = 400 };

        _playerDb.UpsertRelationship(accountId, id.Value, "friends");
        _playerDb.UpsertRelationship(id.Value, accountId, "friends");
        await _hub.PushToAccountAsync(id.Value, "FriendRequestAccepted", RelationshipResponse(id.Value, accountId, 3));
        return new OkObjectResult(RelationshipResponse(accountId, id.Value, 3));
    }

    [HttpGet("/api/relationships/v2/removefriend")]
    public async Task<IActionResult> RemoveFriend([FromQuery] int? id)
    {
        var accountId = CurrentAccountId();
        if (id == null || id == accountId) return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid target account" }) { StatusCode = 400 };

        _playerDb.DeleteRelationship(accountId, id.Value);
        await _hub.PushToAccountAsync(id.Value, "FriendRemoved", RelationshipResponse(id.Value, accountId, 0));
        return Ok2();
    }

    [HttpPost("/api/relationships/v1/ignore")]
    public IActionResult Ignore() => Ok2();

    [HttpPost("/api/relationships/v1/unignore")]
    public IActionResult Unignore() => Ok2();

    [HttpPost("/api/relationships/v1/mute")]
    public IActionResult Mute() => Ok2();

    [HttpPost("/api/relationships/v1/unmute")]
    public IActionResult Unmute() => Ok2();

    [HttpGet("/api/messages/v1/favoriteFriendOnlineStatus")]
    public IActionResult FavoriteFriendStatus() => Content("", "application/json");

    [HttpGet("/api/messages/v1/friendOnlineStatus")]
    public IActionResult FriendOnlineStatus() => new OkObjectResult(new List<object>());

    [HttpGet("/api/messages/v2/get")]
    public IActionResult MessagesGet() => new OkObjectResult(new List<object>());

    [HttpGet("/api/externalfriendinvite/v1/getplatformreferrers")]
    public IActionResult PlatformReferrers() => new OkObjectResult(new List<object>());

    [HttpGet("/api/players/v1/playerPhotoTaggingSetting")]
    public IActionResult PhotoTaggingSetting() => Ok2(new() { ["setting"] = "FriendsOnly" });

    [HttpPost("/invite")]
    public IActionResult Invite() => new OkObjectResult(new List<object>());

    [HttpGet("/api/playerReputation/v1/{pid}")]
    public IActionResult PlayerReputationSingle(string pid)
    {
        var id = int.Parse(pid);
        var cheer = _playerDb.GetSelectedCheer(id);
        var credit = _playerDb.GetCheerCredit(id);
        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["AccountId"] = id, ["Noteriety"] = 0, ["IsCheerful"] = true, ["CheerGeneral"] = 0, ["CheerHelpful"] = 0,
            ["CheerGreatHost"] = 0, ["CheerSportsman"] = 0, ["CheerCreative"] = 0, ["CheerCredit"] = credit,
            ["SelectedCheer"] = cheer, ["CheerCategory"] = cheer,
        });
    }

    [HttpPost("/api/playerReputation/v1/bulk")]
    public IActionResult PlayerReputationBulkV1Post()
    {
        var ids = new List<int>();
        if (Request.Query.TryGetValue("id", out var q) && q.Count > 0)
            ids = q.Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();

        var result = ids.Select(aid =>
        {
            var cheer = _playerDb.GetSelectedCheer(aid);
            var credit = _playerDb.GetCheerCredit(aid);
            return new Dictionary<string, object?>
            {
                ["AccountId"] = aid, ["Noteriety"] = 0, ["IsCheerful"] = true, ["CheerGeneral"] = 0, ["CheerHelpful"] = 0,
                ["CheerGreatHost"] = 0, ["CheerSportsman"] = 0, ["CheerCreative"] = 0, ["CheerCredit"] = credit,
                ["SelectedCheer"] = cheer, ["CheerCategory"] = cheer,
            };
        }).ToList();
        return new OkObjectResult(result);
    }

    [HttpGet("/api/playerReputation/v2/bulk")]
    public IActionResult PlayerReputationBulk([FromQuery(Name = "id")] List<string>? id)
    {
        var result = (id ?? new()).Where(s => int.TryParse(s, out _)).Select(s =>
        {
            var aid = int.Parse(s);
            var cheer = _playerDb.GetSelectedCheer(aid);
            return new Dictionary<string, object?>
            {
                ["accountId"] = aid, ["noteriety"] = 0, ["isCheerful"] = true, ["cheerGeneral"] = 0, ["cheerHelpful"] = 0,
                ["cheerGreatHost"] = 0, ["cheerSportsman"] = 0, ["cheerCreative"] = 0, ["cheerCredit"] = 20,
                ["selectedCheer"] = cheer, ["cheerCategory"] = cheer,
            };
        }).ToList();
        return new OkObjectResult(result);
    }

    [HttpPost("/api/PlayerCheer/v1/create")]
    public async Task<IActionResult> PlayerCheerCreate()
    {
        var accountId = CurrentAccountId();
        var body = ReadJsonBody() ?? new();
        var targetRaw = body.GetValueOrDefault("PlayerIdTo") ?? body.GetValueOrDefault("targetPlayerId") ??
                         body.GetValueOrDefault("TargetPlayerId") ?? body.GetValueOrDefault("targetId") ?? body.GetValueOrDefault("PlayerId");
        var targetId = targetRaw != null && int.TryParse(targetRaw.ToString(), out var t) ? t : 0;

        var remainingCredit = _playerDb.DecrementCheerCredit(accountId);
        var payload = new Dictionary<string, object?>
        {
            ["AccountId"] = accountId, ["Noteriety"] = 0, ["IsCheerful"] = true, ["CheerGeneral"] = 3,
            ["CheerHelpful"] = 0, ["CheerGreatHost"] = 3, ["CheerSportsman"] = 0, ["CheerCreative"] = 0,
            ["CheerCredit"] = remainingCredit, ["SelectedCheer"] = 9000,
        };
        if (targetId != 0)
        {
            var nowStr = DateTime.UtcNow.ToString("o");
            var cheerMsg = new Dictionary<string, object?>
            {
                ["notificationId"] = 0, ["notificationType"] = 50, ["createdAt"] = nowStr, ["updatedAt"] = nowStr,
                ["senderId"] = accountId, ["roomId"] = null, ["payload"] = null,
            };
            await _hub.PushToAccountAsync(targetId, "Cheer", cheerMsg);
        }
        return new OkObjectResult(new Dictionary<string, object?> { ["Message"] = System.Text.Json.JsonSerializer.Serialize(payload), ["Success"] = true });
    }

    [HttpPost("/api/PlayerCheer/v1/SetSelectedCheer")]
    public async Task<IActionResult> PlayerCheerSetSelected()
    {
        var accountId = CurrentAccountId();
        var body = ReadJsonBody() ?? new();
        var cheerRaw = body.GetValueOrDefault("SelectedCheer") ?? body.GetValueOrDefault("selectedCheer") ??
                        body.GetValueOrDefault("CheerId") ?? body.GetValueOrDefault("cheerId") ??
                        body.GetValueOrDefault("CheerCategory") ?? body.GetValueOrDefault("cheerCategory");
        var cheer = cheerRaw != null && int.TryParse(cheerRaw.ToString(), out var c) ? c : 0;
        _playerDb.SetSelectedCheer(accountId, cheer);

        await _hub.BroadcastAsync("ReputationUpdate", new Dictionary<string, object?>
        {
            ["AccountId"] = accountId, ["Noteriety"] = 0, ["IsCheerful"] = true, ["CheerGeneral"] = 0,
            ["CheerHelpful"] = 0, ["CheerGreatHost"] = 0, ["CheerSportsman"] = 0, ["CheerCreative"] = 0,
            ["CheerCredit"] = 0, ["SelectedCheer"] = cheer, ["CheerCategory"] = cheer,
        });
        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true });
    }
}
