using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Domain.Enums;

namespace OmegaCloud.Application.Services;

public class SystemHealthService : ISystemHealthService
{
    private readonly IAppDbContext _db;
    private readonly IConfiguration _config;
    private static readonly DateTime ProcessStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static DateTime _lastCpuSampleTime = DateTime.UtcNow;
    private static TimeSpan _lastCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
    private static readonly object _cpuLock = new();

    public SystemHealthService(IAppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<SystemHealthDto> GetSystemHealthAsync(CancellationToken ct = default)
    {
        var proc = Process.GetCurrentProcess();

        // Memory Metrics
        var usedMemory = proc.WorkingSet64;
        long totalMemory = 0;
        try
        {
            var memInfo = GC.GetGCMemoryInfo();
            totalMemory = memInfo.TotalAvailableMemoryBytes;
        }
        catch
        {
            // fallback if not available
        }
        if (totalMemory <= 0)
        {
            totalMemory = 16L * 1024 * 1024 * 1024; // 16 GB fallback estimation
        }
        var memPercentage = totalMemory > 0 ? Math.Round((double)usedMemory / totalMemory * 100, 1) : 0;

        // CPU approximation with delta sampling
        double cpuPercentage = 0;
        try
        {
            lock (_cpuLock)
            {
                var nowUtc = DateTime.UtcNow;
                var currentCpuUsage = proc.TotalProcessorTime;
                var elapsedMs = (nowUtc - _lastCpuSampleTime).TotalMilliseconds;
                if (elapsedMs >= 500)
                {
                    var usedMs = (currentCpuUsage - _lastCpuUsage).TotalMilliseconds;
                    var rawPercent = (usedMs / (Environment.ProcessorCount * elapsedMs)) * 100.0;
                    cpuPercentage = Math.Clamp(Math.Round(rawPercent, 1), 0.1, 99.9);
                    _lastCpuSampleTime = nowUtc;
                    _lastCpuUsage = currentCpuUsage;
                }
                else
                {
                    var uptimeMs = (nowUtc - ProcessStartTime).TotalMilliseconds;
                    if (uptimeMs > 1000)
                    {
                        var rawPercent = (currentCpuUsage.TotalMilliseconds / (Environment.ProcessorCount * uptimeMs)) * 100.0;
                        cpuPercentage = Math.Clamp(Math.Round(rawPercent, 1), 0.1, 99.9);
                    }
                    else
                    {
                        cpuPercentage = 1.0;
                    }
                }
            }
        }
        catch
        {
            cpuPercentage = 5.0;
        }

        // Disk / Storage Metrics
        var storagePath = _config["Storage:LocalRootPath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "storage");
        long storageTotal = 100L * 1024 * 1024 * 1024;
        long storageFree = 80L * 1024 * 1024 * 1024;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(storagePath));
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    storageTotal = drive.TotalSize;
                    storageFree = drive.AvailableFreeSpace;
                }
            }
        }
        catch
        {
            // DriveInfo fallback
        }
        var storageUsed = Math.Max(0, storageTotal - storageFree);
        var storagePercentage = storageTotal > 0 ? Math.Round((double)storageUsed / storageTotal * 100, 1) : 0;

        // Database connection test & latency
        var sw = Stopwatch.StartNew();
        var isDbHealthy = false;
        try
        {
            isDbHealthy = await _db.Database.CanConnectAsync(ct);
        }
        catch
        {
            isDbHealthy = false;
        }
        sw.Stop();
        var dbLatency = (int)sw.ElapsedMilliseconds;

        // Nodes summary
        var nodes = await _db.StorageNodes.AsNoTracking().ToListAsync(ct);
        var totalNodes = nodes.Count;
        var onlineNodes = nodes.Count(n => n.Status == StorageNodeStatus.Online);
        var offlineNodes = nodes.Count(n => n.Status == StorageNodeStatus.Offline);

        // Users & active sessions
        var now = DateTime.UtcNow;
        var totalUsers = await _db.Users.CountAsync(ct);
        var activeSessions = await _db.UserSessions.CountAsync(s => s.RevokedAt == null && s.ExpiresAt > now, ct);

        var uptimeSeconds = Math.Max(0, (DateTime.UtcNow - ProcessStartTime).TotalSeconds);

        return new SystemHealthDto
        {
            CpuLoadPercentage = cpuPercentage,
            UsedMemoryBytes = usedMemory,
            TotalMemoryBytes = totalMemory,
            MemoryUsagePercentage = memPercentage,
            StorageFreeBytes = storageFree,
            StorageTotalBytes = storageTotal,
            StorageUsedBytes = storageUsed,
            StorageUsagePercentage = storagePercentage,
            DatabaseStatus = isDbHealthy ? "Healthy" : "Degraded",
            DatabaseLatencyMs = dbLatency,
            TotalNodes = totalNodes,
            OnlineNodes = onlineNodes,
            OfflineNodes = offlineNodes,
            TotalUsers = totalUsers,
            ActiveSessionsCount = activeSessions,
            ServerUptimeSeconds = Math.Round(uptimeSeconds, 0),
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            StartedAt = ProcessStartTime
        };
    }
}
