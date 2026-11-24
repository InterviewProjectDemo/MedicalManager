namespace MedicalManager.Data;

public enum LabRangeKind
{
    Between,
    LessThan,
    GreaterThan,
    AtLeast
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
        new("ast", "AST", "U/L", "10–40", LabRangeKind.Between, 10, 40),
        new("hematocrit", "Hematocrit", "%", "36–50", LabRangeKind.Between, 36, 50),
        new("mcv", "MCV", "fL", "80–100", LabRangeKind.Between, 80, 100),
        new("mch", "MCH", "pg", "27–33", LabRangeKind.Between, 27, 33),
        new("mchc", "MCHC", "g/dL", "32–36", LabRangeKind.Between, 32, 36),
        new("rdw", "RDW", "%", "11.5–14.5", LabRangeKind.Between, 11.5m, 14.5m),
        new("neutrophils", "Neutrophils", "%", "40–70", LabRangeKind.Between, 40, 70),
        new("lymphocytes", "Lymphocytes", "%", "20–40", LabRangeKind.Between, 20, 40),
        new("monocytes", "Monocytes", "%", "2–8", LabRangeKind.Between, 2, 8),
        new("eosinophils", "Eosinophils", "%", "1–4", LabRangeKind.Between, 1, 4),
        new("basophils", "Basophils", "%", "0–1", LabRangeKind.Between, 0, 1),
        new("esr", "ESR", "mm/hr", "<20", LabRangeKind.LessThan, null, 20),
        new("reticulocyte", "Reticulocytes", "%", "0.5–1.5", LabRangeKind.Between, 0.5m, 1.5m),
        new("chloride", "Chloride", "mEq/L", "98–106", LabRangeKind.Between, 98, 106),
        new("bicarbonate", "CO2", "mEq/L", "23–29", LabRangeKind.Between, 23, 29),
        new("magnesium", "Magnesium", "mg/dL", "1.7–2.2", LabRangeKind.Between, 1.7m, 2.2m),
        new("phosphorus", "Phosphorus", "mg/dL", "2.5–4.5", LabRangeKind.Between, 2.5m, 4.5m),
        new("albumin", "Albumin", "g/dL", "3.5–5.0", LabRangeKind.Between, 3.5m, 5.0m),
        new("total-protein", "Total Protein", "g/dL", "6.0–8.3", LabRangeKind.Between, 6.0m, 8.3m),
        new("alk-phos", "Alk Phos", "U/L", "44–147", LabRangeKind.Between, 44, 147),
        new("bilirubin", "Bilirubin", "mg/dL", "0.1–1.2", LabRangeKind.Between, 0.1m, 1.2m),
        new("direct-bilirubin", "Direct Bilirubin", "mg/dL", "0.0–0.3", LabRangeKind.Between, 0m, 0.3m),
        new("free-t4", "Free T4", "ng/dL", "0.8–1.8", LabRangeKind.Between, 0.8m, 1.8m),
        new("troponin", "Troponin", "ng/L", "<14", LabRangeKind.LessThan, null, 14),
        new("bnp", "BNP", "pg/mL", "<100", LabRangeKind.LessThan, null, 100),
        new("pt", "PT", "sec", "11–13.5", LabRangeKind.Between, 11, 13.5m),
        new("inr", "INR", "", "0.8–1.2", LabRangeKind.Between, 0.8m, 1.2m),
        new("aptt", "aPTT", "sec", "25–35", LabRangeKind.Between, 25, 35),
        new("iron", "Iron", "mcg/dL", "60–170", LabRangeKind.Between, 60, 170),
        new("tibc", "TIBC", "mcg/dL", "240–450", LabRangeKind.Between, 240, 450),
        new("ferritin", "Ferritin", "ng/mL", "30–400", LabRangeKind.Between, 30, 400),
        new("crp", "CRP", "mg/L", "<10", LabRangeKind.LessThan, null, 10),
        new("vitamin-d", "Vitamin D", "ng/mL", "30–100", LabRangeKind.Between, 30, 100),
        new("b12", "Vitamin B12", "pg/mL", "200–900", LabRangeKind.Between, 200, 900),
        new("folate", "Folate", "ng/mL", ">4", LabRangeKind.GreaterThan, 4, null),
        new("urine-sg", "Urine SG", "", "1.005–1.030", LabRangeKind.Between, 1.005m, 1.030m),
        new("urine-ph", "Urine pH", "", "5.0–8.0", LabRangeKind.Between, 5.0m, 8.0m),
        new("urine-protein", "Urine protein", "mg/dL", "<15", LabRangeKind.LessThan, null, 15),
        new("urine-acr", "Urine ACR", "mg/g", "<30", LabRangeKind.LessThan, null, 30)
    ];

    public static IReadOnlyList<LabTestDefinition> DialysisSection { get; } = CreateDialysisSection();

    public static IReadOnlyList<LabTestDefinition> DialysisOnly { get; } =
        DialysisSection.Where(test => All.All(existing => !existing.Code.Equals(test.Code, StringComparison.OrdinalIgnoreCase))).ToList();

    private static IReadOnlyList<LabTestDefinition> CreateDialysisSection()
    {
        var hemoglobin = All.First(test => test.Code == "hemoglobin");
        return
        [
            hemoglobin,
            new("iron-saturation", "Iron saturation", "%", "20–50", LabRangeKind.Between, 20, 50),
            new("dialysis-ferritin", "Ferritin", "ng/mL", "200–500", LabRangeKind.Between, 200, 500),
            new("dialysis-albumin", "Albumin", "g/dL", "≥4.0", LabRangeKind.AtLeast, 4.0m, null),
            new("dialysis-phosphorus", "Phosphorus", "mg/dL", "3.5–5.5", LabRangeKind.Between, 3.5m, 5.5m),
            new("pth", "PTH intact", "pg/mL", "150–600", LabRangeKind.Between, 150, 600),
            new("dialysis-potassium", "Potassium", "mEq/L", "3.5–5.5", LabRangeKind.Between, 3.5m, 5.5m),
            new("ktv", "Kt/V", "spKt/V", "≥1.2", LabRangeKind.AtLeast, 1.2m, null)
        ];
    }

    private static readonly Dictionary<string, LabTestDefinition> ByCode =
        All.Concat(DialysisSection)
            .GroupBy(test => test.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

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
            LabRangeKind.AtLeast => value >= def.Min,
            _ => true
        };
    }

    public static string CssClass(string testCode, decimal value) =>
        IsInRange(testCode, value) ? "lab-in-range" : "lab-out-of-range";
}
