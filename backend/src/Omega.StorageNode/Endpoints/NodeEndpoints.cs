using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Omega.StorageNode.Models;
using Omega.StorageNode.Services;

namespace Omega.StorageNode.Endpoints;

public static class NodeEndpoints
{
    public static IEndpointRouteBuilder MapStorageNodeEndpoints(this IEndpointRouteBuilder app)
    {
        // 1. Health checks
        app.MapGet("/health", (INodeStateManager stateManager) =>
        {
            return Results.Ok(new
            {
                status = "Healthy",
                nodeId = stateManager.NodeId,
                isConfigured = stateManager.IsConfigured
            });
        }).AllowAnonymous();

        app.MapGet("/api/v1/health", (INodeStateManager stateManager) =>
        {
            return Results.Ok(new
            {
                status = "Healthy",
                nodeId = stateManager.NodeId,
                isConfigured = stateManager.IsConfigured
            });
        }).AllowAnonymous();

        // 2. Ping / Liveness / Latency from Master
        app.MapGet("/api/v1/ping", (
            HttpContext context,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator) =>
        {
            if (!AuthenticateMaster(context, stateManager, hmacValidator))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new PingResponse
            {
                Status = "Healthy",
                NodeId = stateManager.NodeId ?? Guid.Empty,
                Timestamp = DateTime.UtcNow
            });
        });

        // 3. Blobs: Proxy Stream Read from Master
        app.MapGet("/api/v1/blobs/{blobId:guid}", async (
            Guid blobId,
            HttpContext context,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            CancellationToken ct) =>
        {
            if (!AuthenticateMaster(context, stateManager, hmacValidator))
            {
                return Results.Unauthorized();
            }

            var blob = await storageService.OpenReadBlobAsync(blobId, ct);
            if (blob == null)
            {
                return Results.NotFound(new { error = $"Blob '{blobId}' not found on storage node." });
            }

            return Results.Stream(
                blob.Value.Stream,
                contentType: blob.Value.ContentType,
                fileDownloadName: $"{blobId}.blob",
                enableRangeProcessing: true);
        });

