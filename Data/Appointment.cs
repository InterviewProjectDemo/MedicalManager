using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalManager.Data;

public enum AppointmentStatus
{
    Scheduled,
    Completed,
    Cancelled
}

public class Appointment
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ProviderName { get; set; } = string.Empty;

    [NotMapped]
    public string WithWhom
    {
        get => ProviderName;
        set => ProviderName = value;
    }

    [MaxLength(160)]
    public string Location { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; } = DateTime.Now.AddDays(1).Date.AddHours(9);

    public DateTime? EndsAt { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    [MaxLength(400)]
    public string? Notes { get; set; }
}
