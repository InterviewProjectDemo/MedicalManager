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
