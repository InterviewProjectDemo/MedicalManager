using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public sealed class MedicationReminderService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TwilioNotificationSender sender,
    IOptions<NotificationOptions> options,
    ILogger<MedicationReminderService> logger)
{
    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        if (!await sender.EnsureReadyAsync(cancellationToken))
            return;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await db.MedicationSchedules
            .AsNoTracking()
            .Include(schedule => schedule.Medication)
            .Where(schedule => schedule.Medication!.IsActive
                && schedule.Medication.StartDate <= today
                && (schedule.Medication.EndDate == null || schedule.Medication.EndDate >= today))
            .ToListAsync(cancellationToken);

        if (schedules.Count == 0)
            return;

        var userIds = schedules.Select(schedule => schedule.Medication!.UserId).Distinct().ToList();
        var administrations = await db.MedicationAdministrations
            .AsNoTracking()
            .Where(record => record.ScheduledDate == today && userIds.Contains(record.UserId))
            .ToListAsync(cancellationToken);
        var users = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);
        var profiles = await db.PatientProfiles.AsNoTracking().Where(profile => userIds.Contains(profile.UserId)).ToDictionaryAsync(profile => profile.UserId, cancellationToken);

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            profiles.TryGetValue(userId, out var profile);
            if (profile is { MedicationRemindersEnabled: false })
                continue;

            users.TryGetValue(userId, out var user);
            var phone = PhoneNumbers.ToE164(profile?.Phone ?? user?.PhoneNumber);
            if (phone is null)
                continue;

            var name = user?.FullName ?? user?.DisplayName ?? "there";
            foreach (var slot in MedicationScheduleRules.AllSlots)
            {
                var due = schedules
                    .Where(schedule => schedule.Medication!.UserId == userId
                        && schedule.TimeSlot == slot
                        && MedicationDayRules.Includes(schedule.Medication.Days, today))
                    .ToList();
                if (due.Count == 0)
                    continue;

                var earliest = due.Min(schedule => MedicationScheduleRules.EffectiveAdministerAt(schedule.TimeSlot, schedule.AdministerAt));
                var time = TimeOnly.FromDateTime(now);
                if (time < earliest || time > MedicationScheduleRules.GetWindowEnd(slot))
                    continue;

                var doses = due
                    .Where(schedule => !IsSettled(administrations, userId, schedule.MedicationId, slot))
                    .Select(DescribeDose)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (doses.Count == 0)
                    continue;

                var speech = MedicationReminderCopy.BuildVoiceSsml(name, profile?.NamePronunciation, slot, doses);
                await SendAsync(db, userId, today, slot, phone, speech, cancellationToken);
            }
        }
    }

    private static string DescribeDose(MedicationSchedule schedule)
    {
        var medication = schedule.Medication!;
        var dose = $"{medication.Name} {medication.Dosage}".Trim();
        return schedule.AdministerAt is TimeOnly administerAt
            ? $"{dose} at {MedicationScheduleRules.FormatClock(administerAt)}"
            : dose;
    }

    private static bool IsSettled(
        List<MedicationAdministration> administrations,
        string userId,
        int medicationId,
        MedicationTimeSlot slot)
    {
        var record = administrations.FirstOrDefault(item =>
            item.UserId == userId && item.MedicationId == medicationId && item.TimeSlot == slot);
        return record?.Status is AdministrationStatus.Administered or AdministrationStatus.Missed;
    }

    private async Task SendAsync(
        ApplicationDbContext db,
        string userId,
        DateOnly today,
        MedicationTimeSlot slot,
        string phone,
        string body,
        CancellationToken cancellationToken)
    {
        var existing = await db.MedicationReminders.FirstOrDefaultAsync(
            reminder => reminder.UserId == userId
                && reminder.ScheduledDate == today
                && reminder.TimeSlot == slot,
            cancellationToken);

        if (existing?.Status is AppointmentReminderStatus.Sent or AppointmentReminderStatus.Skipped)
            return;
        if (existing?.Status == AppointmentReminderStatus.Sending && existing.LastAttemptAt > DateTime.Now.AddMinutes(-2))
            return;
        if (existing is not null && existing.AttemptCount >= 3)
            return;

        if (existing is null)
        {
            existing = new MedicationReminder
            {
                UserId = userId,
                ScheduledDate = today,
                TimeSlot = slot,
                Status = AppointmentReminderStatus.Sending,
                AttemptCount = 1,
                LastAttemptAt = DateTime.Now
            };
            db.MedicationReminders.Add(existing);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(existing).State = EntityState.Detached;
                return;
            }
        }
        else
        {
            existing.Status = AppointmentReminderStatus.Sending;
            existing.AttemptCount++;
            existing.LastAttemptAt = DateTime.Now;
            existing.Detail = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        OutboundResult result;
        try
        {
            result = await sender.CallAsync(phone, body, options.Value.Voice.PollyVoice, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Medication {Slot} reminder failed to send.", slot);
            result = new OutboundResult(false, false, null, "The reminder could not be sent.");
        }

        existing.Status = result.Ok
            ? AppointmentReminderStatus.Sent
            : result.Permanent ? AppointmentReminderStatus.Skipped : AppointmentReminderStatus.Failed;
        existing.SentAt = result.Ok ? DateTime.Now : null;
        existing.ProviderMessageId = result.Sid;
        existing.Detail = result.Detail;
        await db.SaveChangesAsync(cancellationToken);
    }
}
