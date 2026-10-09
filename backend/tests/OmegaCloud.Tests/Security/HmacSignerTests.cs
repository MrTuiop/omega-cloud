using OmegaCloud.Infrastructure.Security;
using OmegaCloud.Infrastructure.Services;

namespace OmegaCloud.Tests.Security;

public class HmacSignerTests
{
    private readonly HmacSigner _signer;
    private readonly DateTimeProvider _dateTimeProvider;

    public HmacSignerTests()
    {
        _dateTimeProvider = new DateTimeProvider();
        _signer = new HmacSigner(_dateTimeProvider);
    }

    [Fact]
    public void ComputeHmacSha256_ReturnsConsistentHash()
    {
        var secret = "super_node_secret_key_123";
        var data = "download:blob-uuid-123:1700000000";

        var hash1 = _signer.ComputeHmacSha256(data, secret);
        var hash2 = _signer.ComputeHmacSha256(data, secret);

        Assert.NotNull(hash1);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void VerifyHmacSha256_ValidSignature_ReturnsTrue()
    {
        var secret = "my_secret_token";
        var data = "node_id:123456789";

        var signature = _signer.ComputeHmacSha256(data, secret);
        var isValid = _signer.VerifyHmacSha256(data, secret, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifyHmacSha256_InvalidSecretOrData_ReturnsFalse()
    {
        var secret = "my_secret_token";
        var signature = _signer.ComputeHmacSha256("original_data", secret);

        Assert.False(_signer.VerifyHmacSha256("tampered_data", secret, signature));
        Assert.False(_signer.VerifyHmacSha256("original_data", "wrong_secret", signature));
        Assert.False(_signer.VerifyHmacSha256("original_data", secret, "invalid_sig"));
        Assert.False(_signer.VerifyHmacSha256("original_data", secret, ""));
    }

    [Fact]
    public void GenerateDirectDownloadUrl_GeneratesValidUrlWithSignature()
    {
        var baseUrl = "https://node.nas.internal:8443";
        var blobId = Guid.NewGuid();
        var secret = "nas_secret_key";
        var validFor = TimeSpan.FromMinutes(15);

        var url = _signer.GenerateDirectDownloadUrl(baseUrl, blobId, validFor, secret);

        Assert.StartsWith($"{baseUrl}/api/v1/download/{blobId}?", url);
        Assert.Contains("expires=", url);
        Assert.Contains("signature=", url);
    }

    [Fact]
    public void GenerateDirectUploadChunkUrl_GeneratesValidUrlWithSignature()
    {
        var baseUrl = "https://node.nas.internal:8443";
        var sessionId = Guid.NewGuid();
        var chunkIndex = 3;
        var secret = "nas_secret_key";
        var validFor = TimeSpan.FromMinutes(30);

        var url = _signer.GenerateDirectUploadChunkUrl(baseUrl, sessionId, chunkIndex, validFor, secret);

        Assert.StartsWith($"{baseUrl}/api/v1/upload/{sessionId}/chunks/{chunkIndex}?", url);
        Assert.Contains("expires=", url);
        Assert.Contains("signature=", url);
    }

    [Fact]
    public void ValidateDirectUrlSignature_ValidAndExpiredTests()
    {
        var secret = "nas_secret_key";
        var blobId = Guid.NewGuid().ToString();
        var futureUnix = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var pastUnix = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();

        var validSig = _signer.ComputeHmacSha256($"download:{blobId}:{futureUnix}", secret);
        var expiredSig = _signer.ComputeHmacSha256($"download:{blobId}:{pastUnix}", secret);

        Assert.True(_signer.ValidateDirectUrlSignature("download", blobId, futureUnix, validSig, secret));
        Assert.False(_signer.ValidateDirectUrlSignature("download", blobId, pastUnix, expiredSig, secret));
        Assert.False(_signer.ValidateDirectUrlSignature("upload", blobId, futureUnix, validSig, secret)); // Wrong action
    }

    [Fact]
    public void VerifyHmacSha256_EmptyOrWhitespaceSecret_ReturnsFalse()
    {
        var data = "some_data";
        var signature = _signer.ComputeHmacSha256(data, "");

        Assert.False(_signer.VerifyHmacSha256(data, "", signature));
        Assert.False(_signer.VerifyHmacSha256(data, "   ", signature));
        Assert.False(_signer.ValidateDirectUrlSignature("download", "res", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), signature, ""));
    }

    [Fact]
    public void ValidateDirectDownloadAndUploadChunkSignature_WorkCorrectly()
    {
        var secret = "node_secret_key";
        var blobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var chunkIndex = 2;
        var expiresUnix = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();

        var dlSig = _signer.ComputeHmacSha256($"download:{blobId}:{expiresUnix}", secret);
        var upSig = _signer.ComputeHmacSha256($"upload:{sessionId}:{chunkIndex}:{expiresUnix}", secret);

        Assert.True(_signer.ValidateDirectDownloadSignature(blobId, expiresUnix, dlSig, secret));
        Assert.True(_signer.ValidateDirectUploadChunkSignature(sessionId, chunkIndex, expiresUnix, upSig, secret));
        Assert.False(_signer.ValidateDirectDownloadSignature(blobId, expiresUnix, "invalid", secret));
    }
}
