using Microsoft.AspNetCore.Mvc;
using Gallium2021.Classes;
using Gallium2021.Hub;

namespace Gallium2021.Controllers;

[Controller]
public class Commerce : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly CommerceStore _store;
    private readonly HubState _hub;

    public Commerce(PlayerDB playerDb, SessionManager sessions, CommerceStore store, HubState hub)
    {
        _playerDb = playerDb;
        _sessions = sessions;
        _store = store;
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

    [HttpGet("/api/storefronts/v1/balanceAddType/{currency:int}/{addType:int}")]
    public IActionResult StorefrontBalanceAddType(int currency, int addType)
    {
        var balance = currency == 2 ? _playerDb.GetTokenBalance(CurrentAccountId()) : 0;
        return new OkObjectResult(new Dictionary<string, object?> { ["Balance"] = balance, ["CurrencyType"] = currency, ["BalanceType"] = addType });
    }

    [HttpGet("/api/storefronts/v1/p2p/betaEnabled")]
    public IActionResult P2pBetaEnabled() => new OkObjectResult(true);

    [HttpGet("/api/roomcurrencies/v1/betaEnabled")]
    public IActionResult RoomcurrenciesBetaEnabled() => new OkObjectResult(true);

    [HttpGet("/api/roomcurrencies/v1/getAllBalances")]
    public IActionResult RoomcurrenciesGetAllBalances() => new OkObjectResult(new List<object>());

    [HttpGet("/api/roomcurrencies/v1/currencies")]
    public IActionResult RoomcurrenciesCurrencies() => new OkObjectResult(new List<object>());

    [HttpGet("/api/storefronts/v1/adcarouselitems")]
    public IActionResult AdCarouselItems() => new OkObjectResult(new List<Dictionary<string, object?>>
    {
        new() { ["AdCarouselItemId"] = 1, ["ImageName"] = "nothing", ["Title"] = "There's nothing.", ["Description"] = "Thank you son.", ["PurchasableItemIds"] = new List<object>() },
    });

    [HttpGet("/econ/roomInventory/room/{rid}")]
    public IActionResult RoomInventory(string rid) => Ok2(new() { ["inventory"] = new List<object>(), ["roomId"] = int.Parse(rid) });

    [HttpGet("/econ/roomInventory/room/{rid}/player")]
    public IActionResult RoomInventoryPlayer(string rid) => Ok2(new() { ["playerInventory"] = new List<object>(), ["roomId"] = int.Parse(rid), ["accountId"] = ServerConfig.MockPlayerId });

    [HttpGet("/econ/roomInventoryItemTags/room/{rid}")]
    public IActionResult RoomInventoryItemTags(string rid) => Ok2(new() { ["tags"] = new List<object>(), ["roomId"] = int.Parse(rid) });

    [HttpGet("/econ/roomOffer/room/{rid}/purchaseCounts")]
    public IActionResult RoomOfferPurchaseCounts(string rid) => Ok2(new() { ["purchaseCounts"] = new List<object>(), ["roomId"] = int.Parse(rid) });

    [HttpGet("/econ/roomOffer/room/{rid}")]
    public IActionResult RoomOffer(string rid)
    {
        var roomId = int.Parse(rid);
        var storefrontType = _store.ResolveStorefrontType(roomId);
        var items = _store.ItemsForStorefront(storefrontType.ToString());

        var offers = items.Select(entry =>
        {
            var giftDrop = entry.GetValueOrDefault("GiftDrop") as Dictionary<string, object?> ?? new();
            return new Dictionary<string, object?>
            {
                ["PurchasableItemId"] = entry.GetValueOrDefault("PurchasableItemId"),
                ["GiftDropId"] = giftDrop.GetValueOrDefault("GiftDropId"),
                ["RoomId"] = roomId,
                ["StorefrontType"] = storefrontType,
                ["Prices"] = entry.GetValueOrDefault("Prices") ?? new List<object>(),
                ["SubscriberPrices"] = entry.GetValueOrDefault("SubscriberPrices") ?? new List<object>(),
                ["IsFeatured"] = entry.GetValueOrDefault("IsFeatured") ?? false,
            };
        }).ToList();

        return Ok2(new() { ["offers"] = offers, ["roomId"] = roomId });
    }

    [HttpGet("/econ/roomGiftDropShops/room/{rid}")]
    public IActionResult GiftDropShops(string rid)
    {
        var roomId = int.Parse(rid);
        var storefrontType = _store.ResolveStorefrontType(roomId);
        return Ok2(new() { ["shops"] = new List<Dictionary<string, object?>> { new() { ["StorefrontType"] = storefrontType, ["RoomId"] = roomId } }, ["roomId"] = roomId });
    }

    [HttpGet("/econ/roomEconConfig/{rid}")]
    public IActionResult RoomEconConfig(string rid) => Ok2(new() { ["config"] = new Dictionary<string, object?> { ["enabled"] = true, ["currency"] = "Tokens" }, ["roomId"] = int.Parse(rid) });

    [HttpGet("/Commerce/api/catalog/v1/all")]
    public IActionResult CommerceCatalog() => new OkObjectResult(new List<object>());

    [HttpGet("/Commerce/purchasecampaign/allcurrent/v2")]
    public IActionResult PurchaseCampaigns() => Ok2(new() { ["campaigns"] = new List<object>() });

    [HttpGet("/api/storefronts/v4/balance/{currency}")]
    public IActionResult StorefrontBalance(string currency)
    {
        var currencyType = int.TryParse(currency, out var c) ? c : 2;
        var balance = currencyType == 2 ? _playerDb.GetTokenBalance(CurrentAccountId()) : 0;
        return new OkObjectResult(new List<Dictionary<string, object?>> { new() { ["Balance"] = balance, ["CurrencyType"] = currencyType, ["BalanceType"] = -2 } });
    }

    [HttpGet("/api/storefronts/v3/giftdropstore/{sid}")]
    public IActionResult GiftDropStore(string sid)
    {
        var items = _store.ItemsForStorefront(sid);
        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["StorefrontType"] = int.TryParse(sid, out var i) ? i : sid,
            ["NextUpdate"] = _store.NextUpdate(),
            ["StoreItems"] = items,
            ["SubscriberDiscountPercent"] = _store.SubscriberDiscountPercent(),
        });
    }

    [HttpPost("/api/storefronts/v2/buyItem")]
    public async Task<IActionResult> StorefrontBuyItem()
    {
        var accountId = CurrentAccountId();
        var body = ReadJsonBody() ?? new();

        var purchasableItemIdRaw = body.GetValueOrDefault("PurchasableItemId") ?? body.GetValueOrDefault("purchasableItemId") ?? body.GetValueOrDefault("Id");
        if (purchasableItemIdRaw == null)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "missing PurchasableItemId" }) { StatusCode = 400 };
        var purchasableItemId = int.Parse(purchasableItemIdRaw.ToString()!);

        var toPlayerIdRaw = body.GetValueOrDefault("ToPlayerId") ?? body.GetValueOrDefault("toPlayerId");
        var toPlayerId = toPlayerIdRaw != null ? int.Parse(toPlayerIdRaw.ToString()!) : accountId;
        var giftMessage = body.GetValueOrDefault("GiftMessage")?.ToString() ?? body.GetValueOrDefault("Message")?.ToString() ?? body.GetValueOrDefault("message")?.ToString() ?? "A gift for you <3";
        var giftContextRaw = body.GetValueOrDefault("GiftContext") ?? body.GetValueOrDefault("Context");
        var giftContext = giftContextRaw != null ? int.Parse(giftContextRaw.ToString()!) : 110000;
        var requestedCurrencyTypeRaw = body.GetValueOrDefault("CurrencyType");
        int? requestedCurrencyType = requestedCurrencyTypeRaw != null ? int.Parse(requestedCurrencyTypeRaw.ToString()!) : null;

        var selectedEntry = _store.FindStoreItem(purchasableItemId);
        if (selectedEntry == null)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "item not found" }) { StatusCode = 404 };

        var selectedGiftDrop = selectedEntry.GetValueOrDefault("GiftDrop") as Dictionary<string, object?> ?? new();
        var prices = selectedEntry.GetValueOrDefault("Prices") as List<object?> ?? new();

        Dictionary<string, object?>? priceEntry = null;
        if (requestedCurrencyType != null)
        {
            priceEntry = prices.OfType<Dictionary<string, object?>>().FirstOrDefault(p =>
                p.GetValueOrDefault("CurrencyType") != null && int.Parse(p["CurrencyType"]!.ToString()!) == requestedCurrencyType);
        }
        priceEntry ??= prices.OfType<Dictionary<string, object?>>().FirstOrDefault() ?? new Dictionary<string, object?> { ["CurrencyType"] = 2, ["Price"] = 0 };

        var currencyType = priceEntry.GetValueOrDefault("CurrencyType") != null ? int.Parse(priceEntry["CurrencyType"]!.ToString()!) : 2;
        var price = priceEntry.GetValueOrDefault("Price") != null ? int.Parse(priceEntry["Price"]!.ToString()!) : 0;

        var currentTokens = currencyType == 2 ? _playerDb.GetTokenBalance(accountId) : 0;
        if (currencyType == 2 && currentTokens < price)
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "insufficient funds" }) { StatusCode = 400 };

        var newBalance = currentTokens;
        if (currencyType == 2) newBalance = _playerDb.AddTokens(accountId, -price);

        var giftId = Math.Abs(Guid.NewGuid().GetHashCode()) % 1_000_000_000;

        var consumableItemDesc = selectedGiftDrop.GetValueOrDefault("ConsumableItemDesc")?.ToString();
        if (!string.IsNullOrEmpty(consumableItemDesc))
        {
            var msg = new Dictionary<string, object?>
            {
                ["Id"] = giftId, ["ConsumableItemDesc"] = consumableItemDesc,
                ["CreatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ["Count"] = 1, ["InitialCount"] = 1, ["IsActive"] = false, ["ActiveDurationMinutes"] = null, ["IsTransferable"] = false,
            };
            await _hub.PushToAccountAsync(accountId, "ConsumableMappingAdded", msg);
        }

        var avatarItemDesc = selectedGiftDrop.GetValueOrDefault("AvatarItemDesc")?.ToString();
        if (!string.IsNullOrEmpty(avatarItemDesc))
        {
            var itemType = selectedGiftDrop.GetValueOrDefault("AvatarItemType") != null ? int.Parse(selectedGiftDrop["AvatarItemType"]!.ToString()!) : 0;
            var friendlyName = selectedEntry.GetValueOrDefault("FriendlyName")?.ToString() ?? selectedGiftDrop.GetValueOrDefault("FriendlyName")?.ToString() ?? "";
            var tooltip = selectedEntry.GetValueOrDefault("ToolTip")?.ToString() ?? selectedGiftDrop.GetValueOrDefault("ToolTip")?.ToString() ?? "";
            var rarity = selectedGiftDrop.GetValueOrDefault("Rarity") != null ? int.Parse(selectedGiftDrop["Rarity"]!.ToString()!) : 0;
            _playerDb.GrantAvatarItem(toPlayerId, avatarItemDesc, itemType, friendlyName, tooltip, rarity);
        }

        var response = new Dictionary<string, object?>
        {
            ["BalanceUpdates"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["UpdateResponse"] = 0,
                    ["Data"] = new List<Dictionary<string, object?>>
                    {
                        new()
                        {
                            ["Id"] = giftId, ["FromPlayerId"] = accountId, ["ConsumableItemDesc"] = consumableItemDesc,
                            ["AvatarItemDesc"] = avatarItemDesc, ["AvatarItemType"] = selectedGiftDrop.GetValueOrDefault("AvatarItemType") ?? 0,
                            ["CurrencyType"] = selectedGiftDrop.GetValueOrDefault("CurrencyType") ?? 0, ["Currency"] = selectedGiftDrop.GetValueOrDefault("Currency") ?? 0,
                            ["Xp"] = 0, ["PackageType"] = 0, ["Message"] = giftMessage,
                            ["EquipmentPrefabName"] = selectedGiftDrop.GetValueOrDefault("EquipmentPrefabName"),
                            ["EquipmentModificationGuid"] = selectedGiftDrop.GetValueOrDefault("EquipmentModificationGuid"),
                            ["GiftContext"] = giftContext, ["GiftRarity"] = selectedGiftDrop.GetValueOrDefault("Rarity") ?? 0,
                            ["Platform"] = -1, ["PlatformsToSpawnOn"] = -1, ["BalanceType"] = null,
                        },
                    },
                },
            },
            ["Balance"] = newBalance,
            ["CurrencyType"] = currencyType,
        };
        return new OkObjectResult(response);
    }
}
