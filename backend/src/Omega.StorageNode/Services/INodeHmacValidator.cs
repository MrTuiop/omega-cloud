using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Omega.StorageNode.Services;

public interface INodeHmacValidator
{
    string ComputeHmacSha256(string data, string secretKey);
    bool VerifyHmacSha256(string data, string secretKey, string providedSignature);
    bool ValidateDirectDownloadSignature(Guid blobId, long expiresUnix, string signature, string secretKey);
    bool ValidateDirectUploadChunkSignature(Guid sessionId, int chunkIndex, long expiresUnix, string signature, string secretKey);
    bool ValidateMasterRequest(HttpContext context, Guid? expectedNodeId, string secretKey);
}

public class NodeHmacValidator : INodeHmacValidator
{
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<NodeHmacValidator> _logger;

    public NodeHmacValidator(
        IDateTimeProvider dateTimeProvider,
        ILogger<NodeHmacValidator> logger)
    {
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
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

    public bool ValidateDirectDownloadSignature(Guid blobId, long expiresUnix, string signature, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var currentUnix = new DateTimeOffset(_dateTimeProvider.UtcNow).ToUnixTimeSeconds();
        if (currentUnix > expiresUnix)
        {
            _logger.LogWarning("Direct download URL expired for blob {BlobId}. Current: {Current}, Expires: {Expires}",
                blobId, currentUnix, expiresUnix);
            return false;
        }

        var data = $"download:{blobId}:{expiresUnix}";
        return VerifyHmacSha256(data, secretKey, signature);
    }

    public bool ValidateDirectUploadChunkSignature(Guid sessionId, int chunkIndex, long expiresUnix, string signature, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var currentUnix = new DateTimeOffset(_dateTimeProvider.UtcNow).ToUnixTimeSeconds();
        if (currentUnix > expiresUnix)
        {
            _logger.LogWarning("Direct upload chunk URL expired for session {SessionId}, chunk {ChunkIndex}. Current: {Current}, Expires: {Expires}",
                sessionId, chunkIndex, currentUnix, expiresUnix);
            return false;
        }

        var data = $"upload:{sessionId}:{chunkIndex}:{expiresUnix}";
        return VerifyHmacSha256(data, secretKey, signature);
    }

    public bool ValidateMasterRequest(HttpContext context, Guid? expectedNodeId, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return false;
        }

        // 1. Direct Secret via X-Node-Secret header
        if (context.Request.Headers.TryGetValue("X-Node-Secret", out var nodeSecretHeader) &&
            !string.IsNullOrWhiteSpace(nodeSecretHeader))
        {
            if (FixedTimeEquals(secretKey, nodeSecretHeader.ToString().Trim()))
            {
                return true;
            }
        }

        // 2. Direct Secret via Authorization header (Bearer or Node format)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) &&
            !string.IsNullOrWhiteSpace(authHeader))
        {
            var authStr = authHeader.ToString().Trim();
            if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authStr["Bearer ".Length..].Trim();
                if (FixedTimeEquals(secretKey, token))
                {
                    return true;
                }
            }
            else if (authStr.StartsWith("Node ", StringComparison.OrdinalIgnoreCase))
            {
                var credentials = authStr["Node ".Length..].Trim();
                var parts = credentials.Split(':', 2);
                var token = parts.Length == 2 ? parts[1].Trim() : parts[0].Trim();
                if (FixedTimeEquals(secretKey, token))
                {
                    return true;
                }
            }
        }

        // 3. HMAC-signed Master inter-service headers (X-Node-Timestamp + X-Node-Signature)
        if (context.Request.Headers.TryGetValue("X-Node-Signature", out var sigHeader) &&
            context.Request.Headers.TryGetValue("X-Node-Timestamp", out var timeHeader) &&
            !string.IsNullOrWhiteSpace(sigHeader) &&
            !string.IsNullOrWhiteSpace(timeHeader))
        {
            var timestampStr = timeHeader.ToString().Trim();
            var signatureStr = sigHeader.ToString().Trim();

            DateTimeOffset timestampDt = default;
            bool validTimestamp = false;

            if (long.TryParse(timestampStr, out var unixSeconds))
            {
                timestampDt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                validTimestamp = true;
            }
            else if (DateTimeOffset.TryParse(timestampStr, out var parsedIso))
            {
                timestampDt = parsedIso;
                validTimestamp = true;
            }

            if (!validTimestamp)
            {
                _logger.LogWarning("Rejecting inter-service request: invalid timestamp format '{Timestamp}'", timestampStr);
                return false;
            }

            var diff = Math.Abs((_dateTimeProvider.UtcNow - timestampDt).TotalSeconds);
            if (diff > 300) // 5 minutes max clock skew
            {
                _logger.LogWarning("Rejecting inter-service request: clock skew is {Diff}s (max 300s allowed)", diff);
                return false;
            }

            // Optional check for X-Node-Id match
            if (context.Request.Headers.TryGetValue("X-Node-Id", out var idHeader) &&
                Guid.TryParse(idHeader, out var headerNodeId) &&
                expectedNodeId.HasValue &&
                headerNodeId != expectedNodeId.Value)
            {
                _logger.LogWarning("Rejecting inter-service request: header NodeId {HeaderNodeId} does not match expected {ExpectedNodeId}",
                    headerNodeId, expectedNodeId);
                return false;
            }

            string nodeIdToSign;
            if (expectedNodeId.HasValue)
            {
                nodeIdToSign = expectedNodeId.Value.ToString();
            }
            else if (context.Request.Headers.TryGetValue("X-Node-Id", out var nid) && Guid.TryParse(nid, out var parsedNid))
            {
                nodeIdToSign = parsedNid.ToString();
            }
            else
            {
                nodeIdToSign = string.Empty;
            }

            // Payload "{nodeId}:{unixSeconds}"
            if (long.TryParse(timestampStr, out var uSec))
            {
                var payload1 = $"{nodeIdToSign}:{uSec}";
                if (VerifyHmacSha256(payload1, secretKey, signatureStr))
                {
                    return true;
                }
            }

            // Payload "{nodeId}:{timestampStr}"
            var payload2 = $"{nodeIdToSign}:{timestampStr}";
            if (VerifyHmacSha256(payload2, secretKey, signatureStr))
            {
                return true;
            }

            _logger.LogWarning("Rejecting inter-service request: HMAC signature verification failed");
        }

        return false;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
