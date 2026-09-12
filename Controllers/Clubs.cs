using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;

namespace Mocha2021.Controllers;

[Controller]
public class Clubs : ControllerBase
{
    private readonly CommerceStore _store;

    public Clubs(CommerceStore store)
    {
        _store = store;
    }

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpGet("/club/home/me")]
    public IActionResult ClubHomeMeLegacy() => new NotFoundObjectResult(new Dictionary<string, object?>());

    [HttpGet("/clubs/announcements/v2/subscription/mine/unread")]
    public IActionResult SubscriptionAnnouncementsUnread() => new OkObjectResult(new List<object>());

    [HttpGet("/clubs/club/home/me")]
    public IActionResult ClubHomeMe() => Ok2(new() { ["home"] = null });

    [HttpGet("/clubs/club/mine/member")]
    public IActionResult ClubMineMember() => new OkObjectResult(new List<object>());

    [HttpGet("/clubs/club/mine/created")]
    [HttpGet("/club/mine/created")]
    public IActionResult ClubMineCreated() => new OkObjectResult(new List<object>());

    [HttpGet("/clubs/club/account/{accountId}/created")]
    [HttpGet("/club/account/{accountId}/created")]
    public IActionResult ClubAccountCreated(string accountId) => new OkObjectResult(new List<object>());

    [HttpGet("/clubs/club/search")]
    [HttpGet("/club/search")]
    public IActionResult ClubSearch([FromQuery] string? category, [FromQuery] int count = 30)
    {
        category ??= "";
        var clubs = _store.ClubsForCategory(category, count);
        return new OkObjectResult(new Dictionary<string, object?> { ["Clubs"] = clubs, ["TotalClubs"] = clubs.Count, ["ContinuationToken"] = null });
    }

    [HttpGet("/clubs/categories")]
    [HttpGet("/club/categories")]
    [HttpGet("/clubs/categoryTags")]
    [HttpGet("/club/categoryTags")]
    public IActionResult ClubCategories() => new OkObjectResult(new[] { "Social", "Creative", "Competitive", "Casual", "Educational", "Entertainment", "Lifestyle" });

    [HttpGet("/subscription/mine/member")]
    public IActionResult SubscriptionMineMember() => new OkObjectResult(new List<object>());
}
