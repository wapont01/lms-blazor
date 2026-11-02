using Lms.Application.Services;
using Xunit;

namespace Lms.Application.Tests.Services;

public class RosterSearchProtectionTests
{
    [Fact]
    public void RejectsThirdConcurrentSearchWithoutQueueAndReleasesOnce()
    {
        var guard = new RosterSearchProtection();
        var first = guard.Enter(new object());
        using var second = guard.Enter(new object());
        Assert.Throws<RosterRequestException>(() => guard.Enter(new object()));
        first.Dispose();
        first.Dispose();
        using var replacement = guard.Enter(new object());
        Assert.Throws<RosterRequestException>(() => guard.Enter(new object()));
    }

    [Fact]
    public void VisitorBudgetResetsAfterOneMinute()
    {
        var clock = new Clock();
        var guard = new RosterSearchProtection(clock);
        var visitor = new object();
        for (var i = 0; i < 8; i++) guard.Enter(visitor).Dispose();
        Assert.Throws<RosterRequestException>(() => guard.Enter(visitor));
        clock.Now += TimeSpan.FromSeconds(59);
        Assert.Throws<RosterRequestException>(() => guard.Enter(visitor));
        clock.Now += TimeSpan.FromSeconds(1);
        guard.Enter(visitor).Dispose();
    }

    [Fact]
    public void NewVisitorsCannotBypassGlobalMinuteBudget()
    {
        var guard = new RosterSearchProtection();
        for (var i = 0; i < 60; i++) guard.Enter(new object()).Dispose();
        Assert.Throws<RosterRequestException>(() => guard.Enter(new object()));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
