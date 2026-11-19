namespace MedicalManager.Data;

public class MedicationChange
{
    public int Id { get; set; }

    public int MedicationId { get; set; }

    public Medication? Medication { get; set; }

    public string PatientUserId { get; set; } = string.Empty;

    public string? ActorUserId { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

public static class MedicationHistoryText
{
    public static string TimeLabel(string? frequency, MedicationDays days, IEnumerable<MedicationSchedule>? schedules)
    {
        var formatted = schedules is null ? null : MedicationDayRules.FormatSchedule(days, schedules);
        var freq = frequency?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(formatted) && !string.IsNullOrWhiteSpace(freq)
            && !formatted.Contains(freq, StringComparison.OrdinalIgnoreCase))
        {
            return $"{freq} · {formatted}";
        }

        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;

        return string.IsNullOrWhiteSpace(freq) ? "Not set" : freq;
    }

    public static string Added(string name, string dose, string time) =>
        $"Added {name.Trim()}. Dose {dose.Trim()}. Time {time.Trim()}.";

    public static string DoseChanged(string previous, string current) =>
        $"Dose changed from {Show(previous)} to {Show(current)}.";

    public static string TimeChanged(string previous, string current) =>
        $"Time changed from {Show(previous)} to {Show(current)}.";

    private static string Show(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not set" : value.Trim();
}
