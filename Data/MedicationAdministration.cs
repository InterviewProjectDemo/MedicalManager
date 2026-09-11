using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class MedicationAdministration
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required]
    public int MedicationId { get; set; }

    public Medication? Medication { get; set; }

    public DateOnly ScheduledDate { get; set; }

    public MedicationTimeSlot TimeSlot { get; set; }

    public AdministrationStatus Status { get; set; } = AdministrationStatus.Pending;

    public DateTime? RecordedAt { get; set; }

    [MaxLength(120)]
    public string? RecordedByName { get; set; }

    public string? UpdatedByUserId { get; set; }
}
