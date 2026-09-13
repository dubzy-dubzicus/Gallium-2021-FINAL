namespace Gallium2021.Classes;

public static class ServerConfig
{
    public const string ClientId = "recroom";
    public const string ClientSecret = "VxZ53kgbbEaRoZAeMe00MagtgD12GLL2";
    public const string BuildKey = "20210723";
    public const string AppVersion = "9.6.0";

    public static string ServerBaseUrl = Environment.GetEnvironmentVariable("SERVER_BASE_URL") ?? "https://ns.recuni.org";
    public static string ShareBaseUrl => ServerBaseUrl + "/{0}";

    public const int MockPlayerId = 12345678;
    public const string MockUsername = "poime";
    public static readonly string MockSessionId = Guid.NewGuid().ToString();
    public const int MockRoomId = 87654321;

    public static readonly HashSet<string> ValidGrantTypes = new() { "platform_auth", "cachedlogin", "password", "refresh_token" };

    public const string ContentSignatureHeader =
        "key-id=KEY:RSA:p1.rec.net; " +
        "data=IWwe/pZ5vWWqNSkSM/54isgDxlZkdrP0sUrppKCbNktO2yCOTjq746xWiiLsueGuVcAGQqkjeRTimxolHckS/" +
        "YXSYkEJxtiCXbLlsRia2DyAqtWVkGWsfczzFhp/56U66FVzolTspPCvjScOVlGO7dDIK7sJ+ndcRauWjsQsC6g3e7rUc6uwY099a6gy7sw6xr5BFZQSz8wg+fqyHYD/" +
        "Sc4nQQVOTFZNNASqbJYhpNhEMXRnafCMuLl8a3mkGwvy3t4q2D/7SM48xrGZjEV47qNx1A91KCe28XVToFh4BzwEUU8nZ0d+KwV79MGarLo1cY8igc8FcoThKcovI4ClOg==";

    public const int ImageUploadCooldownSeconds = 20;

    public static readonly HashSet<string> ProfileImageNames = new() { "DefaultImgCOLOR", "DefaultProfileImage" };
}
