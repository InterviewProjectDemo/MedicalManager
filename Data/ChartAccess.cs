namespace MedicalManager.Data;

public enum ChartSection
{
    Lab,
    BloodPressure,
    Sugar
}

public enum ChartAccessLevel
{
    None = 0,
    View = 1,
    Update = 2
}

public enum AccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2
}

public class ChartAccessRequest
{
    public int Id { get; set; }

    public string DoctorUserId { get; set; } = string.Empty;

    public ApplicationUser? Doctor { get; set; }

    public string? PatientUserId { get; set; }

    public ApplicationUser? Patient { get; set; }

    public string RequestedName { get; set; } = string.Empty;

    public DateOnly? RequestedDateOfBirth { get; set; }

    public ChartAccessLevel Lab { get; set; }

    public ChartAccessLevel BloodPressure { get; set; }

    public ChartAccessLevel Sugar { get; set; }

    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;

    public string? CodeSalt { get; set; }

    public string? CodeHash { get; set; }

    public DateTime? CodeExpiresAt { get; set; }

    public int CodeAttempts { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
}

public class ChartAccessGrant
{
    public int Id { get; set; }

    public string DoctorUserId { get; set; } = string.Empty;

    public ApplicationUser? Doctor { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public ApplicationUser? Patient { get; set; }

    public ChartAccessLevel Lab { get; set; }

    public ChartAccessLevel BloodPressure { get; set; }

    public ChartAccessLevel Sugar { get; set; }

    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    public bool Allows(ChartSection section, bool update)
    {
        var level = section switch
        {
            ChartSection.Lab => Lab,
            ChartSection.BloodPressure => BloodPressure,
            ChartSection.Sugar => Sugar,
            _ => ChartAccessLevel.None
        };

        return update ? level == ChartAccessLevel.Update : level != ChartAccessLevel.None;
    }
}

public static class ChartAccessText
{
    public static string Describe(ChartAccessLevel lab, ChartAccessLevel bloodPressure, ChartAccessLevel sugar)
    {
        var parts = new List<string>();
        Add(parts, "Labs", lab);
        Add(parts, "Blood pressure chart and list", bloodPressure);
        Add(parts, "Sugar chart and list", sugar);
        return parts.Count == 0 ? "No records" : string.Join("; ", parts);
    }

    private static void Add(List<string> parts, string name, ChartAccessLevel level)
    {
        if (level == ChartAccessLevel.None)
            return;

        parts.Add($"{name} ({(level == ChartAccessLevel.Update ? "update" : "view")})");
    }
}
