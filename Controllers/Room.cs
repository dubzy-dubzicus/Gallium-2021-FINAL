using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;
using Mocha2021.Hub;
using Mocha2021.Models;

namespace Mocha2021.Controllers;

[Controller]
public class Room : ControllerBase
{
    private readonly RoomDB _roomDb;
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly RoomInstanceManager _instances;
    private readonly HubState _hub;

    public Room(RoomDB roomDb, PlayerDB playerDb, SessionManager sessions, RoomInstanceManager instances, HubState hub)
    {
        _roomDb = roomDb;
        _playerDb = playerDb;
        _sessions = sessions;
        _instances = instances;
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

    [HttpGet("/room/{roomId:int}/instances")]
    public IActionResult RoomInstances(int roomId) => new OkObjectResult(_instances.ListInstancesForRoom(roomId));

    [HttpGet("/roomserver/rooms")]
    public IActionResult RoomsByName([FromQuery] string? name)
    {
        var room = !string.IsNullOrEmpty(name) ? _roomDb.GetRoomByName(name) : null;
        if (room == null) return new NotFoundObjectResult(new Dictionary<string, object?>());
        return new OkObjectResult(RoomDB.BuildRoom(room));
    }

    [HttpGet("/roomserver/rooms/base")]
    [HttpGet("/Room_server/rooms/base")]
    public IActionResult RoomsBase()
    {
        var path = Path.Combine(HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "Seed", "BaseRooms.json");
        return Content(System.IO.File.ReadAllText(path), "application/json");
    }

    [HttpGet("/roomserver/rooms/{roomId:int}")]
    public IActionResult RoomserverRoomById(int roomId)
    {
        var cached = _instances.GetActiveRoom(roomId);
        if (cached != null) return new OkObjectResult(cached);

        Dictionary<string, object?> roomData;
        if (RoomDB.IsDormRoomId(roomId))
        {
            roomData = RoomDB.BuildDormRoom(roomId - RoomDB.DormRoomIdOffset);
        }
        else
        {
            var room = _roomDb.GetRoom(roomId);
            if (room == null) return new NotFoundObjectResult(new Dictionary<string, object?>());
            roomData = RoomDB.BuildRoom(room);
        }
        _instances.SetActiveRoom(roomId, roomData);
        return new OkObjectResult(roomData);
    }

    [HttpPut("/roomserver/rooms/{roomId:int}/description")]
    public IActionResult UpdateDescription(int roomId)
    {
        var accountId = CurrentAccountId();
        if (accountId == 0) return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "unauthorized" }) { StatusCode = 401 };

