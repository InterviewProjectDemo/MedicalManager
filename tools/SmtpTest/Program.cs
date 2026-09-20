using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var config = new ConfigurationBuilder()
    .SetBasePath(repoRoot)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .Build();

var host = config["Email:Smtp:Host"] ?? "smtp.gmail.com";
var port = int.TryParse(config["Email:Smtp:Port"], out var p) ? p : 587;
var user = config["Email:Smtp:User"] ?? config["Email:FromAddress"];
var password = config["Email:Smtp:Password"];
var from = config["Email:FromAddress"] ?? user;

if (string.IsNullOrWhiteSpace(password))
{
    Console.WriteLine("FAIL: Email:Smtp:Password is not configured.");
    return 1;
}

if (string.IsNullOrWhiteSpace(user))
{
    Console.WriteLine("FAIL: Email:Smtp:User is not configured.");
    return 1;
}

try
{
    using var client = new SmtpClient();
    await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
    await client.AuthenticateAsync(user, password);

    var message = new MimeMessage();
    message.From.Add(new MailboxAddress("Medical Manager SMTP Test", from!));
    message.To.Add(MailboxAddress.Parse(from!));
    message.Subject = "Medical Manager SMTP connectivity test";
    message.Body = new TextPart("plain")
    {
        Text = $"SMTP test succeeded at {DateTimeOffset.UtcNow:O}"
    };

    await client.SendAsync(message);
    await client.DisconnectAsync(true);

    Console.WriteLine("SUCCESS: Connected, authenticated, and sent test email via Gmail SMTP.");
    return 0;
}
catch (AuthenticationException ex)
{
    Console.WriteLine($"FAIL: SMTP authentication rejected ({ex.GetType().Name}).");
    Console.WriteLine("Gmail likely requires a Google App Password when 2FA is enabled.");
    return 2;
}
catch (SmtpCommandException ex)
{
    Console.WriteLine($"FAIL: SMTP command error ({ex.GetType().Name}, StatusCode={ex.StatusCode}).");
    return 3;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
    return 4;
}
