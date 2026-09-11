namespace MedicalManager.Data;

public enum LabRangeKind
{
    Between,
    LessThan,
    GreaterThan
}

public sealed record LabTestDefinition(
    string Code,
    string Name,
    string Unit,
    string RangeLabel,
    LabRangeKind RangeKind,
    decimal? Min,
    decimal? Max);

public static class LabTestDefinitions
{
    public static IReadOnlyList<LabTestDefinition> All { get; } =
    [
        new("glucose", "Glucose", "mg/dL", "70–100", LabRangeKind.Between, 70, 100),
        new("hba1c", "HbA1c", "%", "4.0–5.6", LabRangeKind.Between, 4.0m, 5.6m),
        new("total-cholesterol", "Total Cholesterol", "mg/dL", "<200", LabRangeKind.LessThan, null, 200),
        new("ldl", "LDL", "mg/dL", "<100", LabRangeKind.LessThan, null, 100),
        new("hdl", "HDL", "mg/dL", ">40", LabRangeKind.GreaterThan, 40, null),
        new("triglycerides", "Triglycerides", "mg/dL", "<150", LabRangeKind.LessThan, null, 150),
        new("creatinine", "Creatinine", "mg/dL", "0.7–1.3", LabRangeKind.Between, 0.7m, 1.3m),
        new("bun", "BUN", "mg/dL", "7–20", LabRangeKind.Between, 7, 20),
        new("sodium", "Sodium", "mEq/L", "136–145", LabRangeKind.Between, 136, 145),
        new("potassium", "Potassium", "mEq/L", "3.5–5.0", LabRangeKind.Between, 3.5m, 5.0m),
        new("hemoglobin", "Hemoglobin", "g/dL", "12–17", LabRangeKind.Between, 12, 17),
        new("wbc", "WBC", "K/uL", "4.5–11.0", LabRangeKind.Between, 4.5m, 11.0m),
        new("platelets", "Platelets", "K/uL", "150–400", LabRangeKind.Between, 150, 400),
        new("tsh", "TSH", "mIU/L", "0.4–4.0", LabRangeKind.Between, 0.4m, 4.0m),
        new("alt", "ALT", "U/L", "7–56", LabRangeKind.Between, 7, 56),
        new("ast", "AST", "U/L", "10–40", LabRangeKind.Between, 10, 40)
    ];

    private static readonly Dictionary<string, LabTestDefinition> ByCode =
        All.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    public static LabTestDefinition? Get(string code) =>
        ByCode.TryGetValue(code, out var def) ? def : null;

    public static string HeaderLabel(LabTestDefinition def) =>
        $"{def.Name} ({def.RangeLabel} {def.Unit})";

    public static bool IsInRange(string testCode, decimal value)
    {
        var def = Get(testCode);
        if (def is null) return true;

        return def.RangeKind switch
        {
            LabRangeKind.Between => def.Min <= value && value <= def.Max,
            LabRangeKind.LessThan => value < def.Max,
            LabRangeKind.GreaterThan => value > def.Min,
            _ => true
        };
    }

    public static string CssClass(string testCode, decimal value) =>
        IsInRange(testCode, value) ? "lab-in-range" : "lab-out-of-range";
}
