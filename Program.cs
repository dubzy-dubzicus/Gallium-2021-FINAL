using System.Text.Json;
using System.Text.Json.Serialization;
using LiteDB;
using Mocha2021.Classes;
using Mocha2021.Hub;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:3670");

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.DictionaryKeyPolicy = null;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var dataDir = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "images"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "videos"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "uploads"));

var liteDb = new LiteDatabase(Path.Combine(dataDir, "recuni2021.db")) { UtcDate = true };
builder.Services.AddSingleton(liteDb);
builder.Services.AddSingleton<PlayerDB>();
builder.Services.AddSingleton<RoomDB>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<RoomInstanceManager>();
builder.Services.AddSingleton<HubState>();
builder.Services.AddSingleton<HubWebSocketHandler>();
builder.Services.AddSingleton<ServerState>();
builder.Services.AddSingleton<CommerceStore>();
builder.Services.AddSingleton<ObjectiveStateStore>();
builder.Services.AddSingleton<TestCaseStore>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    Console.WriteLine($"[req] {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
    await next();
});

var adminUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME") ?? "admin";
var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
if (string.IsNullOrEmpty(adminPassword))
    Console.WriteLine("[admin] WARNING: ADMIN_PASSWORD env var is not set. The admin panel will refuse all requests until it is set.");

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/admin"))
    {
        await next();
        return;
    }
    if (string.IsNullOrEmpty(adminPassword))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("Admin panel disabled");
        return;
    }
    var header = context.Request.Headers.Authorization.ToString();
    var valid = false;
    if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var idx = decoded.IndexOf(':');
            if (idx >= 0)
            {
                var user = decoded[..idx];
                var pass = decoded[(idx + 1)..];
                valid = user == adminUsername && pass == adminPassword;
            }
        }
        catch { }
    }
    if (!valid)
    {
        context.Response.StatusCode = 401;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Admin Panel\"";
        await context.Response.WriteAsync("Authentication required.");
        return;
    }
    await next();
});

app.UseWebSockets();

app.Use(async (context, next) =>
{
    if ((context.Request.Path == "/hub/v1" || context.Request.Path == "/Notifications/hub/v1") &&
        context.WebSockets.IsWebSocketRequest)
    {
        var handler = context.RequestServices.GetRequiredService<HubWebSocketHandler>();
        await handler.HandleAsync(context);
        return;
    }
    await next();
});

app.MapControllers();

app.Run();
