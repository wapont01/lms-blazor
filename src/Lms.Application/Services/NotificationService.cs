using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface INotificationService
{
    Task<List<SystemNotification>> GetForUserAsync(Guid userId, bool unreadOnly = false, int take = 50);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task MarkAsReadAsync(Guid userId, Guid notificationId);
    Task<int> MarkAllAsReadAsync(Guid userId);
    Task CreateAsync(Guid recipientUserId, string category, string title, string message);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _dbContext;

    public NotificationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<SystemNotification>> GetForUserAsync(Guid userId, bool unreadOnly = false, int take = 50)
    {
        var query = _dbContext.SystemNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId);

        if (unreadOnly)
        {
            query = query.Where(notification => notification.ReadAt == null);
        }

        return await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(Math.Clamp(take, 1, 250))
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId)
    {
        return await _dbContext.SystemNotifications
            .AsNoTracking()
            .CountAsync(notification => notification.RecipientUserId == userId && notification.ReadAt == null);
    }

    public async Task MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await _dbContext.SystemNotifications
            .FirstOrDefaultAsync(existing => existing.Id == notificationId && existing.RecipientUserId == userId);

        if (notification is null || notification.ReadAt.HasValue)
        {
            return;
        }

        notification.ReadAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId)
    {
        var unreadNotifications = await _dbContext.SystemNotifications
            .Where(notification => notification.RecipientUserId == userId && notification.ReadAt == null)
            .ToListAsync();

        if (unreadNotifications.Count == 0)
        {
            return 0;
        }

        var readAt = DateTime.UtcNow;
        foreach (var notification in unreadNotifications)
        {
            notification.ReadAt = readAt;
        }

        await _dbContext.SaveChangesAsync();
        return unreadNotifications.Count;
    }

    public async Task CreateAsync(Guid recipientUserId, string category, string title, string message)
    {
        _dbContext.SystemNotifications.Add(new SystemNotification
        {
            RecipientUserId = recipientUserId,
            Category = category.Trim(),
            Title = title.Trim(),
            Message = message.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }
}
