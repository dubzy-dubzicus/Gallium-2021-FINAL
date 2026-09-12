using Microsoft.AspNetCore.Mvc;
using Mocha2021.Classes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace Mocha2021.Controllers;

[Controller]
public class Media : ControllerBase
{
    private readonly PlayerDB _playerDb;
    private readonly SessionManager _sessions;
    private readonly ServerState _state;
    private readonly IWebHostEnvironment _env;
    private static readonly Dictionary<string, byte[]> ResizeCache = new();
    private static readonly HashSet<string> ProfileImageNames = new() { "DefaultImgCOLOR", "DefaultProfileImage" };

    public Media(PlayerDB playerDb, SessionManager sessions, ServerState state, IWebHostEnvironment env)
    {
        _playerDb = playerDb;
        _sessions = sessions;
        _state = state;
        _env = env;
    }

    private int CurrentAccountId() => _sessions.CurrentAccountId(Request.Headers.Authorization.ToString());

    private static IActionResult Ok2(Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?> { ["status"] = "ok" };
        if (extra != null) foreach (var kv in extra) d[kv.Key] = kv.Value;
        return new OkObjectResult(d);
    }

    [HttpGet("/img/{**name}")]
    public IActionResult ServeImg(string name)
    {
        var imgDir = Path.Combine(_env.ContentRootPath, "images");
        var baseName = Path.GetFileNameWithoutExtension(name);
        var safeName = Path.GetFileName(baseName);
        var matches = Directory.Exists(imgDir) ? Directory.GetFiles(imgDir, safeName + ".*") : Array.Empty<string>();
        var isProfileCrop = Request.Query["cropSquare"] == "1" && Request.Query["sig"] == "p1";

        if (matches.Length == 0)
        {
            if (ProfileImageNames.Contains(safeName) || isProfileCrop) return NotFound();
            var fallback = Path.Combine(imgDir, "DefaultRoomImage.png");
            if (!System.IO.File.Exists(fallback)) return NotFound();
            matches = new[] { fallback };
        }
        var path = matches[0];

        int? width = int.TryParse(Request.Query["width"], out var w) ? w : null;
        int? height = int.TryParse(Request.Query["height"], out var h) ? h : (isProfileCrop ? width : null);

        if (width == null)
        {
            Response.Headers["content-signature"] = ServerConfig.ContentSignatureHeader;
            return PhysicalFile(path, GetMimeType(path));
        }

        var mtime = System.IO.File.GetLastWriteTimeUtc(path);
        var cacheKey = $"{path}|{mtime.Ticks}|{width}|{height}";
        if (ResizeCache.TryGetValue(cacheKey, out var cached))
        {
            Response.Headers["content-signature"] = ServerConfig.ContentSignatureHeader;
            return File(cached, "image/png");
        }

        using var img = Image.Load<Rgba32>(path);
        if (height.HasValue)
        {
            CenterCropToAspect(img, width.Value, height.Value);
            img.Mutate(x => x.Resize(width.Value, height.Value, KnownResamplers.Lanczos3));
        }
        else
        {
            var ratio = (double)width.Value / img.Width;
            var newHeight = Math.Max(1, (int)Math.Round(img.Height * ratio));
            img.Mutate(x => x.Resize(width.Value, newHeight, KnownResamplers.Lanczos3));
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        var data = ms.ToArray();
        ResizeCache[cacheKey] = data;

        Response.Headers["content-signature"] = ServerConfig.ContentSignatureHeader;
        return File(data, "image/png");
    }

    private static void CenterCropToAspect(Image<Rgba32> img, int targetW, int targetH)
    {
        var srcW = img.Width;
        var srcH = img.Height;
        var targetRatio = (double)targetW / targetH;
        var srcRatio = (double)srcW / srcH;
        if (srcRatio > targetRatio)
        {
            var newW = Math.Max(1, (int)Math.Round(srcH * targetRatio));
            var left = (srcW - newW) / 2;
            img.Mutate(x => x.Crop(new Rectangle(left, 0, newW, srcH)));
        }
        else if (srcRatio < targetRatio)
        {
            var newH = Math.Max(1, (int)Math.Round(srcW / targetRatio));
            var top = (srcH - newH) / 2;
            img.Mutate(x => x.Crop(new Rectangle(0, top, srcW, newH)));
        }
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    [HttpGet("/video/{**name}")]
    public IActionResult ServeVideo(string name)
    {
        var videoDir = Path.Combine(_env.ContentRootPath, "videos");
        var baseName = Path.GetFileNameWithoutExtension(name);
        var safeName = Path.GetFileName(baseName);
        var matches = Directory.Exists(videoDir) ? Directory.GetFiles(videoDir, safeName + ".*") : Array.Empty<string>();
        if (matches.Length == 0) return NotFound();
        Response.Headers["content-signature"] = ServerConfig.ContentSignatureHeader;
        return PhysicalFile(matches[0], "video/mp4", enableRangeProcessing: true);
    }

    [HttpGet("/api/images/v2/named")]
    public IActionResult ImagesNamed() => new OkObjectResult(new List<object>());

    [HttpGet("/api/images/v5/cheered/bulk")]
    public IActionResult ImagesCheeredBulk() => new OkObjectResult(new List<object>());

    [HttpPost("/api/images/v1/cheer")]
    public IActionResult ImageCheer() => Ok2(new() { ["cheered"] = true });

    private static readonly List<Dictionary<string, object?>> SlideshowImages = new()
    {
        new() { ["SavedImageId"] = 20, ["ImageName"] = "slideshow1.jpg", ["Username"] = "Mocha2021", ["PlayerId"] = 1, ["RoomName"] = "RecCenter" },
        new() { ["SavedImageId"] = 21, ["ImageName"] = "slideshow2.jpg", ["Username"] = "Mocha2021", ["PlayerId"] = 1, ["RoomName"] = "RecCenter" },
        new() { ["SavedImageId"] = 22, ["ImageName"] = "slideshow3.jpg", ["Username"] = "Mocha2021", ["PlayerId"] = 1, ["RoomName"] = "RecCenter" },
    };

    [HttpGet("/api/images/v1/slideshow")]
    public IActionResult ImagesSlideshow()
    {
        var validTill = DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-ddTHH:mm:ss.fff") + "0Z";
        return new OkObjectResult(new Dictionary<string, object?> { ["ValidTill"] = validTill, ["Images"] = SlideshowImages });
    }

    [HttpGet("/api/images/v4/room/{roomId:int}")]
    public IActionResult ImagesV4Room(int roomId) => new OkObjectResult(new List<object>());

    [HttpPost("/api/images/v4/uploadsaved")]
    public IActionResult ImagesUploadSaved()
    {
        var accountId = CurrentAccountId();
        var now = DateTime.UtcNow;
        if (_state.LastImageUpload.TryGetValue(accountId, out var last) &&
            (now - last).TotalSeconds < ServerConfig.ImageUploadCooldownSeconds)
        {
            var wait = Math.Round(ServerConfig.ImageUploadCooldownSeconds - (now - last).TotalSeconds, 1);
            return new ObjectResult(new Dictionary<string, object?> { ["error"] = "rate_limited", ["error_description"] = $"Please wait {wait}s before uploading another image." }) { StatusCode = 429 };
        }
        _state.LastImageUpload[accountId] = now;

        var imageName = Guid.NewGuid().ToString();
        var file = Request.Form.Files.GetFile("image");
        if (file != null && file.Length > 0)
        {
            var imgDir = Path.Combine(_env.ContentRootPath, "images");
            Directory.CreateDirectory(imgDir);
            using var stream = System.IO.File.Create(Path.Combine(imgDir, $"{imageName}.png"));
            file.CopyTo(stream);
        }
        return new OkObjectResult(new Dictionary<string, object?> { ["ImageName"] = imageName });
    }

    [HttpPost("/upload")]
    public IActionResult UploadFile()
    {
        var accountId = CurrentAccountId();
        var file = Request.Form.Files.GetFile("File");
        var fileType = Request.Form["FileType"].ToString();

        if (file == null) return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid_request", ["error_description"] = "no file provided" }) { StatusCode = 400 };

        using var ms = new MemoryStream();
        file.CopyTo(ms);
        var fileBytes = ms.ToArray();
        if (fileBytes.Length == 0) return new ObjectResult(new Dictionary<string, object?> { ["error"] = "invalid_request", ["error_description"] = "uploaded file is empty" }) { StatusCode = 400 };

        var fileHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileBytes)).ToLowerInvariant();

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) && !string.IsNullOrEmpty(fileType)) ext = "." + fileType.ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{ext}";

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);
        System.IO.File.WriteAllBytes(Path.Combine(uploadsDir, fileName), fileBytes);

        var ownershipProof = MakeOwnershipProof(accountId, fileHash);
        _playerDb.CreateUpload(accountId, fileName, fileHash, ownershipProof);

        return new OkObjectResult(new Dictionary<string, object?> { ["filename"] = fileName, ["Hash"] = fileHash, ["OwnershipProof"] = ownershipProof });
    }

    private static string MakeOwnershipProof(int accountId, string fileHash)
    {
        var msg = System.Text.Encoding.UTF8.GetBytes($"{accountId}:{fileHash}");
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("secret_key"));
        return Convert.ToHexString(hmac.ComputeHash(msg)).ToLowerInvariant();
    }

    [HttpGet("/room/{blobId}")]
    public IActionResult RoomDataBlob(string blobId)
    {
        var safeId = Path.GetFileName(blobId);
        var baseDir = _env.ContentRootPath;
        var uploadsDir = Path.Combine(baseDir, "uploads");
        foreach (var candidateDir in new[] { baseDir, uploadsDir })
        {
            var path = Path.Combine(candidateDir, safeId);
            if (System.IO.File.Exists(path)) return PhysicalFile(path, "application/octet-stream");
        }
        return NotFound();
    }
}
