using Microsoft.AspNetCore.Mvc;
using BCrypt.Net;
using Mocha2021.Classes;

namespace Mocha2021.Controllers;

[Controller]
public class Auth : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;

    public Auth(PlayerDB playerDb, SessionManager sessions)
    {
        _playerDb = playerDb;
        _sessions = sessions;
    }

    private static IActionResult Err(string code, string desc, int http = 400) =>
        new ObjectResult(new Dictionary<string, object?> { ["error"] = code, ["error_description"] = desc }) { StatusCode = http };

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpPost("/Auth/connect/token")]
    public IActionResult AuthToken([FromForm] Dictionary<string, string> form)
    {
        form.TryGetValue("client_id", out var clientId);
        form.TryGetValue("client_secret", out var clientSecret);
        if (clientId != ServerConfig.ClientId || clientSecret != ServerConfig.ClientSecret)
            return Err("invalid_client", "Client authentication failed.");

        form.TryGetValue("grant_type", out var grant);
        grant ??= "";
        if (!ServerConfig.ValidGrantTypes.Contains(grant))
            return Err("unsupported_grant_type", $"grant_type '{grant}' not supported.");

        form.TryGetValue("platformId", out var platformIdRaw1);
        form.TryGetValue("platform_id", out var platformIdRaw2);
        var platformIdRaw = platformIdRaw1 ?? platformIdRaw2;
        if (grant == "platform_auth" && string.IsNullOrEmpty(platformIdRaw))
            return Err("invalid_grant", "platform verification failed");

        int accountId = 1;
        string? platformId = null;

        if (grant == "password")
        {
            form.TryGetValue("username", out var username);
            form.TryGetValue("password", out var password);
            var acct = _playerDb.GetAccountByUsername(username ?? "");
            if (acct == null || string.IsNullOrEmpty(acct.PasswordHash) || !BCrypt.Net.BCrypt.Verify(password ?? "", acct.PasswordHash))
                return Err("invalid_grant", "username or password is incorrect");
            accountId = acct.AccountId;
        }
        else if (grant == "platform_auth")
        {
            platformId = platformIdRaw;
            var acct = _playerDb.GetLatestAccountByPlatformId(platformId!);
            if (acct == null) return Err("invalid_grant", "no account for this platform_id");
            accountId = acct.AccountId;
        }
        else if (grant == "cachedlogin")
        {
            form.TryGetValue("accountId", out var a1);
            form.TryGetValue("account_id", out var a2);
            form.TryGetValue("userId", out var a3);
            var requested = a1 ?? a2 ?? a3;
            if (string.IsNullOrEmpty(requested)) return Err("invalid_grant", "no accountId provided for cachedlogin");
            if (!int.TryParse(requested, out var requestedAccountId)) return Err("invalid_grant", "malformed accountId");

            var acct = _playerDb.GetAccount(requestedAccountId);
            if (acct == null || (acct.PlatformId != platformIdRaw && !string.Equals(acct.PlatformId, platformIdRaw)))
            {
                if (acct == null) return Err("invalid_grant", "accountId is not linked to this platform_id");
            }
            accountId = acct!.AccountId;
            platformId = !string.IsNullOrEmpty(platformIdRaw) ? platformIdRaw : null;
        }

        var (token, isJunior, roles) = _sessions.BuildJwtForAccount(accountId, platformId);
        _playerDb.TouchOnline(accountId);

        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["access_token"] = token,
            ["refresh_token"] = Guid.NewGuid().ToString(),
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["scope"] = "openid profile",
            ["role"] = roles,
            ["isJunior"] = isJunior,
        });
    }

    [HttpPost("/connect/token")]
    public IActionResult ConnectToken([FromForm] Dictionary<string, string> form)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        var token = SessionManager.ExtractToken(authHeader);

        int? accountId = null;
        form.TryGetValue("platform_id", out var p1);
        form.TryGetValue("platformId", out var p2);
        var platformId = p1 ?? p2;

        form.TryGetValue("accountId", out var a1);
        form.TryGetValue("account_id", out var a2);
        var requestedRaw = a1 ?? a2;
        if (!string.IsNullOrEmpty(requestedRaw) && int.TryParse(requestedRaw, out var requestedAccountId))
        {
            var acct = _playerDb.GetAccount(requestedAccountId);
            if (acct != null && (string.IsNullOrEmpty(platformId) || acct.PlatformId == platformId))
                accountId = acct.AccountId;
        }

        form.TryGetValue("username", out var username);
        form.TryGetValue("password", out var password);
        if (accountId == null && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var acct = _playerDb.GetAccountByUsername(username);
            if (acct != null && !string.IsNullOrEmpty(acct.PasswordHash) && BCrypt.Net.BCrypt.Verify(password, acct.PasswordHash))
                accountId = acct.AccountId;
            else
                return Err("invalid_grant", "username or password is incorrect");
        }

        if (accountId == null && !string.IsNullOrEmpty(platformId))
        {
            var acct = _playerDb.GetLatestAccountByPlatformId(platformId);
            if (acct != null) accountId = acct.AccountId;
        }
        if (accountId == null) accountId = _sessions.ResolveAccountIdOrNull(authHeader);
        if (accountId == null && !string.IsNullOrEmpty(platformId))
        {
            var pt = _sessions.GetPlatformToken(platformId);
            if (pt != null) accountId = pt.Value.AccountId;
        }

        if (accountId == null)
        {
            var (displayName, newUsername) = GenerateUniqueUsername();
            var account = _playerDb.CreateAccount(newUsername, displayName, "", platformId);
            accountId = account.AccountId;
        }

        if (!string.IsNullOrEmpty(platformId)) _playerDb.UpsertCachedLogin(platformId, accountId.Value);

        var (jwtToken, isJunior, roles) = _sessions.BuildJwtForAccount(accountId.Value, platformId);
        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["error"] = null,
            ["error_description"] = null,
            ["access_token"] = jwtToken,
            ["refresh_token"] = Guid.NewGuid().ToString(),
            ["key"] = "YWUzYTJlYmYtY2EyNi00ZGJhLWI3NGUtYmM3MjhlNDBjMjJm",
            ["role"] = roles,
            ["isJunior"] = isJunior,
        });
    }

    private (string displayName, string username) GenerateUniqueUsername()
    {
        for (var i = 0; i < 5; i++)
        {
            var (adjective, noun) = GamertagWords.Generate();
            var displayName = $"{adjective}{noun}";
            var username = $"{displayName}{Random.Shared.Next(1000, 10000)}";
            if (_playerDb.GetAccountByUsername(username) == null) return (displayName, username);
        }
        var (a2, n2) = GamertagWords.Generate();
        var dn = $"{a2}{n2}";
        return (dn, $"{dn}{Random.Shared.Next(1000, 10000)}");
    }

    [HttpPost("/Auth/cachedlogin/forplatformids")]
    public IActionResult AuthCachedLogin([FromBody] Dictionary<string, object?>? body)
    {
        body ??= new();
        var platformId = body.GetValueOrDefault("platformId")?.ToString() ?? body.GetValueOrDefault("platform_id")?.ToString();
        if (!string.IsNullOrEmpty(platformId))
        {
            var pt = _sessions.GetPlatformToken(platformId);
            if (pt != null)
                return new OkObjectResult(new Dictionary<string, object?> { ["access_token"] = pt.Value.Token, ["token_type"] = "Bearer", ["expires_in"] = 3600 });
        }
        var acct = !string.IsNullOrEmpty(platformId) ? _playerDb.GetLatestAccountByPlatformId(platformId) : null;
        if (acct == null) return Err("invalid_grant", "no cached account for this platform");

        var token = _sessions.MakeToken();
        _sessions.RegisterToken(token, acct.AccountId);
        if (!string.IsNullOrEmpty(platformId)) _sessions.RegisterPlatformToken(platformId, token, acct.AccountId);
        _playerDb.TouchOnline(acct.AccountId);
        return new OkObjectResult(new Dictionary<string, object?> { ["access_token"] = token, ["token_type"] = "Bearer", ["expires_in"] = 3600 });
    }

    [HttpGet("/Auth/cachedlogin/forplatformid/{platformType}/{platformId}")]
    public IActionResult AuthCachedLoginForPlatform(string platformType, string platformId)
    {
        if (string.IsNullOrEmpty(platformId) || platformId is "0" or "-1" or "null" or "None" or "undefined")
            return new OkObjectResult(new List<object>());

        var accounts = _playerDb.GetAccountsForPlatformId(platformId);
        if (accounts.Count == 0) return new OkObjectResult(new List<object>());

        object platformTypeOut = int.TryParse(platformType, out var pt) ? pt : platformType;

        var result = accounts.Select(row => new Dictionary<string, object?>
        {
            ["accountId"] = row.AccountId,
            ["account"] = new Dictionary<string, object?>
            {
                ["accountId"] = row.AccountId,
                ["createdAt"] = row.CreatedAt.ToString("o"),
                ["displayName"] = row.DisplayName,
                ["username"] = row.Username,
                ["profileImage"] = row.ProfileImage ?? "DefaultProfileImage",
                ["isJunior"] = row.IsJunior ?? false,
                ["platformMask"] = 0,
            },
            ["lastLoginTime"] = "0001-01-01T00:00:00",
            ["platform"] = platformTypeOut,
            ["platformId"] = platformId,
            ["requirePassword"] = false,
        }).ToList();
        return new OkObjectResult(result);
    }

    [HttpGet("/cachedlogin/forplatformid/{platformId}/{userId}")]
    public IActionResult CachedLogin(string platformId, string userId)
    {
        if (string.IsNullOrEmpty(userId) || userId is "0" or "-1" or "null" or "None" or "undefined")
            return new OkObjectResult(new List<object>());

        var accounts = _playerDb.GetAccountsForPlatformId(userId);
        if (accounts.Count == 0) return new OkObjectResult(new List<object>());

        var result = accounts.Select(acct => new Dictionary<string, object?>
        {
            ["platform"] = 0,
            ["platformId"] = userId,
            ["accountId"] = acct.AccountId,
            ["lastLoginTime"] = "2026-06-22T01:55:33.2370783",
            ["requirePassword"] = false,
            ["username"] = acct.Username,
            ["displayName"] = acct.DisplayName,
            ["profileImage"] = acct.ProfileImage,
        }).ToList();

        if (accounts.Count == 1)
        {
            var acct = accounts[0];
            var token = _sessions.MakeToken();
            _sessions.RegisterToken(token, acct.AccountId);
            _sessions.RegisterPlatformToken(userId, token, acct.AccountId);
            _playerDb.TouchOnline(acct.AccountId);
        }
        return new OkObjectResult(result);
    }

    [HttpGet("/Auth/cachedlogin/current")]
    public IActionResult AuthCachedCurrent()
    {
        var token = SessionManager.ExtractToken(Request.Headers.Authorization.ToString());
        var accountId = _sessions.ResolveAccountIdOrNull(Request.Headers.Authorization.ToString()) ?? 1;
        var acct = _playerDb.GetAccount(accountId);
        if (acct == null) return new OkObjectResult(new Dictionary<string, object?> { ["accountId"] = 1, ["username"] = "Coach" });
        return new OkObjectResult(new Dictionary<string, object?> { ["accountId"] = acct.AccountId, ["username"] = acct.Username });
    }

    [HttpPost("/Auth/cachedlogin/migrate")]
    public IActionResult AuthCachedMigrate() => Ok2();

    [HttpGet("/Auth/role/{role}")]
    public IActionResult AuthRole(string role) => Ok2(new() { ["hasRole"] = PlayerDB.ValidRoles.Contains(role), ["role"] = role });

    private string? ExtractPasswordField()
    {
        if (Request.HasFormContentType)
        {
            foreach (var kv in Request.Form)
                if (kv.Key.ToLowerInvariant() is "password" or "newpassword" or "new_password")
                    return kv.Value.ToString();
        }
        if (Request.Query.TryGetValue("password", out var qp)) return qp.ToString();
        if (Request.Query.TryGetValue("newPassword", out var qnp)) return qnp.ToString();
        return null;
    }

    [HttpPost("/Auth/account/me/changepassword")]
    public IActionResult AuthChangePassword() => ChangePasswordImpl();

    [HttpPost("/account/me/changepassword")]
    public IActionResult AccountMeChangePassword() => ChangePasswordImpl();

    private IActionResult ChangePasswordImpl()
    {
        var accountId = _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());
        var password = ExtractPasswordField();
        if (string.IsNullOrEmpty(password))
            return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "password is required" }) { StatusCode = 400 };

        var acct = _playerDb.GetAccount(accountId);
        if (acct == null) return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "not_found" }) { StatusCode = 404 };
        acct.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        _playerDb.SaveAccount(acct);
        _playerDb.SetAccountCreationFlags(accountId, hasCreatedPassword: true);
        return new OkObjectResult(new Dictionary<string, object?> { ["success"] = true });
    }

    [HttpGet("/Auth/account/me/haspassword")]
    public IActionResult AuthHasPassword()
    {
        var accountId = _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());
        var acct = _playerDb.GetAccount(accountId);
        return Ok2(new() { ["hasPassword"] = acct != null && !string.IsNullOrEmpty(acct.PasswordHash) });
    }

    [HttpPost("/Auth/account/recoverpassword")]
    public IActionResult AuthRecoverPassword() => Ok2();

    [HttpGet("/eac/challenge")]
    [HttpPost("/eac/challenge")]
    public IActionResult EacChallenge() => Content("AAAA", "text/plain");

    [HttpPost("/account/create")]
    public IActionResult AccountCreate([FromForm] Dictionary<string, string>? form)
    {
        var data = form ?? new Dictionary<string, string>();
        data.TryGetValue("platform", out var platformRaw);
        data.TryGetValue("platformId", out var pid1);
        data.TryGetValue("platform_id", out var pid2);
        var platformIdRaw = pid1 ?? pid2;

        if (string.IsNullOrEmpty(platformIdRaw))
            return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "platformId is required" }) { StatusCode = 400 };

        if (!int.TryParse(platformRaw, out var platformNum) || !PlayerDB.PlatformEnum.ContainsValue(platformNum))
            return new ObjectResult(new Dictionary<string, object?> { ["success"] = false, ["error"] = "platform is invalid" }) { StatusCode = 403 };

        var platformName = PlayerDB.PlatformEnum.First(kv => kv.Value == platformNum).Key;

        data.TryGetValue("username", out var clientUsername1);
        data.TryGetValue("displayName", out var clientUsername2);
        data.TryGetValue("Username", out var clientUsername3);
        var clientUsername = clientUsername1 ?? clientUsername2 ?? clientUsername3;

        string username, displayName;
        if (!string.IsNullOrEmpty(clientUsername))
        {
            username = clientUsername;
            displayName = clientUsername;
        }
        else
        {
            var (dn, un) = GenerateUniqueUsername();
            displayName = dn;
            username = un;
        }

        var account = _playerDb.CreateAccount(username, displayName, "", platformIdRaw, platformName, null);
        _playerDb.SetAccountCreationFlags(account.AccountId, hasStarted: false, hasChosenUsername: false, hasCreatedPassword: false, hasFinished: false);
        var (jwtToken, isJunior, roles) = _sessions.BuildJwtForAccount(account.AccountId, platformIdRaw);

        var createdAtStr = account.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffff") + "0Z";
        return new OkObjectResult(new Dictionary<string, object?>
        {
            ["success"] = true,
            ["error"] = "",
            ["value"] = new Dictionary<string, object?>
            {
                ["accountId"] = account.AccountId,
                ["profileImage"] = account.ProfileImage ?? "DefaultImgCOLOR",
                ["isJunior"] = null,
                ["platforms"] = -1,
                ["username"] = account.Username,
                ["displayName"] = account.Username,
                ["createdAt"] = createdAtStr,
                ["personalPronouns"] = 0,
                ["identityFlags"] = 0,
            },
            ["access_token"] = jwtToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["role"] = roles,
            ["isJunior"] = isJunior,
        });
    }
}

public static class GamertagWords
{
    private static readonly string[] Adjectives =
    {
        "Sneaky", "Turbo", "Rabid", "Sunny", "Feral", "Cosmic", "Salty", "Chunky",
        "Rogue", "Gnarly", "Cranky", "Zesty", "Wobbly", "Jumbo", "Spicy", "Frosty",
        "Rusty", "Sly", "Loud", "Buff", "Wired", "Grumpy", "Neon", "Adventurous",
        "Mighty", "Curious", "Sleepy", "Jolly", "Fearless", "Chill", "Savage", "Dizzy",
    };

    private static readonly string[] Nouns =
    {
        "Wolf", "Falcon", "Zebra", "Panther", "Yeti", "Otter", "Raptor", "Moose",
        "Badger", "Llama", "Cobra", "Gecko", "Walrus", "Ferret", "Hyena", "Bison",
        "Lynx", "Marmot", "Weasel", "Ibex", "Toucan", "Meerkat", "Antelope", "Sloth",
    };

    public static (string adjective, string noun) Generate() =>
        (Adjectives[Random.Shared.Next(Adjectives.Length)], Nouns[Random.Shared.Next(Nouns.Length)]);
}
