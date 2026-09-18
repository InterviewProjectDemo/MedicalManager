using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class DashboardLayoutService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<DashboardLayout> GetForUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return DashboardWidgetCatalog.Default();

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.UserDashboardLayouts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
        return DashboardLayoutSerializer.Deserialize(row?.LayoutJson);
    }

    public async Task SaveForUserAsync(string userId, DashboardLayout layout)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new InvalidOperationException("You must be signed in to save a dashboard layout.");

        layout = DashboardWidgetCatalog.Normalize(layout);
        var json = DashboardLayoutSerializer.Serialize(layout);
        var now = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.UserDashboardLayouts.FirstOrDefaultAsync(x => x.UserId == userId);
        if (row is null)
        {
            db.UserDashboardLayouts.Add(new UserDashboardLayout
            {
                UserId = userId,
                LayoutJson = json,
                UpdatedAt = now
            });
        }
        else
        {
            row.LayoutJson = json;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
    }
}
