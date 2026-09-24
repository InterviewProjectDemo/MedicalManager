using System.Security.Claims;
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

    public async Task SaveReminderSettingsAsync(
        string userId,
        string? homeAddress,
        string? namePronunciation,
        bool remindersEnabled)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
            ?? throw new InvalidOperationException("Patient profile not found.");

        profile.HomeAddress = string.IsNullOrWhiteSpace(homeAddress) ? null : homeAddress.Trim();
        profile.NamePronunciation = string.IsNullOrWhiteSpace(namePronunciation) ? null : namePronunciation.Trim();
        profile.AppointmentRemindersEnabled = remindersEnabled;
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

public sealed record PatientScope(
    ApplicationUser CurrentUser,
    ApplicationUser? Patient,
    bool IsDoctor,
    bool IsDoctorViewingPatient,
    bool NeedsPatientSelection)
{
    public string? PatientId => Patient?.Id;
}
