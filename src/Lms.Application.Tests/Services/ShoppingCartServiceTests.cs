using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class ShoppingCartServiceTests
{
    [Fact]
    public async Task UpdateItemQuantityAsync_UpdatesCartTotalAndPersistedQuantity()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = new ShoppingCartService(dbContext);

        var learnerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        dbContext.UserAccounts.Add(new UserAccount
        {
            Id = learnerId,
            Email = "learner@example.com",
            DisplayName = "Learner",
            PasswordHash = "hash",
            Role = "Learner",
            CreatedAt = DateTime.UtcNow
        });

        dbContext.Courses.Add(new Course
        {
            Id = courseId,
            Title = "Course",
            Description = "Description",
            Price = 25.50m,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        await service.AddToCartAsync(learnerId, courseId, "Course", 25.50m);

        await service.UpdateItemQuantityAsync(learnerId, courseId, 3);

        var cart = await service.GetCartAsync(learnerId);
        var item = Assert.Single(cart.Items);

        Assert.Equal(3, item.Quantity);
        Assert.Equal(76.50m, cart.GetTotal());

        await connection.DisposeAsync();
    }
}
