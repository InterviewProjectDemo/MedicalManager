using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class VirtualVisitService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> users,
    ChartAccessService chartAccess,
    LabReportService labReports,
    HealthRecordService records)
{
    public async Task<VisitAccess> OpenAsync(ClaimsPrincipal principal, bool labUpdateOnly)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");

        if (!principal.IsInRole(AppRoles.Doctor))
        {
            var doctors = await ListDoctorsForPatientAsync(user.Id);
            return new VisitAccess(false, user.Id, user.DisplayName, [], doctors);
        }

        var grants = await chartAccess.ListGrantsForDoctorAsync(user.Id);
        var patients = labUpdateOnly
            ? grants.Where(grant => grant.Lab == ChartAccessLevel.Update).ToList()
            : grants.ToList();
        return new VisitAccess(true, user.Id, user.DisplayName, patients, []);
    }

    public async Task<LabReport> PrescribeLabsAsync(string doctorUserId, string patientUserId, IReadOnlyCollection<string> orderIds)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: true);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        return await labReports.PrescribeAsync(patientUserId, doctor.DisplayName, orderIds);
    }

    public async Task<IReadOnlyList<ClinicalNote>> ListNotesAsync(ClaimsPrincipal principal, string patientUserId)
    {
        var actor = await RequireReaderAsync(principal, patientUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.ClinicalNotes.AsNoTracking().Where(note => note.PatientUserId == patientUserId);
        if (actor.IsDoctor)
            query = query.Where(note => note.DoctorUserId == actor.UserId);

        return await query.OrderByDescending(note => note.CreatedAt).ToListAsync();
    }

    public async Task AddNoteAsync(string doctorUserId, string patientUserId, string body)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        var text = RequiredText(body, "Note", 8000);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.ClinicalNotes.Add(new ClinicalNote
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorUserId,
            AuthorName = doctor.DisplayName,
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<VisitMessage>> ListMessagesAsync(ClaimsPrincipal principal, string patientUserId, string doctorUserId)
    {
        await RequireThreadAsync(principal, patientUserId, doctorUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.VisitMessages.AsNoTracking()
            .Where(message => message.PatientUserId == patientUserId && message.DoctorUserId == doctorUserId)
            .OrderBy(message => message.SentAt)
            .ToListAsync();
    }

    public async Task SendMessageAsync(ClaimsPrincipal principal, string patientUserId, string doctorUserId, string body)
    {
        var actor = await RequireThreadAsync(principal, patientUserId, doctorUserId);
        var text = RequiredText(body, "Message", 4000);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.VisitMessages.Add(new VisitMessage
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorUserId,
            SenderUserId = actor.UserId,
            Body = text,
            SentAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task SetAppointmentAsync(string doctorUserId, string patientUserId, DateOnly day, TimeOnly time, string reason)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        var purpose = RequiredText(reason, "Reason", 200);
        var starts = day.ToDateTime(time);
        if (starts < DateTime.Now.AddMinutes(-1))
            throw new InvalidOperationException("Choose a current or future date and time.");

        var title = purpose.Length <= 120 ? purpose : purpose[..120];
        await records.SaveAppointmentAsync(new Appointment
        {
            UserId = patientUserId,
            Title = title,
            ProviderName = TrimTo(doctor.DisplayName, 120),
            Location = "Virtual visit",
            Purpose = purpose,
            StartsAt = starts,
            EndsAt = starts.AddMinutes(30),
            Status = AppointmentStatus.Scheduled,
            Notes = TrimTo($"Set by {doctor.DisplayName} during a virtual visit.", 400)
        });
    }

    public async Task<IReadOnlyList<VideoVisitSession>> ListVideoVisitsAsync(ClaimsPrincipal principal, string patientUserId, string doctorUserId)
    {
        await RequireThreadAsync(principal, patientUserId, doctorUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.VideoVisitSessions.AsNoTracking()
            .Where(visit => visit.PatientUserId == patientUserId && visit.DoctorUserId == doctorUserId)
            .OrderByDescending(visit => visit.CreatedAt)
            .ToListAsync();
    }

    public async Task<VideoVisitSession> ScheduleVideoVisitAsync(string doctorUserId, string patientUserId)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var visit = new VideoVisitSession
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorUserId,
            DoctorName = doctor.DisplayName,
            CreatedAt = DateTime.UtcNow,
            Status = "Scheduled"
        };
        db.VideoVisitSessions.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    public async Task<VideoVisitSession> StartVideoVisitAsync(ClaimsPrincipal principal, int visitId, string patientUserId, string doctorUserId)
    {
        await RequireThreadAsync(principal, patientUserId, doctorUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var visit = await db.VideoVisitSessions.FirstOrDefaultAsync(item =>
            item.Id == visitId && item.PatientUserId == patientUserId && item.DoctorUserId == doctorUserId)
            ?? throw new InvalidOperationException("Video visit was not found.");
        visit.StartedAt ??= DateTime.UtcNow;
        visit.EndedAt = null;
        visit.Status = "In progress";
        await db.SaveChangesAsync();
        return visit;
    }

    public async Task<VideoVisitSession> EndVideoVisitAsync(ClaimsPrincipal principal, int visitId, string patientUserId, string doctorUserId)
    {
        await RequireThreadAsync(principal, patientUserId, doctorUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var visit = await db.VideoVisitSessions.FirstOrDefaultAsync(item =>
            item.Id == visitId && item.PatientUserId == patientUserId && item.DoctorUserId == doctorUserId)
            ?? throw new InvalidOperationException("Video visit was not found.");
        visit.EndedAt = DateTime.UtcNow;
        visit.Status = "Ended";
        await db.SaveChangesAsync();
        return visit;
    }

    public async Task<IReadOnlyList<AfterVisitSummary>> ListSummariesAsync(ClaimsPrincipal principal, string patientUserId)
    {
        var actor = await RequireReaderAsync(principal, patientUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.AfterVisitSummaries.AsNoTracking().Where(item => item.PatientUserId == patientUserId);
        if (actor.IsDoctor)
            query = query.Where(item => item.DoctorUserId == actor.UserId);
        return await query.OrderByDescending(item => item.CreatedAt).ToListAsync();
    }

    public async Task AddSummaryAsync(string doctorUserId, string patientUserId, string body)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        var text = RequiredText(body, "Summary", 8000);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.AfterVisitSummaries.Add(new AfterVisitSummary
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorUserId,
            AuthorName = doctor.DisplayName,
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ReferralOrder>> ListReferralsAsync(ClaimsPrincipal principal, string patientUserId)
    {
        var actor = await RequireReaderAsync(principal, patientUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.ReferralOrders.AsNoTracking().Where(item => item.PatientUserId == patientUserId);
        if (actor.IsDoctor)
            query = query.Where(item => item.DoctorUserId == actor.UserId);
        return await query.OrderByDescending(item => item.CreatedAt).ToListAsync();
    }

    public async Task AddReferralAsync(string doctorUserId, string patientUserId, string specialty, string? clinician, string reason)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        var specialtyText = RequiredText(specialty, "Specialty", 160);
        var reasonText = RequiredText(reason, "Reason", 4000);
        var clinicianText = string.IsNullOrWhiteSpace(clinician) ? null : RequiredText(clinician, "Clinician", 160);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.ReferralOrders.Add(new ReferralOrder
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorUserId,
            AuthorName = doctor.DisplayName,
            Specialty = specialtyText,
            ClinicianName = clinicianText,
            Reason = reasonText,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<VisitDoctorOption>> ListDoctorsForPatientAsync(string patientUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var grants = await db.ChartAccessGrants.AsNoTracking()
            .Where(grant => grant.PatientUserId == patientUserId && grant.RevokedAt == null)
            .ToListAsync();
        if (grants.Count == 0)
            return [];

        var doctorIds = grants.Select(grant => grant.DoctorUserId).ToHashSet();
        var names = await db.Users.AsNoTracking()
            .Where(user => doctorIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName);
        return grants
            .Select(grant => new VisitDoctorOption(grant.DoctorUserId, names.GetValueOrDefault(grant.DoctorUserId) ?? "Doctor"))
            .DistinctBy(option => option.DoctorUserId)
            .OrderBy(option => option.DoctorName)
            .ToList();
    }

    private async Task RequireGrantAsync(string doctorUserId, string patientUserId, bool labUpdate)
    {
        var grant = await chartAccess.GetActiveGrantAsync(doctorUserId, patientUserId);
        if (grant is null || (labUpdate && !grant.Allows(ChartSection.Lab, true)))
            throw new UnauthorizedAccessException("This patient has not approved that access.");
    }

    private async Task<Actor> RequireReaderAsync(ClaimsPrincipal principal, string patientUserId)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");
        if (!principal.IsInRole(AppRoles.Doctor))
        {
            if (user.Id != patientUserId)
                throw new UnauthorizedAccessException("Patients can only open their own visit records.");
            return new Actor(user.Id, false);
        }

        await RequireGrantAsync(user.Id, patientUserId, labUpdate: false);
        return new Actor(user.Id, true);
    }

    private async Task<Actor> RequireThreadAsync(ClaimsPrincipal principal, string patientUserId, string doctorUserId)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");
        var grant = await chartAccess.GetActiveGrantAsync(doctorUserId, patientUserId);
        if (grant is null)
            throw new UnauthorizedAccessException("This patient has not approved that access.");

        if (principal.IsInRole(AppRoles.Doctor))
        {
            if (user.Id != doctorUserId)
                throw new UnauthorizedAccessException("This patient has not approved that access.");
            return new Actor(user.Id, true);
        }

        if (user.Id != patientUserId)
            throw new UnauthorizedAccessException("Patients can only open their own visit records.");
        return new Actor(user.Id, false);
    }

    private static string RequiredText(string? value, string label, int max)
    {
        var text = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"{label} is required.");
        if (text.Length > max)
            throw new InvalidOperationException($"{label} is too long.");
        return text;
    }

    private static string TrimTo(string value, int max)
    {
        var text = value.Trim();
        return text.Length <= max ? text : text[..max];
    }

    private sealed record Actor(string UserId, bool IsDoctor);
}
