using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OmegaCloud.Application.Common.Interfaces;
using SkiaSharp;

namespace OmegaCloud.Infrastructure.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly string _cacheDirectory;
    private readonly ILogger<ThumbnailService>? _logger;

    public ThumbnailService(ILogger<ThumbnailService>? logger = null)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(AppContext.BaseDirectory, "omega_thumbnails_cache");
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create thumbnail cache directory {CacheDir}", _cacheDirectory);
        }
    }

    public ThumbnailResult? TryGetCachedThumbnail(
        string contentSha256,
        int targetWidth = 256,
        int targetHeight = 256,
        string format = "webp",
        string? mimeType = null)
    {
        targetWidth = Math.Clamp(targetWidth, 16, 2048);
        targetHeight = Math.Clamp(targetHeight, 16, 2048);
        format = format.ToLowerInvariant();
        if (format != "jpeg" && format != "jpg" && format != "png" && format != "webp")
        {
            format = "webp";
        }

        var isImage = !string.IsNullOrEmpty(mimeType) &&
                      mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                      !mimeType.Contains("svg", StringComparison.OrdinalIgnoreCase);

        var cacheExtension = isImage ? format : "svg";
        var cacheFileName = $"{contentSha256}_{targetWidth}x{targetHeight}.{cacheExtension}";
        var cacheFilePath = Path.Combine(_cacheDirectory, cacheFileName);

        if (File.Exists(cacheFilePath))
        {
            try
            {
                var fileStream = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var etag = $"\"{contentSha256[..Math.Min(16, contentSha256.Length)]}-{targetWidth}x{targetHeight}\"";
                var contentType = isImage
                    ? (format is "jpeg" or "jpg" ? "image/jpeg" : format == "png" ? "image/png" : "image/webp")
                    : "image/svg+xml";

                return new ThumbnailResult
                {
                    Stream = fileStream,
                    ContentType = contentType,
                    ETag = etag
                };
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to read cached thumbnail from {CachePath}", cacheFilePath);
            }
        }

        return null;
    }

    public async Task<ThumbnailResult> GetOrCreateThumbnailAsync(
        Stream sourceStream,
        string fileName,
        string mimeType,
        string contentSha256,
        int targetWidth = 256,
        int targetHeight = 256,
        string format = "webp",
        CancellationToken ct = default)
    {
        var cached = TryGetCachedThumbnail(contentSha256, targetWidth, targetHeight, format, mimeType);
        if (cached != null)
        {
            return cached;
        }

        targetWidth = Math.Clamp(targetWidth, 16, 2048);
        targetHeight = Math.Clamp(targetHeight, 16, 2048);
        format = format.ToLowerInvariant();
        if (format != "jpeg" && format != "jpg" && format != "png" && format != "webp")
        {
            format = "webp";
        }

        var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                      !mimeType.Contains("svg", StringComparison.OrdinalIgnoreCase);

        var cacheExtension = isImage ? format : "svg";
        var cacheFileName = $"{contentSha256}_{targetWidth}x{targetHeight}.{cacheExtension}";
        var cacheFilePath = Path.Combine(_cacheDirectory, cacheFileName);

        var etag = $"\"{contentSha256[..Math.Min(16, contentSha256.Length)]}-{targetWidth}x{targetHeight}\"";
        var contentType = isImage
            ? (format is "jpeg" or "jpg" ? "image/jpeg" : format == "png" ? "image/png" : "image/webp")
            : "image/svg+xml";

        byte[] outputBytes;

        // 2. Generate thumbnail
        if (isImage)
        {
            try
            {
                outputBytes = GenerateImageThumbnail(sourceStream, targetWidth, targetHeight, format);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to decode/resize image {FileName}; falling back to SVG thumbnail", fileName);
                outputBytes = GenerateDocumentSvgThumbnail(fileName, mimeType, targetWidth, targetHeight);
                contentType = "image/svg+xml";
            }
        }
        else
        {
            outputBytes = GenerateDocumentSvgThumbnail(fileName, mimeType, targetWidth, targetHeight);
            contentType = "image/svg+xml";
        }

        // 3. Write to cache asynchronously in background
        _ = Task.Run(async () =>
        {
            try
            {
                if (!Directory.Exists(_cacheDirectory))
                {
                    Directory.CreateDirectory(_cacheDirectory);
                }
                var tempPath = Path.Combine(_cacheDirectory, Guid.NewGuid().ToString("N") + ".tmp");
                await File.WriteAllBytesAsync(tempPath, outputBytes);
                if (File.Exists(cacheFilePath))
                {
                    File.Delete(tempPath);
                }
                else
                {
                    File.Move(tempPath, cacheFilePath, overwrite: true);
                }
            }
            catch
            {
                // Cache write failure is non-fatal
            }
        }, CancellationToken.None);

        return new ThumbnailResult
        {
            Stream = new MemoryStream(outputBytes),
            ContentType = contentType,
            ETag = etag
        };
    }

    private static byte[] GenerateImageThumbnail(Stream sourceStream, int targetWidth, int targetHeight, string format)
    {
        if (sourceStream.CanSeek)
        {
            sourceStream.Position = 0;
        }

        using var memStream = new MemoryStream();
        sourceStream.CopyTo(memStream);
        memStream.Position = 0;

        using var codec = SKCodec.Create(memStream);
        if (codec == null)
        {
            throw new InvalidOperationException("Could not create Skia codec for the image stream.");
        }

        var origInfo = codec.Info;
        var ratioX = (double)targetWidth / origInfo.Width;
        var ratioY = (double)targetHeight / origInfo.Height;
        var ratio = Math.Min(ratioX, ratioY);

        var newWidth = Math.Max(1, (int)(origInfo.Width * ratio));
        var newHeight = Math.Max(1, (int)(origInfo.Height * ratio));

        var targetInfo = new SKImageInfo(newWidth, newHeight, origInfo.ColorType, origInfo.AlphaType);
        using var originalBitmap = SKBitmap.Decode(codec);
        if (originalBitmap == null)
        {
            throw new InvalidOperationException("Could not decode image bitmap.");
        }

        using var resizedBitmap = originalBitmap.Resize(targetInfo, SKSamplingOptions.Default);
        if (resizedBitmap == null)
        {
            throw new InvalidOperationException("Could not resize image bitmap.");
        }

        using var image = SKImage.FromBitmap(resizedBitmap);

        var skFormat = format switch
        {
            "jpeg" or "jpg" => SKEncodedImageFormat.Jpeg,
            "png" => SKEncodedImageFormat.Png,
            _ => SKEncodedImageFormat.Webp
        };

        var quality = skFormat == SKEncodedImageFormat.Png ? 100 : 80;
        using var encodedData = image.Encode(skFormat, quality);
        return encodedData.ToArray();
    }

    private static byte[] GenerateDocumentSvgThumbnail(string fileName, string mimeType, int width, int height)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            ext = "FILE";
        }
        else if (ext.Length > 5)
        {
            ext = ext[..5];
        }

        var (bgCol, badgeCol) = GetBadgeColors(ext, mimeType);
        var cleanFileName = System.Security.SecurityElement.Escape(fileName);

        var svg = $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 256 256">
          <defs>
            <linearGradient id="cardGrad" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stop-color="#1e293b"/>
              <stop offset="100%" stop-color="#0f172a"/>
            </linearGradient>
            <linearGradient id="badgeGrad" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stop-color="{badgeCol}"/>
              <stop offset="100%" stop-color="{bgCol}"/>
            </linearGradient>
            <filter id="shadow" x="-10%" y="-10%" width="120%" height="120%">
              <feDropShadow dx="0" dy="4" stdDeviation="6" flood-color="#000" flood-opacity="0.3"/>
            </filter>
            <clipPath id="textClip">
              <rect x="10" y="200" width="236" height="40" rx="4"/>
            </clipPath>
          </defs>
          <rect width="256" height="256" rx="20" fill="url(#cardGrad)"/>
          <g filter="url(#shadow)" transform="translate(68, 40)">
            <path d="M0 8 C0 3.6 3.6 0 8 0 L80 0 L120 40 L120 136 C120 140.4 116.4 144 112 144 L8 144 C3.6 144 0 140.4 0 136 Z" fill="#334155"/>
            <path d="M80 0 L80 32 C80 36.4 83.6 40 88 40 L120 40 Z" fill="#475569"/>
            <rect x="20" y="80" width="80" height="28" rx="6" fill="url(#badgeGrad)"/>
            <text x="60" y="99" font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif" font-size="14" font-weight="bold" fill="#ffffff" text-anchor="middle">{ext}</text>
          </g>
          <text x="128" y="222" font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif" font-size="13" font-weight="500" fill="#94a3b8" text-anchor="middle" width="220" clip-path="url(#textClip)">{cleanFileName}</text>
        </svg>
        """;

        return Encoding.UTF8.GetBytes(svg);
    }

    private static (string bgCol, string badgeCol) GetBadgeColors(string ext, string mimeType)
    {
        return ext switch
        {
            "PDF" => ("#b91c1c", "#ef4444"),
            "DOC" or "DOCX" or "RTF" => ("#1d4ed8", "#3b82f6"),
            "XLS" or "XLSX" or "CSV" => ("#15803d", "#22c55e"),
            "PPT" or "PPTX" => ("#c2410c", "#f97316"),
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => ("#a16207", "#eab308"),
            "MP3" or "WAV" or "OGG" or "FLAC" or "M4A" => ("#7e22ce", "#a855f7"),
            "MP4" or "MKV" or "AVI" or "MOV" or "WEBM" => ("#be185d", "#ec4899"),
            "JS" or "TS" or "JSON" or "CS" or "PY" or "HTML" or "CSS" => ("#0f766e", "#14b8a6"),
            _ => ("#475569", "#64748b")
        };
    }
}
