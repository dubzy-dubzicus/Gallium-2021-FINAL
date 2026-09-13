using System.Collections.Concurrent;
using Gallium2021.Models;

namespace Gallium2021.Classes;

public class RoomMeta
{
    public int RoomId { get; set; }
    public int SubRoomId { get; set; }
    public string Location { get; set; } = "";
    public string DataBlob { get; set; } = "";
}

public class RoomInstance
{
    public long RoomInstanceId { get; set; }
    public int RoomId { get; set; }
    public int SubRoomId { get; set; }
    public string Location { get; set; } = "";
    public string DataBlob { get; set; } = "";
    public string PhotonRegionId { get; set; } = "us";
    public string PhotonRoomId { get; set; } = "";
    public string Name { get; set; } = "";
    public int MaxCapacity { get; set; } = 10;
    public bool IsFull { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsInProgress { get; set; }
    public int RoomInstanceType { get; set; } = 1;
    public bool EncryptVoiceChat { get; set; }
    public string? RoomCode { get; set; }
    public int? EventId { get; set; }
    public int? ClubId { get; set; }

    public HashSet<int> Occupants { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object?> ToJson() => new()
    {
        ["roomInstanceId"] = RoomInstanceId,
        ["roomId"] = RoomId,
        ["subRoomId"] = SubRoomId,
        ["location"] = Location,
        ["dataBlob"] = DataBlob,
        ["photonRegionId"] = PhotonRegionId,
        ["photonRoomId"] = PhotonRoomId,
        ["name"] = Name,
        ["maxCapacity"] = MaxCapacity,
        ["isFull"] = IsFull,
        ["isPrivate"] = IsPrivate,
        ["isInProgress"] = IsInProgress,
        ["roomInstanceType"] = RoomInstanceType,
        ["EncryptVoiceChat"] = EncryptVoiceChat,
        ["roomCode"] = RoomCode,
        ["eventId"] = EventId,
        ["clubId"] = ClubId,
    };
}

public class RoomInstanceManager
{
    public const string DefaultRoomLocation = "76d98498-60a1-430c-ab76-b54a29b7a163";
    private const int RoomMetaCacheTtlSeconds = 60;
    private const int RoomInstanceTtlSeconds = 20 * 60;

    private readonly RoomDB _roomDb;
    private readonly object _metaLock = new();
    private readonly Dictionary<string, (DateTime Ts, RoomMeta? Data)> _roomMetaCache = new();

    private readonly object _poolLock = new();
    private readonly Dictionary<(int RoomId, int SubRoomId), List<RoomInstance>> _roomPool = new();

    private readonly ConcurrentDictionary<int, RoomInstance> _accountRoomInstances = new();
    private readonly ConcurrentDictionary<int, Dictionary<string, object?>> _activeRoomsJson = new();
    private static readonly Random Rng = new();

    public RoomInstanceManager(RoomDB roomDb)
    {
        _roomDb = roomDb;
    }

    public void InvalidateActiveRoom(int roomId) => _activeRoomsJson.TryRemove(roomId, out _);

    public Dictionary<string, object?>? GetActiveRoom(int roomId) =>
        _activeRoomsJson.TryGetValue(roomId, out var v) ? v : null;

    public void SetActiveRoom(int roomId, Dictionary<string, object?> value) => _activeRoomsJson[roomId] = value;

    public RoomMeta? GetRoomMeta(string roomName, string? subroomName = null)
    {
        var cacheKey = subroomName != null ? $"{roomName}/{subroomName}" : roomName;
        lock (_metaLock)
        {
            if (_roomMetaCache.TryGetValue(cacheKey, out var cached) &&
                (DateTime.UtcNow - cached.Ts).TotalSeconds < RoomMetaCacheTtlSeconds)
            {
                return cached.Data;
            }
        }

        RoomMeta? data = null;
        var room = _roomDb.GetRoomByName(roomName);
        if (room != null)
        {
            var roomId = room.RoomId;
            var location = room.UnitySceneId ?? DefaultRoomLocation;
            var dataBlob = room.DataBlob ?? "";

            SubRoomEntry? subroom = subroomName != null
                ? room.SubRooms.FirstOrDefault(s => s.Name == subroomName)
                : room.SubRooms.FirstOrDefault();

            int subroomId;
            if (subroom != null)
            {
                subroomId = subroom.SubRoomId;
                location = subroom.UnitySceneId ?? location;
                dataBlob = subroom.DataBlob ?? dataBlob;
            }
            else
            {
                subroomId = roomId;
            }
            data = new RoomMeta { RoomId = roomId, SubRoomId = subroomId, Location = location, DataBlob = dataBlob };
        }

        lock (_metaLock) { _roomMetaCache[cacheKey] = (DateTime.UtcNow, data); }
        return data;
    }

    public RoomInstance GetOrCreateRoomInstance(int roomId, int subRoomId, string roomName, string location,
        string dataBlob, bool isPrivate, int accountId)
    {
        var key = (roomId, subRoomId);
        lock (_poolLock)
        {
            if (!_roomPool.TryGetValue(key, out var pool))
            {
                pool = new List<RoomInstance>();
                _roomPool[key] = pool;
            }
            pool.RemoveAll(e => (DateTime.UtcNow - e.CreatedAt).TotalSeconds >= RoomInstanceTtlSeconds);

            RoomInstance? target = null;
            if (!isPrivate)
            {
                target = pool.FirstOrDefault(e => !e.IsPrivate && e.Occupants.Count < e.MaxCapacity);
            }
            if (target == null)
            {
                target = new RoomInstance
                {
                    RoomInstanceId = Rng.Next(10000, 99999999),
                    RoomId = roomId,
                    SubRoomId = subRoomId,
                    Location = location,
                    DataBlob = dataBlob,
                    PhotonRoomId = Guid.NewGuid().ToString(),
                    Name = $"^{roomName}",
                    MaxCapacity = 10,
                    IsPrivate = isPrivate,
                };
                pool.Add(target);
            }

            target.Occupants.Add(accountId);
            target.IsFull = target.Occupants.Count >= target.MaxCapacity;
            return target;
        }
    }

    public void ReleasePriorInstance(int accountId)
    {
        if (!_accountRoomInstances.TryGetValue(accountId, out var prev)) return;
        var key = (prev.RoomId, prev.SubRoomId);
        lock (_poolLock)
        {
            if (!_roomPool.TryGetValue(key, out var pool)) return;
            var entry = pool.FirstOrDefault(e => e.RoomInstanceId == prev.RoomInstanceId);
            if (entry != null)
            {
                entry.Occupants.Remove(accountId);
                entry.IsFull = entry.Occupants.Count >= entry.MaxCapacity;
            }
        }
    }

    public RoomInstance? GetAccountRoomInstance(int accountId) =>
        _accountRoomInstances.TryGetValue(accountId, out var v) ? v : null;

    public void SetAccountRoomInstance(int accountId, RoomInstance instance) => _accountRoomInstances[accountId] = instance;

    public List<Dictionary<string, object?>> ListInstancesForRoom(int roomId)
    {
        var result = new List<Dictionary<string, object?>>();
        lock (_poolLock)
        {
            foreach (var ((poolRoomId, _), pool) in _roomPool)
            {
                if (poolRoomId != roomId) continue;
                foreach (var entry in pool)
                {
                    if ((DateTime.UtcNow - entry.CreatedAt).TotalSeconds >= RoomInstanceTtlSeconds) continue;
                    result.Add(new Dictionary<string, object?>
                    {
                        ["roomInstanceId"] = entry.RoomInstanceId,
                        ["roomId"] = entry.RoomId,
                        ["subRoomId"] = entry.SubRoomId,
                        ["isFull"] = entry.IsFull,
                        ["createdAt"] = entry.CreatedAt.ToString("o"),
                        ["playerIds"] = entry.Occupants.ToList(),
                    });
                }
            }
        }
        return result;
    }
}
