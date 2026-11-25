using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MedicalManager.Components.Account;

namespace MedicalManager.Data;

public sealed class VirtualVisitService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> users,
    ChartAccessService chartAccess,
    LabReportService labReports,
    HealthRecordService records,
    IAppEmailSender email,
    ILogger<VirtualVisitService> logger)
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

    public async Task<int> PrescribeMedicationsAsync(
        string doctorUserId,
        string patientUserId,
        IReadOnlyList<MedicationOrder> orders)
    {
        var grant = await chartAccess.GetActiveGrantAsync(doctorUserId, patientUserId);
        if (grant is null || !grant.Allows(ChartSection.Medication, true))
            throw new UnauthorizedAccessException("This patient has not approved that access.");

        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        return await records.AddPrescribedMedicationsAsync(patientUserId, doctorUserId, doctor.DisplayName, orders);
    }

    public async Task<IReadOnlyList<ClinicalNote>> ListNotesAsync(
        ClaimsPrincipal principal,
        string patientUserId,
        string? doctorUserId = null)
    {
        var actor = await RequireReaderAsync(principal, patientUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.ClinicalNotes.AsNoTracking().Where(note => note.PatientUserId == patientUserId);
        if (actor.IsDoctor)
        {
            // A doctor's notes stay with that doctor and the selected patient.
            // Patient-note rows are not part of this query, so their contents are never returned.
            var doctorId = actor.UserId;
            query = query.Where(note => note.Section == NoteSection.Doctor && note.DoctorUserId == doctorId);
        }
        else
        {
            var viewedDoctor = (doctorUserId ?? "").Trim();
            if (viewedDoctor.Length == 0)
            {
                query = query.Where(note => note.Section == NoteSection.Patient);
            }
            else
            {
                var grant = await chartAccess.GetActiveGrantAsync(viewedDoctor, actor.UserId);
                if (grant is null)
                    throw new UnauthorizedAccessException("You can only read notes from a doctor you approved.");

                query = query.Where(note =>
                    note.Section == NoteSection.Patient
                    || (note.Section == NoteSection.Doctor && note.DoctorUserId == viewedDoctor));
            }
        }

        return await query
            .OrderByDescending(note => note.CreatedAt)
            .ThenByDescending(note => note.Id)
            .ToListAsync();
    }

    public async Task AddNoteAsync(ClaimsPrincipal principal, string patientUserId, string body)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");
        var text = RequiredText(body, "Note", 8000);
        var doctorWriting = principal.IsInRole(AppRoles.Doctor);
        if (doctorWriting)
            await RequireGrantAsync(user.Id, patientUserId, labUpdate: false);
        else if (user.Id != patientUserId)
            throw new UnauthorizedAccessException("Patients can only write in their own note section.");

        await using var db = await dbFactory.CreateDbContextAsync();
        db.ClinicalNotes.Add(new ClinicalNote
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorWriting ? user.Id : patientUserId,
            Section = doctorWriting ? NoteSection.Doctor : NoteSection.Patient,
            AuthorUserId = user.Id,
            AuthorName = user.DisplayName,
            AuthorRole = doctorWriting ? "Doctor" : "Patient",
            Body = text,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<VisitMessageItem>> ListMessagesAsync(ClaimsPrincipal principal, string patientUserId, string doctorUserId)
    {
        await RequireThreadAsync(principal, patientUserId, doctorUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var messages = await db.VisitMessages.AsNoTracking()
            .Where(message => message.PatientUserId == patientUserId && message.DoctorUserId == doctorUserId)
            .OrderBy(message => message.SentAt)
            .ToListAsync();
        var senderIds = messages.Select(message => message.SenderUserId).Distinct().ToList();
        var names = senderIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Users.AsNoTracking()
                .Where(user => senderIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName);
        return messages
            .Select(message => new VisitMessageItem(
                message.Id,
                message.SenderUserId,
                names.GetValueOrDefault(message.SenderUserId) ?? "Member",
                message.Body,
                message.SentAt))
            .ToList();
    }

    public async Task<IReadOnlyList<VisitDoctorOption>> ListMessageDoctorsAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");
        if (principal.IsInRole(AppRoles.Doctor))
            return [];

        var care = await ListDoctorsForPatientAsync(user.Id);
        await using var db = await dbFactory.CreateDbContextAsync();
        var threadIds = await db.VisitMessages.AsNoTracking()
            .Where(message => message.PatientUserId == user.Id)
            .Select(message => message.DoctorUserId)
            .Distinct()
            .ToListAsync();
        var known = care.Select(doctor => doctor.DoctorUserId).ToHashSet();
        var extraIds = threadIds.Where(id => !known.Contains(id)).ToList();
        if (extraIds.Count == 0)
            return care;

        var names = await db.Users.AsNoTracking()
            .Where(account => extraIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id, account => account.DisplayName);
        return care
            .Concat(extraIds.Select(id => new VisitDoctorOption(id, names.GetValueOrDefault(id) ?? "Doctor")))
            .OrderBy(doctor => doctor.DoctorName)
            .ToList();
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

    public async Task<VideoVisitSession> InviteVideoAsync(string doctorUserId, string patientUserId)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var doctor = await users.FindByIdAsync(doctorUserId)
            ?? throw new InvalidOperationException("Doctor account was not found.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var open = await db.VideoVisitSessions
            .Where(visit => visit.PatientUserId == patientUserId
                && visit.DoctorUserId == doctorUserId
                && visit.Status != "Ended")
            .OrderByDescending(visit => visit.CreatedAt)
            .FirstOrDefaultAsync();
        if (open is not null)
            return open;

        var specialty = string.IsNullOrWhiteSpace(doctor.Specialty) ? null : doctor.Specialty.Trim();
        var visit = new VideoVisitSession
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctorUserId,
            DoctorName = doctor.DisplayName,
            Specialty = specialty,
            CreatedAt = DateTime.UtcNow,
            Status = "Invited"
        };
        db.VideoVisitSessions.Add(visit);
        await db.SaveChangesAsync();
        return visit;
    }

    public async Task<VideoVisitSession?> OpenInviteAsync(ClaimsPrincipal principal, string patientUserId, string doctorUserId)
    {
        await RequireThreadAsync(principal, patientUserId, doctorUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.VideoVisitSessions.AsNoTracking()
            .Where(visit => visit.PatientUserId == patientUserId
                && visit.DoctorUserId == doctorUserId
                && visit.Status != "Ended")
            .OrderByDescending(visit => visit.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<VideoVisitSession>> ListOpenInvitesAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");
        if (principal.IsInRole(AppRoles.Doctor))
            return [];

        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.VideoVisitSessions.AsNoTracking()
            .Where(visit => visit.PatientUserId == user.Id && visit.Status != "Ended")
            .OrderByDescending(visit => visit.CreatedAt)
            .ToListAsync();
    }

    public async Task<VideoVisitSession> RequireCallAsync(ClaimsPrincipal principal, int visitId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var visit = await db.VideoVisitSessions.AsNoTracking().FirstOrDefaultAsync(item => item.Id == visitId)
            ?? throw new InvalidOperationException("Video visit was not found.");
        if (visit.Status == "Ended")
            throw new InvalidOperationException("This video visit has ended.");
        await RequireThreadAsync(principal, visit.PatientUserId, visit.DoctorUserId);
        return visit;
    }

    public async Task MarkVideoJoinedAsync(ClaimsPrincipal principal, int visitId)
    {
        var visit = await RequireCallAsync(principal, visitId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.VideoVisitSessions.FirstOrDefaultAsync(item => item.Id == visit.Id)
            ?? throw new InvalidOperationException("Video visit was not found.");
        row.StartedAt ??= DateTime.UtcNow;
        if (row.Status != "Ended")
            row.Status = "In progress";
        await db.SaveChangesAsync();
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

    public async Task UpdateSummaryAsync(string doctorUserId, string patientUserId, int summaryId, string body)
    {
        await RequireGrantAsync(doctorUserId, patientUserId, labUpdate: false);
        var text = RequiredText(body, "Summary", 8000);
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.AfterVisitSummaries.FirstOrDefaultAsync(summary =>
            summary.Id == summaryId
            && summary.PatientUserId == patientUserId
            && summary.DoctorUserId == doctorUserId)
            ?? throw new InvalidOperationException("That summary was not found.");
        item.Body = text;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<MedicalTranscript>> ListTranscriptsAsync(ClaimsPrincipal principal, string patientUserId)
    {
        var actor = await RequireReaderAsync(principal, patientUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.MedicalTranscripts.AsNoTracking().Where(item => item.PatientUserId == patientUserId);
        if (actor.IsDoctor)
            query = query.Where(item => item.DoctorUserId == actor.UserId);
        else
            query = query.Where(item => item.PatientCanView);
        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToListAsync();
    }

    public async Task<MedicalTranscript> StartTranscriptAsync(ClaimsPrincipal principal, string patientUserId, int? videoVisitId)
    {
        var doctor = await RequireDoctorAsync(principal, patientUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var linked = await LinkedVisitIdAsync(db, patientUserId, doctor.Id, videoVisitId);
        var open = await db.MedicalTranscripts
            .Where(item =>
                item.PatientUserId == patientUserId
                && item.DoctorUserId == doctor.Id
                && item.Status == TranscriptStatus.InProgress)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync();
        if (open is not null && (linked is null || open.VideoVisitId is null || open.VideoVisitId == linked))
        {
            if (open.VideoVisitId is null && linked is int visitId)
            {
                open.VideoVisitId = visitId;
                open.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return open;
        }

        var transcript = new MedicalTranscript
        {
            PatientUserId = patientUserId,
            DoctorUserId = doctor.Id,
            DoctorName = doctor.DisplayName,
            VideoVisitId = linked,
            Body = "",
            Status = TranscriptStatus.InProgress,
            PatientCanView = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.MedicalTranscripts.Add(transcript);
        await db.SaveChangesAsync();
        return transcript;
    }

    public async Task<MedicalTranscript> WriteTranscriptAsync(ClaimsPrincipal principal, int transcriptId, string? body, bool patientCanView)
    {
        var doctor = await RequireDoctorAsync(principal);
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await OwnTranscriptAsync(db, doctor.Id, transcriptId);
        await RequireGrantAsync(doctor.Id, item.PatientUserId, labUpdate: false);
        if (item.Status == TranscriptStatus.Archived)
            throw new InvalidOperationException("An archived transcript cannot be changed.");

        item.Body = LimitTranscript(body);
        item.PatientCanView = patientCanView;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<MedicalTranscript> SaveTranscriptAsync(ClaimsPrincipal principal, int transcriptId, string? body, bool patientCanView)
    {
        var doctor = await RequireDoctorAsync(principal);
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await OwnTranscriptAsync(db, doctor.Id, transcriptId);
        await RequireGrantAsync(doctor.Id, item.PatientUserId, labUpdate: false);
        if (item.Status == TranscriptStatus.Archived)
            throw new InvalidOperationException("An archived transcript cannot be changed.");

        item.Body = RequiredText(body, "Transcript", 32000);
        item.PatientCanView = patientCanView;
        item.Status = TranscriptStatus.Saved;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<MedicalTranscript> ArchiveTranscriptAsync(ClaimsPrincipal principal, int transcriptId, string? body, bool patientCanView)
    {
        var doctor = await RequireDoctorAsync(principal);
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await OwnTranscriptAsync(db, doctor.Id, transcriptId);
        await RequireGrantAsync(doctor.Id, item.PatientUserId, labUpdate: false);
        if (item.Status == TranscriptStatus.Archived)
            return item;

        item.Body = LimitTranscript(string.IsNullOrWhiteSpace(body) ? item.Body : body);
        item.PatientCanView = patientCanView;
        item.Status = TranscriptStatus.Archived;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<MedicalTranscript> SetTranscriptViewAsync(ClaimsPrincipal principal, int transcriptId, bool patientCanView)
    {
        var doctor = await RequireDoctorAsync(principal);
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await OwnTranscriptAsync(db, doctor.Id, transcriptId);
        await RequireGrantAsync(doctor.Id, item.PatientUserId, labUpdate: false);
        item.PatientCanView = patientCanView;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<TranscriptMailResult> EmailTranscriptAsync(ClaimsPrincipal principal, int transcriptId, string? body, bool patientCanView)
    {
        var doctor = await RequireDoctorAsync(principal);
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await OwnTranscriptAsync(db, doctor.Id, transcriptId);
        await RequireGrantAsync(doctor.Id, item.PatientUserId, labUpdate: false);

        var changed = false;
        if (item.Status == TranscriptStatus.Archived)
        {
            if (string.IsNullOrWhiteSpace(item.Body))
                return new TranscriptMailResult(false, "This archived transcript has no words to email.");
        }
        else
        {
            var text = (body ?? "").Trim();
            if (text.Length == 0)
                return new TranscriptMailResult(false, "Write the transcript before emailing it.");
            if (text.Length > 32000)
                return new TranscriptMailResult(false, "The transcript is too long to email.");

            item.Body = text;
            item.PatientCanView = patientCanView;
            item.UpdatedAt = DateTime.UtcNow;
            changed = true;
        }

        var patient = await users.FindByIdAsync(item.PatientUserId);
        var address = patient?.Email?.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            if (changed)
                await db.SaveChangesAsync();
            return new TranscriptMailResult(false, "This patient has no email on the account, so the transcript was not emailed.");
        }

        if (!email.IsConfigured)
        {
            if (changed)
                await db.SaveChangesAsync();
            logger.LogWarning("Email is not configured. A medical transcript was not emailed.");
            return new TranscriptMailResult(false, "Email is not configured, so the transcript was not emailed. The words are still kept here.");
        }

        var when = item.CreatedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        var (html, plain) = EmailTemplates.MedicalTranscript(
            patient?.DisplayName ?? "there",
            item.DoctorName,
            when,
            item.Status,
            item.Body);
        var sent = await email.TrySendMedicalTranscriptAsync(address, html, plain);
        if (!sent)
        {
            if (changed)
                await db.SaveChangesAsync();
            logger.LogWarning("A medical transcript email could not be sent.");
            return new TranscriptMailResult(false, "The transcript email could not be sent. The words are still kept here.");
        }

        item.EmailedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new TranscriptMailResult(true, "Emailed to the address on the patient's account.");
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

    private async Task<ApplicationUser> RequireDoctorAsync(ClaimsPrincipal principal, string? patientUserId = null)
    {
        var user = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");
        if (!principal.IsInRole(AppRoles.Doctor))
            throw new UnauthorizedAccessException("Only the doctor can change a medical transcript.");
        if (!string.IsNullOrWhiteSpace(patientUserId))
            await RequireGrantAsync(user.Id, patientUserId, labUpdate: false);
        return user;
    }

    private static async Task<MedicalTranscript> OwnTranscriptAsync(ApplicationDbContext db, string doctorUserId, int transcriptId)
    {
        return await db.MedicalTranscripts.FirstOrDefaultAsync(item => item.Id == transcriptId && item.DoctorUserId == doctorUserId)
            ?? throw new InvalidOperationException("That medical transcript was not found.");
    }

    private static async Task<int?> LinkedVisitIdAsync(ApplicationDbContext db, string patientUserId, string doctorUserId, int? videoVisitId)
    {
        if (videoVisitId is not int visitId || visitId <= 0)
            return null;

        var exists = await db.VideoVisitSessions.AsNoTracking().AnyAsync(item =>
            item.Id == visitId && item.PatientUserId == patientUserId && item.DoctorUserId == doctorUserId);
        return exists ? visitId : null;
    }

    private static string LimitTranscript(string? body)
    {
        var text = (body ?? "").Trim();
        if (text.Length > 32000)
            throw new InvalidOperationException("The transcript is too long.");
        return text;
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
