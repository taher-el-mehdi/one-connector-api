namespace DocuWareSageConnector.Application.Synchronization;

public readonly record struct RecurrenceWeekdays(
    bool Mondays,
    bool Tuesdays,
    bool Wednesdays,
    bool Thursdays,
    bool Fridays,
    bool Saturdays,
    bool Sundays)
{
    public bool Any => Mondays || Tuesdays || Wednesdays || Thursdays || Fridays || Saturdays || Sundays;

    public bool Includes(DayOfWeek day) =>
        day switch
        {
            DayOfWeek.Monday => Mondays,
            DayOfWeek.Tuesday => Tuesdays,
            DayOfWeek.Wednesday => Wednesdays,
            DayOfWeek.Thursday => Thursdays,
            DayOfWeek.Friday => Fridays,
            DayOfWeek.Saturday => Saturdays,
            _ => Sundays
        };
}

public static class RecurrenceSchedule
{
    public static NormalizedRecurrence Normalize(
        bool? enabled,
        string? type,
        RecurrenceWeekdays weekdays,
        string? time,
        int? intervalValue,
        string? intervalUnit,
        string? timezone,
        DateTime utcNow)
    {
        if (enabled is not true)
        {
            return new NormalizedRecurrence(false, null, default, null, null, null, null, null);
        }

        var frequency = type?.Trim().ToUpperInvariant() ?? string.Empty;
        if (frequency is not ("DAILY" or "WEEKLY" or "INTERVAL"))
        {
            throw new ArgumentException("Choose a daily, weekly, or interval schedule.");
        }

        var zoneId = timezone?.Trim() ?? string.Empty;
        if (zoneId.Length is 0 or > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
        {
            throw new ArgumentException("Choose a timezone.");
        }

        TimeSpan? clock = null;
        int? every = null;
        string? unit = null;
        if (frequency is "DAILY" or "WEEKLY")
        {
            clock = ParseTime(time);
            if (frequency == "WEEKLY" && !weekdays.Any)
            {
                throw new ArgumentException("Choose at least one weekday.");
            }

            if (frequency == "DAILY")
            {
                weekdays = default;
            }
        }
        else
        {
            every = intervalValue ?? 0;
            unit = intervalUnit?.Trim().ToUpperInvariant() ?? string.Empty;
            if (every is < 1 or > 10_000)
            {
                throw new ArgumentException("Interval must be from 1 to 10000.");
            }

            if (unit is not ("MINUTE" or "HOUR" or "DAY"))
            {
                throw new ArgumentException("Choose minutes, hours, or days.");
            }
        }

        var settings = new NormalizedRecurrence(true, frequency, weekdays, clock, every, unit, zoneId, null);
        var next = NextUtc(settings, utcNow, zone);
        return settings with { NextRunAt = next };
    }

    public static DateTime NextUtc(NormalizedRecurrence settings, DateTime utcNow, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone ?? "UTC");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var localNext = settings.Type switch
        {
            "DAILY" => NextAtClock(localNow, settings.TimeOfDay!.Value, default),
            "WEEKLY" => NextAtClock(localNow, settings.TimeOfDay!.Value, settings.Weekdays),
            "INTERVAL" => NextInterval(localNow, settings.IntervalValue!.Value, settings.IntervalUnit!, settings.Weekdays),
            _ => localNow.AddMinutes(1)
        };
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localNext, DateTimeKind.Unspecified), zone);
    }

    private static DateTime NextAtClock(DateTime localNow, TimeSpan clock, RecurrenceWeekdays days)
    {
        for (var offset = 0; offset < 8; offset++)
        {
            var day = localNow.Date.AddDays(offset);
            if (days.Any && !days.Includes(day.DayOfWeek))
            {
                continue;
            }

            var candidate = day.Add(clock);
            if (candidate > localNow)
            {
                return candidate;
            }
        }

        return localNow.Date.AddDays(1).Add(clock);
    }

    private static DateTime NextInterval(DateTime localNow, int value, string unit, RecurrenceWeekdays days)
    {
        if (unit == "DAY")
        {
            var cursor = localNow.AddDays(value);
            for (var step = 0; step < 14; step++)
            {
                if (!days.Any || days.Includes(cursor.DayOfWeek))
                {
                    return cursor;
                }

                cursor = cursor.AddDays(1);
            }

            return cursor;
        }

        var span = unit == "HOUR" ? TimeSpan.FromHours(value) : TimeSpan.FromMinutes(value);
        var next = localNow.Add(span);
        if (!days.Any || days.Includes(next.DayOfWeek))
        {
            return next;
        }

        for (var offset = 1; offset <= 7; offset++)
        {
            var day = localNow.Date.AddDays(offset);
            if (days.Includes(day.DayOfWeek))
            {
                return day;
            }
        }

        return next;
    }

    private static TimeSpan ParseTime(string? time)
    {
        if (!TimeSpan.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, out var clock)
            || clock < TimeSpan.Zero
            || clock >= TimeSpan.FromDays(1))
        {
            throw new ArgumentException("Choose a time.");
        }

        return clock;
    }
}

public sealed record NormalizedRecurrence(
    bool Enabled,
    string? Type,
    RecurrenceWeekdays Weekdays,
    TimeSpan? TimeOfDay,
    int? IntervalValue,
    string? IntervalUnit,
    string? Timezone,
    DateTime? NextRunAt);
