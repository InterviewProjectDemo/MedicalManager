namespace MedicalManager.Data;

public class ClinicalNote
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VisitMessage
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    public string SenderUserId { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

public class VideoVisitSession
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    public string DoctorName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public string Status { get; set; } = "Scheduled";
}

public class AfterVisitSummary
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReferralOrder
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string Specialty { get; set; } = string.Empty;

    public string? ClinicianName { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record VisitDoctorOption(string DoctorUserId, string DoctorName);

public sealed record VisitChoice(
    bool IsDoctor,
    string ActorUserId,
    string ActorName,
    string PatientUserId,
    string PatientName,
    string DoctorUserId);

public sealed record MedicationOrder(string Name, string Dose, string Schedule, string? Purpose);

public sealed record VisitAccess(
    bool IsDoctor,
    string ActorUserId,
    string ActorName,
    IReadOnlyList<DoctorGrantItem> Patients,
    IReadOnlyList<VisitDoctorOption> Doctors);
