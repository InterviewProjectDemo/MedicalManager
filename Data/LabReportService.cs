using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class LabReportService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<IReadOnlyList<LabMonthGroup>> GetReportsGroupedByMonthAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var reports = await db.LabReports
            .AsNoTracking()
            .Include(x => x.Results)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ReportDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        return reports
            .GroupBy(x => new DateOnly(x.ReportDate.Year, x.ReportDate.Month, 1))
            .OrderByDescending(g => g.Key)
            .Select(g => new LabMonthGroup(
                g.Key,
                g.Key.ToString("MMMM yyyy"),
                g.OrderByDescending(r => r.ReportDate).ThenByDescending(r => r.Id).ToList()))
            .ToList();
    }

    public async Task<LabReport?> GetReportByIdAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetReportByIdAsync(db, id, userId);
    }

    public async Task SaveReportAsync(LabReport report, IReadOnlyDictionary<string, decimal?> values)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (report.Id == 0)
        {
            report.CreatedAt = DateTime.UtcNow;
            db.LabReports.Add(report);
        }
        else
        {
            var existing = await db.LabReports
                .Include(x => x.Results)
                .FirstOrDefaultAsync(x => x.Id == report.Id && x.UserId == report.UserId)
                ?? throw new InvalidOperationException("Lab report not found.");

            existing.ReportDate = report.ReportDate;
            existing.Notes = report.Notes;
            db.LabResults.RemoveRange(existing.Results);
            report = existing;
        }

        foreach (var (code, value) in values)
        {
            if (value is null) continue;
            var def = LabTestDefinitions.Get(code);
            report.Results.Add(new LabResult
            {
                TestCode = code,
                Value = value.Value,
                Unit = def?.Unit
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteReportAsync(int id, string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var report = await GetReportByIdAsync(db, id, userId);
        if (report is null) return;
        db.LabReports.Remove(report);
        await db.SaveChangesAsync();
    }

    private static Task<LabReport?> GetReportByIdAsync(ApplicationDbContext db, int id, string userId) =>
        db.LabReports
            .Include(x => x.Results)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
}

public sealed record LabMonthGroup(DateOnly Month, string Label, IReadOnlyList<LabReport> Reports);
