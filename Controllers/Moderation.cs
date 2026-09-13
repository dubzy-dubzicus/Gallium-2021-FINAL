using Microsoft.AspNetCore.Mvc;
using Gallium2021.Classes;

namespace Gallium2021.Controllers;

[Controller]
public class Moderation : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;

    public Moderation(PlayerDB playerDb, SessionManager sessions)
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

    [HttpGet("/api/PlayerReporting/v1/voteToKickReasons")]
    public IActionResult VoteToKickReasons() => new OkObjectResult(new List<Dictionary<string, object?>>
    {
        new() { ["ReportCategory"] = 102, ["Reason"] = "Discriminatory language" },
        new() { ["ReportCategory"] = 102, ["Reason"] = "Discriminatory behavior" },
        new() { ["ReportCategory"] = 102, ["Reason"] = "Toxic behavior" },
        new() { ["ReportCategory"] = 101, ["Reason"] = "Sexual behavior in public" },
        new() { ["ReportCategory"] = 103, ["Reason"] = "Microphone spam" },
        new() { ["ReportCategory"] = 103, ["Reason"] = "Abusing bugs or exploits" },
        new() { ["ReportCategory"] = 6, ["Reason"] = "Inactive in games (AFK)" },
        new() { ["ReportCategory"] = 6, ["Reason"] = "Not following game rules" },
    });

    [HttpGet("/api/PlayerReporting/v1/moderationBlockDetails")]
    [HttpPost("/api/PlayerReporting/v1/moderationBlockDetails")]
    public IActionResult ModerationBlockDetails()
    {
        var accountId = CurrentAccountId();
        var action = _playerDb.GetActiveModerationAction(accountId);
        if (action == null)
        {
            return new OkObjectResult(new Dictionary<string, object?>
            {
                ["ReportCategory"] = 0, ["Duration"] = 0, ["GameSessionId"] = 0,
                ["IsHostKick"] = false, ["PlayerIdReporter"] = null, ["Message"] = null, ["IsBan"] = false,
            });
        }

        int duration;
        if (action.ExpiresAt == null) duration = int.MaxValue;
        else duration = Math.Max(0, (int)(action.ExpiresAt.Value - DateTime.UtcNow).TotalSeconds);

        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["ReportCategory"] = action.ReportCategory, ["Duration"] = duration, ["GameSessionId"] = action.GameSessionId,
            ["IsHostKick"] = action.IsHostKick, ["PlayerIdReporter"] = action.ModeratorId,
            ["Message"] = action.Message ?? "", ["IsBan"] = action.IsBan,
        });
    }

    [HttpPost("/api/PlayerReporting/v1/roomModKick")]
    public IActionResult RoomModKick() => Ok2();

    private Dictionary<string, string> ParseBugReportBody()
    {
        if (Request.HasFormContentType)
            return Request.Form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        try { Request.Body.Position = 0; } catch { }
        using var reader = new StreamReader(Request.Body);
        var raw = reader.ReadToEndAsync().GetAwaiter().GetResult() ?? "";
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(raw);
            if (dict != null) return dict.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "");
        }
        catch { }

        var fields = new Dictionary<string, string>();
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || !trimmed.Contains(':')) continue;
            var idx = trimmed.IndexOf(':');
            fields[trimmed[..idx].Trim()] = trimmed[(idx + 1)..].Trim();
        }
        return fields;
    }

    [HttpPost("/api/bugreporting/v2/reportbug")]
    public IActionResult ReportBug()
    {
        var accountId = CurrentAccountId();
        var fields = ParseBugReportBody();
        try
        {
            _playerDb.CreateBugReport(accountId, fields.GetValueOrDefault("Summary", ""), fields.GetValueOrDefault("Description", ""),
                fields.GetValueOrDefault("BuildVersion", ""), fields.GetValueOrDefault("BuildTimestamp", ""),
                System.Text.Json.JsonSerializer.Serialize(fields));
        }
        catch { }
        return Ok2();
    }

    [HttpPost("/api/PlayerReporting/v1/hile")]
    public IActionResult PlayerReportingHile() => Content("", "text/plain");

    [HttpPost("/api/PlayerReporting/v1/instantKick")]
    public IActionResult InstantKick() => Ok2();

    [HttpPost("/api/PlayerReporting/v1/deviceId")]
    public IActionResult ReportDeviceId() => Ok2();

    [HttpPost("/api/PlayerReporting/v3/voteToKick")]
    public IActionResult VoteToKick() => Ok2(new() { ["voteId"] = Guid.NewGuid().ToString() });

    [HttpPost("/api/PlayerReporting/v3/create")]
    public IActionResult ReportCreate() => Ok2(new() { ["reportId"] = Guid.NewGuid().ToString() });

    [HttpPost("/api/PlayerReporting/Response")]
    public IActionResult PlayerReportingResponse() => Ok2();

    [HttpPost("/api/screensharereports/v1/report")]
    public IActionResult ScreenshareReport() => Ok2(new() { ["reportId"] = Guid.NewGuid().ToString() });

    private string ExtractSanitizeInputText()
    {
        try { Request.Body.Position = 0; } catch { }
        using var reader = new StreamReader(Request.Body);
        var raw = reader.ReadToEndAsync().GetAwaiter().GetResult() ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return "";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.String) return root.GetString() ?? "";
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var key in new[] { "value", "text", "message", "content", "chattext", "chatmessage" })
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Name.ToLowerInvariant() == key && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                            return prop.Value.GetString() ?? "";
                    }
                }
                foreach (var prop in root.EnumerateObject())
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String) return prop.Value.GetString() ?? "";
                return "";
            }
        }
        catch { }
        return raw;
    }

    [HttpPost("/api/sanitize/v1")]
    public IActionResult SanitizeText() => new OkObjectResult(Sanitizer.SanitizeText(ExtractSanitizeInputText()));

    [HttpPost("/api/sanitize/v1/isPure")]
    public IActionResult IsPure() => new OkObjectResult(new Dictionary<string, object?> { ["IsPure"] = Sanitizer.IsPure(ExtractSanitizeInputText()) });
}