        // 4. Blobs: Direct Stream Write from Master
        app.MapPost("/api/v1/blobs", async (
            HttpContext context,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            [FromHeader(Name = "X-Blob-Id")] string? blobIdHeader,
            [FromHeader(Name = "X-Expected-SHA256")] string? expectedSha256,
            [FromHeader(Name = "X-Expected-Size")] long? expectedSize,
            CancellationToken ct) =>
        {
            if (!AuthenticateMaster(context, stateManager, hmacValidator))
            {
                return Results.Unauthorized();
            }

            Guid blobId = Guid.TryParse(blobIdHeader, out var parsedId) ? parsedId : Guid.NewGuid();

            Stream contentStream;
            long? actualSize = expectedSize;
            string? targetSha256 = expectedSha256;

            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(ct);
                var formFile = form.Files["file"] ?? form.Files["blob"] ?? form.Files.FirstOrDefault();
                if (formFile == null || formFile.Length == 0)
                {
                    return Results.BadRequest(new { error = "No file content provided in form." });
                }
                contentStream = formFile.OpenReadStream();
                actualSize ??= formFile.Length;
                if (string.IsNullOrWhiteSpace(targetSha256) && form.TryGetValue("sha256", out var formSha))
                {
                    targetSha256 = formSha.ToString();
                }
            }
            else
            {
                contentStream = context.Request.Body;
            }

            try
            {
                await using (contentStream as IAsyncDisposable)
                {
                    var result = await storageService.WriteBlobDirectAsync(
                        blobId,
                        contentStream,
                        targetSha256,
                        actualSize,
                        ct);

                    return Results.Created($"/api/v1/blobs/{result.BlobId}", result);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 5. Blobs: Delete Blob from Master
        app.MapDelete("/api/v1/blobs/{blobId:guid}", async (
            Guid blobId,
            HttpContext context,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            CancellationToken ct) =>
        {
            if (!AuthenticateMaster(context, stateManager, hmacValidator))
            {
                return Results.Unauthorized();
            }

            await storageService.DeleteBlobAsync(blobId, ct);
            return Results.NoContent();
        });

        // 6. Upload Chunks: Direct Browser or Proxy from Master
        app.MapPut("/api/v1/upload/{sessionId:guid}/chunks/{chunkIndex:int}", async (
            Guid sessionId,
            int chunkIndex,
            HttpContext context,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            [FromQuery] long? expires,
            [FromQuery] string? signature,
            [FromQuery] string? sha256,
            [FromHeader(Name = "X-Chunk-SHA256")] string? expectedChunkSha256,
            CancellationToken ct) =>
        {
            var secret = stateManager.NodeSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                return Results.Unauthorized();
            }

            bool isAuthenticated = false;

            // Scenario A: Direct browser chunk upload via query parameters
            if (expires.HasValue && !string.IsNullOrWhiteSpace(signature))
            {
                isAuthenticated = hmacValidator.ValidateDirectUploadChunkSignature(
                    sessionId,
                    chunkIndex,
                    expires.Value,
                    signature,
                    secret);
            }
            // Scenario B: Proxy upload via Master inter-service headers
            else
            {
                isAuthenticated = hmacValidator.ValidateMasterRequest(context, stateManager.NodeId, secret);
            }

            if (!isAuthenticated)
            {
                return Results.Unauthorized();
            }

            Stream chunkStream;
            string? effectiveSha256 = !string.IsNullOrWhiteSpace(expectedChunkSha256) ? expectedChunkSha256 : sha256;

            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(ct);
                var formFile = form.Files["chunk"] ?? form.Files["file"] ?? form.Files.FirstOrDefault();
                if (formFile == null || formFile.Length == 0)
                {
                    return Results.BadRequest(new { error = "No chunk content provided in form." });
                }
                chunkStream = formFile.OpenReadStream();
                if (string.IsNullOrWhiteSpace(effectiveSha256) && form.TryGetValue("sha256", out var formSha))
                {
                    effectiveSha256 = formSha.ToString();
                }
                if (string.IsNullOrWhiteSpace(effectiveSha256) && form.TryGetValue("chunkSha256", out var formCSha))
                {
                    effectiveSha256 = formCSha.ToString();
                }
            }
            else
            {
                chunkStream = context.Request.Body;
            }

            try
            {
                await using (chunkStream as IAsyncDisposable)
                {
                    var result = await storageService.WriteChunkAsync(
                        sessionId,
                        chunkIndex,
                        chunkStream,
                        effectiveSha256,
                        ct);

                    return Results.Ok(result);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 7. Upload: Assemble Chunks
        app.MapPost("/api/v1/upload/{sessionId:guid}/assemble", async (
            Guid sessionId,
            HttpContext context,
            [FromBody] AssembleChunksRequest? request,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            CancellationToken ct) =>
        {
            if (!AuthenticateMaster(context, stateManager, hmacValidator))
            {
                return Results.Unauthorized();
            }

            if (request == null || request.TotalChunks <= 0)
            {
                return Results.BadRequest(new { error = "TotalChunks must be greater than 0." });
            }

            try
            {
                var result = await storageService.AssembleChunksAsync(
                    sessionId,
                    request.TotalChunks,
                    request.ExpectedSha256,
                    request.BlobId,
                    ct);

                return Results.Ok(result);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 8. Upload: Abort / Delete Session Temp Data
        app.MapDelete("/api/v1/upload/{sessionId:guid}", async (
            Guid sessionId,
            HttpContext context,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            CancellationToken ct) =>
        {
            if (!AuthenticateMaster(context, stateManager, hmacValidator))
            {
                return Results.Unauthorized();
            }

            await storageService.DeleteSessionTempDataAsync(sessionId, ct);
            return Results.NoContent();
        });

        // 9. Direct Download: Direct browser download via Pre-signed token
        app.MapGet("/api/v1/download/{blobId:guid}", async (
            Guid blobId,
            [FromQuery] long? expires,
            [FromQuery] string? signature,
            [FromQuery] string? filename,
            [FromQuery] string? contentType,
            INodeStateManager stateManager,
            INodeHmacValidator hmacValidator,
            IStorageService storageService,
            CancellationToken ct) =>
        {
            var secret = stateManager.NodeSecret;
            if (string.IsNullOrWhiteSpace(secret) || !expires.HasValue || string.IsNullOrWhiteSpace(signature))
            {
                return Results.Unauthorized();
            }

            var isValid = hmacValidator.ValidateDirectDownloadSignature(
                blobId,
                expires.Value,
                signature,
                secret);

            if (!isValid)
            {
                return Results.Unauthorized();
            }

            var blob = await storageService.OpenReadBlobAsync(blobId, ct);
            if (blob == null)
            {
                return Results.NotFound(new { error = $"Blob '{blobId}' not found." });
            }

            var downloadName = !string.IsNullOrWhiteSpace(filename) ? filename : $"{blobId}.blob";
            var effectiveContentType = !string.IsNullOrWhiteSpace(contentType)
                ? contentType
                : ResolveContentType(downloadName);

            return Results.Stream(
                blob.Value.Stream,
                contentType: effectiveContentType,
                fileDownloadName: downloadName,
                enableRangeProcessing: true);
        });

        return app;
    }

    private static string ResolveContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".zip" => "application/zip",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".mp3" => "audio/mpeg",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
    }

    private static bool AuthenticateMaster(
        HttpContext context,
        INodeStateManager stateManager,
        INodeHmacValidator hmacValidator)
    {
        var secret = stateManager.NodeSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        return hmacValidator.ValidateMasterRequest(context, stateManager.NodeId, secret);
    }
}
