using LiteDB;
using Gallium2021.Models;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Gallium2021.Classes;

public class PlayerDB
{
    public static readonly HashSet<string> ValidRoles = new() { "developer", "moderator", "beta", "roomcurrency", "creator", "junior" };
    public static readonly Dictionary<string, int> PlatformEnum = new()
    {
        ["Steam"] = 0, ["Oculus"] = 1, ["PSVR"] = 2, ["Viveport"] = 3, ["Android"] = 4, ["iOS"] = 5,
    };

    public const int CheerCreditStartingAmount = 20;

    private readonly LiteDatabase _db;
    private readonly ILiteCollection<Account> _accounts;
    private readonly ILiteCollection<CachedLogin> _cachedLogins;
    private readonly ILiteCollection<Gift> _gifts;
    private readonly ILiteCollection<RewardSelection> _rewardSelections;
    private readonly ILiteCollection<RelationshipRow> _relationships;
    private readonly ILiteCollection<BugReport> _bugReports;
    private readonly ILiteCollection<HeartbeatEntry> _heartbeats;
    private readonly ILiteCollection<UploadEntry> _uploads;
    private readonly ILiteCollection<ModerationAction> _moderationActions;
    private readonly ILiteCollection<WishlistItem> _wishlists;
    private readonly ILiteCollection<AnnouncementEntry> _announcements;

    public readonly List<LevelProgressionEntry> LevelProgression;
    public readonly int MaxLevel;

    public PlayerDB(LiteDatabase db, IWebHostEnvironment env)
    {
        _db = db;
        _accounts = db.GetCollection<Account>("accounts");
        _accounts.EnsureIndex(a => a.Username, unique: true);
        _accounts.EnsureIndex(a => a.PlatformId);
        _cachedLogins = db.GetCollection<CachedLogin>("cached_logins");
        _gifts = db.GetCollection<Gift>("gifts");
        _rewardSelections = db.GetCollection<RewardSelection>("reward_selections");
        _relationships = db.GetCollection<RelationshipRow>("relationships");
        _bugReports = db.GetCollection<BugReport>("bug_reports");
        _heartbeats = db.GetCollection<HeartbeatEntry>("heartbeats");
        _uploads = db.GetCollection<UploadEntry>("uploads");
        _moderationActions = db.GetCollection<ModerationAction>("moderation_actions");
        _wishlists = db.GetCollection<WishlistItem>("wishlists");
        _announcements = db.GetCollection<AnnouncementEntry>("announcements");

        var seedPath = Path.Combine(env.ContentRootPath, "Seed", "LevelProgression.json");
        LevelProgression = JsonSerializer.Deserialize<List<LevelProgressionEntry>>(File.ReadAllText(seedPath))!;
        MaxLevel = LevelProgression[^1].Level;
    }

    public Account? GetAccount(int accountId) => _accounts.FindById(accountId);

    public Account? GetAccountByUsername(string username) =>
        _accounts.FindOne(a => a.Username == username);

    public Account? GetLatestAccountByPlatformId(string platformId) =>
        _accounts.Find(a => a.PlatformId == platformId).OrderByDescending(a => a.AccountId).FirstOrDefault();

    public List<Account> GetAllAccountsForAdmin() => _accounts.FindAll().OrderBy(a => a.AccountId).ToList();

    public List<Account> GetAccountsByIds(IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        return _accounts.Find(a => set.Contains(a.AccountId)).ToList();
    }

    public List<Account> GetAccountsForPlatformId(string platformId)
    {
        var directIds = _accounts.Find(a => a.PlatformId == platformId && a.PlatformId != "").Select(a => a.AccountId);
        var cachedIds = _cachedLogins.Find(c => c.PlatformId == platformId)
            .OrderByDescending(c => c.LastLoginTime)
            .Select(c => c.AccountId);
        var orderedIds = cachedIds.Concat(directIds).Distinct().ToList();
        var accounts = GetAccountsByIds(orderedIds).ToDictionary(a => a.AccountId);
        return orderedIds.Where(accounts.ContainsKey).Select(id => accounts[id]).ToList();
    }

