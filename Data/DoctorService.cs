using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class DoctorService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<List<Doctor>> GetDoctorsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await SyncDiscoveredDoctorsAsync(db, userId);

        var doctors = await db.Doctors
            .Where(x => x.UserId == userId)
            .ToListAsync();

        return doctors
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Doctor?> GetDoctorByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetDoctorByIdAsync(db, id, userId);
    }

    public async Task SaveDoctorAsync(Doctor doctor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        doctor.UpdatedAt = DateTime.UtcNow;

        if (doctor.Id == 0)
        {
            doctor.CreatedAt = DateTime.UtcNow;
            if (!doctor.IsAutoDiscovered)
            {
                doctor.Source = DoctorSource.Manual;
            }

            db.Doctors.Add(doctor);
        }
        else
        {
            db.Doctors.Update(doctor);
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteDoctorAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await GetDoctorByIdAsync(db, id, userId);
        if (item is null) return;
        db.Doctors.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task SyncDiscoveredDoctorsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await SyncDiscoveredDoctorsAsync(db, userId);
    }

    private static async Task SyncDiscoveredDoctorsAsync(ApplicationDbContext db, string userId)
    {
        var appointmentNames = await db.Appointments
            .Where(x => x.UserId == userId && x.ProviderName != "")
            .Select(x => x.ProviderName)
            .ToListAsync();

        var medicationNames = await db.Medications
            .Where(x => x.UserId == userId && x.PrescribedBy != "")
            .Select(x => x.PrescribedBy)
            .ToListAsync();

        var existing = await db.Doctors.Where(x => x.UserId == userId).ToListAsync();
        var now = DateTime.UtcNow;

        foreach (var raw in appointmentNames)
        {
            var name = raw.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            UpsertDiscovered(db, userId, name, DoctorSource.FromAppointment, existing, now);
        }

        foreach (var raw in medicationNames)
        {
            var name = raw.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            UpsertDiscovered(db, userId, name, DoctorSource.FromMedication, existing, now);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private static Task<Doctor?> GetDoctorByIdAsync(ApplicationDbContext db, int id, string userId) =>
        db.Doctors.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    private static void UpsertDiscovered(
        ApplicationDbContext db,
        string userId,
        string name,
        DoctorSource source,
        List<Doctor> existing,
        DateTime now)
    {
        var match = existing.FirstOrDefault(x =>
            string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var created = new Doctor
            {
                UserId = userId,
                Name = name,
                Source = source,
                IsAutoDiscovered = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            existing.Add(created);
            db.Doctors.Add(created);
            return;
        }

        if (!match.IsAutoDiscovered || match.Source == DoctorSource.Manual)
        {
            return;
        }

        match.UpdatedAt = now;
    }
}
