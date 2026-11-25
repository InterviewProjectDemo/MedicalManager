namespace MedicalManager.Data;

public static class NoteSection
{
    public const string Patient = "Patient";
    public const string Doctor = "Doctor";
}

public static class TranscriptStatus
{
    public const string InProgress = "Visit in progress";
    public const string Saved = "Saved";
    public const string Archived = "Archived";
}

public class ClinicalNote
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    /// <summary>Patient or Doctor. Plain text so each section can be queried.</summary>
    public string Section { get; set; } = NoteSection.Doctor;

    public string AuthorUserId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public string AuthorRole { get; set; } = "Doctor";

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

    public string? Specialty { get; set; }

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

public class MedicalTranscript
{
    public int Id { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string DoctorUserId { get; set; } = string.Empty;

    public string DoctorName { get; set; } = string.Empty;

    public int? VideoVisitId { get; set; }

    public string Body { get; set; } = string.Empty;

    public string Status { get; set; } = TranscriptStatus.InProgress;

    public bool PatientCanView { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EmailedAt { get; set; }
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

public sealed record VisitMessageItem(
    int Id,
    string SenderUserId,
    string SenderName,
    string Body,
    DateTime SentAt);

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

public sealed record TranscriptMailResult(bool Sent, string Message);

public sealed record TranscriptCommand(string Kind, int TranscriptId);
