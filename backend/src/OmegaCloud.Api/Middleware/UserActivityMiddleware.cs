using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OmegaCloud.Application.Common.Interfaces;

namespace OmegaCloud.Api.Middleware;

public class UserActivityMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastActivityTimes = new();

    public UserActivityMiddleware(RequestDelegate _next)
    {
        this._next = _next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sessionClaim = context.User.FindFirst("session_id")?.Value;
            if (Guid.TryParse(sessionClaim, out var sessionId))
            {
                var now = DateTime.UtcNow;
                if (!_lastActivityTimes.TryGetValue(sessionId, out var lastTime) || (now - lastTime).TotalSeconds >= 1)
                {
                    _lastActivityTimes[sessionId] = now;
                    try
                    {
                        var dbContext = context.RequestServices.GetRequiredService<IAppDbContext>();
                        var session = await dbContext.UserSessions
                            .Include(s => s.User)
                            .FirstOrDefaultAsync(s => s.Id == sessionId && s.RevokedAt == null);

                        if (session != null)
                        {
                            session.LastActiveAt = now;
                            if (session.User != null)
                            {
                                session.User.UpdatedAt = now;
                            }
                            await dbContext.SaveChangesAsync(context.RequestAborted);
                        }
                    }
                    catch
                    {
                        // Best-effort background activity tracking; should never block request processing
                    }
                }
            }
        }

        await _next(context);
    }
}
