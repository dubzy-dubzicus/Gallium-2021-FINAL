using System.Text.Json;
using Gallium2021.Models;

namespace Gallium2021.Classes;

public class CommerceStore
{
    private readonly RoomDB _roomDb;
    private readonly Dictionary<string, object?> _giftDropStore;
    private readonly Dictionary<string, List<Dictionary<string, object?>>> _clubSearch;

    public static readonly Dictionary<string, int> RoomNameToStorefrontType = new()
    {
        ["RecCenter"] = 300,
        ["RecCenterUGC"] = 300,
    };

    public CommerceStore(RoomDB roomDb, IWebHostEnvironment env)
    {
        _roomDb = roomDb;
        var giftDropPath = Path.Combine(env.ContentRootPath, "Seed", "GiftDropStore.json");
        var rawGiftDrop = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(giftDropPath))!;
        _giftDropStore = JsonUtil.NormalizeDict(rawGiftDrop);

        var clubSearchPath = Path.Combine(env.ContentRootPath, "Seed", "ClubSearch.json");
        var rawClubs = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(clubSearchPath))!;
        _clubSearch = JsonUtil.NormalizeDict(rawClubs).ToDictionary(
            kv => kv.Key,
            kv => (kv.Value as List<object?> ?? new List<object?>()).OfType<Dictionary<string, object?>>().ToList());
    }

    public int ResolveStorefrontType(int roomId)
    {
        var room = _roomDb.GetRoom(roomId);
        if (room != null && RoomNameToStorefrontType.TryGetValue(room.Name, out var t)) return t;
        return 2;
    }

    public List<Dictionary<string, object?>> AllStoreItems()
    {
        if (_giftDropStore.TryGetValue("StoreItems", out var items) && items is List<object?> list)
            return list.OfType<Dictionary<string, object?>>().ToList();
        return new();
    }

    public object? NextUpdate() => _giftDropStore.GetValueOrDefault("NextUpdate");

    public double SubscriberDiscountPercent()
    {
        var v = _giftDropStore.GetValueOrDefault("SubscriberDiscountPercent");
        return v != null ? Convert.ToDouble(v) : 0;
    }

    public List<Dictionary<string, object?>> ItemsForStorefront(string storefrontId)
    {
        var all = AllStoreItems();
        if (_giftDropStore.TryGetValue("CuratedShops", out var shopsObj) && shopsObj is Dictionary<string, object?> shops &&
            shops.TryGetValue(storefrontId, out var curatedRaw) && curatedRaw is List<object?> curatedList)
        {
            var curatedIds = curatedList.Select(Convert.ToInt32).ToList();
            var byId = new Dictionary<int, Dictionary<string, object?>>();
            foreach (var entry in all)
            {
                if (entry.TryGetValue("PurchasableItemId", out var pid) && pid != null)
                    byId.TryAdd(Convert.ToInt32(pid), entry);
            }
            return curatedIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        }
        return all;
    }

    public Dictionary<string, object?>? FindStoreItem(int purchasableItemId) =>
        AllStoreItems().FirstOrDefault(e =>
            e.TryGetValue("PurchasableItemId", out var pid) && pid != null && Convert.ToInt32(pid) == purchasableItemId);

    public List<Dictionary<string, object?>> ClubsForCategory(string category, int count) =>
        _clubSearch.TryGetValue(category, out var clubs) ? clubs.Take(count).ToList() : new();
}
