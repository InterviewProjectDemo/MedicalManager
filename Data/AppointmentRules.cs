namespace MedicalManager.Data;

/// <summary>
/// Auto-miss rules for appointments. Once the visit end (or start if there is no end)
/// is in the past, any status other than Completed or Cancelled is persisted as Missed.
/// </summary>
public static class AppointmentRules
{
    public static DateTime GetEffectiveEnd(Appointment appointment) =>
        appointment.EndsAt ?? appointment.StartsAt;

    public static bool IsPast(Appointment appointment, DateTime now) =>
        GetEffectiveEnd(appointment) < now;

    public static bool IsTerminal(AppointmentStatus status) =>
        status is AppointmentStatus.Completed or AppointmentStatus.Cancelled;

    public static bool ShouldAutoMiss(Appointment appointment, DateTime now) =>
        !IsTerminal(appointment.Status) && IsPast(appointment, now);

    public static string StatusCss(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "status-ok",
        AppointmentStatus.Completed => "status-muted",
        AppointmentStatus.Cancelled => "status-danger",
        AppointmentStatus.Missed => "status-danger",
        _ => "status-muted"
    };

    public static string ActionLabel(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Completed => "Mark completed",
        AppointmentStatus.Cancelled => "Cancel",
        AppointmentStatus.Missed => "Mark missed",
        _ => "Mark scheduled"
    };
}
