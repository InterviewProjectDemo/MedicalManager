using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedicalManager.Data;

public sealed class DashboardLayout
{
    public string Theme { get; set; } = DashboardThemes.ClinicalTeal;
    public string Density { get; set; } = DashboardDensity.Comfortable;
    public string CardStyle { get; set; } = DashboardCardStyle.Elevated;
    public bool ShowHero { get; set; } = true;
    public List<DashboardWidgetPlacement> Widgets { get; set; } = [];

    public IEnumerable<DashboardWidgetPlacement> VisibleWidgets =>
        Widgets.Where(w => w.Enabled).OrderBy(w => w.Order).ThenBy(w => w.Id);

    public DashboardLayout Clone() => new()
    {
        Theme = Theme,
        Density = Density,
        CardStyle = CardStyle,
        ShowHero = ShowHero,
        Widgets = Widgets.Select(w => w.Clone()).ToList()
    };

    public string Fingerprint() => DashboardLayoutSerializer.Serialize(this);
}

public sealed class DashboardWidgetPlacement
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Order { get; set; }
    public string Width { get; set; } = DashboardWidgetWidth.Full;

    public DashboardWidgetPlacement Clone() => new()
    {
        Id = Id,
        Enabled = Enabled,
        Order = Order,
        Width = Width
    };
}

public static class DashboardWidgetIds
{
    public const string MedSchedule = "med-schedule";
    public const string DailyBp = "daily-bp";
    public const string DailySugar = "daily-sugar";
    public const string MedOverview = "med-overview";
    public const string TrendBp = "trend-bp";
    public const string TrendSugar = "trend-sugar";
    public const string DayAppointments = "day-appointments";
    public const string MissedAppointments = "missed-appointments";
    public const string UpcomingAppointments = "upcoming-appointments";
    public const string ToDo = "todos";
}

public static class DashboardWidgetWidth
{
    public const string Third = "third";
    public const string Half = "half";
    public const string Full = "full";

    public static readonly string[] All = [Third, Half, Full];

    public static string Label(string width) => width switch
    {
        Third => "1/3",
        Half => "1/2",
        _ => "Full"
    };

    public static int Span(string? width) => Normalize(width) switch
    {
        Third => 4,
        Half => 6,
        _ => 12
    };

    public static string FromSpan(int span) => span switch
    {
        <= 4 => Third,
        <= 8 => Half,
        _ => Full
    };

    public static string Normalize(string? width) =>
        width is Third or Half or Full ? width : Full;

    public static List<List<DashboardWidgetPlacement>> PackRows(IEnumerable<DashboardWidgetPlacement> widgets)
    {
        var rows = new List<List<DashboardWidgetPlacement>>();
        var current = new List<DashboardWidgetPlacement>();
        var used = 0;
        foreach (var widget in widgets)
        {
            var span = Span(widget.Width);
            if (used > 0 && used + span > 12)
            {
                rows.Add(current);
                current = [];
                used = 0;
            }

            current.Add(widget);
            used += span;
        }

        if (current.Count > 0)
            rows.Add(current);

        return rows;
    }
}

public static class DashboardThemes
{
    public const string ClinicalTeal = "clinical-teal";
    public const string HarborBlue = "harbor-blue";
    public const string WarmSunrise = "warm-sunrise";
    public const string GardenSage = "garden-sage";
    public const string MidnightSlate = "midnight-slate";

    public static readonly DashboardThemeOption[] All =
    [
        new(ClinicalTeal, "Clinical teal", "#0f3d5c", "#1a9b8e", "The familiar Medical Manager look."),
        new(HarborBlue, "Harbor blue", "#123a73", "#2f80ed", "Calm contrast for vitals-first days."),
        new(WarmSunrise, "Warm sunrise", "#9a4a12", "#f2994a", "Soft amber for morning check-ins."),
        new(GardenSage, "Garden sage", "#14532d", "#27ae60", "Quiet greens when medications lead."),
        new(MidnightSlate, "Midnight slate", "#0b1220", "#334155", "Low-glare evening review.")
    ];

    public static string Normalize(string? theme) =>
        All.Any(t => t.Id == theme) ? theme! : ClinicalTeal;
}

public sealed record DashboardThemeOption(string Id, string Name, string From, string To, string Description);

public static class DashboardDensity
{
    public const string Comfortable = "comfortable";
    public const string Compact = "compact";

    public static string Normalize(string? density) =>
        density is Compact ? Compact : Comfortable;
}

public static class DashboardCardStyle
{
    public const string Elevated = "elevated";
    public const string Outlined = "outlined";
    public const string Glass = "glass";

    public static string Normalize(string? style) => style switch
    {
        Outlined => Outlined,
        Glass => Glass,
        _ => Elevated
    };
}

public sealed record DashboardWidgetDefinition(
    string Id,
    string Title,
    string Description,
    string Category,
    string Accent,
    string DefaultWidth);

