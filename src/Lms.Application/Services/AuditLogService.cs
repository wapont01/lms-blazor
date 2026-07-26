using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IAuditLogService
{
    Task WriteAsync(Guid? actorUserId, string? actorEmail, string action, string targetType, Guid? targetId, string? details = null);
    Task<List<AuditLog>> GetRecentAsync(int take = 30);
    Task<List<AuditLog>> GetFilteredAsync(string? actorContains, string? actionContains, DateTime? fromUtc, DateTime? toUtc, int take = 200);
}

public class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _dbContext;

    public AuditLogService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(Guid? actorUserId, string? actorEmail, string action, string targetType, Guid? targetId, string? details = null)
    {
        var normalizedActorEmail = string.IsNullOrWhiteSpace(actorEmail)
            ? "system@lms.local"
            : actorEmail.Trim().ToLowerInvariant();

        _dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            ActorEmail = normalizedActorEmail,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> GetRecentAsync(int take = 30)
    {
        var safeTake = Math.Clamp(take, 1, 200);

        return await _dbContext.AuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedAt)
            .Take(safeTake)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetFilteredAsync(string? actorContains, string? actionContains, DateTime? fromUtc, DateTime? toUtc, int take = 200)
    {
        var safeTake = Math.Clamp(take, 1, 1000);
        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(actorContains))
        {
            var actor = actorContains.Trim().ToLowerInvariant();
            query = query.Where(log => log.ActorEmail.ToLower().Contains(actor));
        }

        if (!string.IsNullOrWhiteSpace(actionContains))
        {
            var action = actionContains.Trim().ToLowerInvariant();
            query = query.Where(log => log.Action.ToLower().Contains(action));
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(log => log.CreatedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(log => log.CreatedAt <= toUtc.Value);
        }

        return await query
            .OrderByDescending(log => log.CreatedAt)
            .Take(safeTake)
            .ToListAsync();
    }
}
