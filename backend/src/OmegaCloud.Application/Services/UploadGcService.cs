using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Upload;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class UploadGcService : IUploadGcService
{
    private readonly IAppDbContext _dbContext;
    private readonly IStorageDriverResolver _driverResolver;

    public UploadGcService(
        IAppDbContext dbContext,
        IStorageDriverResolver driverResolver)
    {
        _dbContext = dbContext;
        _driverResolver = driverResolver;
    }

    public async Task<UploadGcResultDto> CleanupExpiredSessionsAsync(TimeSpan? removeSessionsOlderThan = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 1. Find all uncompleted sessions that have reached expiration or were marked expired without temp cleanup
        var expiredSessions = await _dbContext.UploadSessions
            .Include(s => s.StorageNode)
            .Include(s => s.Chunks)
            .Where(s => s.Status != UploadSessionStatus.Completed &&
                        (s.ExpiresAt <= now || s.Status == UploadSessionStatus.Expired))
            .ToListAsync(ct);

        int newlyExpiredCount = 0;
        int cleanedTempFoldersCount = 0;

        foreach (var session in expiredSessions)
        {
            if (session.Status != UploadSessionStatus.Expired && session.Status != UploadSessionStatus.Aborted)
            {
                session.Status = UploadSessionStatus.Expired;
                newlyExpiredCount++;
            }

            try
            {
                if (session.StorageNode != null)
                {
                    var driver = _driverResolver.GetDriverForNode(session.StorageNode);
                    await driver.DeleteSessionTempDataAsync(session, ct);
                    cleanedTempFoldersCount++;
                }
            }
            catch
            {
                // Best-effort temp files cleanup per session
            }
        }

        // 2. Remove very old expired or aborted sessions from database
        var purgeThreshold = removeSessionsOlderThan ?? TimeSpan.FromDays(7);
        var purgeCutoff = now - purgeThreshold;

        var deadSessionsToRemove = await _dbContext.UploadSessions
            .Include(s => s.StorageNode)
            .Include(s => s.Chunks)
            .Where(s => (s.Status == UploadSessionStatus.Expired || s.Status == UploadSessionStatus.Aborted) &&
                        s.ExpiresAt <= purgeCutoff)
            .ToListAsync(ct);

        foreach (var dead in deadSessionsToRemove)
        {
            try
            {
                if (dead.StorageNode != null)
                {
                    var driver = _driverResolver.GetDriverForNode(dead.StorageNode);
                    await driver.DeleteSessionTempDataAsync(dead, ct);
                }
            }
            catch
            {
                // Best-effort temp cleanup
            }
        }

        int removedOldCount = deadSessionsToRemove.Count;
        if (removedOldCount > 0)
        {
            _dbContext.UploadSessions.RemoveRange(deadSessionsToRemove);
        }

        await _dbContext.SaveChangesAsync(ct);

        return new UploadGcResultDto
        {
            ExpiredSessionsCount = newlyExpiredCount,
            CleanedTempFoldersCount = cleanedTempFoldersCount,
            RemovedOldSessionsCount = removedOldCount
        };
    }
}
