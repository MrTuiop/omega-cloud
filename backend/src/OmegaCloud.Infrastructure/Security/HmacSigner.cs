using System.Security.Cryptography;
using System.Text;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Infrastructure.Security;

public class HmacSigner : IHmacSigner
{
    private readonly IDateTimeProvider _dateTimeProvider;

    public HmacSigner(IDateTimeProvider dateTimeProvider)
    {
        _dateTimeProvider = dateTimeProvider;
    }

    public string ComputeHmacSha256(string data, string secretKey)
    {
        if (string.IsNullOrEmpty(data)) data = string.Empty;
        if (string.IsNullOrEmpty(secretKey)) secretKey = string.Empty;

        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var dataBytes = Encoding.UTF8.GetBytes(data);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool VerifyHmacSha256(string data, string secretKey, string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(secretKey))
        {
            return false;
        }

        var expectedSignature = ComputeHmacSha256(data, secretKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());
        var providedBytes = Encoding.UTF8.GetBytes(providedSignature.Trim().ToLowerInvariant());

        if (expectedBytes.Length != providedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    public string GenerateDirectDownloadUrl(string baseUrl, Guid blobId, TimeSpan validFor, string secretKey)
    {
        var expiresUnix = _dateTimeProvider.UtcNow.Add(validFor);
        var expiresSeconds = new DateTimeOffset(expiresUnix).ToUnixTimeSeconds();
        var data = $"download:{blobId}:{expiresSeconds}";
        var signature = ComputeHmacSha256(data, secretKey);

        var trimmedBase = baseUrl.TrimEnd('/');
        return $"{trimmedBase}/api/v1/download/{blobId}?expires={expiresSeconds}&signature={signature}";
    }

    public string GenerateDirectUploadChunkUrl(string baseUrl, Guid sessionId, int chunkIndex, TimeSpan validFor, string secretKey)
    {
        var expiresUnix = _dateTimeProvider.UtcNow.Add(validFor);
        var expiresSeconds = new DateTimeOffset(expiresUnix).ToUnixTimeSeconds();
        var data = $"upload:{sessionId}:{chunkIndex}:{expiresSeconds}";
        var signature = ComputeHmacSha256(data, secretKey);

        var trimmedBase = baseUrl.TrimEnd('/');
        return $"{trimmedBase}/api/v1/upload/{sessionId}/chunks/{chunkIndex}?expires={expiresSeconds}&signature={signature}";
    }

    public bool ValidateDirectUrlSignature(string action, string resourceId, long expiresUnix, string signature, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return false;
        }

        var currentUnix = new DateTimeOffset(_dateTimeProvider.UtcNow).ToUnixTimeSeconds();
        if (currentUnix > expiresUnix)
        {
            return false;
        }

        var data = $"{action}:{resourceId}:{expiresUnix}";
        return VerifyHmacSha256(data, secretKey, signature);
    }

    public bool ValidateDirectDownloadSignature(Guid blobId, long expiresUnix, string signature, string secretKey)
    {
        return ValidateDirectUrlSignature("download", blobId.ToString(), expiresUnix, signature, secretKey);
    }

    public bool ValidateDirectUploadChunkSignature(Guid sessionId, int chunkIndex, long expiresUnix, string signature, string secretKey)
    {
        return ValidateDirectUrlSignature("upload", $"{sessionId}:{chunkIndex}", expiresUnix, signature, secretKey);
    }
}
