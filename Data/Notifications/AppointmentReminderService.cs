using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public sealed class AppointmentReminderService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    TwilioNotificationSender sender,
    TravelTimeService travel,
    IOptions<NotificationOptions> options,
    ILogger<AppointmentReminderService> logger)
{
    private static int _missingTwilioLogged;

    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            return;

        if (!options.Value.Twilio.IsConfigured)
        {
            if (Interlocked.Exchange(ref _missingTwilioLogged, 1) == 0)
            {
                logger.LogInformation(
                    "Appointment reminders are ready. Add a Twilio account SID, auth token, and from number to place the calls and texts.");
            }

            return;
        }

        var now = DateTime.Now;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var upcoming = await db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Status == AppointmentStatus.Scheduled
                && appointment.StartsAt > now.AddMinutes(8)
                && appointment.StartsAt <= now.AddHours(24))
            .ToListAsync(cancellationToken);

        if (upcoming.Count == 0)
            return;

        var userIds = upcoming.Select(appointment => appointment.UserId).Distinct().ToList();
        var users = await db.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
        var profiles = await db.PatientProfiles
            .AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, cancellationToken);

        foreach (var appointment in upcoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            users.TryGetValue(appointment.UserId, out var user);
            profiles.TryGetValue(appointment.UserId, out var profile);
            if (profile is { AppointmentRemindersEnabled: false })
                continue;

            var phone = PhoneNumbers.ToE164(profile?.Phone ?? user?.PhoneNumber);
            if (phone is null)
            {
                logger.LogDebug(
                    "Skipped reminders for appointment {AppointmentId} because no phone number is saved.",
                    appointment.Id);
                continue;
            }

            var person = new PersonFacts(
                user?.FullName ?? user?.DisplayName ?? "friend",
                profile?.NamePronunciation,
                profile?.HomeAddress);

            if (AppointmentReminderSchedule.IsDayBeforeDue(appointment.StartsAt, now))
            {
                var facts = await BuildFactsAsync(appointment, person, AppointmentReminderKind.DayBefore, now, cancellationToken);
                await SendAsync(db, appointment, facts, phone, AppointmentReminderChannel.Voice, cancellationToken);
                await SendAsync(db, appointment, facts, phone, AppointmentReminderChannel.Sms, cancellationToken);
            }

            if (AppointmentReminderSchedule.IsHourBeforeDue(appointment.StartsAt, now))
            {
                var facts = await BuildFactsAsync(appointment, person, AppointmentReminderKind.HourBefore, now, cancellationToken);
                await SendAsync(db, appointment, facts, phone, AppointmentReminderChannel.Voice, cancellationToken);
            }
        }
    }

    private async Task<ReminderFacts> BuildFactsAsync(
        Appointment appointment,
        PersonFacts person,
        AppointmentReminderKind kind,
        DateTime now,
        CancellationToken cancellationToken)
    {
        string? travelSentence = null;
        if (kind is AppointmentReminderKind.DayBefore or AppointmentReminderKind.HourBefore)
        {
            var estimate = await travel.TryEstimateAsync(person.HomeAddress, appointment.Location, cancellationToken);
            travelSentence = estimate?.Sentence;
        }

        return new ReminderFacts(
            person.FullName,
            person.NamePronunciation,
            appointment.StartsAt,
            now,
            kind,
            string.IsNullOrWhiteSpace(appointment.Purpose) ? appointment.Title : appointment.Purpose,
            appointment.ProviderName,
            appointment.Location,
            travelSentence);
    }

    private async Task SendAsync(
        ApplicationDbContext db,
        Appointment appointment,
        ReminderFacts facts,
        string phone,
        AppointmentReminderChannel channel,
        CancellationToken cancellationToken)
    {
        var existing = await db.AppointmentReminders.FirstOrDefaultAsync(
            reminder => reminder.AppointmentId == appointment.Id
                && reminder.Kind == facts.Kind
                && reminder.Channel == channel
                && reminder.StartsAtSnapshot == appointment.StartsAt,
            cancellationToken);

        if (existing?.Status is AppointmentReminderStatus.Sent or AppointmentReminderStatus.Skipped)
            return;

        if (existing?.Status == AppointmentReminderStatus.Sending
            && existing.LastAttemptAt > DateTime.Now.AddMinutes(-2))
            return;

        if (existing is not null && existing.AttemptCount >= 3)
            return;

        if (existing is null)
        {
            existing = new AppointmentReminder
            {
                AppointmentId = appointment.Id,
                Kind = facts.Kind,
                Channel = channel,
                StartsAtSnapshot = appointment.StartsAt,
                Status = AppointmentReminderStatus.Sending,
                AttemptCount = 1,
                LastAttemptAt = DateTime.Now
            };
            db.AppointmentReminders.Add(existing);
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

        var voice = options.Value.Voice;
        OutboundResult result;
        try
        {
            result = channel == AppointmentReminderChannel.Sms
                ? await sender.TextAsync(phone, AppointmentReminderCopy.BuildSms(facts), cancellationToken)
                : await sender.CallAsync(phone, AppointmentReminderCopy.BuildTwiml(facts, voice.PollyVoice, voice.Language), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Appointment {Kind} {Channel} reminder failed to send.", facts.Kind, channel);
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

    private sealed record PersonFacts(string FullName, string? NamePronunciation, string? HomeAddress);
}
