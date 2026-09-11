using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class Medication
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Dosage { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Frequency { get; set; } = "Once daily";

    [MaxLength(400)]
    public string? Notes { get; set; }

    [MaxLength(120)]
    public string PrescribedBy { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? EndDate { get; set; }

    public List<MedicationSchedule> Schedules { get; set; } = [];
}
