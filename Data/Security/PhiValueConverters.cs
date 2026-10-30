using System.Globalization;
using MedicalManager.Data;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MedicalManager.Data.Security;

public static class PhiValueConverters
{
    public static ValueConverter<string, string> RequiredString(IPhiProtector protector) =>
        new(v => protector.Protect(v), v => protector.Unprotect(v) ?? string.Empty);

    public static ValueConverter<string?, string?> OptionalString(IPhiProtector protector) =>
        new(v => string.IsNullOrEmpty(v) ? v : protector.Protect(v), v => protector.Unprotect(v));

    public static ValueConverter<byte[]?, byte[]?> OptionalBytes(IPhiProtector protector) =>
        new(v => protector.ProtectBytes(v), v => protector.UnprotectBytes(v));

    public static ValueConverter<DateOnly?, string?> OptionalDate(IPhiProtector protector) =>
        new(
            v => v.HasValue ? protector.Protect(v.Value.ToString("yyyy-MM-dd")) : null,
            v => ParseOptionalDate(protector.Unprotect(v)));

    public static ValueConverter<int, string> RequiredInt(IPhiProtector protector) =>
        new(
            v => protector.Protect(v.ToString(CultureInfo.InvariantCulture)),
            v => ParseRequiredInt(protector.Unprotect(v)));

    public static ValueConverter<int?, string?> OptionalInt(IPhiProtector protector) =>
        new(
            v => v.HasValue ? protector.Protect(v.Value.ToString(CultureInfo.InvariantCulture)) : null,
            v => ParseOptionalInt(protector.Unprotect(v)));

    public static ValueConverter<decimal, string> RequiredDecimal(IPhiProtector protector) =>
        new(
            v => protector.Protect(v.ToString(CultureInfo.InvariantCulture)),
            v => ParseRequiredDecimal(protector.Unprotect(v)));

    public static ValueConverter<SugarKind, string> SugarKindValue(IPhiProtector protector) =>
        new(
            v => protector.Protect(((int)v).ToString(CultureInfo.InvariantCulture)),
            v => ParseSugarKind(protector.Unprotect(v)));

    private static DateOnly? ParseOptionalDate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : DateOnly.Parse(text);

    private static int ParseRequiredInt(string? text) =>
        TryParseInt(text, out var value)
            ? value
            : throw new FormatException("A whole-number health value could not be read.");

    private static int? ParseOptionalInt(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : TryParseInt(text, out var value) ? value
        : throw new FormatException("A whole-number health value could not be read.");

    private static decimal ParseRequiredDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("A numeric health value could not be read.");

        if (decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value))
            return value;

        throw new FormatException("A numeric health value could not be read.");
    }

    private static SugarKind ParseSugarKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SugarKind.Random;

        if (TryParseInt(text, out var number) && Enum.IsDefined((SugarKind)number))
            return (SugarKind)number;

        if (Enum.TryParse<SugarKind>(text, ignoreCase: true, out var named))
            return named;

        throw new FormatException("A blood sugar reading type could not be read.");
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            && number == decimal.Truncate(number)
            && number >= int.MinValue
            && number <= int.MaxValue)
        {
            value = (int)number;
            return true;
        }

        return false;
    }
}