        var room = _roomDb.GetRoom(roomId);
        if (room == null) return new NotFoundObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "room_not_found" });

        var body = ReadJsonBody() ?? new();
        var newDescription = body.GetValueOrDefault("description")?.ToString() ?? body.GetValueOrDefault("Description")?.ToString() ?? "";

        room.Description = newDescription;
        _roomDb.SaveRoom(room);
        _instances.InvalidateActiveRoom(roomId);

        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true, ["error"] = "" });
    }

    [HttpPost("/roomserver/rooms/{roomId:int}/clone")]
    public IActionResult CloneRoom(int roomId)
    {
        var accountId = CurrentAccountId();
        if (accountId == 0) return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "unauthorized", ["value"] = null }) { StatusCode = 401 };

        var source = _roomDb.GetRoom(roomId);
        if (source == null) return new NotFoundObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "room_not_found", ["value"] = null });

        var body = ReadJsonBody() ?? new();
        var sourceSubroom = source.SubRooms.FirstOrDefault();
        var sourceSceneId = sourceSubroom?.UnitySceneId ?? source.UnitySceneId ?? Guid.NewGuid().ToString();

        var newRoomName = body.GetValueOrDefault("name")?.ToString() ?? body.GetValueOrDefault("Name")?.ToString() ?? $"{source.Name} (Clone)";
        var existing = _roomDb.GetRoomByName(newRoomName);
        if (existing != null)
            return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "Room name is taken", ["value"] = null }) { StatusCode = 409 };

        var newDescription = body.GetValueOrDefault("Description")?.ToString() ?? source.Description ?? "A cloned masterpiece.";
        var newImageName = body.GetValueOrDefault("ImageName")?.ToString() ?? source.ImageName ?? "";
        var newMaxPlayers = TryInt(body.GetValueOrDefault("MaxPlayers")) ?? source.MaxPlayers;

        var newRoom = new Models.Room
        {
            Name = newRoomName,
            Description = newDescription,
            ImageName = newImageName,
            MaxPlayers = newMaxPlayers,
            OwnerId = accountId,
            IsDorm = false,
            Accessibility = 0,
            CloningAllowed = false,
            IsRRO = false,
            CreatedAt = DateTime.UtcNow,
            Platforms = source.Platforms,
            SupportsLevelVoting = source.SupportsLevelVoting,
            SubRooms = new List<SubRoomEntry>
            {
                new()
                {
                    Name = "Home",
                    DataBlob = "",
                    IsSandbox = false,
                    MaxPlayers = newMaxPlayers,
                    Accessibility = 1,
                    UnitySceneId = sourceSceneId,
                    SavedByAccountId = -1,
                },
            },
            Roles = new List<RoomRoleEntry> { new() { AccountId = accountId, Role = 255 } },
        };
        newRoom = _roomDb.CreateRoom(newRoom);
        _instances.InvalidateActiveRoom(newRoom.RoomId);

        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true, ["error"] = "", ["value"] = RoomDB.BuildRoom(newRoom) });
    }

    private static int? TryInt(object? v) => v != null && int.TryParse(v.ToString(), out var i) ? i : null;

    [HttpGet("/roomserver/rooms/createdby/me")]
    public IActionResult CreatedByMe() => new OkObjectResult(new List<object>());

    [HttpGet("/roomserver/rooms/bulk")]
    [HttpGet("/Room_server/rooms/bulk")]
    public IActionResult RoomsBulk([FromQuery(Name = "id")] List<string>? id, [FromQuery(Name = "name")] List<string>? name)
    {
        var ids = id ?? new List<string>();
        var names = name ?? new List<string>();
        var dormIds = ids.Select(int.Parse).Where(RoomDB.IsDormRoomId).ToList();
        var regularIds = ids.Select(int.Parse).Where(i => !RoomDB.IsDormRoomId(i)).ToList();

        var rooms = new List<Models.Room>();
        if (regularIds.Count > 0) rooms.AddRange(_roomDb.GetRoomsByIds(regularIds));
        if (names.Count > 0) rooms.AddRange(_roomDb.GetRoomsByNames(names));

        var results = rooms.Select(RoomDB.BuildRoom).ToList();
        results.AddRange(dormIds.Select(rid => RoomDB.BuildDormRoom(rid - RoomDB.DormRoomIdOffset)));
        return new OkObjectResult(results);
    }

    [HttpGet("/roomserver/rooms/topcreators")]
    [HttpGet("/Room_server/rooms/topcreators")]
    public IActionResult TopCreators() => new OkObjectResult(new List<Dictionary<string, object?>> { RecCenterStub() });

    private static Dictionary<string, object?> RecCenterStub() => new()
    {
        ["RoomId"] = 7716, ["IsDorm"] = false, ["MaxPlayerCalculationMode"] = 1, ["MaxPlayers"] = 10,
        ["CloningAllowed"] = false, ["DisableMicAutoMute"] = false, ["DisableRoomComments"] = false,
        ["EncryptVoiceChat"] = false, ["ToxmodEnabled"] = false, ["LoadScreenLocked"] = false,
        ["PersistenceVersion"] = 1, ["AutoLocalizeRoom"] = false, ["IsDeveloperOwned"] = false,
        ["RankedEntityId"] = "", ["Name"] = "RecCenter", ["Description"] = "A social hub to meet and mingle with friends new and old.",
        ["ImageName"] = "RecCenter", ["WarningMask"] = 0, ["CustomWarning"] = "", ["CreatorAccountId"] = 1,
        ["State"] = 0, ["Accessibility"] = 1, ["SupportsLevelVoting"] = false, ["IsRRO"] = true,
        ["SupportsScreens"] = true, ["SupportsWalkVR"] = true, ["SupportsTeleportVR"] = true, ["SupportsVRLow"] = true,
        ["SupportsQuest2"] = true, ["SupportsMobile"] = true, ["SupportsJuniors"] = true, ["MinLevel"] = 0,
        ["CreatedAt"] = "2024-02-25T10:21:44.7442905",
        ["Stats"] = new Dictionary<string, object?> { ["CheerCount"] = 999, ["FavoriteCount"] = 999, ["VisitorCount"] = 0, ["VisitCount"] = 9999 },
        ["RankingContext"] = 0,
        ["SubRooms"] = new List<Dictionary<string, object?>>
        {
            new() { ["SubRoomId"] = 1, ["RoomId"] = 7716, ["Name"] = "Home", ["DataBlob"] = "", ["IsSandbox"] = false,
                ["MaxPlayers"] = 12, ["Accessibility"] = 1, ["UnitySceneId"] = "cbad71af-0831-44d8-b8ef-69edafa841f6", ["SavedByAccountId"] = -1 },
        },
        ["Roles"] = new List<Dictionary<string, object?>> { new() { ["AccountId"] = 1, ["Role"] = 255, ["InvitedRole"] = 0 } },
        ["DataBlob"] = null, ["UgcVersion"] = 1,
        ["Tags"] = new List<Dictionary<string, object?>> { new() { ["Tag"] = "hangout", ["Type"] = 0 } },
        ["PromoImages"] = new List<object>(), ["PromoExternalContent"] = new List<object>(), ["LoadScreens"] = new List<object>(),
    };

    [HttpGet("/roomserver/rooms/hot")]
    [HttpGet("/Room_server/rooms/hot")]
    public IActionResult RoomsHot([FromQuery] string? tag)
    {
        List<Models.Room> rooms;
        if (!string.IsNullOrEmpty(tag)) rooms = _roomDb.GetRoomsByTag(tag).Where(r => !r.IsDorm).OrderByDescending(r => r.Visits).ToList();
        else rooms = _roomDb.ListNonDorm(27);
        var results = rooms.Select(RoomDB.BuildRoom).ToList();
        return new OkObjectResult(new Dictionary<string, object?> { ["TotalResults"] = results.Count, ["Results"] = results });
    }

    [HttpGet("/roomserver/roomsandplaylists/search")]
    [HttpGet("/Room_server/roomsandplaylists/search")]
    public IActionResult RoomsAndPlaylistsSearch([FromQuery] string? query)
    {
        query ??= "";
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tags = terms.Where(t => t.StartsWith('#') && t.Length > 1).Select(t => t[1..]).ToList();
        var keywords = terms.Where(t => !t.StartsWith('#')).ToList();

        List<Models.Room> rooms;
        if (tags.Count > 0)
        {
            rooms = _roomDb.GetRoomsByTag(tags[0]).Where(r => !r.IsDorm && tags.All(t => r.Tags.Contains(t))).OrderByDescending(r => r.Visits).ToList();
        }
        else if (keywords.Count > 0)
        {
            rooms = _roomDb.SearchByName(string.Join(' ', keywords));
        }
        else
        {
            rooms = _roomDb.ListNonDorm(27);
        }
        var results = rooms.Select(RoomDB.BuildRoom).ToList();
        return new OkObjectResult(new Dictionary<string, object?> { ["TotalResults"] = results.Count, ["Results"] = results, ["Playlists"] = new List<object>() });
    }

    [HttpGet("/roomserver/rooms/search")]
    public IActionResult RoomsSearchV2([FromQuery] string? query)
    {
        var rooms = string.IsNullOrWhiteSpace(query) ? _roomDb.ListNonDorm(27) : _roomDb.SearchByName(query.Trim());
        var results = rooms.Select(RoomDB.BuildRoom).ToList();
        return new OkObjectResult(new Dictionary<string, object?> { ["TotalResults"] = results.Count, ["Results"] = results });
    }

    [HttpGet("/Room_server/rooms/search")]
    public IActionResult RoomsSearch([FromQuery] string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["results"] = new List<object>(), ["totalCount"] = 0 });
        var rooms = _roomDb.SearchByName(query.Trim());
        var results = rooms.Select(RoomDB.BuildRoom).ToList();
        return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["results"] = results, ["totalCount"] = results.Count });
    }

    [HttpGet("/Room_server/rooms/autocomplete_search")]
    public IActionResult RoomsAutocomplete() => new OkObjectResult(new Dictionary<string, object?>
    {
        ["status"] = "ok", ["suggestions"] = new List<Dictionary<string, object?>> { new() { ["name"] = "Mock Room", ["roomId"] = ServerConfig.MockRoomId } },
    });

    [HttpGet("/Room_server/rooms/ownedby/me")]
    public IActionResult LegacyRoomsOwnedByMe() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["rooms"] = new List<object> { RoomStub(ServerConfig.MockRoomId) } });

    [HttpGet("/Room_server/rooms/visitedby/me")]
    public IActionResult LegacyRoomsVisitedByMe() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["rooms"] = new List<object> { RoomStub(ServerConfig.MockRoomId) } });

    [HttpGet("/Room_server/rooms")]
    public IActionResult LegacyRoomsList() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["rooms"] = Enumerable.Range(0, 3).Select(i => RoomStub(ServerConfig.MockRoomId + i)).ToList() });

    [HttpGet("/Room_server/rooms/{rid}")]
    public IActionResult LegacyRoomById(string rid) => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["room"] = RoomStub(int.Parse(rid)) });

    [HttpGet("/Room_server/rooms/{rid}/experience")]
    public IActionResult RoomExperience(string rid) => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["experience"] = new Dictionary<string, object?> { ["roomId"] = int.Parse(rid), ["visitCount"] = 420, ["cheers"] = 69 } });

    [HttpGet("/Room_server/rooms/{rid}/experience/player")]
    public IActionResult RoomExperiencePlayer(string rid) => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["playerExperience"] = new Dictionary<string, object?> { ["roomId"] = int.Parse(rid), ["accountId"] = ServerConfig.MockPlayerId, ["visits"] = 5 } });

    [HttpGet("/Room_server/rooms/{rid}/interactionby/me")]
    public IActionResult LegacyRoomInteraction(string rid) => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["interaction"] = new Dictionary<string, object?> { ["liked"] = false, ["cheered"] = false, ["reported"] = false, ["favorited"] = false } });

    [HttpGet("/Room_server/rooms/{rid}/subrooms/{sub}/saves/{save}")]
    public IActionResult SubroomSave(string rid, string sub, string save) => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["saveData"] = null, ["subroomId"] = sub, ["saveSlot"] = save });

    [HttpPost("/roomserver/rooms/{roomId:int}/subrooms/{subroomId:int}/data")]
    public IActionResult SubroomDataSave(int roomId, int subroomId)
    {
        var accountId = CurrentAccountId();
        var payload = ReadJsonBody() ?? new();

        var filename = payload.GetValueOrDefault("filename")?.ToString();
        var inventionUsage = payload.GetValueOrDefault("inventionUsage")?.ToString();
        var savedByRaw = payload.GetValueOrDefault("savedByAccountId");
        var savedBy = TryInt(savedByRaw) ?? accountId;

        var room = _roomDb.GetRoom(roomId);
        var subroom = room?.SubRooms.FirstOrDefault(s => s.SubRoomId == subroomId);

        bool success;
        string error;
        object? value = null;

        if (room == null) { success = false; error = "room not found"; }
        else if (subroom == null) { success = false; error = "subroom not found"; }
        else
        {
            subroom.DataBlob = filename;
            subroom.InventionUsage = inventionUsage;
            subroom.SavedByAccountId = savedBy;
            _roomDb.SaveRoom(room);
            _instances.InvalidateActiveRoom(roomId);
            success = true;
            error = "";
            value = RoomDB.BuildRoom(room);
        }

        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = success, ["error"] = error, ["value"] = value });
    }

    [HttpGet("/Room_server/dormroom/me")]
    public IActionResult DormRoomMe()
    {
        var accountId = CurrentAccountId();
        var r = RoomStub(1_000_000 + accountId);
        r["isDorm"] = true;
        r["isPrivate"] = true;
        return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["dormRoom"] = r });
    }

    [HttpGet("/Room_server/featuredrooms/current")]
    public IActionResult FeaturedRooms() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["rooms"] = Enumerable.Range(0, 3).Select(i => RoomStub(ServerConfig.MockRoomId + i)).ToList() });

    [HttpGet("/Room_server/photon_access_token")]
    public IActionResult PhotonAccessToken() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["token"] = Guid.NewGuid().ToString(), ["appId"] = "cfaed505-6eb8-49b5-8c2e-2556410cfd22", ["region"] = "us" });

    [HttpGet("/Room_server/publishState/configs")]
    public IActionResult PublishStateConfigs() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["configs"] = new List<Dictionary<string, object?>> { new() { ["key"] = "publishEnabled", ["value"] = true } } });

    [HttpGet("/roomserver/rooms/visitedby/me")]
    public IActionResult RoomsVisitedByMeV2() => new OkObjectResult(new List<object>());

    [HttpGet("/roomserver/rooms/favoritedby/me")]
    public IActionResult RoomsFavoritedByMe() => new OkObjectResult(new List<object>());

    [HttpGet("/roomserver/featuredrooms/current")]
    public IActionResult FeaturedRoomsV2()
    {
        var room = _roomDb.GetRoomByName("RecCenter");
        if (room == null) return new OkObjectResult(new Dictionary<string, object?>());
        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["FeaturedRoomGroupId"] = 1,
            ["Name"] = "Featured Rooms",
            ["Rooms"] = new List<Dictionary<string, object?>> { new() { ["RoomName"] = room.Name, ["RoomId"] = room.RoomId, ["ImageName"] = room.ImageName ?? "" } },
        });
    }

    [HttpGet("/roomserver/rooms/curated_playlists")]
    public IActionResult CuratedPlaylists() => new OkObjectResult(new List<object>());

    [HttpGet("/roomserver/playlists/{playlistId:int}")]
    public IActionResult PlaylistById(int playlistId) => new OkObjectResult(new Dictionary<string, object?>
    {
        ["PlaylistId"] = playlistId, ["Name"] = "Featured Playlist", ["Description"] = "", ["ImageName"] = "",
        ["CreatorAccountId"] = 1, ["IsFeatured"] = true, ["HasGoldenTrophy"] = true, ["Rooms"] = new List<object>(),
        ["RoomCount"] = 0, ["Stats"] = new Dictionary<string, object?> { ["FavoriteCount"] = 0, ["VisitCount"] = 0 },
        ["CreatedAt"] = "2024-01-01T00:00:00",
    });

    [HttpGet("/roomserver/rooms/ownedby/me")]
    public IActionResult RoomsOwnedByMe() => new OkObjectResult(new List<object>());

    [HttpGet("/roomserver/rooms/{roomId:int}/interactionby/me")]
    public IActionResult RoomInteractionByMe(int roomId)
    {
        var accountId = CurrentAccountId();
        var row = _roomDb.GetInteraction(accountId, roomId);
        if (row != null)
        {
            var str = row.LastVisitedAt?.ToString("yyyy-MM-ddTHH:mm:ss") ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            return new OkObjectResult(new Dictionary<string, object?> { ["Cheered"] = row.IsCheered, ["Favorited"] = row.IsFavorited, ["LastVisitedAt"] = str });
        }
        return new OkObjectResult(new Dictionary<string, object?> { ["Cheered"] = false, ["Favorited"] = false, ["LastVisitedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") });
    }

    [HttpPost("/roomserver/rooms/{roomId:int}/interactionby/me/favorite")]
    [HttpPut("/roomserver/rooms/{roomId:int}/interactionby/me/favorite")]
    [HttpDelete("/roomserver/rooms/{roomId:int}/interactionby/me/favorite")]
    public IActionResult FavoriteToggle(int roomId)
    {
        var accountId = CurrentAccountId();
        var row = _roomDb.GetInteraction(accountId, roomId);
        var isCheered = row?.IsCheered ?? false;
        var wasFavorited = row?.IsFavorited ?? false;
        var nowFavorited = Request.Method == "DELETE" ? false : (row != null ? !wasFavorited : true);
        var now = DateTime.UtcNow;

        _roomDb.SaveInteraction(new RoomInteraction { AccountId = accountId, RoomId = roomId, IsCheered = isCheered, IsFavorited = nowFavorited, LastVisitedAt = now });

        var delta = nowFavorited != wasFavorited ? (nowFavorited ? 1 : -1) : 0;
        if (delta != 0)
        {
            var room = _roomDb.GetRoom(roomId);
            if (room != null)
            {
                room.FavoritesCount = Math.Max(room.FavoritesCount + delta, 0);
                _roomDb.SaveRoom(room);
                _instances.InvalidateActiveRoom(roomId);
            }
        }
        return new OkObjectResult(new Dictionary<string, object?> { ["Cheered"] = isCheered, ["Favorited"] = nowFavorited, ["LastVisitedAt"] = now.ToString("yyyy-MM-ddTHH:mm:ss") });
    }

    [HttpPost("/roomserver/rooms/{roomId:int}/interactionby/me/cheer")]
    [HttpPut("/roomserver/rooms/{roomId:int}/interactionby/me/cheer")]
    [HttpDelete("/roomserver/rooms/{roomId:int}/interactionby/me/cheer")]
    public IActionResult CheerToggle(int roomId)
    {
        var accountId = CurrentAccountId();
        var row = _roomDb.GetInteraction(accountId, roomId);
        var wasCheered = row?.IsCheered ?? false;
        var isFavorited = row?.IsFavorited ?? false;
        var nowCheered = Request.Method == "DELETE" ? false : (row != null ? !wasCheered : true);
        var now = DateTime.UtcNow;

        _roomDb.SaveInteraction(new RoomInteraction { AccountId = accountId, RoomId = roomId, IsCheered = nowCheered, IsFavorited = isFavorited, LastVisitedAt = now });

        var delta = nowCheered != wasCheered ? (nowCheered ? 1 : -1) : 0;
        if (delta != 0)
        {
            var room = _roomDb.GetRoom(roomId);
            if (room != null)
            {
                room.Cheers = Math.Max(room.Cheers + delta, 0);
                _roomDb.SaveRoom(room);
                _instances.InvalidateActiveRoom(roomId);
            }
        }
        return new OkObjectResult(new Dictionary<string, object?> { ["Cheered"] = nowCheered, ["Favorited"] = isFavorited, ["LastVisitedAt"] = now.ToString("yyyy-MM-ddTHH:mm:ss") });
    }

    [HttpPost("/roominstance/{instanceId:int}/reportjoinresult")]
    public IActionResult ReportJoinResultV2(int instanceId) => new OkObjectResult(new Dictionary<string, object?>());

    [HttpPost("/goto/none")]
    public IActionResult GotoNone() => GotoRoom("DormRoom");

    [HttpPost("/goto/room/{roomName}")]
    public IActionResult GotoRoom(string roomName)
    {
        roomName = roomName.Replace('+', ' ');
        var joinModeRaw = Request.HasFormContentType ? Request.Form["JoinMode"].ToString() : null;
        int.TryParse(joinModeRaw, out var joinMode);

        var accountId = CurrentAccountId();
        _instances.ReleasePriorInstance(accountId);

        RoomInstance instance;
        if (roomName == "DormRoom")
        {
            var targetRoomId = 1_000_000 + accountId;
            instance = new RoomInstance
            {
                RoomInstanceId = Random.Shared.Next(10000, 99999999),
                RoomId = targetRoomId,
                SubRoomId = targetRoomId,
                Location = RoomInstanceManager.DefaultRoomLocation,
                DataBlob = "",
                PhotonRoomId = Guid.NewGuid().ToString(),
                Name = $"^{roomName}",
                MaxCapacity = 10,
                IsPrivate = true,
            };
        }
        else
        {
            var meta = _instances.GetRoomMeta(roomName);
            int targetRoomId, subroomId;
            string location, dataBlob;
            if (meta != null) { targetRoomId = meta.RoomId; subroomId = meta.SubRoomId; location = meta.Location; dataBlob = meta.DataBlob; }
            else { targetRoomId = 1; subroomId = 1; location = RoomInstanceManager.DefaultRoomLocation; dataBlob = ""; }

            var isPrivate = joinMode == 2;
            instance = _instances.GetOrCreateRoomInstance(targetRoomId, subroomId, roomName, location, dataBlob, isPrivate, accountId);
        }

        _instances.SetAccountRoomInstance(accountId, instance);
        return new JsonResult(new Dictionary<string, object?> { ["errorCode"] = 0, ["roomInstance"] = instance.ToJson() });
    }

    [HttpPost("/goto/room/{roomName}/{subroomName}")]
    public IActionResult GotoSubroom(string roomName, string subroomName)
    {
        roomName = roomName.Replace('+', ' ');
        subroomName = subroomName.Replace('+', ' ');
        var joinModeRaw = Request.HasFormContentType ? Request.Form["JoinMode"].ToString() : null;
        int.TryParse(joinModeRaw, out var joinMode);

        var accountId = CurrentAccountId();
        _instances.ReleasePriorInstance(accountId);

        var meta = _instances.GetRoomMeta(roomName, subroomName);
        int targetRoomId, subroomId;
        string location, dataBlob;
        if (meta != null) { targetRoomId = meta.RoomId; subroomId = meta.SubRoomId; location = meta.Location; dataBlob = meta.DataBlob; }
        else { targetRoomId = 1; subroomId = 1; location = RoomInstanceManager.DefaultRoomLocation; dataBlob = ""; }

        var isPrivate = joinMode == 2;
        var instance = _instances.GetOrCreateRoomInstance(targetRoomId, subroomId, roomName, location, dataBlob, isPrivate, accountId);
        _instances.SetAccountRoomInstance(accountId, instance);
        return new JsonResult(new Dictionary<string, object?> { ["errorCode"] = 0, ["roomInstance"] = instance.ToJson() });
    }

    [HttpGet("/api/rooms/v1/filters")]
    public IActionResult RoomFilters() => new OkObjectResult(new Dictionary<string, object?>
    {
        ["PinnedFilters"] = new[] { "rro", "community", "featured", "quest", "pvp", "hangout", "game", "art", "horror" },
        ["PopularFilters"] = new[] { "pvp", "quest", "game", "hangout", "art" },
        ["TrendingFilters"] = new[] { "featured", "game", "horror", "quest" },
    });

    [HttpPost("/api/rooms/v1/verifyRole")]
    public IActionResult VerifyRole()
    {
        var body = ReadJsonBody() ?? new();
        var roomIdRaw = body.GetValueOrDefault("roomId") ?? body.GetValueOrDefault("RoomId") ?? Request.Query["roomId"].ToString();
        var accountId = CurrentAccountId();
        if (roomIdRaw == null || !int.TryParse(roomIdRaw.ToString(), out var roomId))
            return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["hasRole"] = false, ["role"] = 0 });

        var room = _roomDb.GetRoom(roomId);
        if (room != null && room.OwnerId == accountId)
            return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["hasRole"] = true, ["role"] = 255 });

        var roleEntry = room?.Roles.FirstOrDefault(r => r.AccountId == accountId);
        if (roleEntry != null)
            return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["hasRole"] = true, ["role"] = roleEntry.Role });

        return new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["hasRole"] = false, ["role"] = 0 });
    }

    [HttpPost("/api/rooms/v3/report")]
    public IActionResult ReportRoom() => new OkObjectResult(new Dictionary<string, object?> { ["status"] = "ok", ["reportId"] = Guid.NewGuid().ToString() });

    private static Dictionary<string, object?> RoomStub(int rid) => new()
    {
        ["roomId"] = rid,
        ["name"] = "Mock Room",
        ["description"] = "A mock Rec Room.",
        ["playerCount"] = Random.Shared.Next(1, 21),
        ["maxPlayers"] = 20,
        ["isPrivate"] = false,
        ["isDorm"] = false,
        ["isRRO"] = false,
        ["tags"] = new List<string> { "Action" },
        ["createdAt"] = "2023-01-01T00:00:00Z",
        ["stats"] = new Dictionary<string, object?> { ["visits"] = 9999, ["cheers"] = 420, ["favorites"] = 69 },
    };
}
