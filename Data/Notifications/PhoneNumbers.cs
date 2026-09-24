namespace MedicalManager.Data.Notifications;

public static class PhoneNumbers
{
    public static string? ToE164(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length is < 10 or > 15)
            return null;

        if (trimmed.StartsWith('+'))
            return "+" + digits;

        if (digits.Length == 10)
            return "+1" + digits;

        if (digits.Length == 11 && digits[0] == '1')
            return "+" + digits;

        return "+" + digits;
    }
}
