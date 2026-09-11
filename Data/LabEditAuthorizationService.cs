using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace MedicalManager.Data;

/// <summary>
/// Tracks short-lived password confirmation for lab report editing within a Blazor circuit.
/// </summary>
public sealed class LabEditAuthorizationService(
    UserManager<ApplicationUser> users,
    AuthenticationStateProvider auth)
{
    private static readonly TimeSpan VerificationTtl = TimeSpan.FromMinutes(5);

    private DateTimeOffset? _verifiedUntil;

    public bool IsAuthorized => _verifiedUntil is not null && _verifiedUntil > DateTimeOffset.UtcNow;

    public async Task<(bool Success, string? Error)> ConfirmPasswordAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "Enter your password to continue.");
        }

        var state = await auth.GetAuthenticationStateAsync();
        var user = await users.GetUserAsync(state.User);
        if (user is null)
        {
            return (false, "You must be signed in.");
        }

        if (!await users.HasPasswordAsync(user))
        {
            GrantAccess();
            return (true, null);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            return (false, "Incorrect password.");
        }

        GrantAccess();
        return (true, null);
    }

    private void GrantAccess() => _verifiedUntil = DateTimeOffset.UtcNow.Add(VerificationTtl);
}
