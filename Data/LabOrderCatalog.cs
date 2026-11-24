namespace MedicalManager.Data;

public sealed record LabOrderItem(string Id, string Name, IReadOnlyList<string> AnalyteCodes);

public sealed record LabOrderGroup(string Name, IReadOnlyList<LabOrderItem> Items);

public static class LabOrderCatalog
{
    public static IReadOnlyList<LabOrderGroup> Groups { get; } =
    [
        new("Hematology",
        [
            new("cbc", "CBC with differential", ["wbc", "hemoglobin", "hematocrit", "platelets", "mcv", "mch", "mchc", "rdw", "neutrophils", "lymphocytes", "monocytes", "eosinophils", "basophils"]),
            new("platelet-count", "Platelet count", ["platelets"]),
            new("esr", "ESR", ["esr"]),
            new("retic", "Reticulocyte count", ["reticulocyte"])
        ]),
        new("Chemistry",
        [
            new("bmp", "BMP", ["sodium", "potassium", "chloride", "bicarbonate", "bun", "creatinine", "glucose"]),
            new("cmp", "CMP", ["sodium", "potassium", "chloride", "bicarbonate", "bun", "creatinine", "glucose", "albumin", "total-protein", "alk-phos", "alt", "ast", "bilirubin"]),
            new("magnesium", "Magnesium", ["magnesium"]),
            new("phosphorus", "Phosphorus", ["phosphorus"])
        ]),
        new("Diabetes",
        [
            new("fasting-glucose", "Fasting glucose", ["glucose"]),
            new("hba1c-order", "Hemoglobin A1c", ["hba1c"])
        ]),
        new("Lipids",
        [
            new("lipid-panel", "Lipid panel", ["total-cholesterol", "ldl", "hdl", "triglycerides"])
        ]),
        new("Kidney",
        [
            new("ua", "Urinalysis", ["urine-sg", "urine-ph", "urine-protein"]),
            new("uacr", "Urine microalbumin/creatinine", ["urine-acr"])
        ]),
        new("Liver",
        [
            new("hfp", "Hepatic function panel", ["albumin", "total-protein", "bilirubin", "direct-bilirubin", "alk-phos", "alt", "ast"])
        ]),
        new("Thyroid",
        [
            new("tsh-order", "TSH", ["tsh"]),
            new("free-t4", "Free T4", ["free-t4"])
        ]),
        new("Cardiac",
        [
            new("troponin", "Troponin", ["troponin"]),
            new("bnp", "BNP", ["bnp"])
        ]),
        new("Coagulation",
        [
            new("pt-inr", "PT/INR", ["pt", "inr"]),
            new("aptt", "aPTT", ["aptt"])
        ]),
        new("Iron",
        [
            new("iron", "Iron", ["iron"]),
            new("tibc", "TIBC", ["tibc"]),
            new("ferritin", "Ferritin", ["ferritin"])
        ]),
        new("Inflammation",
        [
            new("crp", "CRP", ["crp"])
        ]),
        new("Vitamins",
        [
            new("vitamin-d", "Vitamin D", ["vitamin-d"]),
            new("b12", "Vitamin B12", ["b12"]),
            new("folate", "Folate", ["folate"])
        ]),
        new("Dialysis",
        [
            new("dialysis-hemoglobin", "Hemoglobin", ["hemoglobin"]),
            new("iron-saturation", "Iron saturation", ["iron-saturation"]),
            new("dialysis-ferritin", "Ferritin", ["dialysis-ferritin"]),
            new("dialysis-albumin", "Albumin", ["dialysis-albumin"]),
            new("dialysis-phosphorus", "Phosphorus", ["dialysis-phosphorus"]),
            new("pth", "PTH intact", ["pth"]),
            new("dialysis-potassium", "Potassium", ["dialysis-potassium"]),
            new("ktv", "Kt/V", ["ktv"])
        ])
    ];

    private static readonly Dictionary<string, LabOrderItem> ById =
        Groups.SelectMany(group => group.Items).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Expand(IEnumerable<string> orderIds)
    {
        var codes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in orderIds)
        {
            if (!ById.TryGetValue(id, out var item))
                continue;

            foreach (var code in item.AnalyteCodes)
            {
                if (LabTestDefinitions.Get(code) is null || !seen.Add(code))
                    continue;

                codes.Add(code);
            }
        }

        return codes;
    }

    public static string AnalyteLabel(LabOrderItem item) =>
        string.Join(", ", item.AnalyteCodes.Select(code => LabTestDefinitions.Get(code)?.Name ?? code));
}
