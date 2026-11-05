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
    DateTime CreatedAt,
    bool WaitingForPhone,
    bool CodeExpired);

public sealed record PatientGrantItem(
    int Id,
    string DoctorName,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    DateTime ApprovedAt);

public sealed record DoctorAccessRequestItem(
    int Id,
    string RequestedName,
    DateOnly? DateOfBirth,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    AccessRequestStatus Status,
    DateTime CreatedAt,
    bool WaitingForPhone);

public sealed record DoctorGrantItem(
    int Id,
    string PatientId,
    string PatientName,
    ChartAccessLevel Lab,
    ChartAccessLevel BloodPressure,
    ChartAccessLevel Sugar,
    DateTime ApprovedAt);

public sealed class ChartAccessService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> users,
    TwilioNotificationSender twilio,
    IHostEnvironment environment,
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

        return grants
            .Select(x => new DoctorGrantItem(
                x.Id,
                x.PatientUserId,
                names.GetValueOrDefault(x.PatientUserId) ?? "Patient",
                x.Lab,
                x.BloodPressure,
                x.Sugar,
                x.ApprovedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<DoctorAccessRequestItem>> ListRequestsForDoctorAsync(string doctorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.ChartAccessRequests.AsNoTracking()
            .Where(x => x.DoctorUserId == doctorUserId && x.Status == AccessRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return rows.Select(x => new DoctorAccessRequestItem(
            x.Id,
            x.RequestedName,
            x.RequestedDateOfBirth,
            x.Lab,
            x.BloodPressure,
            x.Sugar,
            x.Status,
            x.CreatedAt,
            WaitingForPhone: x.CodeHash is null)).ToList();
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
                x.CreatedAt,
                WaitingForPhone: x.CodeHash is null,
                CodeExpired: x.CodeExpiresAt is not null && x.CodeExpiresAt < DateTime.UtcNow);
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
            x.ApprovedAt)).ToList();
    }

    public async Task<AccessRequestResult> SubmitAsync(
        string doctorUserId,
        string patientName,
        DateOnly? dateOfBirth,
        ChartAccessLevel lab,
        ChartAccessLevel bloodPressure,
        ChartAccessLevel sugar)
    {
        var doctor = await users.FindByIdAsync(doctorUserId);
        if (doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor))
            return new AccessRequestResult(false, "Only a doctor account can request access.");

        var name = (patientName ?? "").Trim();
        if (name.Length < 3 || name.Length > 200 || name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            return new AccessRequestResult(false, "Enter the patient's full name.");
        if (dateOfBirth is null || dateOfBirth > DateOnly.FromDateTime(DateTime.Today) || dateOfBirth < new DateOnly(1900, 1, 1))
            return new AccessRequestResult(false, "Enter the patient's date of birth.");
        if (lab == ChartAccessLevel.None && bloodPressure == ChartAccessLevel.None && sugar == ChartAccessLevel.None)
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

        var (phone, smsConsent) = await PatientTextDestinationAsync(db, patient);
        var request = new ChartAccessRequest
        {
            DoctorUserId = doctorUserId,
            PatientUserId = patient.Id,
            RequestedName = name,
            RequestedDateOfBirth = dateOfBirth,
            Lab = lab,
            BloodPressure = bloodPressure,
            Sugar = sugar,
            Status = AccessRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        string? code = null;
        if (!string.IsNullOrWhiteSpace(phone) && smsConsent)
        {
            code = NewCode();
            var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            request.CodeSalt = salt;
            request.CodeHash = HashCode(salt, code);
            request.CodeExpiresAt = DateTime.UtcNow.AddMinutes(CodeLifetimeMinutes);
        }

        db.ChartAccessRequests.Add(request);
        await db.SaveChangesAsync();

        if (code is null)
        {
            var waiting = string.IsNullOrWhiteSpace(phone)
                ? "The request is waiting. A confirmation code can be sent only to the phone already saved on that patient's account."
                : "The request is waiting. A confirmation code can be sent only after the patient checks the text-message consent box on their profile.";
            return new AccessRequestResult(true, waiting, false);
        }

        var body = BuildMessage(doctor, lab, bloodPressure, sugar, code);
        var sent = await twilio.TextAsync(phone!, body, CancellationToken.None, confirmDelivery: true);
        if (environment.IsDevelopment())
        {
            logger.LogInformation("Access approval code for request {RequestId} is {Code}.", request.Id, code);
        }

        if (!sent.Ok)
        {
            logger.LogWarning("Access approval text for request {RequestId} was not sent ({Detail}).", request.Id, sent.Detail ?? "unknown");
            return new AccessRequestResult(true, DeliveryProblem(sent.Detail), false);
        }

        return new AccessRequestResult(true, "A confirmation code was sent to the phone on the patient's account. You can open a chart after they enter that code.");
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

        var (phone, smsConsent) = await PatientTextDestinationAsync(db, patient);
        if (string.IsNullOrWhiteSpace(phone))
            return new AccessRequestResult(false, "A code can be sent only to the phone already saved on that patient's account.");
        if (!smsConsent)
            return new AccessRequestResult(false, "A code can be sent only after the patient checks the text-message consent box on their profile.");

        var doctor = await users.FindByIdAsync(doctorUserId);
        var code = NewCode();
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        request.CodeSalt = salt;
        request.CodeHash = HashCode(salt, code);
        request.CodeExpiresAt = DateTime.UtcNow.AddMinutes(CodeLifetimeMinutes);
        request.CodeAttempts = 0;
        await db.SaveChangesAsync();

        var body = BuildMessage(doctor, request.Lab, request.BloodPressure, request.Sugar, code);
        var sent = await twilio.TextAsync(phone, body, CancellationToken.None, confirmDelivery: true);
        if (environment.IsDevelopment())
        {
            logger.LogInformation("Access approval code for request {RequestId} is {Code}.", request.Id, code);
        }

        if (!sent.Ok)
        {
            logger.LogWarning("Access approval text for request {RequestId} was not sent ({Detail}).", request.Id, sent.Detail ?? "unknown");
            return new AccessRequestResult(true, DeliveryProblem(sent.Detail), false);
        }

        return new AccessRequestResult(true, "A new confirmation code was sent to the phone on the patient's account.");
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
            return new AccessRequestResult(false, "Add a phone number on your profile, then ask the doctor to send a new code.");
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

    private static async Task<(string? Phone, bool SmsConsent)> PatientTextDestinationAsync(ApplicationDbContext db, ApplicationUser patient)
    {
        var profile = await db.PatientProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == patient.Id);
        var phone = string.IsNullOrWhiteSpace(profile?.Phone) ? patient.PhoneNumber : profile.Phone;
        if (PhoneNumbers.ToE164(phone) is null)
            return (null, false);

        return (phone!.Trim(), SmsConsent.IsGranted(profile, phone));
    }

    private static string DeliveryProblem(string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail)
            && (detail.Contains("30034", StringComparison.Ordinal) || detail.Contains("30032", StringComparison.Ordinal)))
        {
            return "The request is saved, but the confirmation text was not delivered. This sending number is not registered for US text messages yet.";
        }

        if (string.Equals(detail, "waiting", StringComparison.OrdinalIgnoreCase))
            return "The request is saved, but the confirmation text has not reached the phone yet. Use Send a new code if it does not arrive.";

        return "The request is saved, but the confirmation text was not delivered. Use Send a new code to try again.";
    }

    private static string BuildMessage(
        ApplicationUser? doctor,
        ChartAccessLevel lab,
        ChartAccessLevel bloodPressure,
        ChartAccessLevel sugar,
        string code)
    {
        var who = string.IsNullOrWhiteSpace(doctor?.FullName) ? "A doctor" : doctor.FullName.Trim();
        var what = ChartAccessText.Describe(lab, bloodPressure, sugar);
        return SmsCopy.Finish($"Medical Manager: {who} requests {what}. Approval code {code}. It expires in 15 minutes. Sign in and enter this code under Access requests. If you did not expect this, do not share the code.");
    }

    private static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string HashCode(string salt, string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + ":" + code));
        return Convert.ToHexString(bytes);
    }
}
