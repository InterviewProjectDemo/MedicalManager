using Microsoft.AspNetCore.Identity;
using MedicalManager.Data;

namespace MedicalManager.Components.Account;

public interface IAppEmailSender : IEmailSender<ApplicationUser>
{
    Task SendWelcomeEmailAsync(ApplicationUser user, string email, CancellationToken cancellationToken = default);
}
