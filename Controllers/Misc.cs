using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;

namespace Mocha2021.Controllers;

[Controller]
public class Misc : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly RoomInstanceManager _instances;

    public Misc(PlayerDB playerDb, SessionManager sessions, RoomInstanceManager instances)
    {
        _playerDb = playerDb;
        _sessions = sessions;
        _instances = instances;
    }

    private int CurrentAccountId() => _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpPost("/player/heartbeat")]
    [HttpGet("/player/heartbeat")]
    public IActionResult Heartbeat()
    {
        var accountId = CurrentAccountId();
        _playerDb.TouchOnline(accountId);
        _playerDb.LogHeartbeat(accountId);

        var defaultInstance = new Dictionary<string, object?>
        {
            ["roomInstanceId"] = Guid.NewGuid().ToString(), ["roomId"] = 1, ["subRoomId"] = 0, ["roomInstanceType"] = 0,
            ["location"] = RoomInstanceManager.DefaultRoomLocation, ["photonRegionId"] = "us", ["photonRoomId"] = Guid.NewGuid().ToString(), ["name"] = "^DormRoom",
            ["maxCapacity"] = 16, ["isFull"] = false, ["isPrivate"] = true, ["isInProgress"] = true,
            ["EncryptVoiceChat"] = false, ["matchmakingPolicy"] = 0, ["clubId"] = 0, ["eventId"] = 0, ["roomCode"] = "",
        };
        var accountInstance = _instances.GetAccountRoomInstance(accountId);
        var roomInstance = accountInstance != null ? (object)accountInstance.ToJson() : defaultInstance;

        return new JsonResult(new Dictionary<string, object?>
        {
            ["playerId"] = accountId, ["statusVisibility"] = 0, ["deviceClass"] = 2, ["vrMovementMode"] = 1,
            ["roomInstance"] = roomInstance, ["isOnline"] = true, ["appVersion"] = "20210820", ["platform"] = 0,
        });
    }

    [HttpPost("/player/login")]
    public IActionResult PlayerLogin() => Content("0", "text/plain");

    [HttpPost("/player/logout")]
    public IActionResult PlayerLogout() => Content("", "text/plain");

    [HttpPut("/player/photonregionpings")]
    [HttpPost("/player/photonregionpings")]
    public IActionResult PhotonRegionPings() => Content("", "application/json");

    [HttpPut("/player/statusvisibility")]
    [HttpPost("/player/statusvisibility")]
    public IActionResult PlayerStatusVisibility() => Content("0", "application/json");

    [HttpGet("/player/avoidjuniors")]
    [HttpPost("/player/avoidjuniors")]
    [HttpPut("/player/avoidjuniors")]
    public IActionResult AvoidJuniors() => Content("false", "application/json");

    [HttpGet("/subscription/subscriberCount/{accountId:int}")]
    public IActionResult SubscriptionSubscriberCount(int accountId) => Content("0", "application/json");

    [HttpGet("/api/roomkeys/v1/room")]
    public IActionResult RoomkeysRoom() => new OkObjectResult(new List<object>());

    [HttpGet("/api/roomkeys/v1/mine")]
    public IActionResult RoomkeysMine() => new OkObjectResult(new List<object>());

    [HttpGet("/announcements/v2/mine/unread")]
    public IActionResult AnnouncementsUnread() => new OkObjectResult(new List<object>());

    [HttpGet("/announcements/v2/subscription/mine/unread")]
    public IActionResult SubscriptionAnnouncementsUnreadV2() => new OkObjectResult(new List<object>());

    [HttpGet("/datalink/data/{code}")]
    public IActionResult DatalinkData(string code) => Ok2(new() { ["data"] = null, ["code"] = code });

    [HttpPost("/datalink")]
    public IActionResult Datalink() => Ok2(new() { ["code"] = Guid.NewGuid().ToString("N")[..8] });

    [HttpGet("/referral")]
    public IActionResult Referral() => Ok2(new() { ["referrerId"] = null });

    [HttpGet("/actionlink/valid")]
    public IActionResult ActionlinkValid() => Ok2(new() { ["valid"] = true });

    [HttpPost("/actionlink")]
    public IActionResult Actionlink()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var code = new string(Enumerable.Range(0, 9).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
        return new OkObjectResult($"{ServerConfig.ServerBaseUrl}/p/Share?a={code}");
    }

    [HttpPost("/newPlayer")]
    public IActionResult NewPlayer([FromForm] Dictionary<string, string>? form)
    {
        var data = form ?? new();
        data.TryGetValue("username", out var username);
        data.TryGetValue("password", out var password);
        data.TryGetValue("platformId", out var p1);
        data.TryGetValue("platform_id", out var p2);
        var platformId = p1 ?? p2;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid_request", ["error_description"] = "username and password are required" }) { StatusCode = 400 };

        var existing = _playerDb.GetAccountByUsername(username);
        if (existing != null)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "username_taken", ["error_description"] = "that username is already in use" }) { StatusCode = 400 };

        var pwHash = BCrypt.Net.BCrypt.HashPassword(password);
        data.TryGetValue("displayName", out var displayName);
        var account = _playerDb.CreateAccount(username, displayName ?? username, pwHash, platformId);

        var token = _sessions.MakeToken();
        _sessions.RegisterToken(token, account.AccountId);
        if (!string.IsNullOrEmpty(platformId)) _sessions.RegisterPlatformToken(platformId, token, account.AccountId);

        return Ok2(new() { ["accountId"] = account.AccountId, ["username"] = username, ["access_token"] = token });
    }

    [HttpPost("/newInstall")]
    public IActionResult NewInstall() => Ok2();

    [HttpPost("/pageview/consume")]
    public IActionResult PageviewConsume() => Ok2();

    [HttpPost("/platformnotifications/register")]
    public IActionResult PlatformNotificationsRegister() => Ok2(new() { ["registered"] = true });

    [HttpGet("/playersettings/me")]
    public IActionResult PlayerSettingsMe() => Ok2(new()
    {
        ["settings"] = new Dictionary<string, object?>
        {
            ["voiceChat"] = true, ["textChat"] = true, ["showOnlineStatus"] = true, ["allowFriendRequests"] = true,
            ["screensMinimalHUD"] = "0", ["minimalHUD"] = "0", ["screensMinimalHud"] = "0",
        },
    });

    [HttpPut("/playersettings/me")]
    public IActionResult PlayerSettingsUpdate() => Ok2(new() { ["updated"] = true });

    [HttpGet("/discovery/v1/feed")]
    public IActionResult DiscoveryFeed() => Ok2(new()
    {
        ["feed"] = Enumerable.Range(0, 5).Select(i => RoomStub(ServerConfig.MockRoomId + i)).ToList(),
        ["hasMore"] = false,
    });

    private static Dictionary<string, object?> RoomStub(int rid) => new()
    {
        ["roomId"] = rid, ["name"] = "Mock Room", ["description"] = "A mock Rec Room.",
        ["playerCount"] = Random.Shared.Next(1, 21), ["maxPlayers"] = 20, ["isPrivate"] = false,
        ["isDorm"] = false, ["isRRO"] = false, ["tags"] = new List<string> { "Action" },
        ["createdAt"] = "2023-01-01T00:00:00Z",
        ["stats"] = new Dictionary<string, object?> { ["visits"] = 9999, ["cheers"] = 420, ["favorites"] = 69 },
    };

    [HttpGet("/cards/v1/mine")]
    public IActionResult CardsMine() => Ok2(new() { ["cards"] = new List<object>() });

    [HttpGet("/lists/v1/mine")]
    public IActionResult ListsMine() => Ok2(new() { ["lists"] = new List<object>() });

    [HttpGet("/purchase/v1/hasspentmoney")]
    public IActionResult HasSpentMoney() => Content("false", "application/json");

    [HttpPost("/api/Leaderboard/CheckAndSetStat")]
    public IActionResult LeaderboardCheckSet() => Ok2(new() { ["updated"] = true, ["newValue"] = 0 });

    [HttpGet("/api/Leaderboard/GetPlayerRank")]
    public IActionResult LeaderboardPlayerRank() => Ok2(new() { ["rank"] = 1, ["score"] = 0, ["accountId"] = ServerConfig.MockPlayerId });

    [HttpGet("/api/Leaderboard/GetRanks")]
    public IActionResult LeaderboardGetRanks() => Ok2(new() { ["ranks"] = new List<object>(), ["totalCount"] = 0 });

    [HttpGet("/api/quickPlay/v1/getandclear")]
    [HttpPost("/api/quickPlay/v1/getandclear")]
    public IActionResult QuickPlay() => Ok2(new() { ["queue"] = new List<object>(), ["cleared"] = true });
}
