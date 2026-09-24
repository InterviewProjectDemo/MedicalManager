using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public static class AppointmentReminderSchedule
{
    public static bool IsDue(DateTime startsAt, DateTime now, AppointmentReminderKind kind) =>
        kind switch
        {
            AppointmentReminderKind.DayBefore => IsDayBeforeDue(startsAt, now),
            AppointmentReminderKind.HourBefore => IsHourBeforeDue(startsAt, now),
            _ => false
        };

    /// <summary>
    /// Opens 24 hours before the visit and stays open until 2 hours before,
    /// so a short outage still delivers the day-ahead call and text.
    /// </summary>
    public static bool IsDayBeforeDue(DateTime startsAt, DateTime now)
    {
        var opens = startsAt.AddHours(-24);
        var closes = startsAt.AddHours(-2);
        return now >= opens && now < closes;
    }

    public static void SelfCheck()
    {
        var start = new DateTime(2026, 9, 25, 9, 0, 0);
        if (!IsDayBeforeDue(start, start.AddHours(-24))
            || !IsDayBeforeDue(start, start.AddHours(-3))
            || IsDayBeforeDue(start, start.AddHours(-24).AddMinutes(-1))
            || IsDayBeforeDue(start, start.AddHours(-2)))
        {
            throw new InvalidOperationException("Day-before reminder window is wrong.");
        }

        if (!IsHourBeforeDue(start, start.AddHours(-1))
            || !IsHourBeforeDue(start, start.AddMinutes(-30))
            || IsHourBeforeDue(start, start.AddHours(-1).AddMinutes(-1))
            || IsHourBeforeDue(start, start.AddMinutes(-8)))
        {
            throw new InvalidOperationException("Hour-before reminder window is wrong.");
        }
    }

    /// <summary>
    /// Opens one hour before the visit and stays open until 8 minutes before,
    /// so the call finishes while there is still time to leave.
    /// </summary>
    public static bool IsHourBeforeDue(DateTime startsAt, DateTime now)
    {
        var opens = startsAt.AddHours(-1);
        var closes = startsAt.AddMinutes(-8);
        return now >= opens && now < closes;
    }
}
