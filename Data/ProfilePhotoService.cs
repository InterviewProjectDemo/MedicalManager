using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public class ProfilePhotoService
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly AuthenticationStateProvider _auth;

    public ProfilePhotoService(IDbContextFactory<ApplicationDbContext> dbFactory, AuthenticationStateProvider auth)
    {
        _dbFactory = dbFactory;
        _auth = auth;
    }

    public string? GetPhotoUrl(string userId, DateTime? updatedAt)
    {
        if (updatedAt is null)
            return null;

        return $"/profile-photo/{userId}?v={updatedAt.Value.Ticks}";
    }

    public async Task<string?> GetPhotoUrlForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var updatedAt = await db.PatientProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ProfilePhotoData != null)
            .Select(p => p.ProfilePhotoUpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return GetPhotoUrl(userId, updatedAt);
    }

    public async Task<(byte[] Data, string ContentType)?> GetPhotoDataAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.PatientProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.ProfilePhotoData != null)
            .Select(p => new { p.ProfilePhotoData, p.ProfilePhotoContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (profile?.ProfilePhotoData is null || string.IsNullOrWhiteSpace(profile.ProfilePhotoContentType))
            return null;

        return (profile.ProfilePhotoData, profile.ProfilePhotoContentType);
    }

    public async Task<(bool Success, string? Error, string? PhotoUrl)> SaveBytesAsync(
        string? targetUserId,
        byte[] bytes,
        string? contentType,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream(bytes);
        return await SaveUploadAsync(targetUserId, stream, fileName, contentType, bytes.LongLength, cancellationToken);
    }

    public async Task<(bool Success, string? Error, string? PhotoUrl)> SaveUploadAsync(
        string? targetUserId,
        Stream stream,
        string? fileName,
        string? contentType,
        long size,
        CancellationToken cancellationToken = default)
    {
        var (userId, resolveError) = await ResolveTargetUserIdAsync(targetUserId);
        if (resolveError is not null)
            return (false, resolveError, null);

        if (size <= 0 || size > MaxBytes)
            return (false, "Image must be between 1 byte and 5 MB.", null);

        var normalizedContentType = ResolveContentType(fileName, contentType);
        if (normalizedContentType is null)
            return (false, "Only JPG, PNG, and WebP images are allowed.", null);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await GetOrCreateProfileAsync(db, userId!, cancellationToken);

        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            return (false, "Image must be between 1 byte and 5 MB.", null);

        profile.ProfilePhotoData = bytes;
        profile.ProfilePhotoContentType = normalizedContentType;
        profile.ProfilePhotoUpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return (true, null, GetPhotoUrl(userId!, profile.ProfilePhotoUpdatedAt));
    }

    public async Task DeletePhotoAsync(string? targetUserId, CancellationToken cancellationToken = default)
    {
        var (userId, _) = await ResolveTargetUserIdAsync(targetUserId);
        if (userId is null)
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is null)
            return;

        profile.ProfilePhotoData = null;
        profile.ProfilePhotoContentType = null;
        profile.ProfilePhotoUpdatedAt = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string? UserId, string? Error)> ResolveTargetUserIdAsync(string? targetUserId)
    {
        var state = await _auth.GetAuthenticationStateAsync();
        var principal = state.User;
        if (principal.Identity?.IsAuthenticated != true)
            return (null, "You must be signed in to save a profile photo.");

        var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim();
        if (string.IsNullOrWhiteSpace(currentUserId))
            return (null, "You must be signed in to save a profile photo.");

        var resolved = string.IsNullOrWhiteSpace(targetUserId)
            ? currentUserId
            : targetUserId.Trim();

        if (!string.Equals(resolved, currentUserId, StringComparison.Ordinal)
            && !principal.IsInRole(AppRoles.Doctor))
        {
            return (null, "You can only update your own profile photo.");
        }

        return (resolved, null);
    }

    private static async Task<PatientProfile> GetOrCreateProfileAsync(
        ApplicationDbContext db, string userId, CancellationToken cancellationToken)
    {
        var profile = await db.PatientProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (profile is not null)
            return profile;

        profile = new PatientProfile { UserId = userId };
        db.PatientProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    private static string? ResolveContentType(string? fileName, string? contentType)
    {
        var normalizedType = NormalizeContentType(contentType);
        if (normalizedType is not null && AllowedContentTypes.Contains(normalizedType))
            return normalizedType;

        var fromName = string.IsNullOrWhiteSpace(fileName)
            ? null
            : Path.GetExtension(fileName);

        if (fromName is not null && AllowedExtensions.Contains(fromName))
        {
            return NormalizeExtension(fromName) switch
            {
                ".jpg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => null
            };
        }

        return null;
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var trimmed = contentType.Trim();
        var semi = trimmed.IndexOf(';');
        if (semi >= 0)
            trimmed = trimmed[..semi].Trim();

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizeExtension(string extension) =>
        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : extension.ToLowerInvariant();
}
