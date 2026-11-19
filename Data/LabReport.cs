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

    public string? Status { get; set; }

    public string? OrderingDoctorName { get; set; }

    public DateTime? OrderedAt { get; set; }

    public string? OrderedTestCodes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LabResult> Results { get; set; } = [];
}

public static class LabReportStatus
{
    public static bool IsPrescribed(LabReport report) =>
        !string.IsNullOrWhiteSpace(report.Status);

    public static bool IsOrdered(LabReport report, string code)
    {
        if (string.IsNullOrWhiteSpace(report.OrderedTestCodes))
            return false;

        foreach (var part in report.OrderedTestCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals(code, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string ResultLabel(LabReport report)
    {
        if (report.Results.Count == 0)
            return "—";

        var outOfRange = report.Results.Any(result =>
            LabTestDefinitions.Get(result.TestCode) is not null
            && !LabTestDefinitions.IsInRange(result.TestCode, result.Value));
        return outOfRange ? "Out of range" : "In range";
    }
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
