using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public interface IAllowOutOfRange
{
    bool AllowOutOfRange { get; }
}

public static class OverwriteNote
{
    public const string Lead = "Overwritten by ";
    public const int MaxLength = 400;

    public static string LockLine(string name, string role) =>
        $"{Lead}{name.Trim()}, {role.Trim()}.";

    public static bool IsOverwrite(string? notes) =>
        notes?.StartsWith(Lead, StringComparison.Ordinal) == true;

    public static string Compose(string lockLine, string? extra)
    {
        var rest = extra?.Replace("\r\n", "\n") ?? "";
        var notes = rest.Length == 0 ? lockLine : lockLine + "\n" + rest;
        return notes.Length <= MaxLength ? notes : notes[..MaxLength];
    }

    public static bool TrySplit(string? notes, out string lockLine, out string extra)
    {
        lockLine = "";
        extra = "";
        if (!IsOverwrite(notes) || notes is null)
            return false;

        var split = notes.IndexOf('\n');
        if (split < 0)
        {
            lockLine = notes;
            return true;
        }

        lockLine = notes[..split];
        extra = notes[(split + 1)..];
        return true;
    }

    public static string Person(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var cleaned = new string(value.Where(character => !char.IsControl(character)).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length <= max)
            return cleaned;

        return cleaned[..max].Trim();
    }

    public static void SelfCheck()
    {
        var line = LockLine("Jane Doe", "Nurse");
        if (line != "Overwritten by Jane Doe, Nurse.")
            throw new InvalidOperationException("Overwrite line is wrong.");

        var notes = Compose(line, "After lunch");
        if (!TrySplit(notes, out var locked, out var extra) || locked != line || extra != "After lunch")
            throw new InvalidOperationException("Overwrite note did not keep the extra text.");
        if (TrySplit("Morning reading", out _, out _))
            throw new InvalidOperationException("A normal note was treated as an overwrite.");

        var blocked = new BloodPressureReading
        {
            UserId = "patient",
            Systolic = 300,
            Diastolic = 80,
            AllowOutOfRange = false
        };
        if (Valid(blocked))
            throw new InvalidOperationException("Systolic 300 should stay blocked until overwrite.");

        blocked.AllowOutOfRange = true;
        if (!Valid(blocked))
            throw new InvalidOperationException("Overwrite should allow a systolic reading outside the usual range.");

        var sugar = new SugarReading
        {
            UserId = "patient",
            Value = 900,
            AllowOutOfRange = true
        };
        if (!Valid(sugar))
            throw new InvalidOperationException("Overwrite should allow a sugar reading outside the usual range.");
    }

    private static bool Valid(object model)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
    }
}
