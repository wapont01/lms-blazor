using Lms.Domain.Entities;
using Lms.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IShoppingCartService
{
    Task<ShoppingCart> GetCartAsync(Guid learnerId);
    Task AddToCartAsync(Guid learnerId, Guid courseId, string courseTitle, decimal price);
    Task UpdateItemQuantityAsync(Guid learnerId, Guid courseId, int quantity);
    Task RemoveFromCartAsync(Guid learnerId, Guid courseId);
    Task ClearCartAsync(Guid learnerId);
    Task<ShoppingCart> GetCartWithCoursesAsync(Guid learnerId);
}

public class ShoppingCartService : IShoppingCartService
{
    private readonly ApplicationDbContext _dbContext;

    public ShoppingCartService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ShoppingCart> GetCartAsync(Guid learnerId)
    {
        var carts = await _dbContext.ShoppingCarts
            .Include(c => c.Items)
            .Where(c => c.LearnerId == learnerId)
            .OrderByDescending(c => c.LastModifiedAt ?? c.CreatedAt)
            .ToListAsync();

        var cart = carts.FirstOrDefault();

        if (cart == null)
        {
            cart = new ShoppingCart { LearnerId = learnerId };
            _dbContext.ShoppingCarts.Add(cart);
            await _dbContext.SaveChangesAsync();
            return cart;
        }

        if (carts.Count == 1)
        {
            return cart;
        }

        // Self-heal legacy/duplicate carts by merging into the most recently modified cart.
        foreach (var duplicate in carts.Skip(1))
        {
            foreach (var duplicateItem in duplicate.Items)
            {
                var existingItem = cart.Items.FirstOrDefault(item => item.CourseId == duplicateItem.CourseId);
                if (existingItem != null)
                {
                    existingItem.Quantity += Math.Max(1, duplicateItem.Quantity);
                    existingItem.Price = duplicateItem.Price;
                    existingItem.CourseTitle = duplicateItem.CourseTitle;
                }
                else
                {
                    cart.Items.Add(new CartItem
                    {
                        CourseId = duplicateItem.CourseId,
                        CourseTitle = duplicateItem.CourseTitle,
                        Price = duplicateItem.Price,
                        Quantity = Math.Max(1, duplicateItem.Quantity),
                        AddedAt = duplicateItem.AddedAt
                    });
                }
            }

            _dbContext.ShoppingCarts.Remove(duplicate);
        }

        cart.LastModifiedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return cart;
    }

    public async Task AddToCartAsync(Guid learnerId, Guid courseId, string courseTitle, decimal price)
    {
        var cart = await GetCartAsync(learnerId);

        var existingItem = cart.Items.FirstOrDefault(i => i.CourseId == courseId);
        if (existingItem != null)
        {
            existingItem.Quantity++;
        }
        else
        {
            cart.Items.Add(new CartItem
            {
                CourseId = courseId,
                CourseTitle = courseTitle,
                Price = price,
                Quantity = 1
            });
        }

        cart.LastModifiedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateItemQuantityAsync(Guid learnerId, Guid courseId, int quantity)
    {
        var cart = await GetCartAsync(learnerId);
        var item = cart.Items.FirstOrDefault(i => i.CourseId == courseId);

        if (item == null)
        {
            return;
        }

        item.Quantity = Math.Clamp(quantity, 1, 100);
        cart.LastModifiedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveFromCartAsync(Guid learnerId, Guid courseId)
    {
        var cart = await GetCartAsync(learnerId);
        var item = cart.Items.FirstOrDefault(i => i.CourseId == courseId);

        if (item != null)
        {
            cart.Items.Remove(item);
            cart.LastModifiedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task ClearCartAsync(Guid learnerId)
    {
        var cart = await GetCartAsync(learnerId);
        cart.Items.Clear();
        cart.LastModifiedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<ShoppingCart> GetCartWithCoursesAsync(Guid learnerId)
    {
        var cart = await GetCartAsync(learnerId);
        
        if (!cart.Items.Any())
            return cart;

        // Hydrate course details for display
        var courseIds = cart.Items.Select(i => i.CourseId).ToList();
        var courses = await _dbContext.Courses
            .Where(c => courseIds.Contains(c.Id))
            .ToListAsync();

        foreach (var item in cart.Items)
        {
            var course = courses.FirstOrDefault(c => c.Id == item.CourseId);
            if (course != null)
            {
                item.CourseTitle = course.Title;
                item.Price = course.Price;
            }
        }

        return cart;
    }
}
