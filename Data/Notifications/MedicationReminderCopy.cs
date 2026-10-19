using System.Net;
using System.Text;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public static class MedicationReminderCopy
{
    public static void SelfCheck()
    {
        var body = BuildSms("Alex Rivera", MedicationTimeSlot.Morning, ["Lisinopril 10 mg", "Metformin 500 mg"]);
        if (!body.Contains("Alex Rivera", StringComparison.Ordinal)
            || !body.Contains("Morning", StringComparison.Ordinal)
            || !body.Contains("Lisinopril 10 mg", StringComparison.Ordinal)
            || !body.Contains("Metformin 500 mg", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Medication reminder text is missing the name or doses.");
        }

        var speech = BuildVoiceSsml("Alex Rivera", null, MedicationTimeSlot.Morning, ["Lisinopril 10 mg", "Metformin 500 mg"]);
        if (!speech.Contains("Alex", StringComparison.Ordinal)
            || !speech.Contains("morning", StringComparison.Ordinal)
            || !speech.Contains("Lisinopril 10 mg", StringComparison.Ordinal)
            || !speech.Contains("Metformin 500 mg", StringComparison.Ordinal)
            || !speech.Contains("<speak", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Medication reminder call is missing the name or doses.");
        }

        if (!MedicationScheduleRules.IsWindowOpen(new DateOnly(2026, 10, 14), MedicationTimeSlot.Morning, new DateTime(2026, 10, 14, 6, 5, 0))
            || MedicationScheduleRules.IsWindowOpen(new DateOnly(2026, 10, 14), MedicationTimeSlot.Morning, new DateTime(2026, 10, 14, 5, 59, 0))
            || MedicationScheduleRules.IsWindowOpen(new DateOnly(2026, 10, 14), MedicationTimeSlot.Morning, new DateTime(2026, 10, 14, 11, 1, 0)))
        {
            throw new InvalidOperationException("Medication reminder window is wrong.");
        }
    }

    public static string BuildSms(string fullName, MedicationTimeSlot slot, IReadOnlyList<string> doses)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim();
        var when = MedicationScheduleRules.GetDisplayName(slot);
        var until = MedicationScheduleRules.GetWindowLabel(slot);
        var listed = doses.Where(dose => !string.IsNullOrWhiteSpace(dose)).Select(dose => dose.Trim()).ToList();
        if (listed.Count == 0)
            listed.Add("your scheduled medicine");

        var builder = new StringBuilder();
        builder.Append("Hello ").Append(name).Append(", this is Medical Manager. ");
        builder.Append(when).Append(" medicine reminder");
        if (!string.IsNullOrWhiteSpace(until))
            builder.Append(", ").Append(until.ToLowerInvariant());
        builder.Append(": ");
        builder.Append(string.Join("; ", listed));
        builder.Append(". We hope this dose is easy to take.");
        var text = builder.ToString();
        return text.Length <= 480 ? text : text[..477] + "...";
    }

    public static string BuildVoiceSsml(string fullName, string? pronunciation, MedicationTimeSlot slot, IReadOnlyList<string> doses)
    {
        var name = NameSpeaker.Ssml(fullName, pronunciation);
        var when = Xml(MedicationScheduleRules.GetDisplayName(slot).ToLowerInvariant());
        var listed = doses.Where(dose => !string.IsNullOrWhiteSpace(dose)).Select(dose => dose.Trim()).ToList();
        if (listed.Count == 0)
            listed.Add("your scheduled medicine");

        var body = $"""
            Hi, {name}.
            <break time="180ms"/>
            It's Medical Manager, with a gentle reminder about your {when} medicine.
            <break time="140ms"/>
            Please take {SpeakList(listed)}.
            <break time="160ms"/>
            I hope this dose is easy to take. Take care.
            """;
        return $"""
            <speak xml:lang="en-US">
              <prosody rate="92%">{body}</prosody>
            </speak>
            """;
    }

    private static string SpeakList(IReadOnlyList<string> items)
    {
        var encoded = items.Select(Xml).ToList();
        if (encoded.Count == 1)
            return encoded[0];
        if (encoded.Count == 2)
            return $"{encoded[0]}, and {encoded[1]}";
        return string.Join(", ", encoded.Take(encoded.Count - 1)) + ", and " + encoded[^1];
    }

    private static string Xml(string value) => WebUtility.HtmlEncode(value);
}
