using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

/// <summary>
/// Records that this device already spoke a reminder. The words themselves are not stored.
/// </summary>
public class ApplicationVoiceReminder
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ReminderKey { get; set; } = string.Empty;

    public AppointmentReminderStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime? SpokenAt { get; set; }
}
