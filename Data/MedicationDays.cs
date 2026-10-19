namespace MedicalManager.Data;

[Flags]
public enum MedicationDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}

public static class MedicationDayRules
{
    public static readonly IReadOnlyList<MedicationDays> All =
    [
        MedicationDays.Sunday,
        MedicationDays.Monday,
        MedicationDays.Tuesday,
        MedicationDays.Wednesday,
        MedicationDays.Thursday,
        MedicationDays.Friday,
        MedicationDays.Saturday
    ];

    public static void SelfCheck()
    {
        var weekdays = MedicationDays.Monday | MedicationDays.Tuesday | MedicationDays.Wednesday
            | MedicationDays.Thursday | MedicationDays.Friday;
        if (!Includes(weekdays, new DateOnly(2026, 10, 14))
            || Includes(weekdays, new DateOnly(2026, 10, 18))
            || Format(MedicationDays.All) != "Every day"
            || Format(weekdays) != "Weekdays")
        {
            throw new InvalidOperationException("Medication day selection is wrong.");
        }
    }

    public static bool Includes(MedicationDays days, DateOnly date)
    {
        if (days == MedicationDays.None)
            return false;

        var flag = (MedicationDays)(1 << (int)date.DayOfWeek);
        return days.HasFlag(flag);
    }

    public static string Label(MedicationDays day) => day switch
    {
        MedicationDays.Sunday => "Sun",
        MedicationDays.Monday => "Mon",
        MedicationDays.Tuesday => "Tue",
        MedicationDays.Wednesday => "Wed",
        MedicationDays.Thursday => "Thu",
        MedicationDays.Friday => "Fri",
        MedicationDays.Saturday => "Sat",
        _ => day.ToString()
    };

    public static string Format(MedicationDays days)
    {
        if (days == MedicationDays.None)
            return "No days selected";
        if (days == MedicationDays.All)
            return "Every day";

        const MedicationDays weekdays = MedicationDays.Monday | MedicationDays.Tuesday | MedicationDays.Wednesday
            | MedicationDays.Thursday | MedicationDays.Friday;
        const MedicationDays weekend = MedicationDays.Saturday | MedicationDays.Sunday;
        if (days == weekdays)
            return "Weekdays";
        if (days == weekend)
            return "Weekends";

        return string.Join(", ", All.Where(day => days.HasFlag(day)).Select(Label));
    }

    public static string? FormatSchedule(MedicationDays days, IEnumerable<MedicationSchedule> schedules)
    {
        var parts = schedules
            .OrderBy(schedule => schedule.TimeSlot)
            .ThenBy(schedule => schedule.AdministerAt)
            .Select(schedule =>
            {
                var name = MedicationScheduleRules.GetDisplayName(schedule.TimeSlot);
                return schedule.AdministerAt is TimeOnly time
                    ? $"{name} {time:h:mm tt}"
                    : name;
            })
            .ToList();
        if (parts.Count == 0)
            return null;

        return $"{string.Join(", ", parts)} · {Format(days)}";
    }
}
