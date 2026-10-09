namespace OmegaCloud.Application.Common.Interfaces;

public interface IHmacSigner
{
    string ComputeHmacSha256(string data, string secretKey);
    bool VerifyHmacSha256(string data, string secretKey, string providedSignature);
    string GenerateDirectDownloadUrl(string baseUrl, Guid blobId, TimeSpan validFor, string secretKey);
    string GenerateDirectUploadChunkUrl(string baseUrl, Guid sessionId, int chunkIndex, TimeSpan validFor, string secretKey);
    bool ValidateDirectUrlSignature(string action, string resourceId, long expiresUnix, string signature, string secretKey);
    bool ValidateDirectDownloadSignature(Guid blobId, long expiresUnix, string signature, string secretKey);
    bool ValidateDirectUploadChunkSignature(Guid sessionId, int chunkIndex, long expiresUnix, string signature, string secretKey);
}
