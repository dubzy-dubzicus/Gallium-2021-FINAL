using Microsoft.AspNetCore.Mvc;
using Gallium2021.Classes;

namespace Gallium2021.Controllers;

[Controller]
public class Match : ControllerBase
{
    private readonly SessionManager _sessions;

    public Match(SessionManager sessions)
    {
        _sessions = sessions;
    }

    private static long Ts() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpPost("/hub/v1/negotiate")]
    public IActionResult HubNegotiate() => new OkObjectResult(new Dictionary<string, object?>
    {
        ["negotiateVersion"] = 0,
        ["connectionId"] = Guid.NewGuid().ToString("N"),
        ["availableTransports"] = new List<Dictionary<string, object?>>
        {
            new() { ["transport"] = "WebSockets", ["transferFormats"] = new[] { "Text", "Binary" } },
        },
    });

    [HttpGet("/Matchmaking/player")]
    public IActionResult MatchmakingPlayer() => Ok2(new()
    {
        ["player"] = new Dictionary<string, object?> { ["accountId"] = ServerConfig.MockPlayerId, ["status"] = "Online", ["roomInstanceId"] = null },
    });

    [HttpPost("/Matchmaking/player/login")]
    public IActionResult MatchmakingLogin() => Ok2(new() { ["sessionId"] = ServerConfig.MockSessionId, ["serverTime"] = Ts() });

    [HttpPost("/Matchmaking/player/logout")]
    public IActionResult MatchmakingLogout() => Ok2();

    [HttpPost("/Matchmaking/player/heartbeat")]
    public IActionResult MatchmakingHeartbeat() => Ok2(new() { ["serverTime"] = Ts() });

    [HttpGet("/Matchmaking/player/qos")]
    public IActionResult MatchmakingQos() => Ok2(new() { ["qos"] = new Dictionary<string, object?> { ["ping"] = 42, ["region"] = "us" } });

    [HttpGet("/Matchmaking/player/connection-info")]
    public IActionResult MatchmakingConnectionInfo() => Ok2(new()
    {
        ["connectionInfo"] = new Dictionary<string, object?> { ["ip"] = "127.0.0.1", ["port"] = 5000, ["region"] = "us" },
    });

    [HttpPost("/Matchmaking/player/exclusivelogin")]
    public IActionResult MatchmakingExclusiveLogin() => Ok2(new() { ["sessionId"] = ServerConfig.MockSessionId });

    [HttpPut("/Matchmaking/player/gameserverregionpings")]
    public IActionResult RegionPings() => Ok2();

    [HttpPut("/Matchmaking/player/statusvisibility")]
    public IActionResult StatusVisibility() => Ok2();

    [HttpPost("/Matchmaking/matchmake/dorm")]
    public IActionResult MatchmakeDorm()
    {
        var accountId = _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());
        var inst = RoomInstanceStub(1_000_000 + accountId);
        inst["roomInstanceType"] = "DormRoom";
        inst["isPrivate"] = true;
        return Ok2(new() { ["roomInstance"] = inst });
    }

    [HttpPost("/Matchmaking/matchmake/v2/room/{rid}")]
    public IActionResult MatchmakeRoom(string rid) => Ok2(new() { ["roomInstance"] = RoomInstanceStub(int.Parse(rid)) });

    [HttpPost("/Matchmaking/matchmake/none")]
    public IActionResult MatchmakeNone() => Ok2();

    [HttpPost("/Matchmaking/roominstance/{rid}/reportjoinresult")]
    public IActionResult ReportJoinResult(string rid) => Ok2();

    [HttpGet("/Matchmaking/rooms/requiring/developer")]
    public IActionResult RoomsRequiringDeveloper() => Ok2(new() { ["rooms"] = new List<object>() });

    [HttpGet("/Matchmaking/rooms/requiring/rrplus")]
    public IActionResult RoomsRequiringRrplus() => Ok2(new() { ["rooms"] = new List<object>() });

    private static Dictionary<string, object?> RoomInstanceStub(int rid) => new()
    {
        ["roomId"] = rid,
        ["roomInstanceId"] = Guid.NewGuid().ToString(),
        ["subRoomId"] = 0,
        ["roomInstanceType"] = "Normal",
        ["roomCode"] = $"RR+{Random.Shared.Next(100000, 1000000)}",
        ["photonRegionId"] = "us",
        ["photonRoomId"] = Guid.NewGuid().ToString(),
        ["maxCapacity"] = 20,
        ["isFull"] = false,
        ["isPrivate"] = false,
        ["isInProgress"] = true,
        ["encryptVoiceChat"] = true,
    };
}