public static class DashboardWidgetCatalog
{
    // Catalog order is the recommended top-to-bottom priority for first-time users.
    public static readonly IReadOnlyList<DashboardWidgetDefinition> All =
    [
        new(DashboardWidgetIds.MedSchedule, "Today's medications", "Morning through night dose schedule with taken, skipped, and missed.", "Medications", "#27ae60", DashboardWidgetWidth.Full),
        new(DashboardWidgetIds.DailyBp, "Daily blood pressure", "Readings by time with high, low, and average.", "Vitals", "#2f80ed", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.DailySugar, "Daily blood sugar", "Glucose readings by time with high, low, and average.", "Vitals", "#f2994a", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.UpcomingAppointments, "Upcoming appointments", "What is next on the calendar.", "Appointments", "#9b51e0", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.MissedAppointments, "Missing appointments", "This month's visits that passed unmarked.", "Appointments", "#b42318", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.ToDo, "To Do", "Open tasks by finish date, with overdue items and near-term high-priority alerts.", "Follow-up", "#db2777", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.MedOverview, "Medication overview", "Active prescriptions with purpose and dose.", "Medications", "#27ae60", DashboardWidgetWidth.Full),
        new(DashboardWidgetIds.TrendBp, "Blood pressure trend", "Month-by-month systolic and diastolic chart.", "Trends", "#2f80ed", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.TrendSugar, "Blood sugar trend", "Month-by-month glucose chart.", "Trends", "#f2994a", DashboardWidgetWidth.Half),
        new(DashboardWidgetIds.DayAppointments, "Selected-day appointments", "Appointments for a date you choose.", "Appointments", "#9b51e0", DashboardWidgetWidth.Full)
    ];

    private static readonly (string Id, string Width)[] Recommended =
    [
        (DashboardWidgetIds.MedSchedule, DashboardWidgetWidth.Full),
        (DashboardWidgetIds.DailyBp, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.DailySugar, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.UpcomingAppointments, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.MissedAppointments, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.ToDo, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.MedOverview, DashboardWidgetWidth.Full),
        (DashboardWidgetIds.TrendBp, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.TrendSugar, DashboardWidgetWidth.Half),
        (DashboardWidgetIds.DayAppointments, DashboardWidgetWidth.Full)
    ];

    public static DashboardWidgetDefinition? Find(string id) =>
        All.FirstOrDefault(w => w.Id == id);

    public static DashboardLayout Default()
    {
        var layout = new DashboardLayout
        {
            Theme = DashboardThemes.ClinicalTeal,
            Density = DashboardDensity.Comfortable,
            CardStyle = DashboardCardStyle.Elevated,
            ShowHero = true
        };

        var order = 0;
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, width) in Recommended)
        {
            layout.Widgets.Add(new DashboardWidgetPlacement
            {
                Id = id,
                Enabled = true,
                Order = order++,
                Width = width
            });
            placed.Add(id);
        }

        foreach (var def in All.Where(d => !placed.Contains(d.Id)))
        {
            layout.Widgets.Add(new DashboardWidgetPlacement
            {
                Id = def.Id,
                Enabled = false,
                Order = 100 + order++,
                Width = def.DefaultWidth
            });
        }

        return layout;
    }

    public static DashboardLayout Normalize(DashboardLayout? layout)
    {
        layout ??= Default();
        layout.Theme = DashboardThemes.Normalize(layout.Theme);
        layout.Density = DashboardDensity.Normalize(layout.Density);
        layout.CardStyle = DashboardCardStyle.Normalize(layout.CardStyle);

        var byId = layout.Widgets
            .GroupBy(w => w.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var normalized = new List<DashboardWidgetPlacement>();
        foreach (var def in All)
        {
            if (byId.TryGetValue(def.Id, out var existing))
            {
                existing.Width = DashboardWidgetWidth.Normalize(existing.Width);
                normalized.Add(existing);
            }
            else
            {
                normalized.Add(new DashboardWidgetPlacement
                {
                    Id = def.Id,
                    Enabled = false,
                    Order = 100 + normalized.Count,
                    Width = def.DefaultWidth
                });
            }
        }

        var order = 0;
        foreach (var widget in normalized.Where(w => w.Enabled).OrderBy(w => w.Order).ThenBy(w => w.Id))
            widget.Order = order++;
        foreach (var widget in normalized.Where(w => !w.Enabled))
            widget.Order = 100 + order++;

        layout.Widgets = normalized;
        return layout;
    }
}

public static class DashboardLayoutSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(DashboardLayout layout) =>
        JsonSerializer.Serialize(layout, Options);

    public static DashboardLayout Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return DashboardWidgetCatalog.Default();

        try
        {
            var parsed = JsonSerializer.Deserialize<DashboardLayout>(json, Options);
            return DashboardWidgetCatalog.Normalize(parsed);
        }
        catch (JsonException)
        {
            return DashboardWidgetCatalog.Default();
        }
    }
}
