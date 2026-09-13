using Microsoft.AspNetCore.Mvc;
using Gallium2021.Classes;

namespace Gallium2021.Controllers;

[Controller]
public class Progression : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly ObjectiveStateStore _objectives;

    public Progression(PlayerDB playerDb, SessionManager sessions, ObjectiveStateStore objectives)
    {
        _playerDb = playerDb;
        _sessions = sessions;
        _objectives = objectives;
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

    private List<int> ExtractBulkIds()
    {
        if (Request.Query.TryGetValue("id", out var q) && q.Count > 0)
            return q.Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
        var body = ReadJsonBody();
        return new List<int>();
    }

    [HttpGet("/api/players/v2/progression/bulk")]
    public IActionResult ProgressionBulk([FromQuery(Name = "id")] List<string>? id)
    {
        var ids = (id ?? new()).Select(int.Parse);
        var result = ids.Select(aid =>
        {
            var (level, xp) = _playerDb.GetProgression(aid);
            return new Dictionary<string, object?> { ["PlayerId"] = aid, ["Level"] = level, ["XP"] = xp };
        }).ToList();
        return new OkObjectResult(result);
    }

    [HttpPost("/api/players/v1/progression/bulk")]
    public IActionResult ProgressionBulkV1Post()
    {
        var ids = new List<int>();
        if (Request.Query.TryGetValue("id", out var q) && q.Count > 0)
        {
            ids = q.Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
        }
        else
        {
            var body = ReadJsonBody();
            if (body != null)
            {
                foreach (var key in new[] { "PlayerIds", "playerIds", "ids", "Ids", "id" })
                {
                    if (body.TryGetValue(key, out var val) && val is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        ids = el.EnumerateArray().Select(e => e.GetInt32()).ToList();
                        break;
                    }
                }
            }
        }
        var result = ids.Select(aid =>
        {
            var (level, xp) = _playerDb.GetProgression(aid);
            return new Dictionary<string, object?> { ["PlayerId"] = aid, ["Level"] = level, ["XP"] = xp };
        }).ToList();
        return new OkObjectResult(result);
    }

    [HttpGet("/api/players/v1/progression/{accountId:int}")]
    public IActionResult ProgressionSingle(int accountId)
    {
        var (level, xp) = _playerDb.GetProgression(accountId);
        return new OkObjectResult(new Dictionary<string, object?> { ["PlayerId"] = accountId, ["Level"] = level, ["XP"] = xp });
    }

    [HttpGet("/player")]
    public IActionResult PlayerSingleByQuery([FromQuery] int? id)
    {
        if (id == null) return new NotFoundObjectResult(new Dictionary<string, object?>());
        var acct = _playerDb.GetAccount(id.Value);
        if (acct == null) return new NotFoundObjectResult(new Dictionary<string, object?>());
        return new OkObjectResult(_playerDb.GetHeartbeats(id.Value));
    }

    [HttpPost("/api/players/v1/progression/{accountId:int}/grant")]
    public IActionResult ProgressionGrant(int accountId)
    {
        var body = ReadJsonBody() ?? new();
        var amount = 0;
        if (body.TryGetValue("xp", out var xpVal) && xpVal is System.Text.Json.JsonElement el) amount = el.GetInt32();

        var result = _playerDb.AwardXp(accountId, amount);
        if (result == null)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "not_found", ["error_description"] = "No account with that id" }) { StatusCode = 404 };

        if (result.LeveledUp)
        {
            var newLevel = result.Level;
            var oldLevel = newLevel - result.LevelsGained;
            for (var lvl = oldLevel + 1; lvl <= newLevel; lvl++)
            {
                var cfg = lvl <= _playerDb.MaxLevel ? _playerDb.LevelProgression[lvl] : null;
                _playerDb.CreateRewardSelection(accountId, message: $"You reached Level {lvl}!", giftContext: 110000, rewardType: 1, catalogGiftDropId: cfg?.GiftDropId);
            }
        }

        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["AccountId"] = result.AccountId, ["XP"] = result.Xp, ["Level"] = result.Level,
            ["LeveledUp"] = result.LeveledUp, ["LevelsGained"] = result.LevelsGained,
        });
    }

    [HttpPost("/api/objectives/v1/cleargroup")]
    public IActionResult ObjectivesClearGroup()
    {
        _objectives.Reset(CurrentAccountId());
        return Ok2();
    }

    [HttpGet("/api/objectives/v1/myprogress")]
    public IActionResult ObjectivesProgress()
    {
        var state = _objectives.Get(CurrentAccountId());
        return new OkObjectResult(ObjectiveStateStore.Serialize(state));
    }

    [HttpPost("/api/objectives/v1/updateobjective")]
    public IActionResult ObjectivesUpdate()
    {
        var accountId = CurrentAccountId();
        var state = _objectives.Get(accountId);
        var body = ReadJsonBody() ?? new();

        var indexRaw = body.GetValueOrDefault("Index") ?? body.GetValueOrDefault("index");
        if (indexRaw == null || !int.TryParse(indexRaw.ToString(), out var index))
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "bad_request", ["error_description"] = "Index is required" }) { StatusCode = 400 };
        if (!state.Objectives.TryGetValue(index, out var objective))
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "bad_request", ["error_description"] = $"Unknown objective index {index}" }) { StatusCode = 400 };

        if (body.ContainsKey("Progress") || body.ContainsKey("progress"))
        {
            var v = body.GetValueOrDefault("Progress") ?? body.GetValueOrDefault("progress");
            objective.Progress = int.Parse(v!.ToString()!);
        }
        else if (body.ContainsKey("Amount") || body.ContainsKey("amount"))
        {
            var v = body.GetValueOrDefault("Amount") ?? body.GetValueOrDefault("amount");
            objective.Progress += int.Parse(v!.ToString()!);
        }
        else
        {
            objective.Progress += 1;
        }
        objective.VisualProgress = objective.Progress;
        if (body.TryGetValue("IsCompleted", out var isCompletedRaw))
        {
            var s = isCompletedRaw?.ToString()?.ToLowerInvariant();
            objective.IsCompleted = s is "true" or "1";
        }
        if (objective.IsCompleted && state.Objectives.Values.All(o => o.IsCompleted))
            state.GroupCompleted = true;

        return new OkObjectResult(ObjectiveStateStore.Serialize(state));
    }

    [HttpGet("/api/gamerewards/v1/pending")]
    public IActionResult GameRewardsPending()
    {
        var accountId = CurrentAccountId();
        var pending = _playerDb.GetPendingRewardSelections(accountId);
        var result = pending.Select(r => new Dictionary<string, object?>
        {
            ["RewardSelectionId"] = r.RewardSelectionId, ["Message"] = r.Message, ["GiftContext"] = r.GiftContext,
            ["RewardType"] = r.RewardType, ["GiftDrop1"] = r.Options.ElementAtOrDefault(0), ["GiftDrop2"] = r.Options.ElementAtOrDefault(1),
            ["GiftDrop3"] = r.Options.ElementAtOrDefault(2), ["CreatedAt"] = r.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        }).ToList();
        return new OkObjectResult(result);
    }

    [HttpPost("/api/gamerewards/v1/request")]
    public IActionResult GameRewardsRequest()
    {
        var accountId = CurrentAccountId();
        var body = ReadJsonBody() ?? new();
        var message = body.GetValueOrDefault("Message")?.ToString() ?? body.GetValueOrDefault("message")?.ToString() ?? "";
        var giftContextRaw = body.GetValueOrDefault("GiftContext") ?? body.GetValueOrDefault("Context");
        var giftContext = giftContextRaw != null ? int.Parse(giftContextRaw.ToString()!) : 110000;
        var rewardTypeRaw = body.GetValueOrDefault("RewardType") ?? body.GetValueOrDefault("rewardType");
        var rewardType = rewardTypeRaw != null ? int.Parse(rewardTypeRaw.ToString()!) : 0;
        var baseTokensRaw = body.GetValueOrDefault("Tokens") ?? body.GetValueOrDefault("BaseTokens");
        int? baseTokens = baseTokensRaw != null ? int.Parse(baseTokensRaw.ToString()!) : null;

        var (rewardSelectionId, options) = _playerDb.CreateRewardSelection(accountId, message, giftContext, rewardType, baseTokens);
        return Ok2(new() { ["rewardSelectionId"] = rewardSelectionId, ["options"] = options });
    }

    [HttpPost("/api/gamerewards/v1/select")]
    public IActionResult GameRewardsSelect()
    {
        var accountId = CurrentAccountId();
        var body = ReadJsonBody() ?? new();
        var rewardSelectionId = body.GetValueOrDefault("RewardSelectionId")?.ToString() ?? body.GetValueOrDefault("rewardSelectionId")?.ToString();
        var giftDropId = body.GetValueOrDefault("GiftDropId") ?? body.GetValueOrDefault("giftDropId");
        var slotIndex = body.GetValueOrDefault("SlotIndex") ?? body.GetValueOrDefault("slotIndex");

        if (string.IsNullOrEmpty(rewardSelectionId))
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "missing RewardSelectionId" }) { StatusCode = 400 };

        var chosen = _playerDb.SelectReward(accountId, rewardSelectionId, giftDropId, slotIndex);
        if (chosen == null)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "reward selection not found" }) { StatusCode = 404 };

        return Ok2(new() { ["giftDropId"] = chosen.GetValueOrDefault("GiftDropId"), ["reward"] = chosen });
    }

    [HttpGet("/api/itemWishlists/v1/wishlist/me")]
    public IActionResult ItemWishlistMe()
    {
        var accountId = CurrentAccountId();
        return new OkObjectResult(_playerDb.GetWishlist(accountId).Select(WishlistJson).ToList());
    }

    [HttpGet("/api/itemWishlists/v1/wishlist/{accountId:int}")]
    public IActionResult ItemWishlistForAccount(int accountId) =>
        new OkObjectResult(_playerDb.GetWishlist(accountId).Select(WishlistJson).ToList());

    private static Dictionary<string, object?> WishlistJson(Models.WishlistItem row) => new()
    {
        ["WishlistItemId"] = row.WishlistItemId, ["AccountId"] = row.AccountId,
        ["PurchasableItemId"] = row.PurchasableItemId, ["CreatedAt"] = row.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff"),
    };

    [HttpGet("/api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    public IActionResult ItemWishlistStatus(int purchasableItemId)
    {
        var accountId = CurrentAccountId();
        var item = _playerDb.GetWishlistItem(accountId, purchasableItemId);
        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true, ["error"] = "", ["value"] = item?.WishlistItemId });
    }

    [HttpPost("/api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    [HttpPut("/api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    public IActionResult ItemWishlistAdd(int purchasableItemId)
    {
        var accountId = CurrentAccountId();
        var id = _playerDb.AddWishlistItem(accountId, purchasableItemId);
        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true, ["error"] = "", ["value"] = id });
    }

    [HttpDelete("/api/itemWishlists/v1/wishlist/me/{purchasableItemId:int}")]
    public IActionResult ItemWishlistRemove(int purchasableItemId)
    {
        var accountId = CurrentAccountId();
        _playerDb.RemoveWishlistItem(accountId, purchasableItemId);
        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true, ["error"] = "", ["value"] = null });
    }

    [HttpGet("/api/playerevents/v1/all")]
    public IActionResult PlayerEventsAll() => new OkObjectResult(new Dictionary<string, object?> { ["Created"] = new List<object>(), ["Responses"] = new List<object>() });

    [HttpGet("/api/playerevents/v1/room/{roomId:int}")]
    public IActionResult PlayerEventsRoom(int roomId) => new OkObjectResult(new List<object>());

    [HttpGet("/api/playerevents/v1/tagfilters")]
    public IActionResult PlayerEventTagFilters() => new OkObjectResult(new Dictionary<string, object?> { ["Tags"] = new List<object>() });

    [HttpGet("/api/playerevents/v1/search")]
    public IActionResult PlayerEventsSearch() => new OkObjectResult(new Dictionary<string, object?> { ["Results"] = new List<object>(), ["TotalResults"] = 0 });

    [HttpGet("/api/progressionEvents/active")]
    public IActionResult ProgressionEventsActive() => Ok2(new() { ["events"] = new List<object>() });

    [HttpGet("/api/progressionEvents/event/id")]
    public IActionResult ProgressionEventById() => Ok2(new() { ["event"] = null });

    [HttpGet("/api/purchasableXpBoosts/activations")]
    public IActionResult XpBoostActivations() => Ok2(new() { ["activations"] = new List<object>() });

    [HttpGet("/api/challenge/v2/getCurrent")]
    public IActionResult ChallengeCurrent() => new OkObjectResult(new Dictionary<string, object?>
    {
        ["ChallengeMapId"] = 1, ["CompletedRequired"] = false, ["StartAt"] = "0001-01-01T00:00:00",
        ["EndAt"] = "2099-01-01T00:00:00+00:00", ["ServerTime"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["Challenges"] = new List<object>(), ["Gift"] = null, ["FallbackGiftName"] = "",
    });

    [HttpGet("/api/keepsakes/globalconfig")]
    public IActionResult KeepsakesGlobalConfig() => Ok2(new() { ["config"] = new Dictionary<string, object?> { ["enabled"] = true, ["maxKeepsakes"] = 100 } });

    [HttpGet("/api/keepsakes/categories")]
    public IActionResult KeepsakesCategories() => new OkObjectResult(new List<Dictionary<string, object?>>
    {
        new() { ["id"] = 1, ["name"] = "Achievements" },
        new() { ["id"] = 2, ["name"] = "Events" },
    });
}
