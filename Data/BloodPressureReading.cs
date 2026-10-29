using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalManager.Data;

public class BloodPressureReading : IAllowOutOfRange
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Display(Name = "Systolic")]
    [VitalRange(60, 250)]
    public int Systolic { get; set; }

    [Display(Name = "Diastolic")]
    [VitalRange(40, 160)]
    public int Diastolic { get; set; }

    [Display(Name = "Pulse")]
    [VitalRange(30, 220)]
    public int? Pulse { get; set; }

    [NotMapped]
    public bool AllowOutOfRange { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.Now;

    [MaxLength(400)]
    public string? Notes { get; set; }
}
