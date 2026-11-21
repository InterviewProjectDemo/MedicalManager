using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MedicalManager.Components.Account;
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

public sealed record AffiliatedPatientItem(
    string PatientId,
    string PatientName,
    DateOnly? DateOfBirth,
    DateTime? PhotoUpdatedAt,
    bool HasGrant,
    bool RequestPending);

public sealed class ChartAccessService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> users,
    TwilioNotificationSender twilio,
    IAppEmailSender mail,
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
                    reason = NotSent(Classify(person, profile).Problem, PatientEmail(person) is not null);
                }
                else
                {
                    reason = NotSent(TextDestinationProblem.NoPhone, hasEmail: false);
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
            patientReason = PatientNotSent(problem, patient is not null && PatientEmail(patient) is not null);
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
        var emailAddress = PatientEmail(patient);
        var canText = destination.Problem == TextDestinationProblem.None && destination.E164 is not null;
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
        if (canText || emailAddress is not null)
        {
            code = NewCode();
            StampCode(request, code);
        }

        db.ChartAccessRequests.Add(request);
        await db.SaveChangesAsync();

        if (code is null)
            return new AccessRequestResult(true, NotSent(destination.Problem, emailAddress is not null), false);

        var sender = await users.FindByIdAsync(doctorUserId);
        return await DeliverCodeAsync(
            request.Id,
            canText ? destination.E164 : null,
            emailAddress,
            code,
            sender?.DisplayName ?? "Your doctor",
            ChartAccessText.Describe(lab, bloodPressure, sugar, medication, toDo, doctors),
            renew: false);
    }

    public async Task<IReadOnlyList<AffiliatedPatientItem>> ListAffiliatedPatientsAsync(string doctorUserId)
    {
        var doctor = await users.FindByIdAsync(doctorUserId);
        if (doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor))
            return [];

        var patients = await users.GetUsersInRoleAsync(AppRoles.Patient);
        var patientById = patients.ToDictionary(patient => patient.Id, StringComparer.Ordinal);
        var affiliated = await FindAffiliatedPatientIdsAsync(doctor, patientById.Keys);
        if (affiliated.Count == 0)
            return [];

        var affiliatedIds = affiliated.ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        var profiles = await db.PatientProfiles.AsNoTracking()
            .Where(profile => affiliatedIds.Contains(profile.UserId))
            .Select(profile => new { profile.UserId, profile.DateOfBirth, profile.ProfilePhotoUpdatedAt })
            .ToListAsync();
        var profileById = profiles.ToDictionary(profile => profile.UserId, StringComparer.Ordinal);
        var grantIds = (await db.ChartAccessGrants.AsNoTracking()
            .Where(grant => grant.DoctorUserId == doctorUserId && grant.RevokedAt == null && affiliatedIds.Contains(grant.PatientUserId))
            .Select(grant => grant.PatientUserId)
            .ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var pendingIds = (await db.ChartAccessRequests.AsNoTracking()
            .Where(request => request.DoctorUserId == doctorUserId
                && request.Status == AccessRequestStatus.Pending
                && request.PatientUserId != null
                && affiliatedIds.Contains(request.PatientUserId))
            .Select(request => request.PatientUserId!)
            .ToListAsync()).ToHashSet(StringComparer.Ordinal);

        return affiliatedIds
            .Where(patientById.ContainsKey)
            .Select(id =>
            {
                var patient = patientById[id];
                profileById.TryGetValue(id, out var profile);
                return new AffiliatedPatientItem(
                    id,
                    patient.DisplayName,
                    profile?.DateOfBirth,
                    profile?.ProfilePhotoUpdatedAt,
                    grantIds.Contains(id),
                    pendingIds.Contains(id));
            })
            .OrderBy(item => item.PatientName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AccessRequestResult> SubmitForPatientAsync(
        string doctorUserId,
        string patientUserId,
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
        if (NoSections(lab, bloodPressure, sugar, medication, toDo, doctors))
            return new AccessRequestResult(false, "Check View, Update, or both for at least one record.");

        var patient = await users.FindByIdAsync(patientUserId);
        if (patient is null || patient.Id == doctorUserId || !await users.IsInRoleAsync(patient, AppRoles.Patient))
            return new AccessRequestResult(false, "Choose a patient from the affiliated list.");

        var affiliated = await FindAffiliatedPatientIdsAsync(doctor, [patient.Id]);
        if (!affiliated.Contains(patient.Id))
            return new AccessRequestResult(false, "That patient is not affiliated with this doctor.");

        var name = patient.DisplayName.Trim();
        if (name.Length == 0)
            name = "Patient";

        DateOnly? dateOfBirth;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            dateOfBirth = await db.PatientProfiles.AsNoTracking()
                .Where(profile => profile.UserId == patient.Id)
                .Select(profile => profile.DateOfBirth)
                .FirstOrDefaultAsync();
        }

        return await SavePendingRequestAsync(
            doctorUserId,
            patient,
            name,
            dateOfBirth,
            lab,
            bloodPressure,
            sugar,
            medication,
            toDo,
            doctors);
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
        var emailAddress = PatientEmail(patient);
        var canText = destination.Problem == TextDestinationProblem.None && destination.E164 is not null;
        if (!canText && emailAddress is null)
            return new AccessRequestResult(true, NotSent(destination.Problem, hasEmail: false), false);

        var code = NewCode();
        StampCode(request, code);
        await db.SaveChangesAsync();

        var doctor = await users.FindByIdAsync(doctorUserId);
        return await DeliverCodeAsync(
            request.Id,
            canText ? destination.E164 : null,
            emailAddress,
            code,
            doctor?.DisplayName ?? "Your doctor",
            ChartAccessText.Describe(request.Lab, request.BloodPressure, request.Sugar, request.Medication, request.ToDo, request.Doctors),
            renew: true);
    }

    public async Task<AccessRequestResult> ApproveAsync(string patientUserId, int requestId, string code)
    {
        var entered = (code ?? "").Trim();
        if (entered.Length != 6 || !entered.All(char.IsDigit))
            return new AccessRequestResult(false, "Enter the 6-digit code from the text or the email.");

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

    private async Task<AccessRequestResult> SavePendingRequestAsync(
        string doctorUserId,
        ApplicationUser patient,
        string name,
        DateOnly? dateOfBirth,
        ChartAccessLevel lab,
        ChartAccessLevel bloodPressure,
        ChartAccessLevel sugar,
        ChartAccessLevel medication,
        ChartAccessLevel toDo,
        ChartAccessLevel doctors)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var pending = await db.ChartAccessRequests
            .Where(x => x.DoctorUserId == doctorUserId && x.PatientUserId == patient.Id && x.Status == AccessRequestStatus.Pending)
            .ToListAsync();
        if (pending.Count > 0)
            db.ChartAccessRequests.RemoveRange(pending);

        var destination = await ClassifyAsync(db, patient);
        var emailAddress = PatientEmail(patient);
        var canText = destination.Problem == TextDestinationProblem.None && destination.E164 is not null;
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
        if (canText || emailAddress is not null)
        {
            code = NewCode();
            StampCode(request, code);
        }

        db.ChartAccessRequests.Add(request);
        await db.SaveChangesAsync();

        if (code is null)
            return new AccessRequestResult(true, NotSent(destination.Problem, emailAddress is not null), false);

        var sender = await users.FindByIdAsync(doctorUserId);
        return await DeliverCodeAsync(
            request.Id,
            canText ? destination.E164 : null,
            emailAddress,
            code,
            sender?.DisplayName ?? "Your doctor",
            ChartAccessText.Describe(lab, bloodPressure, sugar, medication, toDo, doctors),
            renew: false);
    }

    private async Task<HashSet<string>> FindAffiliatedPatientIdsAsync(ApplicationUser doctor, IEnumerable<string> patientIds)
    {
        var patients = patientIds.ToHashSet(StringComparer.Ordinal);
        var affiliated = new HashSet<string>(StringComparer.Ordinal);
        if (patients.Count == 0)
            return affiliated;

        var patientIdList = patients.ToList();
        var doctorKey = NormalizePersonName(doctor.FullName);
        await using var db = await dbFactory.CreateDbContextAsync();

        var careTeam = await db.Doctors.AsNoTracking()
            .Where(row => patientIdList.Contains(row.UserId))
            .Select(row => new { row.UserId, row.Name })
            .ToListAsync();
        foreach (var row in careTeam)
        {
            if (doctorKey.Length > 0 && NormalizePersonName(row.Name) == doctorKey)
                affiliated.Add(row.UserId);
        }

        var appointments = await db.Appointments.AsNoTracking()
            .Where(row => patientIdList.Contains(row.UserId))
            .Select(row => new { row.UserId, row.ProviderName })
            .ToListAsync();
        foreach (var row in appointments)
        {
            if (doctorKey.Length > 0 && NormalizePersonName(row.ProviderName) == doctorKey)
                affiliated.Add(row.UserId);
        }

        var medications = await db.Medications.AsNoTracking()
            .Where(row => patientIdList.Contains(row.UserId))
            .Select(row => new { row.UserId, row.PrescribedBy, row.PrescribingDoctorUserId })
            .ToListAsync();
        foreach (var row in medications)
        {
            if (row.PrescribingDoctorUserId == doctor.Id
                || (doctorKey.Length > 0 && NormalizePersonName(row.PrescribedBy) == doctorKey))
                affiliated.Add(row.UserId);
        }

        var grantIds = await db.ChartAccessGrants.AsNoTracking()
            .Where(grant => grant.DoctorUserId == doctor.Id && grant.RevokedAt == null)
            .Select(grant => grant.PatientUserId)
            .ToListAsync();
        var pendingIds = await db.ChartAccessRequests.AsNoTracking()
            .Where(request => request.DoctorUserId == doctor.Id
                && request.Status == AccessRequestStatus.Pending
                && request.PatientUserId != null)
            .Select(request => request.PatientUserId!)
            .ToListAsync();
        foreach (var id in grantIds)
        {
            if (patients.Contains(id))
                affiliated.Add(id);
        }

        foreach (var id in pendingIds)
        {
            if (patients.Contains(id))
                affiliated.Add(id);
        }

        return affiliated;
    }

    private static bool NoSections(
        ChartAccessLevel lab,
        ChartAccessLevel bloodPressure,
        ChartAccessLevel sugar,
        ChartAccessLevel medication,
        ChartAccessLevel toDo,
        ChartAccessLevel doctors) =>
        lab == ChartAccessLevel.None
        && bloodPressure == ChartAccessLevel.None
        && sugar == ChartAccessLevel.None
        && medication == ChartAccessLevel.None
        && toDo == ChartAccessLevel.None
        && doctors == ChartAccessLevel.None;

    private static readonly string[] NameSuffixes = ["md", "do", "np", "pa", "rn", "rd", "phd", "fnp", "pac"];

    private static string NormalizePersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var letters = new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
        var parts = letters.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[0] is "dr" or "doctor")
            parts.RemoveAt(0);
        while (parts.Count > 1 && NameSuffixes.Contains(parts[^1], StringComparer.Ordinal))
            parts.RemoveAt(parts.Count - 1);
        return string.Join(' ', parts);
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

    private async Task<AccessRequestResult> DeliverCodeAsync(
        int requestId,
        string? e164,
        string? emailAddress,
        string code,
        string doctorName,
        string records,
        bool renew)
    {
        var textAttempted = !string.IsNullOrWhiteSpace(e164);
        var emailAttempted = !string.IsNullOrWhiteSpace(emailAddress);
        var textSent = false;
        string? textProblem = null;

        if (textAttempted)
        {
            try
            {
                var sent = await twilio.TextAsync(e164!, SmsCopy.ApprovalCode(code), CancellationToken.None, confirmDelivery: true);
                textSent = sent.Ok;
                if (!textSent)
                {
                    logger.LogWarning("Access approval text for request {RequestId} was not sent ({Detail}).", requestId, sent.Detail ?? "unknown");
                    textProblem = DeliveryProblem(sent.Detail, emailAttempted);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Access approval text for request {RequestId} was not sent.", requestId);
                textProblem = NotSent(TextDestinationProblem.None, emailAttempted);
            }
        }

        var emailSent = false;
        if (emailAttempted)
            emailSent = await mail.TrySendAccessApprovalAsync(emailAddress!, doctorName, records, code);

        if (textSent || emailSent)
        {
            return new AccessRequestResult(
                true,
                SentMessage(textSent, emailSent, textAttempted, emailAttempted, renew));
        }

        if (textProblem is not null && emailAttempted)
            return new AccessRequestResult(true, textProblem + " The same request could not be emailed.", false);

        if (textProblem is not null)
            return new AccessRequestResult(true, textProblem, false);

        return new AccessRequestResult(true, "The request is saved, but the confirmation email was not sent.", false);
    }

    private static void StampCode(ChartAccessRequest request, string code)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        request.CodeSalt = salt;
        request.CodeHash = HashCode(salt, code);
        request.CodeExpiresAt = DateTime.UtcNow.AddMinutes(CodeLifetimeMinutes);
        request.CodeAttempts = 0;
    }

    private static string? PatientEmail(ApplicationUser patient)
    {
        var raw = patient.Email?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!MailAddress.TryCreate(raw, out var parsed) || !parsed.Host.Contains('.', StringComparison.Ordinal))
            return null;

        return parsed.Address;
    }

    private static string SentMessage(bool textSent, bool emailSent, bool textAttempted, bool emailAttempted, bool renew)
    {
        var lead = renew ? "A new confirmation code" : "A confirmation code";
        if (textSent && emailSent)
            return $"{lead} was sent to the phone and the email on the patient's account. You can open a chart after they enter that code.";

        if (textSent && emailAttempted)
            return $"{lead} was sent to the phone on the patient's account. The same request could not be emailed.";

        if (textSent)
            return $"{lead} was sent to the phone on the patient's account. You can open a chart after they enter that code.";

        if (emailSent && textAttempted)
            return "The confirmation text was not sent. The same code was sent to the email on the patient's account. You can open a chart after they enter that code.";

        return $"{lead} was sent to the email on the patient's account. You can open a chart after they enter that code.";
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

    private static string NotSent(TextDestinationProblem problem, bool hasEmail) => problem switch
    {
        TextDestinationProblem.NoPhone =>
            "The request is saved, but the confirmation text was not sent. No phone number is saved on that account." + EmailGap(hasEmail),
        TextDestinationProblem.InvalidPhone =>
            "The request is saved, but the confirmation text was not sent. The phone number saved on that account is not a valid US number." + EmailGap(hasEmail),
        TextDestinationProblem.NoConsent =>
            "The request is saved, but the confirmation text was not sent. The text-message consent box on that patient's profile is not checked." + EmailGap(hasEmail),
        _ =>
            "The request is saved, but the confirmation text was not sent. Use Send a new code to try again." + EmailGap(hasEmail)
    };

    private static string PatientNotSent(TextDestinationProblem problem, bool hasEmail) => problem switch
    {
        TextDestinationProblem.NoPhone =>
            "The confirmation text was not sent. Add a mobile number on your profile, then ask the doctor to send a new code." + EmailGap(hasEmail, yours: true),
        TextDestinationProblem.InvalidPhone =>
            "The confirmation text was not sent. The phone number on your profile is not a valid US number. Correct it, then ask the doctor to send a new code." + EmailGap(hasEmail, yours: true),
        TextDestinationProblem.NoConsent =>
            "The confirmation text was not sent. Check the text-message consent box on your profile, then ask the doctor to send a new code." + EmailGap(hasEmail, yours: true),
        _ =>
            "The confirmation text was not sent. Ask the doctor to send a new code." + EmailGap(hasEmail, yours: true)
    };

    private static string EmailGap(bool hasEmail, bool yours = false) =>
        hasEmail ? "" : yours ? " No email is saved on your account." : " No email is saved on that account.";

    private static string DeliveryProblem(string? detail, bool hasEmail)
    {
        if (!string.IsNullOrWhiteSpace(detail)
            && (detail.Contains("21211", StringComparison.Ordinal)
                || detail.Contains("21614", StringComparison.Ordinal)
                || detail.Contains("not a valid US number", StringComparison.OrdinalIgnoreCase)))
        {
            return NotSent(TextDestinationProblem.InvalidPhone, hasEmail);
        }

        if (!string.IsNullOrWhiteSpace(detail) && detail.Contains("21610", StringComparison.Ordinal))
            return "The request is saved, but the confirmation text was not sent. That number has opted out of text messages." + EmailGap(hasEmail);

        if (!string.IsNullOrWhiteSpace(detail)
            && (detail.Contains("30034", StringComparison.Ordinal) || detail.Contains("30032", StringComparison.Ordinal)))
        {
            return "The request is saved, but the confirmation text was not sent. The carrier rejected the message." + EmailGap(hasEmail);
        }

        return NotSent(TextDestinationProblem.None, hasEmail);
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
