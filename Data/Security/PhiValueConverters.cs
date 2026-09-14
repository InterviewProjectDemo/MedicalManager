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

    private static DateOnly? ParseOptionalDate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : DateOnly.Parse(text);
}
