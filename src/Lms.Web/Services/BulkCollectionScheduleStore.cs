using System.Text.Json;

namespace Lms.Web.Services;

public sealed record BulkCollectionSchedule(
    string State,
    bool Enabled,
    int Interval,
    string Unit,
    string LocalTime,
    string TimeZoneId,
    int AnchorDayOfMonth,
    DateTime? NextRunAtUtc);

public sealed class BulkCollectionScheduleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly IReadOnlyDictionary<string, string> StateTimeZones =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["California"] = "America/Los_Angeles",
            ["Colorado"] = "America/Denver",
            ["Nevada"] = "America/Los_Angeles",
            ["New York"] = "America/New_York",
            ["Arizona"] = "America/Phoenix",
            ["Connecticut"] = "America/New_York",
            ["Oregon"] = "America/Los_Angeles",
            ["Delaware"] = "America/New_York",
            ["Illinois"] = "America/Chicago",
            ["Virginia"] = "America/New_York",
            ["Florida"] = "America/New_York",
            ["Texas"] = "America/Chicago"
        };

    private readonly string filePath;
    private readonly object sync = new();
    private Dictionary<string, BulkCollectionSchedule>? schedules;

    public BulkCollectionScheduleStore(IWebHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, "App_Data", "bulk-collection-schedules.json"))
    {
    }

    public BulkCollectionScheduleStore(string filePath)
    {
        this.filePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(this.filePath)!);
    }

    public BulkCollectionSchedule GetOrCreate(string state, bool enabled, int intervalDays,
        DateTime? lastSuccessUtc)
    {
        lock (sync)
        {
            var all = Read();
            if (all.TryGetValue(state, out var saved)) return saved;

            var zoneId = StateTimeZones.TryGetValue(state, out var knownZone) ? knownZone : "UTC";
            var zone = FindTimeZone(zoneId);
            var anchorDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
            var nextRun = lastSuccessUtc is null
                ? NextDailySlot(anchorDate, "02:00", zone)
                : AddInterval(TimeZoneInfo.ConvertTimeFromUtc(lastSuccessUtc.Value, zone), intervalDays, "Days",
                    anchorDate.Day, "02:00", zone);
            var schedule = new BulkCollectionSchedule(state, enabled, intervalDays, "Days", "02:00",
                zoneId, anchorDate.Day, nextRun);
            all.Add(state, schedule);
            Write(all);
            return schedule;
        }
    }

    public BulkCollectionSchedule Save(string state, bool enabled, int interval, string unit,
        string localTime, string timeZoneId)
    {
        if (interval is < 1 or > 365) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be between 1 and 365.");
        if (unit is not ("Days" or "Weeks" or "Months"))
            throw new ArgumentException("Schedule unit must be Days, Weeks, or Months.", nameof(unit));
        if (!TimeOnly.TryParseExact(localTime, "HH:mm", out var time))
            throw new ArgumentException("Schedule time must use 24-hour HH:mm format.", nameof(localTime));
        var zone = FindTimeZone(timeZoneId);
        var nowUtc = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var anchorDay = localNow.Day;
        var next = AddInterval(localNow, interval, unit, anchorDay, time.ToString("HH:mm"), zone);
        while (next <= localNow)
            next = AddInterval(next, interval, unit, anchorDay, time.ToString("HH:mm"), zone);

        lock (sync)
        {
            var all = Read();
            var schedule = new BulkCollectionSchedule(state, enabled, interval, unit,
                time.ToString("HH:mm"), zone.Id, anchorDay, TimeZoneInfo.ConvertTimeToUtc(next, zone));
            all[state] = schedule;
            Write(all);
            return schedule;
        }
    }

    public BulkCollectionSchedule MarkCompleted(BulkCollectionSchedule schedule, DateTime completedAtUtc)
    {
        lock (sync)
        {
            var all = Read();
            var current = all.TryGetValue(schedule.State, out var saved) ? saved : schedule;
            var zone = FindTimeZone(current.TimeZoneId);
            var next = current.NextRunAtUtc is { } currentNext
                ? TimeZoneInfo.ConvertTimeFromUtc(currentNext, zone)
                : TimeZoneInfo.ConvertTimeFromUtc(completedAtUtc, zone);
            if (TimeZoneInfo.ConvertTimeToUtc(next, zone) > completedAtUtc)
                return current;
            do
            {
                next = AddInterval(next, current.Interval, current.Unit, current.AnchorDayOfMonth,
                    current.LocalTime, zone);
            } while (TimeZoneInfo.ConvertTimeToUtc(next, zone) <= completedAtUtc);

            var updated = current with { NextRunAtUtc = TimeZoneInfo.ConvertTimeToUtc(next, zone) };
            all[current.State] = updated;
            Write(all);
            return updated;
        }
    }

    private Dictionary<string, BulkCollectionSchedule> Read()
    {
        if (schedules is not null) return schedules;
        if (!File.Exists(filePath))
            return schedules = new(StringComparer.OrdinalIgnoreCase);
        schedules = JsonSerializer.Deserialize<Dictionary<string, BulkCollectionSchedule>>(
            File.ReadAllText(filePath), JsonOptions)
            ?? throw new InvalidDataException("Bulk collection schedule file is empty.");
        return schedules;
    }

    private void Write(Dictionary<string, BulkCollectionSchedule> all)
    {
        var nextPath = filePath + ".next";
        File.WriteAllText(nextPath, JsonSerializer.Serialize(all, JsonOptions));
        File.Move(nextPath, filePath, true);
        schedules = all;
    }

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException($"Unknown time zone '{id}'. Use a valid IANA or Windows time-zone ID.", nameof(id), exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException($"Time zone '{id}' is invalid on this server.", nameof(id), exception);
        }
    }

    private static DateTime AddInterval(DateTime fromLocal, int interval, string unit, int anchorDay,
        string localTime, TimeZoneInfo zone)
    {
        var time = TimeOnly.ParseExact(localTime, "HH:mm");
        var date = unit switch
        {
            "Days" => fromLocal.AddDays(interval).Date,
            "Weeks" => fromLocal.AddDays(interval * 7).Date,
            "Months" => AddMonthsPreservingDay(fromLocal, interval, anchorDay).Date,
            _ => throw new InvalidDataException($"Unsupported bulk schedule unit: {unit}.")
        };
        var local = date.Add(time.ToTimeSpan());
        if (zone.IsInvalidTime(local))
            local = date.AddHours(time.Hour).AddMinutes(time.Minute).AddHours(1);
        return local;
    }

    private static DateTime AddMonthsPreservingDay(DateTime fromLocal, int interval, int anchorDay)
    {
        var month = fromLocal.AddMonths(interval);
        return new DateTime(month.Year, month.Month, Math.Min(anchorDay, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    private static DateTime NextDailySlot(DateTime localNow, string localTime, TimeZoneInfo zone)
    {
        var time = TimeOnly.ParseExact(localTime, "HH:mm");
        var next = localNow.Date.Add(time.ToTimeSpan());
        if (next <= localNow) next = next.AddDays(1);
        if (zone.IsInvalidTime(next)) next = next.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(next, zone);
    }
}
