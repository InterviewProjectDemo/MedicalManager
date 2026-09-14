using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
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
