using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;
using MedicalManager.Data;

namespace MedicalManager.Components.Account;

internal sealed class SmtpEmailSender : IAppEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly IHostEnvironment _environment;

    public SmtpEmailSender(
        IOptions<EmailOptions> options,
        ILogger<SmtpEmailSender> logger,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _logger = logger;
        _environment = environment;
    }

    public Task SendWelcomeEmailAsync(ApplicationUser user, string email, CancellationToken cancellationToken = default)
    {
        var (html, plainText) = EmailTemplates.Welcome(user.DisplayName);
        return SendAsync(email, "Welcome to Medical Manager", html, plainText, cancellationToken);
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var (html, plainText) = EmailTemplates.EmailConfirmation(confirmationLink);
        return SendAsync(email, "Confirm your Medical Manager email", html, plainText, CancellationToken.None);
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        var (html, plainText) = EmailTemplates.PasswordReset(resetLink);
        return SendAsync(email, "Reset your Medical Manager password", html, plainText, CancellationToken.None);
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var plainText = $"Your Medical Manager password reset code is: {resetCode}";
        var html = $"""
            <p>Your Medical Manager password reset code is:</p>
            <p style="font-size:24px;font-weight:600;letter-spacing:4px;">{System.Net.WebUtility.HtmlEncode(resetCode)}</p>
            """;
        return SendAsync(email, "Reset your Medical Manager password", html, plainText, CancellationToken.None);
    }

    private async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string plainTextBody,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("Skipped sending email with subject '{Subject}' because the recipient address was empty.", subject);
            return;
        }

        var smtpPassword = _options.Smtp.Password;
        if (string.IsNullOrWhiteSpace(smtpPassword))
        {
            _logger.LogWarning(
                "Email SMTP password is not configured. Email to {Recipient} with subject '{Subject}' was not sent.",
                to,
                subject);

            if (_environment.IsDevelopment())
            {
                _logger.LogInformation(
                    "DEV EMAIL FALLBACK — To: {Recipient}, Subject: {Subject}, Plain text:{NewLine}{Body}",
                    to,
                    subject,
                    Environment.NewLine,
                    plainTextBody);
            }

            return;
        }

        var fromAddress = string.IsNullOrWhiteSpace(_options.FromAddress)
            ? _options.Smtp.User
            : _options.FromAddress;
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            _logger.LogError("Email FromAddress and Smtp.User are not configured. Cannot send to {Recipient}.", to);
            return;
        }

        var smtpUser = string.IsNullOrWhiteSpace(_options.Smtp.User)
            ? fromAddress
            : _options.Smtp.User;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromDisplayName, fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = plainTextBody
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(smtpUser, smtpPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Sent email to {Recipient} with subject '{Subject}'.", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient} with subject '{Subject}'.", to, subject);
            throw;
        }
    }
}
