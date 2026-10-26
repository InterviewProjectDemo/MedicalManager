using Microsoft.AspNetCore.Identity;
using MedicalManager.Data;

namespace MedicalManager.Components.Account;

public interface IAppEmailSender : IEmailSender<ApplicationUser>
{
    Task SendWelcomeEmailAsync(ApplicationUser user, string email, CancellationToken cancellationToken = default);

    Task<bool> SendMonitorShareAsync(string email, string patientName, string link, string code, CancellationToken cancellationToken = default);
}
