namespace DocuWareSageConnector.Application.Synchronization;

public static class RecurrenceSchedule
{
    public static readonly string[] Weekdays =
    [
        "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY"
    ];

    public static NormalizedRecurrence Normalize(
        bool? enabled,
        string? type,
        string? days,
        string? time,
        int? intervalValue,
        string? intervalUnit,
        string? timezone,
        DateTime utcNow)
    {
        if (enabled is not true)
        {
            return new NormalizedRecurrence(false, null, null, null, null, null, null, null);
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

        var selectedDays = ParseDays(days);
        TimeSpan? clock = null;
        int? every = null;
        string? unit = null;
        if (frequency is "DAILY" or "WEEKLY")
        {
            clock = ParseTime(time);
            if (frequency == "WEEKLY" && selectedDays.Count == 0)
            {
                throw new ArgumentException("Choose at least one weekday.");
            }

            if (frequency == "DAILY")
            {
                selectedDays = [];
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

        var storedDays = selectedDays.Count == 0 ? null : string.Join(",", selectedDays);
        var settings = new NormalizedRecurrence(true, frequency, storedDays, clock, every, unit, zoneId, null);
        var next = NextUtc(settings, utcNow, zone);
        return settings with { NextRunAt = next };
    }

    public static DateTime NextUtc(NormalizedRecurrence settings, DateTime utcNow, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone ?? "UTC");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var days = ParseDays(settings.Days);
        var localNext = settings.Type switch
        {
            "DAILY" => NextAtClock(localNow, settings.TimeOfDay!.Value, null),
            "WEEKLY" => NextAtClock(localNow, settings.TimeOfDay!.Value, days),
            "INTERVAL" => NextInterval(localNow, settings.IntervalValue!.Value, settings.IntervalUnit!, days),
            _ => localNow.AddMinutes(1)
        };
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localNext, DateTimeKind.Unspecified), zone);
    }

    private static DateTime NextAtClock(DateTime localNow, TimeSpan clock, IReadOnlySet<string>? days)
    {
        for (var offset = 0; offset < 8; offset++)
        {
            var day = localNow.Date.AddDays(offset);
            if (days is { Count: > 0 } && !days.Contains(Weekday(day)))
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

    private static DateTime NextInterval(DateTime localNow, int value, string unit, IReadOnlySet<string> days)
    {
        if (unit == "DAY")
        {
            var cursor = localNow.AddDays(value);
            for (var step = 0; step < 14; step++)
            {
                if (days.Count == 0 || days.Contains(Weekday(cursor)))
                {
                    return cursor;
                }

                cursor = cursor.AddDays(1);
            }

            return cursor;
        }

        var span = unit == "HOUR" ? TimeSpan.FromHours(value) : TimeSpan.FromMinutes(value);
        var next = localNow.Add(span);
        if (days.Count == 0 || days.Contains(Weekday(next)))
        {
            return next;
        }

        for (var offset = 1; offset <= 7; offset++)
        {
            var day = localNow.Date.AddDays(offset);
            if (days.Contains(Weekday(day)))
            {
                return day;
            }
        }

        return next;
    }

    private static HashSet<string> ParseDays(string? days)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(days))
        {
            return selected;
        }

        foreach (var part in days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var day = part.ToUpperInvariant();
            if (!Weekdays.Contains(day, StringComparer.Ordinal))
            {
                throw new ArgumentException("Weekdays must be Monday through Sunday.");
            }

            selected.Add(day);
        }

        return selected;
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

    private static string Weekday(DateTime value) =>
        value.DayOfWeek switch
        {
            DayOfWeek.Monday => "MONDAY",
            DayOfWeek.Tuesday => "TUESDAY",
            DayOfWeek.Wednesday => "WEDNESDAY",
            DayOfWeek.Thursday => "THURSDAY",
            DayOfWeek.Friday => "FRIDAY",
            DayOfWeek.Saturday => "SATURDAY",
            _ => "SUNDAY"
        };
}

public sealed record NormalizedRecurrence(
    bool Enabled,
    string? Type,
    string? Days,
    TimeSpan? TimeOfDay,
    int? IntervalValue,
    string? IntervalUnit,
    string? Timezone,
    DateTime? NextRunAt);
