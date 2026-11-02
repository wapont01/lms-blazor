using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface ICourseActivityService
{
    Task<Guid> StartAsync(Guid userId, Guid courseId);
    Task PulseAsync(Guid sessionId, Guid userId);
    Task EndAsync(Guid sessionId, Guid userId);
    Task<int> GetCreditedMinutesAsync(Guid userId, Guid courseId);
}

public sealed class CourseActivityService : ICourseActivityService
{
    private static readonly TimeSpan MaximumHeartbeatCredit = TimeSpan.FromMinutes(2);
    private readonly ApplicationDbContext _dbContext;

    public CourseActivityService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid> StartAsync(Guid userId, Guid courseId)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var enrolled = await _dbContext.Enrollments.AnyAsync(enrollment => enrollment.UserAccountId == userId && enrollment.CourseId == courseId);
        if (!enrolled)
        {
            throw new InvalidOperationException("An active enrollment is required to record instructional time.");
        }

        var nowUtc = DateTime.UtcNow;
        var existing = await _dbContext.CourseActivitySessions
            .Where(session => session.UserAccountId == userId && session.CourseId == courseId && session.EndedAtUtc == null)
            .OrderByDescending(session => session.StartedAtUtc)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            await _dbContext.Entry(existing).ReloadAsync();
            existing.LastActivityAtUtc = nowUtc;
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            return existing.Id;
        }

        var session = new CourseActivitySession
        {
            UserAccountId = userId,
            CourseId = courseId,
            StartedAtUtc = nowUtc,
            LastActivityAtUtc = nowUtc
        };
        _dbContext.CourseActivitySessions.Add(session);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return session.Id;
    }

    public async Task PulseAsync(Guid sessionId, Guid userId)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var session = await _dbContext.CourseActivitySessions
            .FirstOrDefaultAsync(existing => existing.Id == sessionId && existing.UserAccountId == userId && existing.EndedAtUtc == null);
        if (session is null)
        {
            throw new InvalidOperationException("This attendance session is no longer active. Refresh the lesson to resume attendance tracking.");
        }

        await _dbContext.Entry(session).ReloadAsync();
        CreditElapsedActivity(session, DateTime.UtcNow);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task EndAsync(Guid sessionId, Guid userId)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var session = await _dbContext.CourseActivitySessions
            .FirstOrDefaultAsync(existing => existing.Id == sessionId && existing.UserAccountId == userId && existing.EndedAtUtc == null);
        if (session is null)
        {
            return;
        }

        await _dbContext.Entry(session).ReloadAsync();
        var nowUtc = DateTime.UtcNow;
        CreditElapsedActivity(session, nowUtc);
        session.EndedAtUtc = nowUtc;
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<int> GetCreditedMinutesAsync(Guid userId, Guid courseId)
    {
        return await _dbContext.CourseActivitySessions
            .AsNoTracking()
            .Where(session => session.UserAccountId == userId && session.CourseId == courseId)
            .SumAsync(session => session.CreditedMinutes);
    }

    private static void CreditElapsedActivity(CourseActivitySession session, DateTime nowUtc)
    {
        var elapsed = nowUtc - session.LastActivityAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var credited = elapsed > MaximumHeartbeatCredit ? MaximumHeartbeatCredit : elapsed;
        session.CreditedMinutes += (int)Math.Floor(credited.TotalMinutes);
        session.LastActivityAtUtc = nowUtc;
    }
}
