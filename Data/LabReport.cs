using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public class LabReport
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    public DateOnly ReportDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [MaxLength(400)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LabResult> Results { get; set; } = [];
}

public class LabResult
{
    public int Id { get; set; }

    public int LabReportId { get; set; }

    public LabReport? LabReport { get; set; }

    [Required]
    [MaxLength(40)]
    public string TestCode { get; set; } = string.Empty;

    public decimal Value { get; set; }

    [MaxLength(20)]
    public string? Unit { get; set; }
}
