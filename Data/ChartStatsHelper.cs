namespace MedicalManager.Data;

public sealed class DailyVitalDayStats
{
    public static DailyVitalDayStats Empty { get; } = new();

    public int ReadingCount { get; init; }
    public string Average { get; init; } = "—";
    public string High { get; init; } = "—";
    public string Low { get; init; } = "—";
    public string? HighTime { get; init; }
    public string? LowTime { get; init; }
    public bool IsAverageOutOfRange { get; init; }
    public bool IsHighOutOfRange { get; init; }
    public bool IsLowOutOfRange { get; init; }

    public bool HasReadings => ReadingCount > 0;
}

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

    public static DailyVitalDayStats ForBloodPressureDay(IReadOnlyList<BloodPressureReading> dayReadings)
    {
        if (dayReadings.Count == 0)
            return DailyVitalDayStats.Empty;

        var avgSys = (int)Math.Round(dayReadings.Average(r => r.Systolic));
        var avgDia = (int)Math.Round(dayReadings.Average(r => r.Diastolic));
        var highSys = dayReadings.MaxBy(r => r.Systolic)!;
        var highDia = dayReadings.MaxBy(r => r.Diastolic)!;
        var lowSys = dayReadings.MinBy(r => r.Systolic)!;
        var lowDia = dayReadings.MinBy(r => r.Diastolic)!;

        return new DailyVitalDayStats
        {
            ReadingCount = dayReadings.Count,
            Average = $"{avgSys}/{avgDia}",
            High = highSys.Id == highDia.Id
                ? $"{highSys.Systolic}/{highSys.Diastolic}"
                : $"{highSys.Systolic}/{highDia.Diastolic}",
            Low = lowSys.Id == lowDia.Id
                ? $"{lowSys.Systolic}/{lowSys.Diastolic}"
                : $"{lowSys.Systolic}/{lowDia.Diastolic}",
            HighTime = FormatBpExtremeTime(highSys, highDia),
            LowTime = FormatBpExtremeTime(lowSys, lowDia),
            IsAverageOutOfRange = VitalStatus.IsBloodPressureOutOfRange(avgSys, avgDia),
            IsHighOutOfRange = VitalStatus.IsBloodPressureOutOfRange(highSys.Systolic, highDia.Diastolic),
            IsLowOutOfRange = VitalStatus.ForBloodPressure(lowSys.Systolic, lowDia.Diastolic).CssClass == "status-danger"
        };
    }

    public static List<ChartStatItem> ForBloodPressureMonth(IEnumerable<BloodPressureReading> readings)
    {
        var month = readings.ToList();
        if (month.Count == 0)
        {
            return
            [
                new() { Label = "Monthly average", Value = "—" },
                new() { Label = "Monthly high", Value = "—" },
                new() { Label = "Monthly low", Value = "—" }
            ];
        }

        return
        [
            Build("Monthly average", FormatAverage(month), month, AverageOutOfRange),
            Build("Monthly high", FormatHigh(month), month, HighOutOfRange, FormatHighTimestamp(month)),
            Build("Monthly low", FormatLow(month), month, LowOutOfRange, FormatLowTimestamp(month))
        ];
    }

    public static List<ChartStatItem> ForSugarMonth(IEnumerable<SugarReading> readings)
    {
        var month = readings.ToList();
        if (month.Count == 0)
        {
            return
            [
                new() { Label = "Monthly average", Value = "—" },
                new() { Label = "Monthly high", Value = "—" },
                new() { Label = "Monthly low", Value = "—" }
            ];
        }

        return
        [
            BuildSugar("Monthly average", month, AverageSugar),
            BuildSugar("Monthly high", month, HighSugar, includeTimestamp: true),
            BuildSugar("Monthly low", month, LowSugar, includeTimestamp: true)
        ];
    }

    public static List<ChartStatItem> ForDailyChart(DailyVitalDayStats stats)
    {
        if (!stats.HasReadings)
            return [];

        return
        [
            new() { Label = "Average", Value = stats.Average, IsOutOfRange = stats.IsAverageOutOfRange },
            new() { Label = "High", Value = stats.High, SubValue = stats.HighTime, IsOutOfRange = stats.IsHighOutOfRange },
            new() { Label = "Low", Value = stats.Low, SubValue = stats.LowTime, IsOutOfRange = stats.IsLowOutOfRange }
        ];
    }

    public static DailyVitalDayStats ForSugarDay(IReadOnlyList<SugarReading> dayReadings)
    {
        if (dayReadings.Count == 0)
            return DailyVitalDayStats.Empty;

        var avg = dayReadings.Average(r => r.Value);
        var unit = dayReadings[0].Unit;
        var kind = PredominantKind(dayReadings);
        var high = dayReadings.MaxBy(r => r.Value)!;
        var low = dayReadings.MinBy(r => r.Value)!;

        return new DailyVitalDayStats
        {
            ReadingCount = dayReadings.Count,
            Average = $"{avg:0} {unit}",
            High = $"{high.Value:0} {unit}",
            Low = $"{low.Value:0} {unit}",
            HighTime = high.RecordedAt.ToString("h:mm tt"),
            LowTime = low.RecordedAt.ToString("h:mm tt"),
            IsAverageOutOfRange = VitalStatus.IsSugarOutOfRange(avg, kind),
            IsHighOutOfRange = VitalStatus.IsSugarOutOfRange(high.Value, high.Kind),
            IsLowOutOfRange = VitalStatus.IsSugarOutOfRange(low.Value, low.Kind)
        };
    }

    private static string FormatLow(IReadOnlyList<BloodPressureReading> window)
    {
        var sysReading = window.MinBy(r => r.Systolic)!;
        var diaReading = window.MinBy(r => r.Diastolic)!;
        return $"{sysReading.Systolic}/{diaReading.Diastolic}";
    }

    private static string? FormatLowTimestamp(IReadOnlyList<BloodPressureReading> window)
    {
        var sysReading = window.MinBy(r => r.Systolic)!;
        var diaReading = window.MinBy(r => r.Diastolic)!;
        if (sysReading.Id == diaReading.Id)
            return FormatTimestamp(sysReading.RecordedAt);
        return $"Sys {FormatTimestamp(sysReading.RecordedAt)} · Dia {FormatTimestamp(diaReading.RecordedAt)}";
    }

    private static bool LowOutOfRange(IReadOnlyList<BloodPressureReading> window)
    {
        var minSys = window.Min(r => r.Systolic);
        var minDia = window.Min(r => r.Diastolic);
        return VitalStatus.ForBloodPressure(minSys, minDia).CssClass == "status-danger";
    }

    private static (string Value, bool OutOfRange, DateTime? RecordedAt) LowSugar(IReadOnlyList<SugarReading> window)
    {
        var low = window.MinBy(r => r.Value)!;
        return ($"{low.Value:0} {low.Unit}", VitalStatus.IsSugarOutOfRange(low.Value, low.Kind), low.RecordedAt);
    }

    private static string FormatBpExtremeTime(BloodPressureReading first, BloodPressureReading second)
    {
        if (first.Id == second.Id)
            return first.RecordedAt.ToString("h:mm tt");
        return $"Sys {first.RecordedAt:h:mm tt} · Dia {second.RecordedAt:h:mm tt}";
    }

}