    public Account CreateAccount(string username, string displayName, string passwordHash,
        string? platformId = null, string? platform = null, bool? isJunior = null)
    {
        var account = new Account
        {
            Username = username,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            PlatformId = platformId,
            Platform = platform,
            IsJunior = isJunior,
            CreatedAt = DateTime.UtcNow,
        };
        account.Roles.Add("junior");
        account.AccountId = _accounts.Insert(account);
        return account;
    }

    public void SaveAccount(Account account) => _accounts.Update(account);

    public void TouchOnline(int accountId)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        acct.IsOnline = true;
        acct.LastHeartbeat = DateTime.UtcNow;
        SaveAccount(acct);
    }

    public void UpsertCachedLogin(string platformId, int accountId)
    {
        var id = $"{platformId}:{accountId}";
        _cachedLogins.Upsert(new CachedLogin { Id = id, PlatformId = platformId, AccountId = accountId, LastLoginTime = DateTime.UtcNow });
    }

    public void DeleteAccount(int accountId)
    {
        _accounts.Delete(accountId);
    }

    public bool HasRole(int accountId, string role)
    {
        if (!ValidRoles.Contains(role)) return false;
        var acct = GetAccount(accountId);
        return acct != null && acct.Roles.Contains(role);
    }

    public List<string> GetRoles(int accountId) => GetAccount(accountId)?.Roles ?? new List<string>();

    public void AddRole(int accountId, string role)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        if (!acct.Roles.Contains(role)) acct.Roles.Add(role);
        SaveAccount(acct);
    }

    public void RemoveRole(int accountId, string role)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        acct.Roles.Remove(role);
        SaveAccount(acct);
    }

    public int GetTokenBalance(int accountId) => GetAccount(accountId)?.Tokens ?? 0;

    public int AddTokens(int accountId, int delta)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return 0;
        acct.Tokens += delta;
        SaveAccount(acct);
        return acct.Tokens;
    }

    private int XpRequiredForLevel(int level)
    {
        if (level <= 0) return 0;
        if (level <= MaxLevel) return LevelProgression[level].RequiredXp;
        return LevelProgression[MaxLevel].RequiredXp;
    }

    public (int level, int remaining) LevelForTotalXp(int totalXp)
    {
        int remaining = totalXp;
        int level = 0;
        while (level < MaxLevel)
        {
            var needed = XpRequiredForLevel(level + 1);
            if (remaining < needed) break;
            remaining -= needed;
            level++;
        }
        return (level, remaining);
    }

    public (int Level, int Xp) GetProgression(int accountId)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return (0, 0);
        return (acct.Level, acct.Xp);
    }

    public class AwardXpResult
    {
        public int AccountId { get; set; }
        public int Xp { get; set; }
        public int Level { get; set; }
        public bool LeveledUp { get; set; }
        public int LevelsGained { get; set; }
    }

    public AwardXpResult? AwardXp(int accountId, int amount)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return null;
        var oldLevel = acct.Level;
        var newTotalXp = Math.Max(0, acct.Xp + amount);
        var (newLevel, _) = LevelForTotalXp(newTotalXp);
        acct.Xp = newTotalXp;
        acct.Level = newLevel;
        SaveAccount(acct);
        return new AwardXpResult
        {
            AccountId = accountId,
            Xp = newTotalXp,
            Level = newLevel,
            LeveledUp = newLevel > oldLevel,
            LevelsGained = newLevel - oldLevel,
        };
    }

    public (bool started, bool chosenUsername, bool createdPassword, bool finished) GetAccountCreationState(int accountId)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return (true, true, true, true);
        return (acct.CreationHasStarted, acct.CreationHasChosenUsername, acct.CreationHasCreatedPassword, acct.CreationHasFinished);
    }

    public void SetAccountCreationFlags(int accountId, bool? hasStarted = null, bool? hasChosenUsername = null,
        bool? hasCreatedPassword = null, bool? hasFinished = null)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        if (hasStarted.HasValue) acct.CreationHasStarted = hasStarted.Value;
        if (hasChosenUsername.HasValue) acct.CreationHasChosenUsername = hasChosenUsername.Value;
        if (hasCreatedPassword.HasValue) acct.CreationHasCreatedPassword = hasCreatedPassword.Value;
        if (hasFinished.HasValue) acct.CreationHasFinished = hasFinished.Value;
        SaveAccount(acct);
    }

    public Dictionary<string, string> GetClientSettings(int accountId) =>
        GetAccount(accountId)?.ClientSettings ?? new Dictionary<string, string>();

    public void SetClientSetting(int accountId, string key, string value)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        acct.ClientSettings[key] = value;
        SaveAccount(acct);
    }

    public void LogHeartbeat(int accountId) => _heartbeats.Insert(new HeartbeatEntry { AccountId = accountId, CreatedAt = DateTime.UtcNow });

    public List<string> GetHeartbeats(int accountId, int limit = 50) =>
        _heartbeats.Find(h => h.AccountId == accountId)
            .OrderByDescending(h => h.CreatedAt)
            .Take(limit)
            .Select(h => h.CreatedAt.ToString("o"))
            .ToList();

    public List<AvatarItemEntry> GetAccountAvatarItems(int accountId) => GetAccount(accountId)?.AvatarItems ?? new();

    public void GrantAvatarItem(int accountId, string avatarItemDesc, int avatarItemType = 0,
        string friendlyName = "", string tooltip = "", int rarity = 0)
    {
        if (string.IsNullOrEmpty(avatarItemDesc)) return;
        var acct = GetAccount(accountId);
        if (acct == null) return;
        if (acct.AvatarItems.Any(i => i.AvatarItemDesc == avatarItemDesc)) return;
        acct.AvatarItems.Add(new AvatarItemEntry
        {
            AvatarItemDesc = avatarItemDesc,
            AvatarItemType = avatarItemType,
            FriendlyName = friendlyName,
            Tooltip = tooltip,
            Rarity = rarity,
        });
        SaveAccount(acct);
    }

    public AvatarDataEntry GetAvatarData(int accountId) => GetAccount(accountId)?.AvatarData ?? new();

    public void SaveAvatarData(int accountId, string outfit, string hair, string skin, string face)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        acct.AvatarData = new AvatarDataEntry { OutfitSelections = outfit, HairColor = hair, SkinColor = skin, FaceFeatures = face };
        SaveAccount(acct);
    }

    public int GetSelectedCheer(int accountId) => GetAccount(accountId)?.SelectedCheer ?? 0;

    public int GetCheerCredit(int accountId) => GetAccount(accountId)?.CheerCredit ?? CheerCreditStartingAmount;

    public int DecrementCheerCredit(int accountId, int amount = 1)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return Math.Max(CheerCreditStartingAmount - amount, 0);
        acct.CheerCredit = Math.Max(acct.CheerCredit - amount, 0);
        SaveAccount(acct);
        return acct.CheerCredit;
    }

    public void SetSelectedCheer(int accountId, int cheer)
    {
        var acct = GetAccount(accountId);
        if (acct == null) return;
        acct.SelectedCheer = cheer;
        SaveAccount(acct);
    }

    public long CreateGift(int accountId, int fromPlayerId = 1, string consumableItemDesc = "", string avatarItemDesc = "",
        string equipmentPrefabName = "", string equipmentModificationGuid = "", int currencyType = -1,
        int currency = 0, int xp = 0, int level = 0, int platform = -1, int platformsToSpawnOn = -1,
        int balanceType = -2, int giftContext = 0, int giftRarity = -1, string message = "", int avatarItemType = 0,
        string friendlyName = "", string tooltip = "")
    {
        var gift = new Gift
        {
            AccountId = accountId, FromPlayerId = fromPlayerId, ConsumableItemDesc = consumableItemDesc,
            AvatarItemDesc = avatarItemDesc, EquipmentPrefabName = equipmentPrefabName,
            EquipmentModificationGuid = equipmentModificationGuid, CurrencyType = currencyType, Currency = currency,
            Xp = xp, Level = level, Platform = platform, PlatformsToSpawnOn = platformsToSpawnOn,
            BalanceType = balanceType, GiftContext = giftContext, GiftRarity = giftRarity, Message = message,
            AvatarItemType = avatarItemType, FriendlyName = friendlyName, Tooltip = tooltip,
        };
        return _gifts.Insert(gift);
    }

    public List<Gift> GetPendingGifts(int accountId) =>
        _gifts.Find(g => g.AccountId == accountId && !g.Consumed).OrderBy(g => g.Id).ToList();

    public Gift? ConsumeGift(int accountId, long giftId)
    {
        var gift = _gifts.FindOne(g => g.Id == giftId && g.AccountId == accountId && !g.Consumed);
        if (gift == null) return null;
        gift.Consumed = true;
        _gifts.Update(gift);

        if (gift.Currency != 0) AddTokens(accountId, gift.Currency);
        if (gift.Xp != 0) AwardXp(accountId, gift.Xp);
        if (!string.IsNullOrEmpty(gift.AvatarItemDesc))
        {
            GrantAvatarItem(accountId, gift.AvatarItemDesc, gift.AvatarItemType, gift.FriendlyName, gift.Tooltip,
                gift.GiftRarity != -1 ? gift.GiftRarity : 0);
        }
        return gift;
    }

    public static Dictionary<string, object?> MakeTokenDrop(int? giftDropId, int tokens, int giftContext, string friendlyName = "Tokens", int rarity = 0) => new()
    {
        ["GiftDropId"] = giftDropId,
        ["FriendlyName"] = friendlyName,
        ["Tooltip"] = $"Contains {tokens} Tokens!",
        ["ConsumableItemDesc"] = "",
        ["AvatarItemDesc"] = "",
        ["AvatarItemType"] = 0,
        ["EquipmentPrefabName"] = "",
        ["EquipmentModificationGuid"] = "",
        ["IsQuery"] = false,
        ["Unique"] = false,
        ["SubscribersOnly"] = false,
        ["Rarity"] = rarity,
        ["CurrencyType"] = 2,
        ["Currency"] = tokens,
        ["Context"] = giftContext,
        ["ItemSetId"] = null,
        ["ItemSetFriendlyName"] = "",
    };

    private static readonly Random Rng = new();

    public List<Dictionary<string, object?>> BuildRewardOptions(int giftContext, int? baseTokens = null, int? catalogGiftDropId = null)
    {
        int[] baseChoices = { 10, 15, 20, 25 };
        var baseVal = baseTokens ?? baseChoices[Rng.Next(baseChoices.Length)];
        var tiers = new List<int> { baseVal, baseVal + Rng.Next(5, 16), baseVal * 2 };
        for (int i = tiers.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (tiers[i], tiers[j]) = (tiers[j], tiers[i]);
        }
        var slot1Id = catalogGiftDropId ?? 1;
        return new List<Dictionary<string, object?>>
        {
            MakeTokenDrop(slot1Id, tiers[0], giftContext),
            MakeTokenDrop(2, tiers[1], giftContext),
            MakeTokenDrop(3, tiers[2], giftContext),
        };
    }

    public (string rewardSelectionId, List<Dictionary<string, object?>> options) CreateRewardSelection(
        int accountId, string message = "", int giftContext = 110000, int rewardType = 0,
        int? baseTokens = null, int? catalogGiftDropId = null)
    {
        var options = BuildRewardOptions(giftContext, baseTokens, catalogGiftDropId);
        var rewardSelectionId = Guid.NewGuid().ToString();
        _rewardSelections.Insert(new RewardSelection
        {
            RewardSelectionId = rewardSelectionId,
            AccountId = accountId,
            Message = message,
            GiftContext = giftContext,
            RewardType = rewardType,
            Options = options,
        });
        return (rewardSelectionId, options);
    }

    public List<RewardSelection> GetPendingRewardSelections(int accountId) =>
        _rewardSelections.Find(r => r.AccountId == accountId && !r.Consumed).OrderBy(r => r.Id).ToList();

    public Dictionary<string, object?>? SelectReward(int accountId, string rewardSelectionId, object? giftDropId = null, object? slotIndex = null)
    {
        var row = _rewardSelections.FindOne(r => r.RewardSelectionId == rewardSelectionId && r.AccountId == accountId && !r.Consumed);
        if (row == null) return null;

        Dictionary<string, object?>? chosen = null;
        if (slotIndex != null && int.TryParse(slotIndex.ToString(), out var idx) && idx >= 0 && idx < row.Options.Count)
        {
            chosen = row.Options[idx];
        }
        if (chosen == null && giftDropId != null)
        {
            chosen = row.Options.FirstOrDefault(o => (o.GetValueOrDefault("GiftDropId")?.ToString() ?? "") == giftDropId.ToString());
        }
        chosen ??= row.Options.FirstOrDefault();
        if (chosen == null) return null;

        row.Consumed = true;
        _rewardSelections.Update(row);

        if (chosen.TryGetValue("CurrencyType", out var ct) && Convert.ToInt32(ct) == 2 &&
            chosen.TryGetValue("Currency", out var cur) && Convert.ToInt32(cur) != 0)
        {
            AddTokens(accountId, Convert.ToInt32(cur));
        }
        return chosen;
    }

    public List<RelationshipRow> GetRelationshipRows(int accountId) =>
        _relationships.Find(r => r.AccountId == accountId || r.OtherAccountId == accountId).ToList();

    public void UpsertRelationship(int accountId, int otherAccountId, string relationship)
    {
        _relationships.Upsert(new RelationshipRow
        {
            Id = $"{accountId}:{otherAccountId}",
            AccountId = accountId,
            OtherAccountId = otherAccountId,
            Relationship = relationship,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public void DeleteRelationship(int a, int b)
    {
        _relationships.DeleteMany(r => (r.AccountId == a && r.OtherAccountId == b) || (r.AccountId == b && r.OtherAccountId == a));
    }

    public List<RelationshipRow> GetPendingIncomingRequests(int accountId) =>
        _relationships.Find(r => r.OtherAccountId == accountId && r.Relationship == "pending").ToList();

    public void CreateBugReport(int? accountId, string summary, string description, string buildVersion, string buildTimestamp, string rawFieldsJson)
    {
        _bugReports.Insert(new BugReport
        {
            AccountId = accountId, Summary = summary, Description = description,
            BuildVersion = buildVersion, BuildTimestamp = buildTimestamp, RawFields = rawFieldsJson,
        });
    }

    public void CreateUpload(int accountId, string fileName, string fileHash, string ownershipProof)
    {
        _uploads.Insert(new UploadEntry { AccountId = accountId, FileName = fileName, FileHash = fileHash, OwnershipProof = ownershipProof });
    }

    public void CreateModerationAction(int targetId, int? moderatorId, int reportCategory, bool isBan, bool isHostKick, string message, double? hours)
    {
        _moderationActions.Insert(new ModerationAction
        {
            TargetId = targetId, ModeratorId = moderatorId, ReportCategory = reportCategory,
            IsBan = isBan, IsHostKick = isHostKick, Message = message,
            ExpiresAt = hours.HasValue ? DateTime.UtcNow.AddHours(hours.Value) : null,
        });
    }

    public ModerationAction? GetActiveModerationAction(int targetId) =>
        _moderationActions.Find(a => a.TargetId == targetId && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(a => a.CreatedAt).FirstOrDefault();

    public List<WishlistItem> GetWishlist(int accountId) => _wishlists.Find(w => w.AccountId == accountId).OrderBy(w => w.CreatedAt).ToList();

    public WishlistItem? GetWishlistItem(int accountId, int purchasableItemId) =>
        _wishlists.FindOne(w => w.AccountId == accountId && w.PurchasableItemId == purchasableItemId);

    public string AddWishlistItem(int accountId, int purchasableItemId)
    {
        var existing = GetWishlistItem(accountId, purchasableItemId);
        if (existing != null) return existing.WishlistItemId;
        var id = Guid.NewGuid().ToString();
        _wishlists.Insert(new WishlistItem { WishlistItemId = id, AccountId = accountId, PurchasableItemId = purchasableItemId });
        return id;
    }

    public void RemoveWishlistItem(int accountId, int purchasableItemId) =>
        _wishlists.DeleteMany(w => w.AccountId == accountId && w.PurchasableItemId == purchasableItemId);

    public List<AnnouncementEntry> GetAnnouncements() => _announcements.FindAll().OrderByDescending(a => a.CreatedAt).ToList();

    public void CreateAnnouncement(AnnouncementEntry entry) => _announcements.Insert(entry);
}
