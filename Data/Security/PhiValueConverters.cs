using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MedicalManager.Data;

namespace MedicalManager.Data.Security;

public static class PhiValueConverters
{
    public static ValueConverter<string, string> RequiredString(IPhiProtector protector) =>
        new(v => protector.Protect(v), v => protector.Unprotect(v) ?? string.Empty);

    public static ValueConverter<string?, string?> OptionalString(IPhiProtector protector) =>
        new(v => string.IsNullOrEmpty(v) ? v : protector.Protect(v), v => protector.Unprotect(v));

    public static ValueConverter<string, string> RequiredLookup(IPhiProtector protector) =>
        new(v => protector.ProtectDeterministic(v), v => protector.Unprotect(v) ?? string.Empty);

    public static ValueConverter<string?, string?> OptionalLookup(IPhiProtector protector) =>
        new(v => string.IsNullOrEmpty(v) ? v : protector.ProtectDeterministic(v), v => protector.Unprotect(v));

    public static ValueConverter<int, string> RequiredInt(IPhiProtector protector) =>
        new(
            v => protector.Protect(v.ToString(CultureInfo.InvariantCulture)),
            v => int.Parse(protector.Unprotect(v) ?? "0", CultureInfo.InvariantCulture));

    public static ValueConverter<int?, string?> OptionalInt(IPhiProtector protector) =>
        new(
            v => v.HasValue ? protector.Protect(v.Value.ToString(CultureInfo.InvariantCulture)) : null,
            v => ParseOptionalInt(protector.Unprotect(v)));

    public static ValueConverter<decimal, string> RequiredDecimal(IPhiProtector protector) =>
        new(
            v => protector.Protect(v.ToString(CultureInfo.InvariantCulture)),
            v => decimal.Parse(protector.Unprotect(v) ?? "0", CultureInfo.InvariantCulture));

    public static ValueConverter<SugarKind, string> SugarKindValue(IPhiProtector protector) =>
        new(
            v => protector.Protect(v.ToString()),
            v => ParseSugarKind(protector.Unprotect(v)));

    public static ValueConverter<byte[]?, byte[]?> OptionalBytes(IPhiProtector protector) =>
        new(v => protector.ProtectBytes(v), v => protector.UnprotectBytes(v));

    public static ValueConverter<DateOnly?, string?> OptionalDate(IPhiProtector protector) =>
        new(
            v => v.HasValue ? protector.Protect(v.Value.ToString("yyyy-MM-dd")) : null,
            v => ParseOptionalDate(protector.Unprotect(v)));

    private static DateOnly? ParseOptionalDate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : DateOnly.Parse(text);

    private static int? ParseOptionalInt(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : int.Parse(text, CultureInfo.InvariantCulture);

    private static SugarKind ParseSugarKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SugarKind.Random;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && Enum.IsDefined(typeof(SugarKind), number))
            return (SugarKind)number;
        return Enum.Parse<SugarKind>(text, ignoreCase: true);
    }
}
