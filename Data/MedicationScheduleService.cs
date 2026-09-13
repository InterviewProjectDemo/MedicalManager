using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class MedicationScheduleService(ApplicationDbContext db)
{
    public async Task<DailyMedicationSchedule> GetDailyScheduleAsync(string userId, DateOnly date)
    {
        await EnsureAdministrationRecordsAsync(userId, date);
        await ApplyAutoMissedAsync(userId, date);

        var schedules = await db.MedicationSchedules
            .Include(x => x.Medication)
            .Where(x => x.Medication!.UserId == userId && x.Medication.IsActive)
            .OrderBy(x => x.Medication!.Name)
            .ToListAsync();

        var administrations = await db.MedicationAdministrations
            .Include(x => x.Medication)
            .Where(x => x.UserId == userId && x.ScheduledDate == date)
            .ToListAsync();

        var groups = MedicationScheduleRules.AllSlots
            .Select(slot => new MedicationSlotGroup(
                slot,
                MedicationScheduleRules.GetDisplayName(slot),
                MedicationScheduleRules.GetWindowLabel(slot),
                administrations
                    .Where(a => a.TimeSlot == slot)
                    .OrderBy(a => a.Medication!.Name)
                    .Select(a => new MedicationDoseItem(
                        a.Id,
                        a.MedicationId,
                        a.Medication!.Name,
                        a.Medication.Dosage,
                        a.Medication.Frequency,
                        a.Status,
                        a.RecordedAt,
                        a.RecordedByName))
                    .ToList()))
            .ToList();

        return new DailyMedicationSchedule(date, groups);
    }

    public async Task<MedicationOverview> GetMedicationOverviewAsync(string userId)
    {
        var rows = await db.Medications
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(med => new MedicationOverviewRow(
                med.Id,
                med.Name,
                med.Purpose,
                med.Dosage,
                med.Frequency))
            .ToListAsync();

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

    private async Task EnsureAdministrationRecordsAsync(string userId, DateOnly date)
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

    private async Task ApplyAutoMissedAsync(string userId, DateOnly date)
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
    string? RecordedByName);

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
    string Frequency);

public sealed record MedicationOverview(
    IReadOnlyList<MedicationOverviewRow> Medications);
