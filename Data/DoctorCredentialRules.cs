using System.Text.RegularExpressions;

namespace MedicalManager.Data;

public static partial class DoctorCredentialRules
{
    public const int ClinicAddressMinLength = 12;
    public const int ClinicAddressMaxLength = 300;
    public const int MedicalSchoolMaxLength = 120;
    public const int SpecialtyMaxLength = 80;
    public const int LicenseMinLength = 5;
    public const int LicenseMaxLength = 32;
    public const int FullNameMaxLength = 200;

    public static IReadOnlyList<(string Message, string Member)> Validate(
        string? fullName,
        string? clinicAddress,
        string? medicalSchool,
        string? specialty,
        string? licenseState,
        string? licenseNumber)
    {
        var errors = new List<(string Message, string Member)>();
        Add(errors, ValidateFullName(fullName), "FullName");
        Add(errors, ValidateClinicAddress(clinicAddress), "ClinicAddress");
        Add(errors, ValidateMedicalSchool(medicalSchool), "MedicalSchool");
        Add(errors, ValidateSpecialty(specialty), "Specialty");
        Add(errors, ValidateLicenseState(licenseState), "LicenseState");
        Add(errors, ValidateLicenseNumber(licenseNumber), "LicenseNumber");
        return errors;
    }

    public static string? ValidateFullName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return "Enter the doctor's full name.";
        if (trimmed.Length > FullNameMaxLength)
            return "Full name must be 200 characters or fewer.";

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(part => LetterCount(part) < 2))
            return "Enter a full name with at least a first and last name.";
        if (parts.Any(IsBlockedNameWord))
            return "Enter the doctor's real name.";
        return null;
    }

    public static string? ValidateClinicAddress(string? address)
    {
        var trimmed = (address ?? "").Trim();
        if (trimmed.Length == 0)
            return "Enter the clinic street address.";
        if (trimmed.Length < ClinicAddressMinLength || trimmed.Length > ClinicAddressMaxLength)
            return "Enter a street address, including the number, city, and state.";
        if (!StreetAddressPattern().IsMatch(trimmed))
            return "Enter a street address, not only a city name.";
        return null;
    }

    public static string? ValidateMedicalSchool(string? school)
    {
        var trimmed = (school ?? "").Trim();
        if (trimmed.Length == 0)
            return "Enter the medical school.";
        if (trimmed.Length > MedicalSchoolMaxLength)
            return "Medical school must be 120 characters or fewer.";
        if (LetterCount(trimmed) < 3 || IsPlaceholder(trimmed))
            return "Enter the medical school name.";
        return null;
    }

    public static string? ValidateSpecialty(string? specialty)
    {
        var trimmed = (specialty ?? "").Trim();
        if (trimmed.Length == 0)
            return "Enter the specialty.";
        if (trimmed.Length > SpecialtyMaxLength)
            return "Specialty must be 80 characters or fewer.";
        if (LetterCount(trimmed) < 2 || IsPlaceholder(trimmed))
            return "Enter a clinical specialty.";
        return null;
    }

    public static string? ValidateLicenseState(string? state)
    {
        if (UsStates.IsValid(state))
            return null;
        return "Select the state where you are licensed to practice.";
    }

    public static string? ValidateLicenseNumber(string? license)
    {
        var trimmed = (license ?? "").Trim();
        if (trimmed.Length == 0)
            return "Enter the license number.";
        if (trimmed.Length < LicenseMinLength || trimmed.Length > LicenseMaxLength)
            return "Enter the full license number.";

        var significant = new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
        if (significant.Length < LicenseMinLength || !significant.Any(char.IsDigit))
            return "Enter the full license number.";
        if (significant.All(c => c == significant[0]))
            return "License number cannot be a repeated character.";
        if (IsPlaceholder(trimmed) || IsSequentialDigits(significant))
            return "Enter the license number issued by the state.";
        return null;
    }

    private static void Add(List<(string Message, string Member)> errors, string? message, string member)
    {
        if (message is not null)
            errors.Add((message, member));
    }

    private static int LetterCount(string value) => value.Count(char.IsLetter);

    private static bool IsBlockedNameWord(string part)
    {
        var letters = LettersOnly(part);
        return letters is "fake" or "test" or "asdf" or "qwerty" or "xxx";
    }

    private static bool IsPlaceholder(string value)
    {
        var letters = LettersOnly(value);
        return letters is "na" or "none" or "fake" or "test" or "asdf" or "unknown" or "tbd" or "xxx" or "null" or "license";
    }

    private static string LettersOnly(string value) =>
        new string(value.Where(char.IsLetter).ToArray()).ToLowerInvariant();

    private static bool IsSequentialDigits(string value)
    {
        if (value.Length < LicenseMinLength || !value.All(char.IsDigit))
            return false;

        var ascending = true;
        var descending = true;
        for (var i = 1; i < value.Length; i++)
        {
            var step = value[i] - value[i - 1];
            if (step != 1)
                ascending = false;
            if (step != -1)
                descending = false;
        }

        return ascending || descending;
    }

    [GeneratedRegex(@"^\d{1,6}\s+\p{L}{2,}")]
    private static partial Regex StreetAddressPattern();
}
