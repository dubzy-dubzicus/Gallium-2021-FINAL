using LiteDB;
using Gallium2021.Models;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonElement = System.Text.Json.JsonElement;
using JsonValueKind = System.Text.Json.JsonValueKind;

namespace Gallium2021.Classes;

public class RoomDB
{
    public static readonly string DefaultRoomLocation = "76d98498-60a1-430c-ab76-b54a29b7a163";
    public const int DormRoomIdOffset = 1_000_000;

    public static readonly Dictionary<string, string> RoomPlatformKeys = new()
    {
        ["screens"] = "SupportsScreens",
        ["walkvr"] = "SupportsWalkVR",
        ["teleportvr"] = "SupportsTeleportVR",
        ["vrlow"] = "SupportsVRLow",
        ["quest2"] = "SupportsQuest2",
        ["mobile"] = "SupportsMobile",
        ["juniors"] = "SupportsJuniors",
    };

    private readonly ILiteCollection<Room> _rooms;
    private readonly ILiteCollection<RoomInteraction> _interactions;
    private int _nextRoomId;

    public RoomDB(LiteDatabase db, IWebHostEnvironment env)
    {
        _rooms = db.GetCollection<Room>("rooms");
        _interactions = db.GetCollection<RoomInteraction>("room_interactions");
        SeedBaseRoomsIfEmpty(env);
        _nextRoomId = (_rooms.FindAll().Select(r => (int?)r.RoomId).Max() ?? 0) + 1;
        if (_nextRoomId < 2) _nextRoomId = 2;
    }

