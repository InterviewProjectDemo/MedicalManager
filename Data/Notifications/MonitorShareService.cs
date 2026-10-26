using System.Net.Mail;
using MedicalManager.Components.Account;

namespace MedicalManager.Data.Notifications;

public sealed record MonitorShareOutcome(bool PhoneSent, bool EmailSent, string Message);

public sealed class MonitorShareService(
    TwilioNotificationSender twilio,
    IAppEmailSender email,
    ILogger<MonitorShareService> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTime>> _recent = new(StringComparer.Ordinal);

    public async Task<MonitorShareOutcome> SendAsync(
        string userId,
        string? phone,
        string? emailAddress,
        string patientName,
        string link,
        string code,
        CancellationToken cancellationToken)
    {
        var phoneText = phone?.Trim() ?? "";
        var emailText = emailAddress?.Trim() ?? "";
        var wantsPhone = phoneText.Length > 0;
        var wantsEmail = emailText.Length > 0;
        if (!wantsPhone && !wantsEmail)
            return new MonitorShareOutcome(false, false, "Enter a phone number or an email. One of them is needed.");

        string? destination = null;
        if (wantsPhone)
        {
            destination = PhoneNumbers.ToE164(phoneText);
            if (destination is null)
                return new MonitorShareOutcome(false, false, "That phone number does not look right. Use a number like 917-555-0100.");
        }

        string? recipient = null;
        if (wantsEmail)
        {
            if (!MailAddress.TryCreate(emailText, out var parsed)
                || !parsed.Host.Contains('.', StringComparison.Ordinal))
                return new MonitorShareOutcome(false, false, "That email does not look right.");

            recipient = parsed.Address;
        }

        if (!Allow(userId))
            return new MonitorShareOutcome(false, false, "Wait a few minutes before sharing again.");

        var name = string.IsNullOrWhiteSpace(patientName) ? "Your family" : patientName.Trim();
        var text = $"{name} shared a Medical Manager family monitor.\nLink: {link}\nMonitor Access Code: {code}";
        var phoneSent = false;
        var emailSent = false;

        if (destination is not null)
        {
            var result = await twilio.TextAsync(destination, text, cancellationToken);
            phoneSent = result.Ok;
            if (!phoneSent)
                logger.LogWarning("Family monitor text was not sent.");
        }

        if (recipient is not null)
        {
            try
            {
                emailSent = await email.SendMonitorShareAsync(recipient, name, link, code, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Family monitor email was not sent.");
                emailSent = false;
            }
        }

        return new MonitorShareOutcome(phoneSent, emailSent, Describe(wantsPhone, wantsEmail, phoneSent, emailSent));
    }

    private bool Allow(string userId)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (!_recent.TryGetValue(userId, out var stamps))
            {
                stamps = new Queue<DateTime>();
                _recent[userId] = stamps;
            }

            while (stamps.Count > 0 && now - stamps.Peek() > TimeSpan.FromMinutes(10))
                stamps.Dequeue();

            if (stamps.Count >= 8)
                return false;

            stamps.Enqueue(now);
            return true;
        }
    }

    private static string Describe(bool wantsPhone, bool wantsEmail, bool phoneSent, bool emailSent)
    {
        if (wantsPhone && wantsEmail)
        {
            if (phoneSent && emailSent)
                return "Sent the link and code by text and email.";
            if (phoneSent)
                return "The text was sent. The email could not be sent.";
            if (emailSent)
                return "The email was sent. The text could not be sent.";
            return "The link and code could not be sent. Check the phone number and email, then try again.";
        }

        if (wantsPhone)
            return phoneSent
                ? "Sent the link and code by text."
                : "The text could not be sent. Check the phone number, then try again.";

        return emailSent
            ? "Sent the link and code by email."
            : "The email could not be sent. Check the address, then try again.";
    }
}
