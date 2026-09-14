using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class HealthRecordService(IDbContextFactory<ApplicationDbContext> dbFactory, DoctorService doctors)
{
    public async Task<List<BloodPressureReading>> GetBloodPressureAsync(string userId, int take = 30)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetBloodPressureAsync(db, userId, take);
    }

    public async Task<BloodPressureReading?> GetBloodPressureByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetBloodPressureByIdAsync(db, id, userId);
    }

    public async Task SaveBloodPressureAsync(BloodPressureReading reading)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (reading.Id == 0) db.BloodPressureReadings.Add(reading);
        else db.BloodPressureReadings.Update(reading);
        await db.SaveChangesAsync();
    }

    public async Task DeleteBloodPressureAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await GetBloodPressureByIdAsync(db, id, userId);
        if (item is null) return;
        db.BloodPressureReadings.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<SugarReading>> GetSugarAsync(string userId, int take = 30)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetSugarAsync(db, userId, take);
    }

    public async Task<List<BloodPressureReading>> GetBloodPressureInRangeAsync(
        string userId, DateTime startInclusive, DateTime endExclusive)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BloodPressureReadings
            .Where(x => x.UserId == userId && x.RecordedAt >= startInclusive && x.RecordedAt < endExclusive)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();
    }

    public async Task<List<SugarReading>> GetSugarInRangeAsync(
        string userId, DateTime startInclusive, DateTime endExclusive)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SugarReadings
            .Where(x => x.UserId == userId && x.RecordedAt >= startInclusive && x.RecordedAt < endExclusive)
            .OrderBy(x => x.RecordedAt)
            .ToListAsync();
    }

    public async Task<DateTime?> GetEarliestBloodPressureDateAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.BloodPressureReadings
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.RecordedAt)
            .Select(x => (DateTime?)x.RecordedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<DateTime?> GetEarliestSugarDateAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SugarReadings
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.RecordedAt)
            .Select(x => (DateTime?)x.RecordedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<SugarReading?> GetSugarByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetSugarByIdAsync(db, id, userId);
    }

    public async Task SaveSugarAsync(SugarReading reading)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (reading.Id == 0) db.SugarReadings.Add(reading);
        else db.SugarReadings.Update(reading);
        await db.SaveChangesAsync();
    }

    public async Task DeleteSugarAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await GetSugarByIdAsync(db, id, userId);
        if (item is null) return;
        db.SugarReadings.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<Medication>> GetMedicationsAsync(string userId, bool activeOnly = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetMedicationsAsync(db, userId, activeOnly);
    }

    public async Task<Medication?> GetMedicationByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetMedicationByIdAsync(db, id, userId);
    }

    public async Task SaveMedicationAsync(Medication medication)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (medication.Id == 0) db.Medications.Add(medication);
        else db.Medications.Update(medication);
        await db.SaveChangesAsync();
        await doctors.SyncDiscoveredDoctorsAsync(medication.UserId);
    }

    public async Task DeleteMedicationAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await GetMedicationByIdAsync(db, id, userId);
        if (item is null) return;
        db.Medications.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetAppointmentsAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        return await db.Appointments.Where(x => x.UserId == userId)
            .OrderBy(x => x.StartsAt < DateTime.Now)
            .ThenBy(x => x.StartsAt)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetUpcomingAppointmentsAsync(string userId, int take = 8)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        return await GetUpcomingAppointmentsAsync(db, userId, take);
    }

    public async Task<Appointment?> GetAppointmentByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        return await GetAppointmentByIdAsync(db, id, userId);
    }

    public async Task<List<Appointment>> GetMissedAppointmentsThisMonthAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        return await GetMissedAppointmentsThisMonthAsync(db, userId);
    }

    public async Task SaveAppointmentAsync(Appointment appointment)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (AppointmentRules.ShouldAutoMiss(appointment, DateTime.Now))
            appointment.Status = AppointmentStatus.Missed;
        if (appointment.Id == 0) db.Appointments.Add(appointment);
        else db.Appointments.Update(appointment);
        await db.SaveChangesAsync();
        await doctors.SyncDiscoveredDoctorsAsync(appointment.UserId);
    }

    public async Task DeleteAppointmentAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        var item = await GetAppointmentByIdAsync(db, id, userId);
        if (item is null) return;
        db.Appointments.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetAppointmentsForDateAsync(string userId, DateOnly date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return await db.Appointments
            .Where(x => x.UserId == userId && x.StartsAt >= start && x.StartsAt < end)
            .OrderBy(x => x.StartsAt)
            .ToListAsync();
    }

    public async Task UpdateAppointmentStatusAsync(int id, string userId, AppointmentStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        var appt = await GetAppointmentByIdAsync(db, id, userId);
        if (appt is null) return;
        appt.Status = status;
        if (AppointmentRules.ShouldAutoMiss(appt, DateTime.Now))
            appt.Status = AppointmentStatus.Missed;
        await db.SaveChangesAsync();
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        var bp = await GetBloodPressureAsync(db, userId, 30);
        var sugar = await GetSugarAsync(db, userId, 30);
        var meds = await GetMedicationsAsync(db, userId, activeOnly: true);
        var upcoming = await GetUpcomingAppointmentsAsync(db, userId);
        var missedThisMonth = await GetMissedAppointmentsThisMonthAsync(db, userId);
        return new DashboardSnapshot(bp, sugar, meds, upcoming, missedThisMonth);
    }

    private static Task<List<BloodPressureReading>> GetBloodPressureAsync(
        ApplicationDbContext db, string userId, int take) =>
        db.BloodPressureReadings.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(take)
            .ToListAsync();

    private static Task<BloodPressureReading?> GetBloodPressureByIdAsync(
        ApplicationDbContext db, int id, string userId) =>
        db.BloodPressureReadings.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    private static Task<List<SugarReading>> GetSugarAsync(ApplicationDbContext db, string userId, int take) =>
        db.SugarReadings.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(take)
            .ToListAsync();

    private static Task<SugarReading?> GetSugarByIdAsync(ApplicationDbContext db, int id, string userId) =>
        db.SugarReadings.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    private static Task<List<Medication>> GetMedicationsAsync(
        ApplicationDbContext db, string userId, bool activeOnly)
    {
        var query = db.Medications.Where(x => x.UserId == userId);
        if (activeOnly) query = query.Where(x => x.IsActive);
        return query.OrderBy(x => x.Name).ToListAsync();
    }

    private static Task<Medication?> GetMedicationByIdAsync(ApplicationDbContext db, int id, string userId) =>
        db.Medications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    private static Task<Appointment?> GetAppointmentByIdAsync(ApplicationDbContext db, int id, string userId) =>
        db.Appointments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

    private static Task<List<Appointment>> GetUpcomingAppointmentsAsync(
        ApplicationDbContext db, string userId, int take = 8) =>
        db.Appointments.Where(x => x.UserId == userId
                && x.Status == AppointmentStatus.Scheduled
                && x.StartsAt >= DateTime.Now)
            .OrderBy(x => x.StartsAt)
            .Take(take)
            .ToListAsync();

    private static Task<List<Appointment>> GetMissedAppointmentsThisMonthAsync(
        ApplicationDbContext db, string userId)
    {
        var now = DateTime.Now;
        var start = new DateTime(now.Year, now.Month, 1);
        var end = start.AddMonths(1);
        return db.Appointments
            .Where(x => x.UserId == userId
                && x.Status == AppointmentStatus.Missed
                && x.StartsAt >= start
                && x.StartsAt < end)
            .OrderBy(x => x.StartsAt)
            .ToListAsync();
    }

    private static async Task ApplyAutoMissedAsync(ApplicationDbContext db, string userId)
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