    private void SeedBaseRoomsIfEmpty(IWebHostEnvironment env)
    {
        if (_rooms.Count() > 0) return;

        var path = Path.Combine(env.ContentRootPath, "Seed", "BaseRooms.json");
        if (!File.Exists(path)) return;
        var rooms = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(File.ReadAllText(path))!;

        static int AsInt(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!);
        static string? AsStrOrNull(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

        foreach (var r in rooms)
        {
            var roomId = AsInt(r["RoomId"]);
            var subRoomEntries = new List<SubRoomEntry>();
            string? unitySceneId = null;
            foreach (var sr in r["SubRooms"].EnumerateArray())
            {
                var sceneId = sr.GetProperty("UnitySceneId").GetString();
                unitySceneId ??= sceneId;
                subRoomEntries.Add(new SubRoomEntry
                {
                    SubRoomId = AsInt(sr.GetProperty("SubRoomId")),
                    RoomId = roomId,
                    Name = sr.GetProperty("Name").GetString() ?? "Home",
                    DataBlob = AsStrOrNull(sr.GetProperty("DataBlob")),
                    IsSandbox = sr.GetProperty("IsSandbox").GetBoolean(),
                    MaxPlayers = AsInt(sr.GetProperty("MaxPlayers")),
                    Accessibility = AsInt(sr.GetProperty("Accessibility")),
                    UnitySceneId = sceneId,
                    SavedByAccountId = AsInt(sr.GetProperty("SavedByAccountId")),
                });
            }

            var roleEntries = r["Roles"].EnumerateArray()
                .Select(role => new RoomRoleEntry { AccountId = AsInt(role.GetProperty("AccountId")), Role = AsInt(role.GetProperty("Role")) })
                .ToList();
            var tags = r["Tags"].EnumerateArray().Select(t => t.GetProperty("Tag").GetString()!).ToList();
            var stats = r["Stats"];

            var room = new Room
            {
                RoomId = roomId,
                Name = r["Name"].GetString() ?? "",
                UnitySceneId = unitySceneId,
                DataBlob = AsStrOrNull(r["DataBlob"]),
                OwnerId = AsInt(r["CreatorAccountId"]),
                MaxPlayers = AsInt(r["MaxPlayers"]),
                Accessibility = AsInt(r["Accessibility"]),
                Description = r["Description"].GetString(),
                ImageName = r["ImageName"].GetString(),
                CloningAllowed = r["CloningAllowed"].GetBoolean(),
                IsDorm = r["IsDorm"].GetBoolean(),
                SupportsLevelVoting = r["SupportsLevelVoting"].GetBoolean(),
                IsRRO = r["IsRRO"].GetBoolean(),
                Cheers = AsInt(stats.GetProperty("CheerCount")),
                FavoritesCount = AsInt(stats.GetProperty("FavoriteCount")),
                Visits = AsInt(stats.GetProperty("VisitCount")),
                CreatedAt = DateTime.UtcNow,
                Tags = tags,
                SubRooms = subRoomEntries,
                Roles = roleEntries,
            };
            _rooms.Insert(room);
        }

        if (_rooms.FindById(7716) == null)
        {
            _rooms.Insert(new Room
            {
                RoomId = 7716,
                Name = "RecCenter",
                UnitySceneId = "cbad71af-0831-44d8-b8ef-69edafa841f6",
                OwnerId = 1,
                MaxPlayers = 10,
                Accessibility = 1,
                Description = "A social hub to meet and mingle with friends new and old.",
                ImageName = "RecCenter",
                IsRRO = true,
                Cheers = 999,
                FavoritesCount = 999,
                Visits = 9999,
                CreatedAt = DateTime.UtcNow,
                Tags = new List<string> { "hangout" },
                SubRooms = new List<SubRoomEntry>
                {
                    new()
                    {
                        SubRoomId = 1, RoomId = 7716, Name = "Home", DataBlob = "", IsSandbox = false,
                        MaxPlayers = 12, Accessibility = 1, UnitySceneId = "cbad71af-0831-44d8-b8ef-69edafa841f6", SavedByAccountId = -1,
                    },
                },
                Roles = new List<RoomRoleEntry> { new() { AccountId = 1, Role = 255 } },
            });
        }
    }

    public static bool IsDormRoomId(int roomId) => roomId >= DormRoomIdOffset;

    public static Dictionary<string, bool> RoomPlatformFlags(string? platformsRaw)
    {
        if (string.IsNullOrWhiteSpace(platformsRaw))
            return RoomPlatformKeys.Values.ToDictionary(v => v, _ => true);
        var enabled = platformsRaw.Split(',').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToHashSet();
        return RoomPlatformKeys.ToDictionary(kv => kv.Value, kv => enabled.Contains(kv.Key));
    }

    public List<Room> GetAllRoomsForAdmin() => _rooms.FindAll().OrderBy(r => r.RoomId).ToList();

    public Room? GetRoom(int roomId) => _rooms.FindById(roomId);

    public Room? GetRoomByName(string name) => _rooms.FindOne(r => r.Name == name);

    public List<Room> GetRoomsByIds(IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        return _rooms.Find(r => set.Contains(r.RoomId)).ToList();
    }

    public List<Room> GetRoomsByNames(IEnumerable<string> names)
    {
        var set = names.ToHashSet();
        return _rooms.Find(r => set.Contains(r.Name)).ToList();
    }

    public List<Room> GetRoomsByTag(string tag) => _rooms.Find(r => r.Tags.Contains(tag)).ToList();

    public List<Room> SearchByName(string query, int limit = 27) =>
        _rooms.Find(r => !r.IsDorm && r.Name.ToLower().Contains(query.ToLower()))
            .OrderByDescending(r => r.Visits).Take(limit).ToList();

    public List<Room> ListNonDorm(int limit = 27) =>
        _rooms.Find(r => !r.IsDorm).OrderByDescending(r => r.Visits).Take(limit).ToList();

    public Room CreateRoom(Room room)
    {
        room.RoomId = _nextRoomId++;
        foreach (var sub in room.SubRooms)
            if (sub.SubRoomId == 0) sub.SubRoomId = room.RoomId;
        _rooms.Insert(room);
        return room;
    }

    public void SaveRoom(Room room) => _rooms.Update(room);

    public void DeleteRoom(int roomId) => _rooms.Delete(roomId);

    public RoomInteraction? GetInteraction(int accountId, int roomId) =>
        _interactions.FindById($"{accountId}:{roomId}");

    public void SaveInteraction(RoomInteraction interaction)
    {
        interaction.Id = $"{interaction.AccountId}:{interaction.RoomId}";
        _interactions.Upsert(interaction);
    }

    public static Dictionary<string, object?> BuildRoom(Room room)
    {
        var platformFlags = RoomPlatformFlags(room.Platforms);
        var subRooms = room.SubRooms.Count > 0
            ? room.SubRooms.Select(s => new Dictionary<string, object?>
            {
                ["SubRoomId"] = s.SubRoomId,
                ["RoomId"] = room.RoomId,
                ["Name"] = s.Name,
                ["DataBlob"] = s.DataBlob ?? "",
                ["IsSandbox"] = s.IsSandbox,
                ["MaxPlayers"] = s.MaxPlayers,
                ["Accessibility"] = s.Accessibility,
                ["UnitySceneId"] = s.UnitySceneId ?? Guid.NewGuid().ToString(),
                ["SavedByAccountId"] = s.SavedByAccountId,
            }).ToList()
            : new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["SubRoomId"] = room.RoomId,
                    ["RoomId"] = room.RoomId,
                    ["Name"] = "Home",
                    ["DataBlob"] = room.DataBlob ?? "",
                    ["IsSandbox"] = room.IsSandbox,
                    ["MaxPlayers"] = room.MaxPlayers,
                    ["Accessibility"] = room.Accessibility,
                    ["UnitySceneId"] = room.UnitySceneId ?? Guid.NewGuid().ToString(),
                    ["SavedByAccountId"] = room.OwnerId,
                },
            };

        var roles = room.Roles.Count > 0
            ? room.Roles.Select(r => new Dictionary<string, object?> { ["AccountId"] = r.AccountId, ["Role"] = r.Role, ["InvitedRole"] = 0 }).ToList()
            : new List<Dictionary<string, object?>> { new() { ["AccountId"] = room.OwnerId, ["Role"] = 255, ["InvitedRole"] = 0 } };

