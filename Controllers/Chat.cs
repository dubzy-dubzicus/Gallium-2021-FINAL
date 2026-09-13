using Microsoft.AspNetCore.Mvc;
using Gallium2021.Classes;

namespace Gallium2021.Controllers;

[Controller]
public class Chat : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;

    public Chat(PlayerDB playerDb, SessionManager sessions)
    {
        _playerDb = playerDb;
        _sessions = sessions;
    }

    private int CurrentAccountId() => _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpGet("/chat/thread")]
    public IActionResult ChatThread() => new OkObjectResult(new List<object>());

    [HttpGet("/reminder/v1")]
    public IActionResult Reminder() => new OkObjectResult(new Dictionary<string, object?>());

    [HttpGet("/chat/thread/party")]
    public IActionResult ChatThreadParty() => Ok2(new()
    {
        ["party"] = new Dictionary<string, object?> { ["threadId"] = Guid.NewGuid().ToString(), ["members"] = new[] { ServerConfig.MockPlayerId } },
    });

    [HttpGet("/chat/thread/chatPrivacySetting")]
    public IActionResult ChatPrivacySetting() => Ok2(new() { ["setting"] = "FriendsOnly" });

    [HttpGet("/Notifications/hub/v1")]
    public IActionResult NotificationsHub()
    {
        var accountId = CurrentAccountId();
        var nowStr = DateTime.UtcNow.ToString("o");
        var notifications = new List<Dictionary<string, object?>>();
        var notifIdCounter = 1;

        var friendRows = _playerDb.GetPendingIncomingRequests(accountId);
        foreach (var row in friendRows)
        {
            var senderId = row.AccountId;
            var createdStr = row.CreatedAt.ToString("o");
            notifications.Add(new Dictionary<string, object?>
            {
                ["notificationId"] = notifIdCounter, ["notificationType"] = 40, ["createdAt"] = createdStr, ["updatedAt"] = createdStr,
                ["senderId"] = senderId, ["roomId"] = null,
                ["payload"] = new Dictionary<string, object?>
                {
                    ["Id"] = senderId, ["PlayerID"] = senderId, ["RelationshipType"] = 2, ["Favorited"] = 0, ["Muted"] = 0,
                    ["Ignored"] = 0, ["AccountId"] = accountId, ["OtherAccountId"] = senderId, ["Relationship"] = "incoming",
                    ["CreatedAt"] = DateTime.UtcNow.ToString("o"),
                },
            });
            notifIdCounter++;
        }

        return Ok2(new() { ["notifications"] = notifications, ["unreadCount"] = notifications.Count });
    }

    [HttpPost("/Notifications/hub/v1/negotiate")]
    public IActionResult NotificationsNegotiate() => new OkObjectResult(new Dictionary<string, object?>
    {
        ["negotiateVersion"] = 0,
        ["connectionId"] = Guid.NewGuid().ToString("N"),
        ["availableTransports"] = new List<Dictionary<string, object?>>
        {
            new() { ["transport"] = "WebSockets", ["transferFormats"] = new[] { "Text", "Binary" } },
        },
    });

    [HttpGet("/Notifications/crm/me/config/v3")]
    public IActionResult NotificationsCrmConfig() => Ok2(new()
    {
        ["config"] = new Dictionary<string, object?> { ["pushEnabled"] = true, ["emailEnabled"] = false, ["frequency"] = "Instant" },
    });
}
