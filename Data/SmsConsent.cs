using MedicalManager.Data.Notifications;

namespace MedicalManager.Data;

public static class SmsConsent
{
    public static bool IsGranted(PatientProfile? profile, string? phone)
    {
        if (profile?.SmsConsentAcceptedAt is null || string.IsNullOrWhiteSpace(profile.SmsConsentPhone))
            return false;

        return SamePhone(profile.SmsConsentPhone, phone);
    }

    public static bool SamePhone(string? left, string? right)
    {
        var a = PhoneNumbers.ToE164(left);
        var b = PhoneNumbers.ToE164(right);
        if (a is not null && b is not null)
            return string.Equals(a, b, StringComparison.Ordinal);

        return string.Equals((left ?? "").Trim(), (right ?? "").Trim(), StringComparison.Ordinal);
    }
}
