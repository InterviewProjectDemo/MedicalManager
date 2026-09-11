namespace MedicalManager.Data;

public sealed class ChartStatItem
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string? SubValue { get; set; }
    public bool IsOutOfRange { get; set; }
}

public static class ChartStatsHelper
{
    public const int WeekDays = 7;
    public const int MonthDays = 30;

    public static List<ChartStatItem> ForBloodPressure(IEnumerable<BloodPressureReading> readings)
    {
        var list = readings.ToList();
        var cutoffWeek = DateTime.Now.AddDays(-WeekDays);
        var cutoffMonth = DateTime.Now.AddDays(-MonthDays);
        var week = list.Where(r => r.RecordedAt >= cutoffWeek).ToList();
        var month = list.Where(r => r.RecordedAt >= cutoffMonth).ToList();

        return
        [
            Build("Weekly average", FormatAverage(week), week, AverageOutOfRange),
            Build("Monthly average", FormatAverage(month), month, AverageOutOfRange),
            Build("Weekly high", FormatHigh(week), week, HighOutOfRange, FormatHighTimestamp(week)),
            Build("Monthly high", FormatHigh(month), month, HighOutOfRange, FormatHighTimestamp(month))
        ];
    }

    public static List<ChartStatItem> ForSugar(IEnumerable<SugarReading> readings)
    {
        var list = readings.ToList();
        var cutoffWeek = DateTime.Now.AddDays(-WeekDays);
        var cutoffMonth = DateTime.Now.AddDays(-MonthDays);
        var week = list.Where(r => r.RecordedAt >= cutoffWeek).ToList();
        var month = list.Where(r => r.RecordedAt >= cutoffMonth).ToList();

        return
        [
            BuildSugar("Weekly average", week, AverageSugar),
            BuildSugar("Monthly average", month, AverageSugar),
            BuildSugar("Weekly high", week, HighSugar, includeTimestamp: true),
            BuildSugar("Monthly high", month, HighSugar, includeTimestamp: true)
        ];
    }

    private static ChartStatItem Build(
        string label,
        string value,
        IReadOnlyList<BloodPressureReading> window,
        Func<IReadOnlyList<BloodPressureReading>, bool> isOutOfRange,
        string? subValue = null) =>
        new()
        {
            Label = label,
            Value = window.Count == 0 ? "—" : value,
            SubValue = window.Count == 0 ? null : subValue,
            IsOutOfRange = window.Count > 0 && isOutOfRange(window)
        };

    private static ChartStatItem BuildSugar(
        string label,
        IReadOnlyList<SugarReading> window,
        Func<IReadOnlyList<SugarReading>, (string Value, bool OutOfRange, DateTime? RecordedAt)> compute,
        bool includeTimestamp = false)
    {
        if (window.Count == 0)
            return new ChartStatItem { Label = label, Value = "—" };

        var (value, outOfRange, recordedAt) = compute(window);
        return new ChartStatItem
        {
            Label = label,
            Value = value,
            SubValue = includeTimestamp && recordedAt is not null ? FormatTimestamp(recordedAt.Value) : null,
            IsOutOfRange = outOfRange
        };
    }

    private static string FormatAverage(IReadOnlyList<BloodPressureReading> window)
    {
        var avgSys = (int)Math.Round(window.Average(r => r.Systolic));
        var avgDia = (int)Math.Round(window.Average(r => r.Diastolic));
        return $"{avgSys}/{avgDia}";
    }

    private static string FormatHigh(IReadOnlyList<BloodPressureReading> window)
    {
        var sysReading = window.MaxBy(r => r.Systolic)!;
        var diaReading = window.MaxBy(r => r.Diastolic)!;
        return $"{sysReading.Systolic}/{diaReading.Diastolic}";
    }

    private static string? FormatHighTimestamp(IReadOnlyList<BloodPressureReading> window)
    {
        var sysReading = window.MaxBy(r => r.Systolic)!;
        var diaReading = window.MaxBy(r => r.Diastolic)!;

        if (sysReading.Id == diaReading.Id)
            return FormatTimestamp(sysReading.RecordedAt);

        return $"Sys {FormatTimestamp(sysReading.RecordedAt)} · Dia {FormatTimestamp(diaReading.RecordedAt)}";
    }

    private static string FormatTimestamp(DateTime recordedAt) =>
        recordedAt.ToString("MMM d, yyyy · h:mm tt");

    private static bool AverageOutOfRange(IReadOnlyList<BloodPressureReading> window)
    {
        var avgSys = (int)Math.Round(window.Average(r => r.Systolic));
        var avgDia = (int)Math.Round(window.Average(r => r.Diastolic));
        return VitalStatus.IsBloodPressureOutOfRange(avgSys, avgDia);
    }

    private static bool HighOutOfRange(IReadOnlyList<BloodPressureReading> window)
    {
        var maxSys = window.Max(r => r.Systolic);
        var maxDia = window.Max(r => r.Diastolic);
        return VitalStatus.IsBloodPressureOutOfRange(maxSys, maxDia);
    }

    private static (string Value, bool OutOfRange, DateTime? RecordedAt) AverageSugar(IReadOnlyList<SugarReading> window)
    {
        var avg = window.Average(r => r.Value);
        var unit = window[0].Unit;
        var kind = PredominantKind(window);
        return ($"{avg:0} {unit}", VitalStatus.IsSugarOutOfRange(avg, kind), null);
    }

    private static (string Value, bool OutOfRange, DateTime? RecordedAt) HighSugar(IReadOnlyList<SugarReading> window)
    {
        var high = window.MaxBy(r => r.Value)!;
        return ($"{high.Value:0} {high.Unit}", VitalStatus.IsSugarOutOfRange(high.Value, high.Kind), high.RecordedAt);
    }

    private static SugarKind PredominantKind(IReadOnlyList<SugarReading> window) =>
        window.GroupBy(r => r.Kind).MaxBy(g => g.Count())!.Key;
}
