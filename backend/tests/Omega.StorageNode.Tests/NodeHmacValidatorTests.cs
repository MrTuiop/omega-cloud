using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Omega.StorageNode.Services;

namespace Omega.StorageNode.Tests;

public class NodeHmacValidatorTests
{
    private class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    }

    private readonly TestDateTimeProvider _timeProvider;
    private readonly NodeHmacValidator _validator;

    public NodeHmacValidatorTests()
    {
        _timeProvider = new TestDateTimeProvider();
        _validator = new NodeHmacValidator(_timeProvider, NullLogger<NodeHmacValidator>.Instance);
    }

    [Fact]
    public void ComputeHmacSha256_ProducesValidLowercaseHex()
    {
        var secret = "super_secret_key_123";
        var data = "test_payload_data";

        var hmac = _validator.ComputeHmacSha256(data, secret);

        Assert.NotNull(hmac);
        Assert.Equal(64, hmac.Length);
        Assert.Equal(hmac.ToLowerInvariant(), hmac);
    }

    [Fact]
    public void VerifyHmacSha256_ValidSignature_ReturnsTrue()
    {
        var secret = "super_secret_key_123";
        var data = "test_payload_data";
        var signature = _validator.ComputeHmacSha256(data, secret);

        Assert.True(_validator.VerifyHmacSha256(data, secret, signature));
    }

    [Fact]
    public void VerifyHmacSha256_InvalidOrTampered_ReturnsFalse()
    {
        var secret = "super_secret_key_123";
        var data = "test_payload_data";
        var signature = _validator.ComputeHmacSha256(data, secret);

        Assert.False(_validator.VerifyHmacSha256(data, "wrong_secret", signature));
        Assert.False(_validator.VerifyHmacSha256("tampered_data", secret, signature));
        Assert.False(_validator.VerifyHmacSha256(data, secret, signature[..^2] + "00"));
        Assert.False(_validator.VerifyHmacSha256(data, secret, ""));
    }

    [Fact]
    public void ValidateDirectDownloadSignature_ValidToken_ReturnsTrue()
    {
        var blobId = Guid.NewGuid();
        var secret = "node_secret_key_abc";
        var expiresUnix = new DateTimeOffset(_timeProvider.UtcNow.AddMinutes(10)).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"download:{blobId}:{expiresUnix}", secret);

        var result = _validator.ValidateDirectDownloadSignature(blobId, expiresUnix, signature, secret);

        Assert.True(result);
    }

    [Fact]
    public void ValidateDirectDownloadSignature_ExpiredToken_ReturnsFalse()
    {
        var blobId = Guid.NewGuid();
        var secret = "node_secret_key_abc";
        var expiredUnix = new DateTimeOffset(_timeProvider.UtcNow.AddMinutes(-1)).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"download:{blobId}:{expiredUnix}", secret);

        var result = _validator.ValidateDirectDownloadSignature(blobId, expiredUnix, signature, secret);

        Assert.False(result);
    }

    [Fact]
    public void ValidateDirectUploadChunkSignature_ValidToken_ReturnsTrue()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 2;
        var secret = "node_secret_key_abc";
        var expiresUnix = new DateTimeOffset(_timeProvider.UtcNow.AddMinutes(15)).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"upload:{sessionId}:{chunkIndex}:{expiresUnix}", secret);

        var result = _validator.ValidateDirectUploadChunkSignature(sessionId, chunkIndex, expiresUnix, signature, secret);

        Assert.True(result);
    }

    [Fact]
    public void ValidateDirectUploadChunkSignature_WrongChunkIndex_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var chunkIndex = 2;
        var secret = "node_secret_key_abc";
        var expiresUnix = new DateTimeOffset(_timeProvider.UtcNow.AddMinutes(15)).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"upload:{sessionId}:{chunkIndex}:{expiresUnix}", secret);

        var result = _validator.ValidateDirectUploadChunkSignature(sessionId, 3, expiresUnix, signature, secret);

        Assert.False(result);
    }

    [Fact]
    public void ValidateMasterRequest_BearerToken_ReturnsTrue()
    {
        var secret = "secret_key_123";
        var context = new DefaultHttpContext();
        context.Request.Headers["Authorization"] = $"Bearer {secret}";

        var result = _validator.ValidateMasterRequest(context, Guid.NewGuid(), secret);

        Assert.True(result);
    }

    [Fact]
    public void ValidateMasterRequest_XNodeSecretHeader_ReturnsTrue()
    {
        var secret = "secret_key_123";
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Node-Secret"] = secret;

        var result = _validator.ValidateMasterRequest(context, Guid.NewGuid(), secret);

        Assert.True(result);
    }

    [Fact]
    public void ValidateMasterRequest_HmacHeaders_ValidTimestamp_ReturnsTrue()
    {
        var nodeId = Guid.NewGuid();
        var secret = "secret_key_123";
        var unixTimestamp = new DateTimeOffset(_timeProvider.UtcNow).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"{nodeId}:{unixTimestamp}", secret);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Node-Id"] = nodeId.ToString();
        context.Request.Headers["X-Node-Timestamp"] = unixTimestamp.ToString();
        context.Request.Headers["X-Node-Signature"] = signature;

        var result = _validator.ValidateMasterRequest(context, nodeId, secret);

        Assert.True(result);
    }

    [Fact]
    public void ValidateMasterRequest_ClockSkewExceeded_ReturnsFalse()
    {
        var nodeId = Guid.NewGuid();
        var secret = "secret_key_123";
        var staleTimestamp = new DateTimeOffset(_timeProvider.UtcNow.AddMinutes(-10)).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"{nodeId}:{staleTimestamp}", secret);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Node-Id"] = nodeId.ToString();
        context.Request.Headers["X-Node-Timestamp"] = staleTimestamp.ToString();
        context.Request.Headers["X-Node-Signature"] = signature;

        var result = _validator.ValidateMasterRequest(context, nodeId, secret);

        Assert.False(result);
    }

    [Fact]
    public void ValidateMasterRequest_MismatchedNodeId_ReturnsFalse()
    {
        var nodeId = Guid.NewGuid();
        var otherNodeId = Guid.NewGuid();
        var secret = "secret_key_123";
        var unixTimestamp = new DateTimeOffset(_timeProvider.UtcNow).ToUnixTimeSeconds();
        var signature = _validator.ComputeHmacSha256($"{nodeId}:{unixTimestamp}", secret);

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Node-Id"] = otherNodeId.ToString();
        context.Request.Headers["X-Node-Timestamp"] = unixTimestamp.ToString();
        context.Request.Headers["X-Node-Signature"] = signature;

        var result = _validator.ValidateMasterRequest(context, nodeId, secret);

        Assert.False(result);
    }
}
