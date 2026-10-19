using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class MedicationReminder
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public DateOnly ScheduledDate { get; set; }

    public MedicationTimeSlot TimeSlot { get; set; }

    public AppointmentReminderStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    [MaxLength(64)]
    public string? ProviderMessageId { get; set; }

    [MaxLength(300)]
    public string? Detail { get; set; }
}

public class ToDoReminder
{
    public int Id { get; set; }

    public int ToDoItemId { get; set; }

    public ToDoItem? ToDoItem { get; set; }

    public AppointmentReminderKind Kind { get; set; }

    /// <summary>
    /// The due time this reminder was claimed for. A new due time creates a new row.
    /// </summary>
    public DateTime FinishBySnapshot { get; set; }

    public AppointmentReminderStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? SentAt { get; set; }

    [MaxLength(64)]
    public string? ProviderMessageId { get; set; }

    [MaxLength(300)]
    public string? Detail { get; set; }
}
