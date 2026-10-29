using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class PatientAccessService(
    UserManager<ApplicationUser> users,
    IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<PatientScope> ResolveAsync(ClaimsPrincipal principal, string? patientUserId)
    {
        var current = await users.GetUserAsync(principal)
            ?? throw new InvalidOperationException("You must be signed in.");

        var isDoctor = principal.IsInRole(AppRoles.Doctor);
        if (!isDoctor)
        {
            if (!string.IsNullOrWhiteSpace(patientUserId) && patientUserId != current.Id)
            {
                throw new UnauthorizedAccessException("Patients can only view their own records.");
            }

            return new PatientScope(current, current, IsDoctor: false, IsDoctorViewingPatient: false, NeedsPatientSelection: false);
        }

        if (string.IsNullOrWhiteSpace(patientUserId))
        {
            return new PatientScope(current, Patient: null, IsDoctor: true, IsDoctorViewingPatient: false, NeedsPatientSelection: true);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == patientUserId)
            ?? throw new InvalidOperationException("Patient not found.");

        return new PatientScope(current, target, IsDoctor: true, IsDoctorViewingPatient: true, NeedsPatientSelection: false);
    }

    public async Task<IReadOnlyList<ApplicationUser>> ListPatientsAsync()
    {
        var patients = await users.GetUsersInRoleAsync(AppRoles.Patient);
        return patients
            .OrderBy(p => p.FullName)
            .ThenBy(p => p.Email)
            .ToList();
    }

    public async Task EnsureProfileAsync(ApplicationUser user)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.PatientProfiles.AnyAsync(p => p.UserId == user.Id))
        {
            return;
        }

        db.PatientProfiles.Add(new PatientProfile { UserId = user.Id });
        await db.SaveChangesAsync();
    }

    public async Task RecordPolicyAcceptanceAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        var acceptedAt = DateTime.UtcNow;
        profile.PrivacyPolicyAcceptedAt = acceptedAt;
        profile.HipaaNoticeAcceptedAt = acceptedAt;
        await db.SaveChangesAsync();
    }

    public async Task<PatientProfile?> GetProfileAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
    }

    public async Task<bool> NeedsOnboardingAsync(ClaimsPrincipal principal)
    {
        if (!principal.IsInRole(AppRoles.Patient))
        {
            return false;
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var profile = await GetProfileAsync(userId);
        return profile is not { HasCompletedOnboarding: true };
    }

    public async Task<string> ResolvePostAuthRedirectAsync(ApplicationUser user, string? returnUrl)
    {
        if (!await users.IsInRoleAsync(user, AppRoles.Patient))
        {
            return NormalizeReturnUrl(returnUrl);
        }

        var profile = await GetProfileAsync(user.Id);
        if (profile is { HasCompletedOnboarding: true })
        {
            return NormalizeReturnUrl(returnUrl);
        }

        var destination = NormalizeReturnUrl(returnUrl);
        if (destination.Contains("/onboarding", StringComparison.OrdinalIgnoreCase))
        {
            return destination;
        }

        return QueryHelpers.AddQueryString("/onboarding", "returnUrl", destination);
    }

    public async Task SaveProfileDetailsAsync(
        string userId,
        string fullName,
        string? phone,
        string? sex,
        string? homeAddress,
        DateOnly? dateOfBirth,
        string? notes)
    {
        var trimmedName = fullName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new InvalidOperationException("Full name is required.");
        if (trimmedName.Length > 200)
            throw new InvalidOperationException("Full name must be 200 characters or fewer.");

        if (dateOfBirth is DateOnly dob)
        {
            if (dob > DateOnly.FromDateTime(DateTime.Today))
                throw new InvalidOperationException("Date of birth cannot be in the future.");
            if (dob < new DateOnly(1900, 1, 1))
                throw new InvalidOperationException("Date of birth is not valid.");
        }

        var phoneValue = NormalizeOptional(phone, 40, "Phone");
        var sexValue = NormalizeOptional(sex, 40, "Sex");
        var addressValue = NormalizeOptional(homeAddress, 300, "Home address");
        var notesValue = NormalizeOptional(notes, 400, "Notes");

        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        user.FullName = trimmedName;
        if (!string.Equals(user.PhoneNumber, phoneValue, StringComparison.Ordinal))
        {
            user.PhoneNumber = phoneValue;
            user.PhoneNumberConfirmed = false;
        }

        profile.Phone = phoneValue;
        profile.Sex = sexValue;
        profile.HomeAddress = addressValue;
        profile.DateOfBirth = dateOfBirth;
        profile.Notes = notesValue;
        await db.SaveChangesAsync();
    }

    public async Task SyncProfilePhoneAsync(string userId, string? phone)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
            return;

        profile.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        await db.SaveChangesAsync();
    }

    public async Task<FamilyContactSummary> GetFamilyContactAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.PatientProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new FamilyContactSummary(
                p.FamilyContactName,
                p.FamilyContactPhone,
                p.FamilyContactPhotoUpdatedAt))
            .FirstOrDefaultAsync();

        return row ?? new FamilyContactSummary(null, null, null);
    }

    public async Task<EmergencyCaller> GetEmergencyCallerAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var phone = await db.PatientProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.Phone)
            .FirstOrDefaultAsync();

        var name = string.IsNullOrWhiteSpace(user?.FullName) ? user?.Email ?? "Patient" : user.FullName.Trim();
        var number = string.IsNullOrWhiteSpace(phone) ? user?.PhoneNumber : phone;
        return new EmergencyCaller(name, string.IsNullOrWhiteSpace(number) ? null : number.Trim());
    }

    public async Task SaveFamilyContactAsync(string userId, string? name, string? phone)
    {
        var nameValue = NormalizeOptional(name, 200, "Family contact name");
        var phoneValue = NormalizeOptional(phone, 40, "Family contact phone");
        if (nameValue is null)
            throw new InvalidOperationException("Family contact name is required.");
        if (phoneValue is null)
            throw new InvalidOperationException("Family contact phone is required.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        profile.FamilyContactName = nameValue;
        profile.FamilyContactPhone = phoneValue;
        await db.SaveChangesAsync();
    }

    public async Task ClearFamilyContactAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        profile.FamilyContactName = null;
        profile.FamilyContactPhone = null;
        profile.FamilyContactPhotoData = null;
        profile.FamilyContactPhotoContentType = null;
        profile.FamilyContactPhotoUpdatedAt = null;
        await db.SaveChangesAsync();
    }

    public async Task SaveReminderSettingsAsync(
        string userId,
        string? homeAddress,
        string? namePronunciation,
        bool appointmentRemindersEnabled,
        bool medicationRemindersEnabled,
        bool toDoRemindersEnabled)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        profile.HomeAddress = string.IsNullOrWhiteSpace(homeAddress) ? null : homeAddress.Trim();
        profile.NamePronunciation = string.IsNullOrWhiteSpace(namePronunciation) ? null : namePronunciation.Trim();
        profile.AppointmentRemindersEnabled = appointmentRemindersEnabled;
        profile.MedicationRemindersEnabled = medicationRemindersEnabled;
        profile.ToDoRemindersEnabled = toDoRemindersEnabled;
        await db.SaveChangesAsync();
    }

    private static string? NormalizeOptional(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new InvalidOperationException($"{label} must be {maxLength} characters or fewer.");

        return trimmed;
    }

    public async Task<MonitorSetup> GetMonitorSetupAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await (
            from profile in db.PatientProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
            where profile.UserId == userId
            select new { profile.MonitorAccessCode, profile.MonitorLinkToken, user.FullName })
            .FirstOrDefaultAsync();

        if (row is null)
            return new MonitorSetup(null, null, "there");

        return new MonitorSetup(row.MonitorAccessCode, row.MonitorLinkToken, FirstName(row.FullName));
    }

    public async Task<MonitorLink?> FindMonitorByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
            return null;

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await (
            from profile in db.PatientProfiles.AsNoTracking()
            join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
            where profile.MonitorLinkToken == token
            select new { profile.UserId, profile.MonitorAccessCode, user.FullName })
            .FirstOrDefaultAsync();

        return row is null
            ? null
            : new MonitorLink(row.UserId, FirstName(row.FullName), row.MonitorAccessCode);
    }

    public async Task<(string Token, bool Changed)> SaveMonitorCodeAsync(string userId, string code)
    {
        var digits = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (!Regex.IsMatch(digits, "^[0-9]{4}$"))
            throw new InvalidOperationException("Enter four numbers, like 2468.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        var changed = !string.Equals(profile.MonitorAccessCode, digits, StringComparison.Ordinal);
        profile.MonitorAccessCode = digits;
        if (string.IsNullOrWhiteSpace(profile.MonitorLinkToken))
            profile.MonitorLinkToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        await db.SaveChangesAsync();
        return (profile.MonitorLinkToken, changed);
    }

    private static string FirstName(string? fullName)
    {
        var name = (fullName ?? "").Trim();
        if (name.Length == 0)
            return "your family member";

        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    public async Task CompleteOnboardingAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        profile.HasCompletedOnboarding = true;
        profile.OnboardingCompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static string NormalizeReturnUrl(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
}

public sealed record MonitorSetup(string? Code, string? Token, string FirstName);

public sealed record MonitorLink(string UserId, string FirstName, string? Code);

public sealed record FamilyContactSummary(string? Name, string? Phone, DateTime? PhotoUpdatedAt);

public sealed record EmergencyCaller(string Name, string? Phone);

public sealed record PatientScope(
    ApplicationUser CurrentUser,
    ApplicationUser? Patient,
    bool IsDoctor,
    bool IsDoctorViewingPatient,
    bool NeedsPatientSelection)
{
    public string? PatientId => Patient?.Id;
}
