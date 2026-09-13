namespace Gallium2021.Classes;

public class TestCase
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string RoomName { get; set; } = "";
    public int Status { get; set; } = 0;
    public List<string> Assignees { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int TestPassId { get; set; }
    public int? RoomId { get; set; }
}

public class TestPass
{
    public int TestPassId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
    public bool IsPublished { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> TestCaseIds { get; set; } = new();
}

public static class TestCaseStatus
{
    public const int NotYetTested = 0;
    public const int Claimed = 1;
    public const int Failed = 2;
    public const int Passed = 3;
    public const int Blocked = 4;
}

public class TestCaseStore
{
    public readonly Dictionary<string, TestCase> Cases = new()
    {
        ["TC-1"] = new TestCase { Id = "TC-1", Title = "test2", Description = "test", RoomName = "", Status = TestCaseStatus.Passed, Tags = new() { "test1", "test2" }, TestPassId = 1 },
        ["TC-2"] = new TestCase { Id = "TC-2", Title = "another test", Description = "test", RoomName = "", Status = TestCaseStatus.Passed, Tags = new() { "smoke" }, TestPassId = 1 },
    };

    public readonly Dictionary<int, TestPass> Passes = new()
    {
        [1] = new TestPass
        {
            TestPassId = 1, Name = "test3", Description = "test", CreatedAt = "2025-01-01T00:00:00Z",
            CompletedAt = "2025-01-01T01:00:00Z", IsPublished = true, Tags = new() { "smoke", "auth" },
            TestCaseIds = new() { "TC-1", "TC-2" },
        },
    };

    public Dictionary<string, object?> CaseJson(TestCase tc) => new()
    {
        ["Id"] = tc.Id, ["Key"] = tc.Id, ["Title"] = tc.Title, ["Description"] = tc.Description, ["RoomName"] = tc.RoomName,
        ["Status"] = tc.Status, ["Assignees"] = tc.Assignees, ["Tags"] = tc.Tags, ["TestCaseId"] = tc.Id,
        ["TestPassId"] = tc.TestPassId, ["RoomId"] = tc.RoomId,
    };

    public Dictionary<string, object?> PassJson(TestPass tp)
    {
        var cases = tp.TestCaseIds.Where(Cases.ContainsKey).Select(id => Cases[id]).ToList();
        return new Dictionary<string, object?>
        {
            ["Id"] = tp.TestPassId, ["Name"] = tp.Name, ["Description"] = tp.Description,
            ["StartDate"] = tp.CreatedAt, ["EndDate"] = tp.CompletedAt, ["WasManuallyClosed"] = tp.IsPublished,
            ["TestCases"] = cases.Select(CaseJson).ToList(), ["Tags"] = tp.Tags,
            ["NumTestCases"] = cases.Count, ["NumPassedTestCases"] = cases.Count(c => c.Status == TestCaseStatus.Passed),
            ["NumFailedTestCases"] = cases.Count(c => c.Status == TestCaseStatus.Failed),
            ["TestPassId"] = tp.TestPassId, ["CreatedAt"] = tp.CreatedAt, ["CompletedAt"] = tp.CompletedAt, ["IsPublished"] = tp.IsPublished,
        };
    }
}
