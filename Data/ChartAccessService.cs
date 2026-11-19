using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MedicalManager.Data.Notifications;

namespace MedicalManager.Data;

public sealed record AccessRequestResult(bool Accepted, string Message, bool CodeSent = true);

public sealed record PatientAccessRequestItem(
    int Id,
    string DoctorName,
    string? Specialty,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    ChartAccessLevel Medication,
    ChartAccessLevel ToDo,
    ChartAccessLevel Doctors,
    DateTime CreatedAt,
    bool WaitingForPhone,
    bool CodeExpired,
    string? TextNotSentReason);

public sealed record PatientGrantItem(
    int Id,
    string DoctorName,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    ChartAccessLevel Medication,
    ChartAccessLevel ToDo,
    ChartAccessLevel Doctors,
    DateTime ApprovedAt);

public sealed record DoctorAccessRequestItem(
    int Id,
    string RequestedName,
    DateOnly? DateOfBirth,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    ChartAccessLevel Medication,
    ChartAccessLevel ToDo,
    ChartAccessLevel Doctors,
    AccessRequestStatus Status,
    DateTime CreatedAt,
    bool WaitingForPhone,
    string? TextNotSentReason);

public sealed record DoctorGrantItem(
    int Id,
    string PatientId,
    string PatientName,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    ChartAccessLevel Medication,
    ChartAccessLevel ToDo,
    ChartAccessLevel Doctors,
    DateTime ApprovedAt,
    DateTime? PhotoUpdatedAt);

