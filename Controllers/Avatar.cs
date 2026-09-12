using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;

namespace Mocha2021.Controllers;

[Controller]
public class Avatar : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly List<Dictionary<string, object?>> _defaultAvatarItems;

    public Avatar(PlayerDB playerDb, SessionManager sessions, IWebHostEnvironment env)
    {
        _playerDb = playerDb;
        _sessions = sessions;
        var path = Path.Combine(env.ContentRootPath, "Seed", "DefaultAvatarItems.json");
        _defaultAvatarItems = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(System.IO.File.ReadAllText(path))!;
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

    [HttpGet("/api/avatar/v2")]
    [HttpPut("/api/avatar/v2")]
    [HttpPost("/api/avatar/v2")]
    public IActionResult AvatarV2()
    {
        var accountId = CurrentAccountId();
        if (Request.Method == "GET")
        {
            var data = _playerDb.GetAvatarData(accountId);
            return new OkObjectResult(new Dictionary<string, object?>
            {
                ["OutfitSelections"] = data.OutfitSelections, ["HairColor"] = data.HairColor,
                ["SkinColor"] = data.SkinColor, ["FaceFeatures"] = data.FaceFeatures,
            });
        }
        var body = ReadJsonBody() ?? new();
        var outfit = body.GetValueOrDefault("OutfitSelections")?.ToString() ?? body.GetValueOrDefault("outfitSelections")?.ToString() ?? "";
        var hair = body.GetValueOrDefault("HairColor")?.ToString() ?? body.GetValueOrDefault("hairColor")?.ToString() ?? "";
        var skin = body.GetValueOrDefault("SkinColor")?.ToString() ?? body.GetValueOrDefault("skinColor")?.ToString() ?? "";
        var face = body.GetValueOrDefault("FaceFeatures")?.ToString() ?? body.GetValueOrDefault("faceFeatures")?.ToString() ?? "";
        _playerDb.SaveAvatarData(accountId, outfit, hair, skin, face);
        return Ok2();
    }

    [HttpGet("/api/avatar/v2/gifts")]
    public IActionResult AvatarGifts()
    {
        var gifts = _playerDb.GetPendingGifts(CurrentAccountId());
        return new OkObjectResult(gifts.Select(GiftToJson).ToList());
    }

    private static Dictionary<string, object?> GiftToJson(Models.Gift r) => new()
    {
        ["Id"] = r.Id, ["FromPlayerId"] = r.FromPlayerId, ["ConsumableItemDesc"] = r.ConsumableItemDesc ?? "",
        ["AvatarItemDesc"] = r.AvatarItemDesc ?? "", ["EquipmentPrefabName"] = r.EquipmentPrefabName ?? "",
        ["EquipmentModificationGuid"] = r.EquipmentModificationGuid ?? "", ["CurrencyType"] = r.CurrencyType,
        ["Currency"] = r.Currency, ["Xp"] = r.Xp, ["Level"] = r.Level, ["Platform"] = r.Platform,
        ["PlatformsToSpawnOn"] = r.PlatformsToSpawnOn, ["BalanceType"] = r.BalanceType, ["GiftContext"] = r.GiftContext,
        ["GiftRarity"] = r.GiftRarity, ["Message"] = r.Message ?? "", ["AvatarItemType"] = r.AvatarItemType,
        ["FriendlyName"] = r.FriendlyName ?? "", ["Tooltip"] = r.Tooltip ?? "",
    };

    [HttpGet("/api/avatar/v3/saved")]
    public IActionResult AvatarSavedV3() => new OkObjectResult(new List<object>());

    [HttpGet("/api/avatar/v2/set")]
    [HttpPost("/api/avatar/v2/set")]
    public IActionResult AvatarSave()
    {
        if (Request.Method == "GET") return new OkObjectResult(new Dictionary<string, object?>());
        var accountId = CurrentAccountId();
        var body = ReadJsonBody() ?? new();
        var outfit = body.GetValueOrDefault("OutfitSelections")?.ToString() ?? body.GetValueOrDefault("outfitSelections")?.ToString() ?? "";
        var hair = body.GetValueOrDefault("HairColor")?.ToString() ?? body.GetValueOrDefault("hairColor")?.ToString() ?? "";
        var skin = body.GetValueOrDefault("SkinColor")?.ToString() ?? body.GetValueOrDefault("skinColor")?.ToString() ?? "";
        var face = body.GetValueOrDefault("FaceFeatures")?.ToString() ?? body.GetValueOrDefault("faceFeatures")?.ToString() ?? "";
        _playerDb.SaveAvatarData(accountId, outfit, hair, skin, face);
        return new OkObjectResult(new Dictionary<string, object?>());
    }

    [HttpPost("/api/avatar/v2/gifts/consume/")]
    [HttpPost("/api/avatar/v2/gifts/consume/{giftId}")]
    public IActionResult AvatarGiftsConsume(string? giftId = null)
    {
        var accountId = CurrentAccountId();
        if (accountId != 0 && !string.IsNullOrEmpty(giftId) && long.TryParse(giftId, out var gid) && gid != 0)
        {
            _playerDb.ConsumeGift(accountId, gid);
        }
        return new OkObjectResult(new Dictionary<string, object?> { ["Success"] = true });
    }

    [HttpPost("/api/consumables/v1/consume")]
    public IActionResult ConsumablesV1Consume() => new OkObjectResult(new Dictionary<string, object?> { ["success"] = true });

    [HttpGet("/api/consumables/v2/getUnlocked")]
    public IActionResult ConsumablesUnlocked() => new OkObjectResult(new List<object>());

    [HttpGet("/api/equipment/v2/getUnlocked")]
    public IActionResult EquipmentUnlocked() => new OkObjectResult(new List<object>());

    [HttpGet("/api/avatar/v1/defaultunlocked")]
    public IActionResult DefaultAvatar() => new OkObjectResult(new List<object>());

    [HttpGet("/api/avatar/v4/items")]
    public IActionResult AvatarItems()
    {
        var accountId = CurrentAccountId();
        var itemsByDesc = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var item in _defaultAvatarItems)
            itemsByDesc[item["AvatarItemDesc"]!.ToString()!] = item;

        if (accountId != 0)
        {
            foreach (var granted in _playerDb.GetAccountAvatarItems(accountId))
            {
                itemsByDesc[granted.AvatarItemDesc] = new Dictionary<string, object?>
                {
                    ["AvatarItemType"] = granted.AvatarItemType, ["AvatarItemDesc"] = granted.AvatarItemDesc,
                    ["FriendlyName"] = granted.FriendlyName, ["ToolTip"] = granted.Tooltip, ["Rarity"] = granted.Rarity,
                };
            }
        }
        return new OkObjectResult(itemsByDesc.Values.ToList());
    }

    [HttpGet("/api/customAvatarItems/v1/bulk")]
    public IActionResult CustomAvatarItemsBulk() => new OkObjectResult(new List<object>());

    [HttpGet("/api/customAvatarItems/v1/isRenderingEnabled")]
    public IActionResult IsRenderingEnabled() => Ok2(new() { ["enabled"] = true });

    [HttpGet("/api/customAvatarItems/v1/isCreationEnabled")]
    public IActionResult IsCreationEnabled() => Ok2(new() { ["enabled"] = true });

    [HttpGet("/api/customAvatarItems/v1/isCreationAllowedForAccount")]
    public IActionResult IsCreationAllowedForAccount() => Ok2(new() { ["allowed"] = true });

    [HttpGet("/outfits/me/saved")]
    public IActionResult OutfitsSaved() => new OkObjectResult(new List<object>());
}
