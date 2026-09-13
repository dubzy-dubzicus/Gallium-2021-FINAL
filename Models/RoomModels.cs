using LiteDB;

namespace Gallium2021.Models;

public class Room
{
    [BsonId]
    public int RoomId { get; set; }
    public string Name { get; set; } = "";
    public string? UnitySceneId { get; set; }
    public string? DataBlob { get; set; }
    public int OwnerId { get; set; }
    public int MaxPlayers { get; set; } = 10;
    public int Accessibility { get; set; } = 0;
    public string? Description { get; set; }
    public string? ImageName { get; set; }
    public bool CloningAllowed { get; set; } = false;
    public bool IsDorm { get; set; } = false;
    public bool SupportsLevelVoting { get; set; } = false;
    public bool IsRRO { get; set; } = false;
    public string? Platforms { get; set; }
    public int Cheers { get; set; } = 0;
    public int FavoritesCount { get; set; } = 0;
    public int Visits { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsSandbox { get; set; } = false;

    public List<string> Tags { get; set; } = new();
    public List<SubRoomEntry> SubRooms { get; set; } = new();
    public List<RoomRoleEntry> Roles { get; set; } = new();
}

public class SubRoomEntry
{
    public int SubRoomId { get; set; }
    public int RoomId { get; set; }
    public string Name { get; set; } = "Home";
    public string? DataBlob { get; set; }
    public bool IsSandbox { get; set; } = false;
    public int MaxPlayers { get; set; } = 10;
    public int Accessibility { get; set; } = 1;
    public string? UnitySceneId { get; set; }
    public int SavedByAccountId { get; set; } = -1;
    public string? InventionUsage { get; set; }
}

public class RoomRoleEntry
{
    public int AccountId { get; set; }
    public int Role { get; set; } = 255;
}

public class RoomInteraction
{
    [BsonId]
    public string Id { get; set; } = "";
    public int AccountId { get; set; }
    public int RoomId { get; set; }
    public bool IsCheered { get; set; } = false;
    public bool IsFavorited { get; set; } = false;
    public DateTime? LastVisitedAt { get; set; }
}
