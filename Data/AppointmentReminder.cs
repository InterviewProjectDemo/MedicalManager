namespace MedicalManager.Data;

public enum AppointmentReminderKind
{
    DayBefore = 0,
    HourBefore = 1
}

public enum AppointmentReminderChannel
{
    Voice = 0,
    Sms = 1
}

public enum AppointmentReminderStatus
{
    Sending = 0,
    Sent = 1,
    Failed = 2,
    Skipped = 3
}

public class AppointmentReminder
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public Appointment? Appointment { get; set; }

    public AppointmentReminderKind Kind { get; set; }

    public AppointmentReminderChannel Channel { get; set; }

    /// <summary>
    /// The visit start this reminder was claimed for. A reschedule creates a new row.
    /// </summary>
    public DateTime StartsAtSnapshot { get; set; }

    public AppointmentReminderStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? ProviderMessageId { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? Detail { get; set; }
}
