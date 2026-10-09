using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Interfaces;
using OmegaCloud.Application.DTOs.Admin;
using OmegaCloud.Domain.Entities;

namespace OmegaCloud.Application.Services;

public class AuditService : IAuditService
{
    private readonly IAppDbContext _db;

    public AuditService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        string action,
        string resource,
        string? resourceId = null,
        Guid? userId = null,
        string? username = null,
        string? ipAddress = null,
        object? details = null,
        CancellationToken ct = default)
    {
        string? detailsJson = null;
        if (details != null)
        {
            try
            {
                detailsJson = details is string str ? str : JsonSerializer.Serialize(details);
            }
            catch
            {
                detailsJson = details.ToString();
            }
        }

        var entry = new AuditLog
        {
            Action = action,
            Resource = resource,
            ResourceId = resourceId,
            UserId = userId,
            UserName = username,
            IpAddress = ipAddress ?? "unknown",
            DetailsJson = detailsJson,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetLogsAsync(
        Guid? userId = null,
        string? action = null,
        string? search = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(action) && !action.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.Action.ToLower() == action.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(a =>
                (a.UserName != null && a.UserName.ToLower().Contains(searchLower)) ||
                a.Resource.ToLower().Contains(searchLower) ||
                (a.ResourceId != null && a.ResourceId.ToLower().Contains(searchLower)) ||
                a.IpAddress.ToLower().Contains(searchLower) ||
                (a.DetailsJson != null && a.DetailsJson.ToLower().Contains(searchLower)));
        }

        var logs = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 500))
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                UserId = a.UserId,
                UserName = a.UserName,
                Action = a.Action,
                Resource = a.Resource,
                ResourceId = a.ResourceId,
                IpAddress = a.IpAddress,
                DetailsJson = a.DetailsJson,
                Timestamp = a.Timestamp
            })
            .ToListAsync(ct);

        return logs;
    }
}
