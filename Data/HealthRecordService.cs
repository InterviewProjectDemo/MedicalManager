using System.Globalization;
using System.Text.RegularExpressions;
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

    public async Task<IReadOnlyList<string>> GetBloodPressureNoteSuggestionsAsync(
        string userId, int? excludeReadingId = null)
    {
        var now = DateTime.Now;
        var start = now - ReadingNotes.HistoryWindow;
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.BloodPressureReadings
            .Where(x => x.UserId == userId && x.RecordedAt >= start && x.RecordedAt <= now)
            .ToListAsync();
        return ReadingNotes.Suggest(
            rows.Select(row => new ReadingNotes.NoteSample(row.Id, row.UserId, row.RecordedAt, row.Notes)),
            userId,
            now,
            excludeReadingId);
    }

    public async Task<IReadOnlyList<string>> GetSugarNoteSuggestionsAsync(
        string userId, int? excludeReadingId = null)
    {
        var now = DateTime.Now;
        var start = now - ReadingNotes.HistoryWindow;
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.SugarReadings
            .Where(x => x.UserId == userId && x.RecordedAt >= start && x.RecordedAt <= now)
            .ToListAsync();
        return ReadingNotes.Suggest(
            rows.Select(row => new ReadingNotes.NoteSample(row.Id, row.UserId, row.RecordedAt, row.Notes)),
            userId,
            now,
            excludeReadingId);
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

    public async Task<int> AddPrescribedMedicationsAsync(
        string patientUserId,
        string doctorUserId,
        string doctorName,
        IReadOnlyList<MedicationOrder> orders)
    {
        var prepared = PrepareOrders(orders);
        var actor = string.IsNullOrWhiteSpace(doctorName) ? "Doctor" : doctorName.Trim();
        if (actor.Length > 120)
            actor = actor[..120];

        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        foreach (var order in prepared)
        {
            var medication = new Medication
            {
                UserId = patientUserId,
                Name = order.Name,
                Dosage = order.Dose,
                Frequency = order.Schedule,
                Purpose = order.Purpose,
                PrescribedBy = actor,
                PrescribingDoctorUserId = doctorUserId,
                IsActive = true,
                StartDate = DateOnly.FromDateTime(DateTime.Today),
                Days = MedicationDays.All
            };
            foreach (var (slot, time) in SchedulesFor(order.Schedule))
            {
                medication.Schedules.Add(new MedicationSchedule
                {
                    TimeSlot = slot,
                    AdministerAt = time
                });
            }

            db.Medications.Add(medication);
            db.MedicationChanges.Add(new MedicationChange
            {
                Medication = medication,
                PatientUserId = patientUserId,
                ActorUserId = doctorUserId,
                ActorName = actor,
                Summary = MedicationHistoryText.Added(order.Name, order.Dose, order.Schedule),
                ChangedAt = now
            });
        }

        await db.SaveChangesAsync();
        await doctors.SyncDiscoveredDoctorsAsync(patientUserId);
        return prepared.Count;
    }

    public async Task<List<MedicationChange>> GetMedicationChangesAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.MedicationChanges.AsNoTracking()
            .Where(change => change.PatientUserId == userId)
            .OrderByDescending(change => change.ChangedAt)
            .ThenByDescending(change => change.Id)
            .ToListAsync();
    }

    public async Task AddMedicationChangeAsync(MedicationChange change)
    {
        change.ActorName = change.ActorName.Trim();
        change.Summary = change.Summary.Trim();
        if (string.IsNullOrWhiteSpace(change.ActorName))
            change.ActorName = "Patient";
        if (change.ChangedAt == default)
            change.ChangedAt = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync();
        db.MedicationChanges.Add(change);
        await db.SaveChangesAsync();
    }

    private static List<MedicationOrder> PrepareOrders(IReadOnlyList<MedicationOrder> orders)
    {
        var prepared = new List<MedicationOrder>();
        foreach (var order in orders)
        {
            var name = (order.Name ?? "").Trim();
            var dose = (order.Dose ?? "").Trim();
            var schedule = (order.Schedule ?? "").Trim();
            var purpose = string.IsNullOrWhiteSpace(order.Purpose) ? null : order.Purpose.Trim();
            if (name.Length == 0 && dose.Length == 0 && schedule.Length == 0 && purpose is null)
                continue;
            if (name.Length == 0 || dose.Length == 0 || schedule.Length == 0)
                throw new InvalidOperationException("Each medication needs a name, dose, and time.");
            if (name.Length > 120)
                throw new InvalidOperationException("Medication name is too long.");
            if (dose.Length > 80)
                throw new InvalidOperationException("Dose is too long.");
            if (schedule.Length > 80)
                throw new InvalidOperationException("Time / schedule is too long.");
            if (purpose is not null && purpose.Length > 160)
                throw new InvalidOperationException("Purpose is too long.");
            prepared.Add(new MedicationOrder(name, dose, schedule, purpose));
        }

        if (prepared.Count == 0)
            throw new InvalidOperationException("Enter at least one medication.");
        return prepared;
    }

    private static readonly Regex ClockWithMeridiem = new(
        @"\b(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<meridiem>a\.?m\.?|p\.?m\.?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Clock24 = new(
        @"\b(?<hour>\d{1,2}):(?<minute>\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static List<(MedicationTimeSlot Slot, TimeOnly Time)> SchedulesFor(string text)
    {
        var times = new List<TimeOnly>();
        var covered = new List<(int Start, int Length)>();
        foreach (Match match in ClockWithMeridiem.Matches(text))
        {
            if (!TryClock(match.Groups["hour"].Value, match.Groups["minute"].Value, match.Groups["meridiem"].Value, out var time))
                continue;
            times.Add(time);
            covered.Add((match.Index, match.Length));
        }

        foreach (Match match in Clock24.Matches(text))
        {
            var insideMeridiem = false;
            foreach (var span in covered)
            {
                if (match.Index >= span.Start && match.Index < span.Start + span.Length)
                {
                    insideMeridiem = true;
                    break;
                }
            }

            if (insideMeridiem)
                continue;
            if (TryClock(match.Groups["hour"].Value, match.Groups["minute"].Value, null, out var time))
                times.Add(time);
        }

        var rows = new List<(MedicationTimeSlot, TimeOnly)>();
        var used = new HashSet<MedicationTimeSlot>();
        foreach (var time in times)
        {
            var slot = SlotFor(time);
            if (slot is null || !used.Add(slot.Value))
                continue;
            rows.Add((slot.Value, time));
        }

        return rows;
    }

    private static MedicationTimeSlot? SlotFor(TimeOnly time)
    {
        foreach (var slot in MedicationScheduleRules.AllSlots)
        {
            if (time >= MedicationScheduleRules.GetWindowStart(slot) && time <= MedicationScheduleRules.GetWindowEnd(slot))
                return slot;
        }

        return null;
    }

    private static bool TryClock(string hourText, string minuteText, string? meridiem, out TimeOnly time)
    {
        time = default;
        if (!int.TryParse(hourText, NumberStyles.None, CultureInfo.InvariantCulture, out var hour))
            return false;
        var minute = 0;
        if (!string.IsNullOrEmpty(minuteText)
            && !int.TryParse(minuteText, NumberStyles.None, CultureInfo.InvariantCulture, out minute))
            return false;
        if (minute is < 0 or > 59)
            return false;

        if (!string.IsNullOrWhiteSpace(meridiem))
        {
            if (hour is < 1 or > 12)
                return false;
            var pm = meridiem.StartsWith('p') || meridiem.StartsWith('P');
            if (hour == 12)
                hour = pm ? 12 : 0;
            else if (pm)
                hour += 12;
        }
        else if (hour is < 0 or > 23)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
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

    public async Task<List<ToDoItem>> GetToDosAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ToDoItems.Where(x => x.UserId == userId)
            .OrderBy(x => x.IsDone)
            .ThenBy(x => x.FinishBy)
            .ThenBy(x => x.Priority)
            .ToListAsync();
    }

    public async Task<ToDoItem?> GetToDoByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ToDoItems.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
    }

    public async Task SaveToDoAsync(ToDoItem item)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (item.Id == 0) db.ToDoItems.Add(item);
        else db.ToDoItems.Update(item);
        await db.SaveChangesAsync();
    }

    public async Task RescheduleToDoAsync(int id, string userId, DateTime finishBy)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.ToDoItems.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (item is null) return;
        item.FinishBy = finishBy;
        await db.SaveChangesAsync();
    }

    public async Task DeleteToDoAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var item = await db.ToDoItems.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (item is null) return;
        db.ToDoItems.Remove(item);
        await db.SaveChangesAsync();
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(string userId, bool includeMedications = true, bool includeToDos = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await ApplyAutoMissedAsync(db, userId);
        var bp = await GetBloodPressureAsync(db, userId, 30);
        var sugar = await GetSugarAsync(db, userId, 30);
        var meds = includeMedications
            ? await GetMedicationsAsync(db, userId, activeOnly: true)
            : [];
        var upcoming = await GetUpcomingAppointmentsAsync(db, userId);
        var missedThisMonth = await GetMissedAppointmentsThisMonthAsync(db, userId);
        var openToDos = includeToDos
            ? await GetOpenToDosAsync(db, userId)
            : [];
        return new DashboardSnapshot(bp, sugar, meds, upcoming, missedThisMonth, openToDos);
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
        var query = db.Medications.Include(x => x.Schedules).Where(x => x.UserId == userId);
        if (activeOnly) query = query.Where(x => x.IsActive);
        return query.OrderBy(x => x.Name).ToListAsync();
    }

    private static Task<Medication?> GetMedicationByIdAsync(ApplicationDbContext db, int id, string userId) =>
        db.Medications.Include(x => x.Schedules).FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);

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

    private static async Task<List<ToDoItem>> GetOpenToDosAsync(
        ApplicationDbContext db, string userId)
    {
        var items = await db.ToDoItems
            .Where(x => x.UserId == userId && !x.IsDone)
            .ToListAsync();
        var now = DateTime.Now;
        return items
            .OrderBy(x => ToDoRules.IsNearTermHigh(x, now) ? 0 : 1)
            .ThenBy(x => x.FinishBy)
            .ThenBy(x => x.Priority)
            .ToList();
    }

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
    List<Appointment> MissedThisMonth,
    List<ToDoItem> OpenToDos);