public sealed class ChartAccessService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> users,
    TwilioNotificationSender twilio,
    ILogger<ChartAccessService> logger)
{
    public const int CodeLifetimeMinutes = 15;
    public const int MaxCodeAttempts = 5;

    public async Task<ChartAccessGrant?> GetActiveGrantAsync(string doctorUserId, string patientUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ChartAccessGrants.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.DoctorUserId == doctorUserId
                && x.PatientUserId == patientUserId
                && x.RevokedAt == null);
    }

    public async Task<IReadOnlyList<DoctorGrantItem>> ListGrantsForDoctorAsync(string doctorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var grants = await db.ChartAccessGrants.AsNoTracking()
            .Where(x => x.DoctorUserId == doctorUserId && x.RevokedAt == null)
            .OrderByDescending(x => x.ApprovedAt)
            .ToListAsync();
        if (grants.Count == 0)
            return [];

        var patientIds = grants.Select(x => x.PatientUserId).ToHashSet();
        var names = await db.Users.AsNoTracking()
            .Where(x => patientIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        var photos = await db.PatientProfiles.AsNoTracking()
            .Where(x => patientIds.Contains(x.UserId) && x.ProfilePhotoData != null)
            .Select(x => new { x.UserId, x.ProfilePhotoUpdatedAt })
            .ToDictionaryAsync(x => x.UserId, x => x.ProfilePhotoUpdatedAt);

        return grants
            .Select(x => new DoctorGrantItem(
                x.Id,
                x.PatientUserId,
                names.GetValueOrDefault(x.PatientUserId) ?? "Patient",
                x.Lab,
                x.BloodPressure,
                x.Sugar,
                x.Medication,
                x.ToDo,
                x.Doctors,
                x.ApprovedAt,
                photos.GetValueOrDefault(x.PatientUserId)))
            .ToList();
    }

    public async Task<IReadOnlyList<DoctorAccessRequestItem>> ListRequestsForDoctorAsync(string doctorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.ChartAccessRequests.AsNoTracking()
            .Where(x => x.DoctorUserId == doctorUserId && x.Status == AccessRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var patientIds = rows
            .Select(x => x.PatientUserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet();
        var people = patientIds.Count == 0
            ? new Dictionary<string, ApplicationUser>()
            : await db.Users.AsNoTracking()
                .Where(x => patientIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);
        var profiles = patientIds.Count == 0
            ? new Dictionary<string, PatientProfile>()
            : await db.PatientProfiles.AsNoTracking()
                .Where(x => patientIds.Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId);

        return rows.Select(x =>
        {
            string? reason = null;
            if (x.CodeHash is null)
            {
                if (x.PatientUserId is not null
                    && people.TryGetValue(x.PatientUserId, out var person))
                {
                    profiles.TryGetValue(x.PatientUserId, out var profile);
                    reason = NotSent(Classify(person, profile).Problem);
                }
                else
                {
                    reason = NotSent(TextDestinationProblem.NoPhone);
                }
            }

            return new DoctorAccessRequestItem(
                x.Id,
                x.RequestedName,
                x.RequestedDateOfBirth,
                x.Lab,
                x.BloodPressure,
                x.Sugar,
                x.Medication,
                x.ToDo,
                x.Doctors,
                x.Status,
                x.CreatedAt,
                WaitingForPhone: x.CodeHash is null,
                TextNotSentReason: reason);
        }).ToList();
    }

    public async Task<IReadOnlyList<PatientAccessRequestItem>> ListPendingForPatientAsync(string patientUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.ChartAccessRequests.AsNoTracking()
            .Where(x => x.PatientUserId == patientUserId && x.Status == AccessRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        if (rows.Count == 0)
            return [];

        var doctorIds = rows.Select(x => x.DoctorUserId).ToHashSet();
        var doctors = await db.Users.AsNoTracking()
            .Where(x => doctorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);
        string? patientReason = null;
        if (rows.Any(x => x.CodeHash is null))
        {
            var patient = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == patientUserId);
            var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == patientUserId);
            var problem = patient is null
                ? TextDestinationProblem.NoPhone
                : Classify(patient, profile).Problem;
            patientReason = PatientNotSent(problem);
        }

        return rows.Select(x =>
        {
            doctors.TryGetValue(x.DoctorUserId, out var doctor);
            return new PatientAccessRequestItem(
                x.Id,
                doctor?.DisplayName ?? "Doctor",
                doctor?.Specialty,
                x.Lab,
                x.BloodPressure,
                x.Sugar,
                x.Medication,
                x.ToDo,
                x.Doctors,
                x.CreatedAt,
                WaitingForPhone: x.CodeHash is null,
                CodeExpired: x.CodeExpiresAt is not null && x.CodeExpiresAt < DateTime.UtcNow,
                TextNotSentReason: x.CodeHash is null ? patientReason : null);
        }).ToList();
    }

    public async Task<IReadOnlyList<PatientGrantItem>> ListGrantsForPatientAsync(string patientUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var grants = await db.ChartAccessGrants.AsNoTracking()
            .Where(x => x.PatientUserId == patientUserId && x.RevokedAt == null)
            .OrderByDescending(x => x.ApprovedAt)
            .ToListAsync();
        if (grants.Count == 0)
            return [];

        var doctorIds = grants.Select(x => x.DoctorUserId).ToHashSet();
        var names = await db.Users.AsNoTracking()
            .Where(x => doctorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

        return grants.Select(x => new PatientGrantItem(
            x.Id,
            names.GetValueOrDefault(x.DoctorUserId) ?? "Doctor",
            x.Lab,
            x.BloodPressure,
            x.Sugar,
            x.Medication,
            x.ToDo,
            x.Doctors,
            x.ApprovedAt)).ToList();
    }

    public async Task<AccessRequestResult> SubmitAsync(
        string doctorUserId,
        string patientName,
        DateOnly? dateOfBirth,
        ChartAccessLevel lab,
        ChartAccessLevel bloodPressure,
        ChartAccessLevel sugar,
        ChartAccessLevel medication,
        ChartAccessLevel toDo,
        ChartAccessLevel doctors)
    {
        var doctor = await users.FindByIdAsync(doctorUserId);
        if (doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor))
            return new AccessRequestResult(false, "Only a doctor account can request access.");

        var name = (patientName ?? "").Trim();
        if (name.Length < 3 || name.Length > 200 || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            return new AccessRequestResult(false, "Enter the patient's full name.");
        if (dateOfBirth is null || dateOfBirth > DateOnly.FromDateTime(DateTime.Today) || dateOfBirth < new DateOnly(1900, 1, 1))
            return new AccessRequestResult(false, "Enter the patient's date of birth.");
        if (lab == ChartAccessLevel.None
            && bloodPressure == ChartAccessLevel.None
            && sugar == ChartAccessLevel.None
            && medication == ChartAccessLevel.None
            && toDo == ChartAccessLevel.None
            && doctors == ChartAccessLevel.None)
            return new AccessRequestResult(false, "Choose at least one record, and View or Update for each record you select.");

        var patient = await MatchPatientAsync(name, dateOfBirth.Value);
        if (patient is null || patient.Id == doctorUserId)
            return new AccessRequestResult(false, "We could not match that patient. Check the full name and date of birth.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var pending = await db.ChartAccessRequests
            .Where(x => x.DoctorUserId == doctorUserId && x.PatientUserId == patient.Id && x.Status == AccessRequestStatus.Pending)
            .ToListAsync();
        if (pending.Count > 0)
        {
            db.ChartAccessRequests.RemoveRange(pending);
        }

        var destination = await ClassifyAsync(db, patient);
        var request = new ChartAccessRequest
        {
            DoctorUserId = doctorUserId,
            PatientUserId = patient.Id,
            RequestedName = name,
            RequestedDateOfBirth = dateOfBirth,
            Lab = lab,
            BloodPressure = bloodPressure,
            Sugar = sugar,
            Medication = medication,
            ToDo = toDo,
            Doctors = doctors,
            Status = AccessRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        string? code = null;
        if (destination.Problem == TextDestinationProblem.None)
        {
            code = NewCode();
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            request.CodeSalt = salt;
            request.CodeHash = HashCode(salt, code);
            request.CodeExpiresAt = DateTime.UtcNow.AddMinutes(CodeLifetimeMinutes);
        }

        db.ChartAccessRequests.Add(request);
        await db.SaveChangesAsync();

        if (code is null || destination.E164 is null)
            return new AccessRequestResult(true, NotSent(destination.Problem), false);

        return await DeliverCodeAsync(
            request.Id,
            destination.E164,
            code,
            "A confirmation code was sent to the phone on the patient's account. You can open a chart after they enter that code.");
    }

    public async Task<AccessRequestResult> ResendAsync(string doctorUserId, int requestId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var request = await db.ChartAccessRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.DoctorUserId == doctorUserId);
        if (request is null || request.Status != AccessRequestStatus.Pending || request.PatientUserId is null)
            return new AccessRequestResult(false, "That request is no longer waiting.");

        var patient = await users.FindByIdAsync(request.PatientUserId);
        if (patient is null)
            return new AccessRequestResult(false, "That request is no longer waiting.");

        var destination = await ClassifyAsync(db, patient);
        if (destination.Problem != TextDestinationProblem.None || destination.E164 is null)
            return new AccessRequestResult(true, NotSent(destination.Problem), false);

        var code = NewCode();
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        request.CodeSalt = salt;
        request.CodeHash = HashCode(salt, code);
        request.CodeExpiresAt = DateTime.UtcNow.AddMinutes(CodeLifetimeMinutes);
        request.CodeAttempts = 0;
        await db.SaveChangesAsync();

        return await DeliverCodeAsync(
            request.Id,
            destination.E164,
            code,
            "A new confirmation code was sent to the phone on the patient's account.");
    }

    public async Task<AccessRequestResult> ApproveAsync(string patientUserId, int requestId, string code)
    {
        var entered = (code ?? "").Trim();
        if (entered.Length != 6 || !entered.All(char.IsDigit))
            return new AccessRequestResult(false, "Enter the 6-digit code from the text message.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var request = await db.ChartAccessRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.PatientUserId == patientUserId);
        if (request is null || request.Status != AccessRequestStatus.Pending)
            return new AccessRequestResult(false, "That request is no longer waiting.");
        if (request.DoctorUserId == patientUserId)
            return new AccessRequestResult(false, "You cannot approve your own request.");
        if (request.CodeHash is null || request.CodeSalt is null)
            return new AccessRequestResult(false, "A confirmation code has not been sent for this request. Ask the doctor to send a new code.");
        if (request.CodeExpiresAt is null || request.CodeExpiresAt < DateTime.UtcNow)
            return new AccessRequestResult(false, "That code has expired. Ask the doctor to send a new one.");
        if (request.CodeAttempts >= MaxCodeAttempts)
            return new AccessRequestResult(false, "Too many attempts. Ask the doctor to send a new code.");

        if (!string.Equals(request.CodeHash, HashCode(request.CodeSalt, entered), StringComparison.Ordinal))
        {
            request.CodeAttempts++;
            await db.SaveChangesAsync();
            return new AccessRequestResult(false, "That code is not correct.");
        }

        var grant = await db.ChartAccessGrants
            .FirstOrDefaultAsync(x => x.DoctorUserId == request.DoctorUserId && x.PatientUserId == patientUserId);
        if (grant is null)
        {
            grant = new ChartAccessGrant
            {
                DoctorUserId = request.DoctorUserId,
                PatientUserId = patientUserId
            };
            db.ChartAccessGrants.Add(grant);
        }

        grant.Lab = request.Lab;
        grant.BloodPressure = request.BloodPressure;
        grant.Sugar = request.Sugar;
        grant.Medication = request.Medication;
        grant.ToDo = request.ToDo;
        grant.Doctors = request.Doctors;
        grant.ApprovedAt = DateTime.UtcNow;
        grant.RevokedAt = null;

        request.Status = AccessRequestStatus.Approved;
        request.ResolvedAt = DateTime.UtcNow;
        request.CodeHash = null;
        request.CodeSalt = null;
        await db.SaveChangesAsync();
        return new AccessRequestResult(true, "Access approved. The doctor can open only the records you allowed.");
    }

    public async Task<AccessRequestResult> DenyAsync(string patientUserId, int requestId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var request = await db.ChartAccessRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.PatientUserId == patientUserId);
        if (request is null || request.Status != AccessRequestStatus.Pending)
            return new AccessRequestResult(false, "That request is no longer waiting.");

        request.Status = AccessRequestStatus.Denied;
        request.ResolvedAt = DateTime.UtcNow;
        request.CodeHash = null;
        request.CodeSalt = null;
        await db.SaveChangesAsync();
        return new AccessRequestResult(true, "Request denied.");
    }

    public async Task<AccessRequestResult> RevokeAsync(string patientUserId, int grantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var grant = await db.ChartAccessGrants.FirstOrDefaultAsync(x => x.Id == grantId && x.PatientUserId == patientUserId && x.RevokedAt == null);
        if (grant is null)
            return new AccessRequestResult(false, "That access is already closed.");

        grant.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new AccessRequestResult(true, "Access revoked. The doctor can no longer open those records.");
    }

    private async Task<ApplicationUser?> MatchPatientAsync(string name, DateOnly dateOfBirth)
    {
        var patients = await users.GetUsersInRoleAsync(AppRoles.Patient);
        ApplicationUser? match = null;
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var patient in patients)
        {
            if (!string.Equals(patient.FullName.Trim(), name, StringComparison.OrdinalIgnoreCase))
                continue;

            var dob = await db.PatientProfiles.AsNoTracking()
                .Where(x => x.UserId == patient.Id)
                .Select(x => x.DateOfBirth)
                .FirstOrDefaultAsync();
            if (dob != dateOfBirth)
                continue;

            if (match is not null)
                return null;

            match = patient;
        }

        return match;
    }

    private async Task<AccessRequestResult> DeliverCodeAsync(int requestId, string e164, string code, string sentMessage)
    {
        OutboundResult sent;
        try
        {
            sent = await twilio.TextAsync(e164, SmsCopy.ApprovalCode(code), CancellationToken.None, confirmDelivery: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Access approval text for request {RequestId} was not sent.", requestId);
            return new AccessRequestResult(true, NotSent(TextDestinationProblem.None), false);
        }

        if (!sent.Ok)
        {
            logger.LogWarning("Access approval text for request {RequestId} was not sent ({Detail}).", requestId, sent.Detail ?? "unknown");
            return new AccessRequestResult(true, DeliveryProblem(sent.Detail), false);
        }

        return new AccessRequestResult(true, sentMessage);
    }

    private static async Task<TextDestination> ClassifyAsync(ApplicationDbContext db, ApplicationUser patient)
    {
        var profile = await db.PatientProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == patient.Id);
        return Classify(patient, profile);
    }

    private static TextDestination Classify(ApplicationUser patient, PatientProfile? profile)
    {
        var profilePhone = profile?.Phone;
        var raw = string.IsNullOrWhiteSpace(profilePhone) ? patient.PhoneNumber : profilePhone;
        if (string.IsNullOrWhiteSpace(raw))
            return new TextDestination(null, TextDestinationProblem.NoPhone);

        var e164 = PhoneNumbers.ToE164(raw);
        if (!PhoneNumbers.IsUsE164(e164))
            return new TextDestination(null, TextDestinationProblem.InvalidPhone);

        if (!SmsConsent.IsGranted(profile, raw))
            return new TextDestination(null, TextDestinationProblem.NoConsent);

        return new TextDestination(e164, TextDestinationProblem.None);
    }

    private static string NotSent(TextDestinationProblem problem) => problem switch
    {
        TextDestinationProblem.NoPhone =>
            "The request is saved, but the confirmation text was not sent. No phone number is saved on that account.",
        TextDestinationProblem.InvalidPhone =>
            "The request is saved, but the confirmation text was not sent. The phone number saved on that account is not a valid US number.",
        TextDestinationProblem.NoConsent =>
            "The request is saved, but the confirmation text was not sent. The text-message consent box on that patient's profile is not checked.",
        _ =>
            "The request is saved, but the confirmation text was not sent. Use Send a new code to try again."
    };

    private static string PatientNotSent(TextDestinationProblem problem) => problem switch
    {
        TextDestinationProblem.NoPhone =>
            "The confirmation text was not sent. Add a mobile number on your profile, then ask the doctor to send a new code.",
        TextDestinationProblem.InvalidPhone =>
            "The confirmation text was not sent. The phone number on your profile is not a valid US number. Correct it, then ask the doctor to send a new code.",
        TextDestinationProblem.NoConsent =>
            "The confirmation text was not sent. Check the text-message consent box on your profile, then ask the doctor to send a new code.",
        _ =>
            "The confirmation text was not sent. Ask the doctor to send a new code."
    };

    private static string DeliveryProblem(string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail)
            && (detail.Contains("21211", StringComparison.Ordinal)
                || detail.Contains("21614", StringComparison.Ordinal)
                || detail.Contains("not a valid US number", StringComparison.OrdinalIgnoreCase)))
        {
            return NotSent(TextDestinationProblem.InvalidPhone);
        }

        if (!string.IsNullOrWhiteSpace(detail) && detail.Contains("21610", StringComparison.Ordinal))
            return "The request is saved, but the confirmation text was not sent. That number has opted out of text messages.";

        if (!string.IsNullOrWhiteSpace(detail)
            && (detail.Contains("30034", StringComparison.Ordinal) || detail.Contains("30032", StringComparison.Ordinal)))
        {
            return "The request is saved, but the confirmation text was not sent. The carrier rejected the message.";
        }

        return NotSent(TextDestinationProblem.None);
    }

    private enum TextDestinationProblem
    {
        None,
        NoPhone,
        InvalidPhone,
        NoConsent
    }

    private readonly record struct TextDestination(string? E164, TextDestinationProblem Problem);

    private static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string salt, string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + ":" + code));
        return Convert.ToHexString(bytes);
    }
}
