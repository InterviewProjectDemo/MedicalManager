using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public enum SugarKind
{
    Fasting,
    BeforeMeal,
    AfterMeal,
    Random
}

public class SugarReading
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Range(20, 800)]
    public decimal Value { get; set; }

    [MaxLength(20)]
    public string Unit { get; set; } = "mg/dL";

    public SugarKind Kind { get; set; } = SugarKind.Random;

    public DateTime RecordedAt { get; set; } = DateTime.Now;

    [MaxLength(400)]
    public string? Notes { get; set; }
}
