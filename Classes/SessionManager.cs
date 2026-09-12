using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mocha2021.Models;

namespace Mocha2021.Classes;

public class SessionManager
{
    private readonly ConcurrentDictionary<string, int> _tokenSessions = new();
    private readonly ConcurrentDictionary<string, (string Token, int AccountId)> _platformTokens = new();
    private readonly PlayerDB _playerDb;

    public SessionManager(PlayerDB playerDb)
    {
        _playerDb = playerDb;
    }

    public string MakeToken() => $"eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.{Guid.NewGuid():N}.mock_sig";

    public void RegisterToken(string token, int accountId) => _tokenSessions[token] = accountId;

    public void RegisterPlatformToken(string platformId, string token, int accountId) =>
        _platformTokens[platformId] = (token, accountId);

    public (string Token, int AccountId)? GetPlatformToken(string platformId) =>
        _platformTokens.TryGetValue(platformId, out var v) ? v : null;

    public (string token, bool? isJunior, List<string> roles) BuildJwtForAccount(int accountId, string? platformId = null)
    {
        var acct = _playerDb.GetAccount(accountId);
        var resolvedPlatformId = acct?.PlatformId ?? platformId ?? "";
        bool? isJunior = acct?.IsJunior;
        var roles = acct?.Roles ?? new List<string>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var header = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payload = JsonSerializer.Serialize(new
        {
            jti = Guid.NewGuid().ToString(),
            sub = accountId.ToString(),
            rn_plat = "0",
            rn_platid = resolvedPlatformId,
            rn_junior = isJunior,
            role = roles,
            client_id = "rec",
            scope = new[] { "openid", "rn.api", "rn.commerce", "rn.notify", "rn.match", "rn.chat", "rn.accounts", "rn.auth", "rn.link", "rn.lists", "rn.clubs", "rn.rooms", "rn.data", "offline_access" },
            nbf = now,
            exp = 9999999999L,
            iat = now,
            iss = "http://localhost:5000",
            aud = "http://localhost:5000",
        });

        var headerEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("secret_key"));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{headerEncoded}.{payloadEncoded}")));
        var jwtToken = $"{headerEncoded}.{payloadEncoded}.{signature}";

        _tokenSessions[jwtToken] = accountId;
        if (!string.IsNullOrEmpty(resolvedPlatformId)) _platformTokens[resolvedPlatformId] = (jwtToken, accountId);

        return (jwtToken, isJunior, roles);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    public int CurrentAccountId(string authorizationHeader)
    {
        var token = ExtractToken(authorizationHeader);
        if (_tokenSessions.TryGetValue(token, out var accountId)) return accountId;

        try
        {
            var parts = token.Split('.');
            if (parts.Length == 3)
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("sub", out var subEl))
                {
                    var sub = subEl.ValueKind == JsonValueKind.String ? subEl.GetString() : subEl.GetRawText();
                    if (int.TryParse(sub, out var parsedId))
                    {
                        _tokenSessions[token] = parsedId;
                        return parsedId;
                    }
                }
            }
        }
        catch { }
        return 1;
    }

    public int? ResolveAccountIdOrNull(string authorizationHeader, string? queryToken = null)
    {
        var token = ExtractToken(authorizationHeader);
        if (string.IsNullOrEmpty(token)) token = queryToken ?? "";
        if (string.IsNullOrEmpty(token)) return null;

        if (_tokenSessions.TryGetValue(token, out var accountId)) return accountId;
        try
        {
            var parts = token.Split('.');
            if (parts.Length == 3)
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("sub", out var subEl))
                {
                    var sub = subEl.ValueKind == JsonValueKind.String ? subEl.GetString() : subEl.GetRawText();
                    if (int.TryParse(sub, out var parsedId)) return parsedId;
                }
            }
        }
        catch { }
        return null;
    }

    public static string ExtractToken(string authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader)) return "";
        var idx = authorizationHeader.LastIndexOf(' ');
        return idx >= 0 ? authorizationHeader[(idx + 1)..] : authorizationHeader;
    }
}
