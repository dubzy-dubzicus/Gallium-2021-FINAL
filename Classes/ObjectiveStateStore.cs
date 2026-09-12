using System.Collections.Concurrent;

namespace Mocha2021.Classes;

public class ObjectiveEntry
{
    public int Index { get; set; }
    public int Group { get; set; } = 0;
    public int Progress { get; set; } = 0;
    public int VisualProgress { get; set; } = 0;
    public bool IsCompleted { get; set; } = false;
    public bool IsRewarded { get; set; } = false;
    public bool HasClaimedReward { get; set; } = false;
}

public class ObjectiveState
{
    public Dictionary<int, ObjectiveEntry> Objectives { get; set; } = new()
    {
        [0] = new ObjectiveEntry { Index = 0 },
        [1] = new ObjectiveEntry { Index = 1 },
        [2] = new ObjectiveEntry { Index = 2 },
    };
    public string? GroupClearedAt { get; set; }
    public bool GroupCompleted { get; set; } = false;
}

public class ObjectiveStateStore
{
    private readonly ConcurrentDictionary<int, ObjectiveState> _state = new();

    public ObjectiveState Get(int accountId) => _state.GetOrAdd(accountId, _ => new ObjectiveState());

    public ObjectiveState Reset(int accountId)
    {
        var state = new ObjectiveState { GroupClearedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") };
        _state[accountId] = state;
        return state;
    }

    public static Dictionary<string, object?> Serialize(ObjectiveState state) => new()
    {
        ["Objectives"] = state.Objectives.Values.Select(o => new Dictionary<string, object?>
        {
            ["Index"] = o.Index, ["Group"] = o.Group, ["Progress"] = o.Progress, ["VisualProgress"] = o.VisualProgress,
            ["IsCompleted"] = o.IsCompleted, ["IsRewarded"] = o.IsRewarded, ["HasClaimedReward"] = o.HasClaimedReward,
        }).ToList(),
        ["ObjectiveGroups"] = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["Group"] = 0,
                ["IsCompleted"] = state.GroupCompleted,
                ["ClearedAt"] = state.GroupClearedAt ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            },
        },
    };
}
