using Microsoft.EntityFrameworkCore;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public sealed class ToDoReminderService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TwilioNotificationSender sender,
    ILogger<ToDoReminderService> logger)
{
    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        if (!await sender.EnsureReadyAsync(cancellationToken))
            return;

        var now = DateTime.Now;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var open = await db.ToDoItems
            .AsNoTracking()
            .Where(item => !item.IsDone
                && item.FinishBy > now.AddMinutes(8)
                && item.FinishBy <= now.AddHours(24))
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
            return;

        var userIds = open.Select(item => item.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(user => userIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);
        var profiles = await db.PatientProfiles.AsNoTracking().Where(profile => userIds.Contains(profile.UserId)).ToDictionaryAsync(profile => profile.UserId, cancellationToken);

        foreach (var item in open)
        {
            cancellationToken.ThrowIfCancellationRequested();
            profiles.TryGetValue(item.UserId, out var profile);
            if (profile is { ToDoRemindersEnabled: false })
                continue;

            users.TryGetValue(item.UserId, out var user);
            var phone = PhoneNumbers.ToE164(profile?.Phone ?? user?.PhoneNumber);
            if (phone is null)
                continue;

            var name = user?.FullName ?? user?.DisplayName ?? "there";
            if (AppointmentReminderSchedule.IsDayBeforeDue(item.FinishBy, now))
            {
                await SendAsync(
                    db,
                    item,
                    phone,
                    ToDoReminderCopy.BuildSms(name, item.Description, item.FinishBy, AppointmentReminderKind.DayBefore),
                    AppointmentReminderKind.DayBefore,
                    cancellationToken);
            }

            if (AppointmentReminderSchedule.IsHourBeforeDue(item.FinishBy, now))
            {
                await SendAsync(
                    db,
                    item,
                    phone,
                    ToDoReminderCopy.BuildSms(name, item.Description, item.FinishBy, AppointmentReminderKind.HourBefore),
                    AppointmentReminderKind.HourBefore,
                    cancellationToken);
            }
        }
    }

    private async Task SendAsync(
        ApplicationDbContext db,
        ToDoItem item,
        string phone,
        string body,
        AppointmentReminderKind kind,
        CancellationToken cancellationToken)
    {
        var existing = await db.ToDoReminders.FirstOrDefaultAsync(
            reminder => reminder.ToDoItemId == item.Id
                && reminder.Kind == kind
                && reminder.FinishBySnapshot == item.FinishBy,
            cancellationToken);

        if (existing?.Status is AppointmentReminderStatus.Sent or AppointmentReminderStatus.Skipped)
            return;
        if (existing?.Status == AppointmentReminderStatus.Sending && existing.LastAttemptAt > DateTime.Now.AddMinutes(-2))
            return;
        if (existing is not null && existing.AttemptCount >= 3)
            return;

        if (existing is null)
        {
            existing = new ToDoReminder
            {
                ToDoItemId = item.Id,
                Kind = kind,
                FinishBySnapshot = item.FinishBy,
                Status = AppointmentReminderStatus.Sending,
                AttemptCount = 1,
                LastAttemptAt = DateTime.Now
            };
            db.ToDoReminders.Add(existing);
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
            result = await sender.TextAsync(phone, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "To-do {Kind} reminder failed to send.", kind);
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
