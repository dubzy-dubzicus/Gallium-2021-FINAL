using LiteDB;

namespace Mocha2021.Models;

public class Account
{
    [BsonId]
    public int AccountId { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? PlatformId { get; set; }
    public string? Platform { get; set; }
    public bool? IsJunior { get; set; }
    public string? ProfileImage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Tokens { get; set; } = 0;
    public int Xp { get; set; } = 0;
    public int Level { get; set; } = 0;
    public string? Bio { get; set; }
    public int AvailableUsernameChanges { get; set; } = 3;
    public DateTime? Birthday { get; set; }
    public bool IsOnline { get; set; } = false;
    public DateTime? LastHeartbeat { get; set; }
    public string? Email { get; set; }
    public int PersonalPronouns { get; set; } = 0;
    public int IdentityFlags { get; set; } = 0;

    public List<string> Roles { get; set; } = new();
    public int SelectedCheer { get; set; } = 0;
    public int CheerCredit { get; set; } = 20;

    public bool CreationHasStarted { get; set; } = true;
    public bool CreationHasChosenUsername { get; set; } = true;
    public bool CreationHasCreatedPassword { get; set; } = true;
    public bool CreationHasFinished { get; set; } = true;

    public Dictionary<string, string> ClientSettings { get; set; } = new();
    public List<AvatarItemEntry> AvatarItems { get; set; } = new();
    public AvatarDataEntry AvatarData { get; set; } = new();
}

public class AvatarItemEntry
{
    public string AvatarItemDesc { get; set; } = "";
    public int AvatarItemType { get; set; } = 0;
    public string FriendlyName { get; set; } = "";
    public string Tooltip { get; set; } = "";
    public int Rarity { get; set; } = 0;
}

public class AvatarDataEntry
{
    public string OutfitSelections { get; set; } = "";
    public string HairColor { get; set; } = "";
    public string SkinColor { get; set; } = "";
    public string FaceFeatures { get; set; } = "";
}

public class CachedLogin
{
    [BsonId]
    public string Id { get; set; } = "";
    public string PlatformId { get; set; } = "";
    public int AccountId { get; set; }
    public DateTime LastLoginTime { get; set; } = DateTime.UtcNow;
}

public class Gift
{
    [BsonId(autoId: true)]
    public long Id { get; set; }
    public int AccountId { get; set; }
    public int FromPlayerId { get; set; } = 1;
    public string ConsumableItemDesc { get; set; } = "";
    public string AvatarItemDesc { get; set; } = "";
    public string EquipmentPrefabName { get; set; } = "";
    public string EquipmentModificationGuid { get; set; } = "";
    public int CurrencyType { get; set; } = -1;
    public int Currency { get; set; } = 0;
    public int Xp { get; set; } = 0;
    public int Level { get; set; } = 0;
    public int Platform { get; set; } = -1;
    public int PlatformsToSpawnOn { get; set; } = -1;
    public int BalanceType { get; set; } = -2;
    public int GiftContext { get; set; } = 0;
    public int GiftRarity { get; set; } = -1;
    public string Message { get; set; } = "";
    public int AvatarItemType { get; set; } = 0;
    public string FriendlyName { get; set; } = "";
    public string Tooltip { get; set; } = "";
    public bool Consumed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RewardSelection
{
    [BsonId(autoId: true)]
    public long Id { get; set; }
    public string RewardSelectionId { get; set; } = "";
    public int AccountId { get; set; }
    public string Message { get; set; } = "";
    public int GiftContext { get; set; } = 0;
    public int RewardType { get; set; } = 0;
    public List<Dictionary<string, object?>> Options { get; set; } = new();
    public bool Consumed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RelationshipRow
{
    [BsonId]
    public string Id { get; set; } = "";
    public int AccountId { get; set; }
    public int OtherAccountId { get; set; }
    public string Relationship { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BugReport
{
    [BsonId(autoId: true)]
    public long BugReportId { get; set; }
    public int? AccountId { get; set; }
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public string BuildVersion { get; set; } = "";
    public string BuildTimestamp { get; set; } = "";
    public string RawFields { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class HeartbeatEntry
{
    [BsonId(autoId: true)]
    public long HeartbeatId { get; set; }
    public int AccountId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UploadEntry
{
    [BsonId(autoId: true)]
    public long Id { get; set; }
    public int AccountId { get; set; }
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = "";
    public string OwnershipProof { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ModerationAction
{
    [BsonId(autoId: true)]
    public long ActionId { get; set; }
    public int TargetId { get; set; }
    public int? ModeratorId { get; set; }
    public int ReportCategory { get; set; } = 0;
    public bool IsBan { get; set; } = false;
    public bool IsHostKick { get; set; } = false;
    public int GameSessionId { get; set; } = 0;
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

public class WishlistItem
{
    [BsonId]
    public string WishlistItemId { get; set; } = "";
    public int AccountId { get; set; }
    public int PurchasableItemId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AnnouncementEntry
{
    [BsonId(autoId: true)]
    public long AnnouncementId { get; set; }
    public int AnnouncementType { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ImageName { get; set; }
    public int LinkType { get; set; }
    public string? LinkName { get; set; }
    public string? LinkUri { get; set; }
    public int Platform { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
