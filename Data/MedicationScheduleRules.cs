namespace MedicalManager.Data;

/// <summary>
/// Time-window rules for medication slots. A slot auto-marks as Missed once
/// local time passes the window end on the scheduled date.
/// Morning: 06:00–11:00 · Midday: 11:00–15:00 · Evening: 15:00–21:00 · Bedtime: 21:00–23:59
/// </summary>
public static class MedicationScheduleRules
{
    public static TimeOnly GetWindowEnd(MedicationTimeSlot slot) => slot switch
    {
        MedicationTimeSlot.Morning => new(11, 0),
        MedicationTimeSlot.Midday => new(15, 0),
        MedicationTimeSlot.Evening => new(21, 0),
        MedicationTimeSlot.Bedtime => new(23, 59),
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    public static string GetDisplayName(MedicationTimeSlot slot) => slot switch
    {
        MedicationTimeSlot.Morning => "Morning",
        MedicationTimeSlot.Midday => "Lunch",
        MedicationTimeSlot.Evening => "Evening",
        MedicationTimeSlot.Bedtime => "Bedtime",
        _ => slot.ToString()
    };

    public static string GetWindowLabel(MedicationTimeSlot slot) => slot switch
    {
        MedicationTimeSlot.Morning => "Before 11:00 AM",
        MedicationTimeSlot.Midday => "Before 3:00 PM",
        MedicationTimeSlot.Evening => "Before 9:00 PM",
        MedicationTimeSlot.Bedtime => "Before midnight",
        _ => ""
    };

    public static bool IsWindowPassed(DateOnly scheduledDate, MedicationTimeSlot slot, DateTime now)
    {
        if (scheduledDate < DateOnly.FromDateTime(now)) return true;
        if (scheduledDate > DateOnly.FromDateTime(now)) return false;
        return TimeOnly.FromDateTime(now) > GetWindowEnd(slot);
    }

    public static IReadOnlyList<MedicationTimeSlot> AllSlots { get; } =
        [MedicationTimeSlot.Morning, MedicationTimeSlot.Midday, MedicationTimeSlot.Evening, MedicationTimeSlot.Bedtime];
}
