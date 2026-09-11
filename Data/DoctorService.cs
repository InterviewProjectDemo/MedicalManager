using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class DoctorService(ApplicationDbContext db)
{
    public async Task<List<Doctor>> GetDoctorsAsync(string userId)
    {
        await SyncDiscoveredDoctorsAsync(userId);
        return await db.Doctors
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    public Task<Doctor?> GetDoctorByIdAsync(int id, string userId) =>
        db.Doctors.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    public async Task SaveDoctorAsync(Doctor doctor)
    {
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
        var item = await GetDoctorByIdAsync(id, userId);
        if (item is null) return;
        db.Doctors.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task SyncDiscoveredDoctorsAsync(string userId)
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
            UpsertDiscovered(userId, name, DoctorSource.FromAppointment, existing, now);
        }

        foreach (var raw in medicationNames)
        {
            var name = raw.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            UpsertDiscovered(userId, name, DoctorSource.FromMedication, existing, now);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync();
        }
    }

    private void UpsertDiscovered(
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
