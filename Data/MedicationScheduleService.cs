using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class MedicationScheduleService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<DailyMedicationSchedule> GetDailyScheduleAsync(string userId, DateOnly date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await EnsureAdministrationRecordsAsync(db, userId, date);
        await ApplyAutoMissedAsync(db, userId, date);

        var schedules = await db.MedicationSchedules
            .Include(x => x.Medication)
            .Where(x => x.Medication!.UserId == userId && x.Medication.IsActive)
            .OrderBy(x => x.Medication!.Name)
            .ToListAsync();

        var administrations = await db.MedicationAdministrations
            .Include(x => x.Medication)
            .Where(x => x.UserId == userId && x.ScheduledDate == date)
            .ToListAsync();

        var timeByDose = schedules.ToDictionary(
            schedule => (schedule.MedicationId, schedule.TimeSlot),
            schedule => schedule.AdministerAt);
        var groups = MedicationScheduleRules.AllSlots
            .Select(slot => new MedicationSlotGroup(
                slot,
                MedicationScheduleRules.GetDisplayName(slot),
                MedicationScheduleRules.GetWindowLabel(slot),
                administrations
                    .Where(a => a.TimeSlot == slot
                        && a.Medication is not null
                        && MedicationDayRules.Includes(a.Medication.Days, date)
                        && timeByDose.ContainsKey((a.MedicationId, a.TimeSlot)))
                    .OrderBy(a => a.Medication!.Name)
                    .Select(a => new MedicationDoseItem(
                        a.Id,
                        a.MedicationId,
                        a.Medication!.Name,
                        a.Medication.Dosage,
                        a.Medication.Frequency,
                        a.Status,
                        a.RecordedAt,
                        a.RecordedByName,
                        timeByDose.TryGetValue((a.MedicationId, a.TimeSlot), out var administerAt) && administerAt is TimeOnly time
                            ? MedicationScheduleRules.FormatClock(time)
                            : null))
                    .ToList()))
            .ToList();

        return new DailyMedicationSchedule(date, groups);
    }

    public async Task<MedicationOverview> GetMedicationOverviewAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var medications = await db.Medications
            .AsNoTracking()
            .Include(x => x.Schedules)
            .Where(x => x.UserId == userId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();
        var rows = medications
            .Select(med => new MedicationOverviewRow(
                med.Id,
                med.Name,
                med.Purpose,
                med.Dosage,
                med.Frequency,
                MedicationDayRules.FormatSchedule(med.Days, med.Schedules)))
            .ToList();

        return new MedicationOverview(rows);
    }

    public async Task UpdateAdministrationStatusAsync(
        int administrationId,
        string userId,
        AdministrationStatus status,
        string recordedByName,
        DateTime recordedAt,
        string? updatedByUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var record = await db.MedicationAdministrations
            .FirstOrDefaultAsync(x => x.Id == administrationId && x.UserId == userId);
        if (record is null) return;

        record.Status = status;
        record.UpdatedByUserId = updatedByUserId;
        record.RecordedByName = recordedByName.Trim();
        record.RecordedAt = recordedAt;
        await db.SaveChangesAsync();
    }

    public async Task EnsureSchedulesForMedicationAsync(int medicationId, IEnumerable<MedicationTimeSlot> slots)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.MedicationSchedules
            .Where(x => x.MedicationId == medicationId)
            .ToListAsync();
        if (existing.Count > 0) return;

        foreach (var slot in slots)
        {
            db.MedicationSchedules.Add(new MedicationSchedule
            {
                MedicationId = medicationId,
                TimeSlot = slot
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task ReplaceSchedulesAsync(int medicationId, IReadOnlyList<(MedicationTimeSlot Slot, TimeOnly Time)> times)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var medication = await db.Medications.FirstOrDefaultAsync(item => item.Id == medicationId);
        if (medication is null)
            return;

        var existing = await db.MedicationSchedules
            .Where(item => item.MedicationId == medicationId)
            .ToListAsync();
        var wanted = times.Select(time => time.Slot).ToHashSet();

        foreach (var time in times)
        {
            var row = existing.FirstOrDefault(item => item.TimeSlot == time.Slot);
            if (row is null)
            {
                db.MedicationSchedules.Add(new MedicationSchedule
                {
                    MedicationId = medicationId,
                    TimeSlot = time.Slot,
                    AdministerAt = time.Time
                });
            }
            else
            {
                row.AdministerAt = time.Time;
            }
        }

        var removed = existing.Where(item => !wanted.Contains(item.TimeSlot)).ToList();
        if (removed.Count > 0)
            db.MedicationSchedules.RemoveRange(removed);

        var pending = await db.MedicationAdministrations
            .Where(item => item.MedicationId == medicationId && item.Status == AdministrationStatus.Pending)
            .ToListAsync();
        foreach (var record in pending)
        {
            var stillDue = wanted.Contains(record.TimeSlot)
                && MedicationDayRules.Includes(medication.Days, record.ScheduledDate);
            if (!stillDue)
                db.MedicationAdministrations.Remove(record);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureAdministrationRecordsAsync(
        ApplicationDbContext db, string userId, DateOnly date)
    {
        var schedules = await db.MedicationSchedules
            .Include(x => x.Medication)
            .Where(x => x.Medication!.UserId == userId && x.Medication.IsActive)
            .ToListAsync();

        if (schedules.Count == 0) return;

        var existing = await db.MedicationAdministrations
            .Where(x => x.UserId == userId && x.ScheduledDate == date)
            .Select(x => new { x.MedicationId, x.TimeSlot })
            .ToListAsync();

        var existingSet = existing.Select(x => (x.MedicationId, x.TimeSlot)).ToHashSet();
        var added = false;

        foreach (var schedule in schedules)
        {
            if (schedule.Medication is null
                || schedule.Medication.StartDate > date
                || (schedule.Medication.EndDate is DateOnly end && end < date)
                || !MedicationDayRules.Includes(schedule.Medication.Days, date))
            {
                continue;
            }

            if (existingSet.Contains((schedule.MedicationId, schedule.TimeSlot))) continue;

            db.MedicationAdministrations.Add(new MedicationAdministration
            {
                UserId = userId,
                MedicationId = schedule.MedicationId,
                ScheduledDate = date,
                TimeSlot = schedule.TimeSlot,
                Status = AdministrationStatus.Pending
            });
            added = true;
        }

        if (added) await db.SaveChangesAsync();
    }

    private static async Task ApplyAutoMissedAsync(ApplicationDbContext db, string userId, DateOnly date)
    {
        var now = DateTime.Now;
        if (date > DateOnly.FromDateTime(now)) return;

        var pending = await db.MedicationAdministrations
            .Where(x => x.UserId == userId
                && x.ScheduledDate == date
                && x.Status == AdministrationStatus.Pending)
            .ToListAsync();

        var changed = false;
        foreach (var record in pending)
        {
            if (!MedicationScheduleRules.IsWindowPassed(record.ScheduledDate, record.TimeSlot, now)) continue;
            record.Status = AdministrationStatus.Missed;
            changed = true;
        }

        if (changed) await db.SaveChangesAsync();
    }
}

public sealed record MedicationDoseItem(
    int AdministrationId,
    int MedicationId,
    string Name,
    string Dosage,
    string Frequency,
    AdministrationStatus Status,
    DateTime? RecordedAt,
    string? RecordedByName,
    string? TimeLabel);

public sealed record MedicationSlotGroup(
    MedicationTimeSlot Slot,
    string Label,
    string WindowLabel,
    List<MedicationDoseItem> Doses);

public sealed record DailyMedicationSchedule(DateOnly Date, List<MedicationSlotGroup> Groups);

public sealed record MedicationOverviewRow(
    int MedicationId,
    string Name,
    string? Purpose,
    string Dosage,
    string Frequency,
    string? ScheduleText);

public sealed record MedicationOverview(
    IReadOnlyList<MedicationOverviewRow> Medications);
