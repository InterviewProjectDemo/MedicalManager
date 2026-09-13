using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class HealthRecordService(ApplicationDbContext db, DoctorService doctors)
{
    public Task<List<BloodPressureReading>> GetBloodPressureAsync(string userId, int take = 30) =>
        db.BloodPressureReadings.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(take)
            .ToListAsync();

    public Task<BloodPressureReading?> GetBloodPressureByIdAsync(int id, string userId) =>
        db.BloodPressureReadings.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    public async Task SaveBloodPressureAsync(BloodPressureReading reading)
    {
        if (reading.Id == 0) db.BloodPressureReadings.Add(reading);
        else db.BloodPressureReadings.Update(reading);
        await db.SaveChangesAsync();
    }

    public async Task DeleteBloodPressureAsync(int id, string userId)
    {
        var item = await GetBloodPressureByIdAsync(id, userId);
        if (item is null) return;
        db.BloodPressureReadings.Remove(item);
        await db.SaveChangesAsync();
    }

    public Task<List<SugarReading>> GetSugarAsync(string userId, int take = 30) =>
        db.SugarReadings.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(take)
            .ToListAsync();

    public Task<List<BloodPressureReading>> GetBloodPressureInRangeAsync(
        string userId, DateTime startInclusive, DateTime endExclusive) =>
        db.BloodPressureReadings
            .Where(x => x.UserId == userId && x.RecordedAt >= startInclusive && x.RecordedAt < endExclusive)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();

    public Task<List<SugarReading>> GetSugarInRangeAsync(
        string userId, DateTime startInclusive, DateTime endExclusive) =>
        db.SugarReadings
            .Where(x => x.UserId == userId && x.RecordedAt >= startInclusive && x.RecordedAt < endExclusive)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();

    public Task<DateTime?> GetEarliestBloodPressureDateAsync(string userId) =>
        db.BloodPressureReadings
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.RecordedAt)
            .Select(x => (DateTime?)x.RecordedAt)
            .FirstOrDefaultAsync();

    public Task<DateTime?> GetEarliestSugarDateAsync(string userId) =>
        db.SugarReadings
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.RecordedAt)
            .Select(x => (DateTime?)x.RecordedAt)
            .FirstOrDefaultAsync();

    public Task<SugarReading?> GetSugarByIdAsync(int id, string userId) =>
        db.SugarReadings.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    public async Task SaveSugarAsync(SugarReading reading)
    {
        if (reading.Id == 0) db.SugarReadings.Add(reading);
        else db.SugarReadings.Update(reading);
        await db.SaveChangesAsync();
    }

    public async Task DeleteSugarAsync(int id, string userId)
    {
        var item = await GetSugarByIdAsync(id, userId);
        if (item is null) return;
        db.SugarReadings.Remove(item);
        await db.SaveChangesAsync();
    }

    public Task<List<Medication>> GetMedicationsAsync(string userId, bool activeOnly = false)
    {
        var query = db.Medications.Where(x => x.UserId == userId);
        if (activeOnly) query = query.Where(x => x.IsActive);
        return query.OrderBy(x => x.Name).ToListAsync();
    }

    public Task<Medication?> GetMedicationByIdAsync(int id, string userId) =>
        db.Medications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    public async Task SaveMedicationAsync(Medication medication)
    {
        if (medication.Id == 0) db.Medications.Add(medication);
        else db.Medications.Update(medication);
        await db.SaveChangesAsync();
        await doctors.SyncDiscoveredDoctorsAsync(medication.UserId);
    }

    public async Task DeleteMedicationAsync(int id, string userId)
    {
        var item = await GetMedicationByIdAsync(id, userId);
        if (item is null) return;
        db.Medications.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetAppointmentsAsync(string userId)
    {
        await ApplyAutoMissedAsync(userId);
        return await db.Appointments.Where(x => x.UserId == userId)
            .OrderBy(x => x.StartsAt < DateTime.Now)
            .ThenBy(x => x.StartsAt)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetUpcomingAppointmentsAsync(string userId, int take = 8)
    {
        await ApplyAutoMissedAsync(userId);
        return await db.Appointments.Where(x => x.UserId == userId
                && x.Status == AppointmentStatus.Scheduled
                && x.StartsAt >= DateTime.Now)
            .OrderBy(x => x.StartsAt)
            .Take(take)
            .ToListAsync();
    }

    public async Task<Appointment?> GetAppointmentByIdAsync(int id, string userId)
    {
        await ApplyAutoMissedAsync(userId);
        return await db.Appointments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
    }

    public async Task<List<Appointment>> GetMissedAppointmentsThisMonthAsync(string userId)
    {
        await ApplyAutoMissedAsync(userId);
        var now = DateTime.Now;
        var start = new DateTime(now.Year, now.Month, 1);
        var end = start.AddMonths(1);
        return await db.Appointments
            .Where(x => x.UserId == userId
                && x.Status == AppointmentStatus.Missed
                && x.StartsAt >= start
                && x.StartsAt < end)
            .OrderBy(x => x.StartsAt)
            .ToListAsync();
    }

    public async Task SaveAppointmentAsync(Appointment appointment)
    {
        if (AppointmentRules.ShouldAutoMiss(appointment, DateTime.Now))
            appointment.Status = AppointmentStatus.Missed;
        if (appointment.Id == 0) db.Appointments.Add(appointment);
        else db.Appointments.Update(appointment);
        await db.SaveChangesAsync();
        await doctors.SyncDiscoveredDoctorsAsync(appointment.UserId);
    }

    public async Task DeleteAppointmentAsync(int id, string userId)
    {
        var item = await GetAppointmentByIdAsync(id, userId);
        if (item is null) return;
        db.Appointments.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetAppointmentsForDateAsync(string userId, DateOnly date)
    {
        await ApplyAutoMissedAsync(userId);
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return await db.Appointments
            .Where(x => x.UserId == userId && x.StartsAt >= start && x.StartsAt < end)
            .OrderBy(x => x.StartsAt)
            .ToListAsync();
    }

    public async Task UpdateAppointmentStatusAsync(int id, string userId, AppointmentStatus status)
    {
        var appt = await GetAppointmentByIdAsync(id, userId);
        if (appt is null) return;
        appt.Status = status;
        if (AppointmentRules.ShouldAutoMiss(appt, DateTime.Now))
            appt.Status = AppointmentStatus.Missed;
        await db.SaveChangesAsync();
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(string userId)
    {
        await ApplyAutoMissedAsync(userId);
        var bp = await GetBloodPressureAsync(userId, 30);
        var sugar = await GetSugarAsync(userId, 30);
        var meds = await GetMedicationsAsync(userId, activeOnly: true);
        var upcoming = await GetUpcomingAppointmentsAsync(userId);
        var missedThisMonth = await GetMissedAppointmentsThisMonthAsync(userId);
        return new DashboardSnapshot(bp, sugar, meds, upcoming, missedThisMonth);
    }

    private async Task ApplyAutoMissedAsync(string userId)
    {
        var now = DateTime.Now;
        var overdue = await db.Appointments
            .Where(x => x.UserId == userId
                && x.Status != AppointmentStatus.Completed
                && x.Status != AppointmentStatus.Cancelled
                && x.Status != AppointmentStatus.Missed
                && (x.EndsAt ?? x.StartsAt) < now)
            .ToListAsync();

        if (overdue.Count == 0) return;

        foreach (var appt in overdue)
            appt.Status = AppointmentStatus.Missed;

        await db.SaveChangesAsync();
    }
}

public sealed record DashboardSnapshot(
    List<BloodPressureReading> BloodPressure,
    List<SugarReading> Sugar,
    List<Medication> Medications,
    List<Appointment> UpcomingAppointments,
    List<Appointment> MissedThisMonth);
