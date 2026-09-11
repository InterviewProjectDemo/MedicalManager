using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class BloodPressureReading
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Range(60, 250)]
    public int Systolic { get; set; }

    [Range(40, 160)]
    public int Diastolic { get; set; }

    [Range(30, 220)]
    public int? Pulse { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.Now;

    [MaxLength(400)]
    public string? Notes { get; set; }
}
