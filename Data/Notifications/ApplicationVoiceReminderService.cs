using Microsoft.EntityFrameworkCore;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public sealed record ApplicationVoiceLine(string Key, string Text);

/// <summary>
/// Speaks the same medication, appointment, and to-do reminders on this device.
/// Phone calls stay independent, so a person can keep one channel or both.
/// A phone number is not required.
/// </summary>
public sealed class ApplicationVoiceReminderService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TravelTimeService travel,
    ILogger<ApplicationVoiceReminderService> logger)
{
    public async Task<ApplicationVoiceLine?> TakeNextAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is not { ApplicationRemindersEnabled: true })
        {
            logger.LogInformation("Application voice is turned off.");
            return null;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(account => account.Id == userId, cancellationToken);
        var name = user?.FullName ?? user?.DisplayName ?? "there";
        var now = DateTime.Now;
        var due = new List<DueReminder>();

        // The three phone switches do not gate this device. Application reminders speaks every due type.
        due.AddRange(await DueMedicationsAsync(db, userId, name, profile.NamePronunciation, now, cancellationToken));
        due.AddRange(await DueAppointmentsAsync(db, userId, name, profile, now, cancellationToken));
        due.AddRange(await DueToDosAsync(db, userId, name, profile.NamePronunciation, now, cancellationToken));

        logger.LogInformation("Application voice check. Ready={Ready}.", due.Count);

        foreach (var reminder in due.OrderBy(item => item.Rank).ThenBy(item => item.When))
        {
            var line = await ClaimAsync(db, userId, reminder, cancellationToken);
            if (line is not null)
                return line;
        }

        return null;
    }

    public async Task CompleteAsync(string userId, string key, bool heard, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(key))
            return;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.ApplicationVoiceReminders.FirstOrDefaultAsync(
            reminder => reminder.UserId == userId && reminder.ReminderKey == key,
            cancellationToken);
        if (row is null)
            return;

        if (heard)
        {
            row.Status = AppointmentReminderStatus.Sent;
            row.SpokenAt = DateTime.Now;
        }
        else
        {
            row.Status = AppointmentReminderStatus.Failed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<DueReminder>> DueMedicationsAsync(
        ApplicationDbContext db,
        string userId,
        string name,
        string? pronunciation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now);
        var schedules = await db.MedicationSchedules
            .AsNoTracking()
            .Include(schedule => schedule.Medication)
            .Where(schedule => schedule.Medication!.UserId == userId
                && schedule.Medication.IsActive
                && schedule.Medication.StartDate <= today
                && (schedule.Medication.EndDate == null || schedule.Medication.EndDate >= today))
            .ToListAsync(cancellationToken);
        if (schedules.Count == 0)
            return [];

        var administrations = await db.MedicationAdministrations
            .AsNoTracking()
            .Where(record => record.UserId == userId && record.ScheduledDate == today)
            .ToListAsync(cancellationToken);
        var due = new List<DueReminder>();
        var time = TimeOnly.FromDateTime(now);
        foreach (var slot in MedicationScheduleRules.AllSlots)
        {
            var doses = schedules
                .Where(schedule => schedule.TimeSlot == slot && MedicationDayRules.Includes(schedule.Medication!.Days, today))
                .ToList();
            if (doses.Count == 0)
                continue;

            var earliest = doses.Min(schedule => MedicationScheduleRules.EffectiveAdministerAt(schedule.TimeSlot, schedule.AdministerAt));
            if (time < earliest || time > MedicationScheduleRules.GetWindowEnd(slot))
                continue;

            var pending = doses
                .Where(schedule => !IsSettled(administrations, schedule.MedicationId, slot))
                .Select(DescribeDose)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pending.Count == 0)
                continue;

            due.Add(new DueReminder(
                $"med:{today:yyyy-MM-dd}:{(int)slot}",
                1,
                today.ToDateTime(earliest),
                MedicationReminderCopy.BuildSpoken(name, pronunciation, slot, pending)));
        }

        return due;
    }

    private async Task<List<DueReminder>> DueAppointmentsAsync(
        ApplicationDbContext db,
        string userId,
        string name,
        PatientProfile profile,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var horizon = now.AddHours(24);
        var earliestEnd = now.AddMinutes(-1);
        var upcoming = await db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.UserId == userId
                && appointment.Status == AppointmentStatus.Scheduled
                && appointment.StartsAt <= horizon
                && (appointment.EndsAt ?? appointment.StartsAt) >= earliestEnd)
            .ToListAsync(cancellationToken);
        var due = new List<DueReminder>();
        foreach (var appointment in upcoming)
        {
            var visitDue = IsVisitVoiceDue(appointment.StartsAt, appointment.EndsAt, now);
            var dayBefore = AppointmentReminderSchedule.IsDayBeforeDue(appointment.StartsAt, now);
            if (!visitDue && !dayBefore)
                continue;

            var kind = visitDue
                ? AppointmentReminderKind.HourBefore
                : AppointmentReminderKind.DayBefore;
            var key = $"appt:{appointment.Id}:{(int)kind}:{appointment.StartsAt:yyyyMMddHHmmss}";
            if (await AlreadyHandledAsync(db, userId, key, cancellationToken))
                continue;

            var estimate = await travel.TryEstimateAsync(profile.HomeAddress, appointment.Location, cancellationToken);
            var travelSentence = estimate?.Sentence;
            var facts = new ReminderFacts(
                name,
                profile.NamePronunciation,
                appointment.StartsAt,
                now,
                kind,
                string.IsNullOrWhiteSpace(appointment.Purpose) ? appointment.Title : appointment.Purpose,
                appointment.ProviderName,
                appointment.Location,
                travelSentence);
            due.Add(new DueReminder(
                key,
                kind == AppointmentReminderKind.HourBefore ? 0 : 2,
                appointment.StartsAt,
                Fit(AppointmentReminderCopy.BuildSpoken(facts))));
        }

        logger.LogInformation(
            "Application voice checked appointments. Upcoming={Upcoming} Ready={Ready}.",
            upcoming.Count,
            due.Count);

        return due;
    }

    private static async Task<List<DueReminder>> DueToDosAsync(
        ApplicationDbContext db,
        string userId,
        string name,
        string? pronunciation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var open = await db.ToDoItems
            .AsNoTracking()
            .Where(item => item.UserId == userId
                && !item.IsDone
                && item.FinishBy > now.AddMinutes(8)
                && item.FinishBy <= now.AddHours(24))
            .ToListAsync(cancellationToken);
        var due = new List<DueReminder>();
        foreach (var item in open)
        {
            if (AppointmentReminderSchedule.IsDayBeforeDue(item.FinishBy, now))
            {
                due.Add(new DueReminder(
                    $"todo:{item.Id}:{(int)AppointmentReminderKind.DayBefore}:{item.FinishBy:yyyyMMddHHmmss}",
                    2,
                    item.FinishBy,
                    ToDoReminderCopy.BuildSpoken(name, pronunciation, item.Description, item.FinishBy, AppointmentReminderKind.DayBefore)));
            }

            if (AppointmentReminderSchedule.IsHourBeforeDue(item.FinishBy, now))
            {
                due.Add(new DueReminder(
                    $"todo:{item.Id}:{(int)AppointmentReminderKind.HourBefore}:{item.FinishBy:yyyyMMddHHmmss}",
                    1,
                    item.FinishBy,
                    ToDoReminderCopy.BuildSpoken(name, pronunciation, item.Description, item.FinishBy, AppointmentReminderKind.HourBefore)));
            }
        }

        return due;
    }

    private static async Task<bool> AlreadyHandledAsync(
        ApplicationDbContext db,
        string userId,
        string key,
        CancellationToken cancellationToken)
    {
        var existing = await db.ApplicationVoiceReminders.AsNoTracking().FirstOrDefaultAsync(
            row => row.UserId == userId && row.ReminderKey == key,
            cancellationToken);
        if (existing is null)
            return false;
        if (existing.Status is AppointmentReminderStatus.Sent or AppointmentReminderStatus.Skipped)
            return true;
        if (existing.Status == AppointmentReminderStatus.Sending && existing.LastAttemptAt > DateTime.Now.AddSeconds(-90))
            return true;
        return existing.AttemptCount >= 12;
    }

    private static async Task<ApplicationVoiceLine?> ClaimAsync(
        ApplicationDbContext db,
        string userId,
        DueReminder reminder,
        CancellationToken cancellationToken)
    {
        var text = Fit(reminder.Text);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var existing = await db.ApplicationVoiceReminders.FirstOrDefaultAsync(
            row => row.UserId == userId && row.ReminderKey == reminder.Key,
            cancellationToken);
        if (existing?.Status is AppointmentReminderStatus.Sent or AppointmentReminderStatus.Skipped)
            return null;
        if (existing?.Status == AppointmentReminderStatus.Sending && existing.LastAttemptAt > DateTime.Now.AddSeconds(-90))
            return null;
        if (existing is not null && existing.AttemptCount >= 12)
            return null;

        if (existing is null)
        {
            existing = new ApplicationVoiceReminder
            {
                UserId = userId,
                ReminderKey = reminder.Key,
                Status = AppointmentReminderStatus.Sending,
                AttemptCount = 1,
                LastAttemptAt = DateTime.Now
            };
            db.ApplicationVoiceReminders.Add(existing);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(existing).State = EntityState.Detached;
                return null;
            }
        }
        else
        {
            existing.Status = AppointmentReminderStatus.Sending;
            existing.AttemptCount++;
            existing.LastAttemptAt = DateTime.Now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ApplicationVoiceLine(reminder.Key, text);
    }

    private static bool IsSettled(List<MedicationAdministration> administrations, int medicationId, MedicationTimeSlot slot)
    {
        var record = administrations.FirstOrDefault(item => item.MedicationId == medicationId && item.TimeSlot == slot);
        return record?.Status is AdministrationStatus.Administered or AdministrationStatus.Missed;
    }

    private static string DescribeDose(MedicationSchedule schedule)
    {
        var medication = schedule.Medication!;
        var dose = $"{medication.Name} {medication.Dosage}".Trim();
        return schedule.AdministerAt is TimeOnly administerAt
            ? $"{dose} at {MedicationScheduleRules.FormatClock(administerAt)}"
            : dose;
    }

    /// <summary>
    /// The device keeps the visit reminder from one hour before the start
    /// through the end of the visit. The phone call still stops eight minutes before.
    /// </summary>
    private static bool IsVisitVoiceDue(DateTime startsAt, DateTime? endsAt, DateTime now)
    {
        var ends = endsAt ?? startsAt;
        if (ends < startsAt)
            ends = startsAt;
        return now >= startsAt.AddHours(-1) && now <= ends;
    }

    private static string Fit(string text)
    {
        var limit = OnboardingVoiceService.ConversationMaxCharacters;
        var spoken = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (spoken.Length <= limit)
            return spoken;

        var cut = spoken.LastIndexOf('.', limit - 1);
        return cut < 80 ? spoken[..(limit - 1)].TrimEnd() + "." : spoken[..(cut + 1)];
    }

    private sealed record DueReminder(string Key, int Rank, DateTime When, string Text);
}
