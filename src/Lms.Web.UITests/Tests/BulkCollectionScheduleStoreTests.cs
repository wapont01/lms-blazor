using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class BulkCollectionScheduleStoreTests
{
    [Fact]
    public void FirstCollectionWaitsForConfiguredLocalTimeInsteadOfStartingImmediately()
    {
        var root = Path.Combine(Path.GetTempPath(), "bulk-schedule-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BulkCollectionScheduleStore(Path.Combine(root, "schedules.json"));
            var now = DateTime.UtcNow;
            var schedule = store.GetOrCreate("Colorado", true, 7, null);

            Assert.Equal("America/Denver", schedule.TimeZoneId);
            Assert.True(schedule.NextRunAtUtc > now);
            Assert.True(schedule.NextRunAtUtc < now.AddHours(26));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScheduleSettingsPersistAndAdvanceAfterScheduledCompletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "bulk-schedule-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "schedules.json");
        try
        {
            var store = new BulkCollectionScheduleStore(path);
            var saved = store.Save("Colorado", true, 2, "Weeks", "04:30", "UTC");
            Assert.True(saved.NextRunAtUtc > DateTime.UtcNow);

            var restarted = new BulkCollectionScheduleStore(path);
            var loaded = restarted.GetOrCreate("Colorado", false, 1, null);
            Assert.Equal(saved, loaded);

            var completed = loaded.NextRunAtUtc!.Value;
            var advanced = restarted.MarkCompleted(loaded, completed);
            Assert.Equal(completed.AddDays(14), advanced.NextRunAtUtc);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManualCompletionDoesNotPushAnAlreadyFutureScheduleBack()
    {
        var root = Path.Combine(Path.GetTempPath(), "bulk-schedule-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "schedules.json");
        try
        {
            var store = new BulkCollectionScheduleStore(path);
            var saved = store.Save("Colorado", true, 1, "Months", "04:30", "UTC");
            var completed = store.MarkCompleted(saved, DateTime.UtcNow);
            Assert.Equal(saved.NextRunAtUtc, completed.NextRunAtUtc);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "Days", "04:30", "UTC")]
    [InlineData(1, "Hours", "04:30", "UTC")]
    [InlineData(1, "Days", "4:30", "UTC")]
    [InlineData(1, "Days", "04:30", "Unknown/Zone")]
    public void InvalidScheduleSettingsAreRejected(int interval, string unit, string time, string zone)
    {
        var root = Path.Combine(Path.GetTempPath(), "bulk-schedule-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BulkCollectionScheduleStore(Path.Combine(root, "schedules.json"));
            Assert.ThrowsAny<ArgumentException>(() => store.Save("Colorado", true, interval, unit, time, zone));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