        return new Dictionary<string, object?>
        {
            ["RoomId"] = room.RoomId,
            ["IsDorm"] = room.IsDorm,
            ["MaxPlayerCalculationMode"] = 1,
            ["MaxPlayers"] = room.MaxPlayers,
            ["CloningAllowed"] = room.CloningAllowed,
            ["DisableMicAutoMute"] = false,
            ["DisableRoomComments"] = false,
            ["EncryptVoiceChat"] = false,
            ["ToxmodEnabled"] = false,
            ["LoadScreenLocked"] = false,
            ["PersistenceVersion"] = 1,
            ["AutoLocalizeRoom"] = false,
            ["IsDeveloperOwned"] = false,
            ["RankedEntityId"] = "",
            ["Name"] = room.Name,
            ["Description"] = room.Description ?? "",
            ["ImageName"] = room.ImageName ?? "",
            ["WarningMask"] = 0,
            ["CustomWarning"] = "",
            ["CreatorAccountId"] = room.OwnerId,
            ["State"] = 0,
            ["Accessibility"] = room.Accessibility,
            ["SupportsLevelVoting"] = room.SupportsLevelVoting,
            ["IsRRO"] = room.IsRRO,
            ["SupportsScreens"] = platformFlags["SupportsScreens"],
            ["SupportsWalkVR"] = platformFlags["SupportsWalkVR"],
            ["SupportsTeleportVR"] = platformFlags["SupportsTeleportVR"],
            ["SupportsVRLow"] = platformFlags["SupportsVRLow"],
            ["SupportsQuest2"] = platformFlags["SupportsQuest2"],
            ["SupportsMobile"] = platformFlags["SupportsMobile"],
            ["SupportsJuniors"] = platformFlags["SupportsJuniors"],
            ["MinLevel"] = 0,
            ["CreatedAt"] = room.CreatedAt.ToString("o"),
            ["Stats"] = new Dictionary<string, object?>
            {
                ["CheerCount"] = room.Cheers,
                ["FavoriteCount"] = room.FavoritesCount,
                ["VisitorCount"] = 0,
                ["VisitCount"] = room.Visits,
            },
            ["RankingContext"] = 0,
            ["SubRooms"] = subRooms,
            ["Roles"] = roles,
            ["DataBlob"] = room.DataBlob,
            ["UgcVersion"] = 1,
            ["Tags"] = room.Tags.Select(t => new Dictionary<string, object?> { ["Tag"] = t, ["Type"] = 0 }).ToList(),
            ["PromoImages"] = new List<object>(),
            ["PromoExternalContent"] = new List<object>(),
            ["LoadScreens"] = new List<object>(),
        };
    }

    public static Dictionary<string, object?> BuildDormRoom(int accountId)
    {
        var roomId = DormRoomIdOffset + accountId;
        return new Dictionary<string, object?>
        {
            ["RoomId"] = roomId,
            ["IsDorm"] = true,
            ["MaxPlayerCalculationMode"] = 1,
            ["MaxPlayers"] = 10,
            ["CloningAllowed"] = false,
            ["DisableMicAutoMute"] = false,
            ["DisableRoomComments"] = false,
            ["EncryptVoiceChat"] = false,
            ["ToxmodEnabled"] = false,
            ["LoadScreenLocked"] = false,
            ["PersistenceVersion"] = 1,
            ["AutoLocalizeRoom"] = false,
            ["IsDeveloperOwned"] = false,
            ["RankedEntityId"] = "",
            ["Name"] = "DormRoom",
            ["Description"] = "",
            ["ImageName"] = "",
            ["WarningMask"] = 0,
            ["CustomWarning"] = "",
            ["CreatorAccountId"] = accountId,
            ["State"] = 0,
            ["Accessibility"] = 0,
            ["SupportsLevelVoting"] = false,
            ["IsRRO"] = false,
            ["SupportsScreens"] = true,
            ["SupportsWalkVR"] = true,
            ["SupportsTeleportVR"] = true,
            ["SupportsVRLow"] = true,
            ["SupportsQuest2"] = true,
            ["SupportsMobile"] = true,
            ["SupportsJuniors"] = true,
            ["MinLevel"] = 0,
            ["CreatedAt"] = "2024-01-01T00:00:00",
            ["Stats"] = new Dictionary<string, object?> { ["CheerCount"] = 0, ["FavoriteCount"] = 0, ["VisitorCount"] = 0, ["VisitCount"] = 0 },
            ["RankingContext"] = 0,
            ["SubRooms"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["SubRoomId"] = roomId,
                    ["RoomId"] = roomId,
                    ["Name"] = "Home",
                    ["DataBlob"] = "",
                    ["IsSandbox"] = false,
                    ["MaxPlayers"] = 10,
                    ["Accessibility"] = 0,
                    ["UnitySceneId"] = DefaultRoomLocation,
                    ["SavedByAccountId"] = accountId,
                },
            },
            ["Roles"] = new List<Dictionary<string, object?>> { new() { ["AccountId"] = accountId, ["Role"] = 255, ["InvitedRole"] = 0 } },
            ["DataBlob"] = null,
            ["UgcVersion"] = 1,
            ["Tags"] = new List<object>(),
            ["PromoImages"] = new List<object>(),
            ["PromoExternalContent"] = new List<object>(),
            ["LoadScreens"] = new List<object>(),
        };
    }
}
