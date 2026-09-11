using System.Text.Json;
using System.Text.RegularExpressions;

namespace MedicalManager.Data;

public enum InteractionSeverity
{
    Minor,
    Moderate,
    Serious
}

public sealed record DrugInteraction(
    string ExistingMedication,
    string QueriedMedication,
    InteractionSeverity Severity,
    string Description);

public sealed record MedicationResearchResult(
    string QueriedMedication,
    string? MatchedDrugName,
    IReadOnlyList<DrugInteraction> Interactions,
    IReadOnlyList<string> SideEffects,
    bool UsedOpenFda,
    string? DataSourceNote);

public sealed class MedicationResearchService(
    IHttpClientFactory httpFactory,
    HealthRecordService records)
{
    private static readonly Dictionary<string, string> DrugAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lipitor"] = "atorvastatin",
        ["zocor"] = "simvastatin",
        ["crestor"] = "rosuvastatin",
        ["glucophage"] = "metformin",
        ["prinivil"] = "lisinopril",
        ["zestril"] = "lisinopril",
        ["advil"] = "ibuprofen",
        ["motrin"] = "ibuprofen",
        ["tylenol"] = "acetaminophen",
        ["coumadin"] = "warfarin",
        ["eliquis"] = "apixaban",
        ["xarelto"] = "rivaroxaban",
        ["plavix"] = "clopidogrel",
        ["aspirin"] = "aspirin",
        ["synthroid"] = "levothyroxine",
        ["lasix"] = "furosemide",
        ["norvasc"] = "amlodipine",
        ["prozac"] = "fluoxetine",
        ["zoloft"] = "sertraline",
    };

    private static readonly Dictionary<string, CuratedDrugProfile> CuratedProfiles = BuildCuratedProfiles();

    private static readonly List<InteractionRule> InteractionRules = BuildInteractionRules();

    public async Task<MedicationResearchResult> ResearchAsync(
        string medicationName,
        string patientUserId,
        Func<string, Task>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        await ReportAsync(onProgress, "Initializing research agent…");
        await DelayStepAsync(cancellationToken);

        await ReportAsync(onProgress, "Loading your current medications…");
        await DelayStepAsync(cancellationToken);
        var existingMeds = await records.GetMedicationsAsync(patientUserId, activeOnly: true);

        var normalizedQuery = NormalizeDrugName(medicationName);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            throw new ArgumentException("Enter a medication name to research.");
        }

        await ReportAsync(onProgress, $"Searching FDA database for {medicationName.Trim()}…");
        await DelayStepAsync(cancellationToken);
        var (openFdaSideEffects, matchedName, usedOpenFda) =
            await FetchOpenFdaSideEffectsAsync(medicationName.Trim(), cancellationToken);

        var sideEffects = openFdaSideEffects.Count > 0
            ? openFdaSideEffects
            : GetCuratedSideEffects(normalizedQuery);

        var interactions = new List<DrugInteraction>();
        foreach (var med in existingMeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReportAsync(onProgress, $"Checking interactions with {med.Name}…");
            await DelayStepAsync(cancellationToken);

            var found = FindInteractions(normalizedQuery, medicationName.Trim(), med.Name);
            interactions.AddRange(found);
        }

        await ReportAsync(onProgress, "Analyzing side effects…");
        await DelayStepAsync(cancellationToken);

        await ReportAsync(onProgress, "Compiling report…");
        await DelayStepAsync(cancellationToken);

        var dataNote = usedOpenFda
            ? "Side effects sourced from OpenFDA drug labeling."
            : sideEffects.Count > 0
                ? "Side effects from curated clinical reference (FDA label unavailable)."
                : "Limited data available for this medication.";

        return new MedicationResearchResult(
            medicationName.Trim(),
            matchedName ?? Capitalize(normalizedQuery),
            interactions.OrderByDescending(i => i.Severity).ToList(),
            sideEffects,
            usedOpenFda,
            dataNote);
    }

    private static async Task ReportAsync(Func<string, Task>? onProgress, string message)
    {
        if (onProgress is not null)
        {
            await onProgress(message);
        }
    }

    private static Task DelayStepAsync(CancellationToken cancellationToken) =>
        Task.Delay(500, cancellationToken);

    private async Task<(List<string> SideEffects, string? MatchedName, bool UsedOpenFda)> FetchOpenFdaSideEffectsAsync(
        string searchName,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = httpFactory.CreateClient("OpenFda");
            var escaped = Uri.EscapeDataString(searchName);
            var url =
                $"drug/label.json?search=openfda.brand_name:\"{escaped}\"+openfda.generic_name:\"{escaped}\"&limit=1";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ([], null, false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.GetArrayLength() == 0)
            {
                return ([], null, false);
            }

            var label = results[0];
            var matched = ExtractMatchedName(label, searchName);
            var effects = new List<string>();

            AddLabelSection(label, "adverse_reactions", effects);
            AddLabelSection(label, "warnings", effects);
            AddLabelSection(label, "warnings_and_cautions", effects);

            var distinct = effects
                .Select(NormalizeSideEffect)
                .Where(e => e.Length > 3)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            return (distinct, matched, distinct.Count > 0);
        }
        catch
        {
            return ([], null, false);
        }
    }

    private static void AddLabelSection(JsonElement label, string property, List<string> target)
    {
        if (!label.TryGetProperty(property, out var section))
        {
            return;
        }

        foreach (var item in section.EnumerateArray())
        {
            var text = item.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var line in SplitSideEffectLines(text))
            {
                target.Add(line);
            }
        }
    }

    private static IEnumerable<string> SplitSideEffectLines(string text)
    {
        var chunks = Regex.Split(text, @"(?<=[.;])\s+|\n+");
        foreach (var chunk in chunks)
        {
            var trimmed = chunk.Trim();
            if (trimmed.Length >= 8 && trimmed.Length <= 180)
            {
                yield return trimmed;
            }
        }

        if (!chunks.Any(c => c.Trim().Length >= 8) && text.Length <= 180)
        {
            yield return text.Trim();
        }
    }

    private static string? ExtractMatchedName(JsonElement label, string fallback)
    {
        if (label.TryGetProperty("openfda", out var openFda))
        {
            if (openFda.TryGetProperty("brand_name", out var brands) && brands.GetArrayLength() > 0)
            {
                return brands[0].GetString();
            }

            if (openFda.TryGetProperty("generic_name", out var generics) && generics.GetArrayLength() > 0)
            {
                return Capitalize(generics[0].GetString() ?? fallback);
            }
        }

        if (label.TryGetProperty("brand_name", out var brandName) && brandName.GetArrayLength() > 0)
        {
            return brandName[0].GetString();
        }

        return fallback;
    }

    private static List<string> GetCuratedSideEffects(string normalizedQuery)
    {
        if (CuratedProfiles.TryGetValue(normalizedQuery, out var profile))
        {
            return profile.SideEffects.ToList();
        }

        return
        [
            "Nausea or stomach upset",
            "Headache",
            "Dizziness",
            "Fatigue",
            "Consult product labeling for complete adverse reaction profile."
        ];
    }

    private static List<DrugInteraction> FindInteractions(
        string normalizedQuery,
        string displayQuery,
        string existingMedName)
    {
        var normalizedExisting = NormalizeDrugName(existingMedName);
        if (string.IsNullOrWhiteSpace(normalizedExisting))
        {
            return [];
        }

        if (normalizedQuery.Equals(normalizedExisting, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new DrugInteraction(
                    existingMedName,
                    displayQuery,
                    InteractionSeverity.Moderate,
                    "This appears to be the same medication already on your list. Verify dosage and avoid duplicate therapy.")
            ];
        }

        var hits = new List<DrugInteraction>();
        foreach (var rule in InteractionRules)
        {
            if (rule.Matches(normalizedQuery, normalizedExisting))
            {
                hits.Add(new DrugInteraction(
                    existingMedName,
                    displayQuery,
                    rule.Severity,
                    rule.Description));
            }
        }

        return hits;
    }

    private static string NormalizeDrugName(string name)
    {
        var cleaned = Regex.Replace(name.Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"(?i)\b\d+\s*(mg|mcg|g|ml|units?|iu)\b", "").Trim();
        cleaned = Regex.Replace(cleaned, @"(?i)\b(tablet|capsule|cap|tab|er|xr|sr|oral)\b", "").Trim();
        cleaned = cleaned.TrimEnd(',', '.', '-', ' ');

        if (DrugAliases.TryGetValue(cleaned, out var alias))
        {
            return alias;
        }

        return cleaned.ToLowerInvariant();
    }

    private static string NormalizeSideEffect(string text)
    {
        var single = Regex.Replace(text, @"\s+", " ").Trim();
        if (single.Length > 160)
        {
            single = single[..157] + "…";
        }

        return single;
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static Dictionary<string, CuratedDrugProfile> BuildCuratedProfiles() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lisinopril"] = new(
            [
                "Dry cough",
                "Dizziness or lightheadedness",
                "Elevated blood potassium",
                "Kidney function changes",
                "Angioedema (rare, seek emergency care for facial swelling)"
            ]),
            ["metformin"] = new(
            [
                "Nausea and diarrhea",
                "Abdominal discomfort",
                "Metallic taste",
                "Vitamin B12 deficiency with long-term use",
                "Lactic acidosis (rare, seek emergency care)"
            ]),
            ["atorvastatin"] = new(
            [
                "Muscle pain or weakness",
                "Joint pain",
                "Elevated liver enzymes",
                "Headache",
                "Digestive upset"
            ]),
            ["ibuprofen"] = new(
            [
                "Stomach irritation or heartburn",
                "Nausea",
                "Dizziness",
                "Increased blood pressure",
                "Kidney effects with prolonged use"
            ]),
            ["warfarin"] = new(
            [
                "Easy bruising or bleeding",
                "Nosebleeds",
                "Hair loss",
                "Skin necrosis (rare)",
                "Requires regular INR monitoring"
            ]),
            ["aspirin"] = new(
            [
                "Stomach irritation",
                "Heartburn",
                "Increased bleeding risk",
                "Ringing in ears (at high doses)",
                "Allergic reactions in sensitive patients"
            ]),
            ["amlodipine"] = new(
            [
                "Ankle swelling",
                "Flushing",
                "Palpitations",
                "Dizziness",
                "Fatigue"
            ]),
            ["furosemide"] = new(
            [
                "Increased urination",
                "Dehydration",
                "Low potassium",
                "Dizziness",
                "Muscle cramps"
            ]),
            ["levothyroxine"] = new(
            [
                "Palpitations",
                "Weight changes",
                "Heat intolerance",
                "Insomnia",
                "Tremor if dose is too high"
            ]),
            ["apixaban"] = new(
            [
                "Bleeding or bruising",
                "Nausea",
                "Anemia",
                "Spinal hematoma risk with spinal procedures",
                "No routine INR monitoring required"
            ]),
        };

    private static List<InteractionRule> BuildInteractionRules() =>
    [
        new(["lisinopril", "enalapril", "ramipril"], ["ibuprofen", "naproxen", "aspirin", "diclofenac"],
            InteractionSeverity.Moderate,
            "ACE inhibitors with NSAIDs may reduce blood pressure control and increase kidney injury risk."),
        new(["lisinopril", "enalapril", "ramipril"], ["potassium", "spironolactone", "triamterene"],
            InteractionSeverity.Serious,
            "Combined use can cause dangerously high potassium (hyperkalemia)."),
        new(["metformin"], ["alcohol", "ethanol"],
            InteractionSeverity.Moderate,
            "Alcohol increases the risk of lactic acidosis and low blood sugar with metformin."),
        new(["metformin"], ["contrast", "iodinated"],
            InteractionSeverity.Serious,
            "Iodinated contrast agents may increase lactic acidosis risk; metformin is often held around imaging."),
        new(["atorvastatin", "simvastatin", "rosuvastatin"], ["gemfibrozil", "fibrates"],
            InteractionSeverity.Serious,
            "Statins with fibrates significantly increase risk of muscle breakdown (rhabdomyolysis)."),
        new(["atorvastatin", "simvastatin"], ["clarithromycin", "erythromycin", "azole"],
            InteractionSeverity.Serious,
            "Certain antibiotics and antifungals raise statin levels and muscle toxicity risk."),
        new(["warfarin"], ["aspirin", "ibuprofen", "naproxen", "clopidogrel", "apixaban", "rivaroxaban"],
            InteractionSeverity.Serious,
            "Anticoagulants with antiplatelet or NSAID drugs greatly increase bleeding risk."),
        new(["warfarin"], ["amiodarone", "fluconazole", "metronidazole"],
            InteractionSeverity.Serious,
            "These agents can raise INR and bleeding risk with warfarin."),
        new(["lisinopril", "enalapril"], ["amlodipine", "hydrochlorothiazide", "furosemide"],
            InteractionSeverity.Moderate,
            "Combined antihypertensives may cause additive blood pressure lowering and dizziness."),
        new(["metformin"], ["furosemide", "thiazide"],
            InteractionSeverity.Moderate,
            "Diuretics may worsen blood sugar control and affect kidney function alongside metformin."),
        new(["levothyroxine"], ["calcium", "iron", "omeprazole"],
            InteractionSeverity.Moderate,
            "These agents reduce levothyroxine absorption; separate dosing by several hours."),
        new(["aspirin"], ["ibuprofen", "naproxen"],
            InteractionSeverity.Moderate,
            "Combining aspirin with NSAIDs increases gastrointestinal bleeding risk."),
        new(["apixaban", "rivaroxaban", "eliquis", "xarelto"], ["aspirin", "clopidogrel", "ibuprofen"],
            InteractionSeverity.Serious,
            "Direct oral anticoagulants with antiplatelet/NSAID therapy increase major bleeding risk."),
    ];

    private sealed record CuratedDrugProfile(IReadOnlyList<string> SideEffects);

    private sealed record InteractionRule(
        string[] DrugA,
        string[] DrugB,
        InteractionSeverity Severity,
        string Description)
    {
        public bool Matches(string queryNorm, string existingNorm) =>
            (ContainsAny(queryNorm, DrugA) && ContainsAny(existingNorm, DrugB)) ||
            (ContainsAny(queryNorm, DrugB) && ContainsAny(existingNorm, DrugA));

        private static bool ContainsAny(string name, IEnumerable<string> keys) =>
            keys.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
}
