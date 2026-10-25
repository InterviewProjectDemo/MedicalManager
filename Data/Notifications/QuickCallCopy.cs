using System.Globalization;
using System.Net;

namespace MedicalManager.Data.Notifications;

public static class QuickCallCopy
{
    public static void SelfCheck()
    {
        var emergency = EmergencySsml("Alex Rivera", "512-555-0142", 30.267153, -97.743061, 6);
        if (!emergency.Contains("emergency call", StringComparison.OrdinalIgnoreCase)
            || !emergency.Contains("latitude", StringComparison.OrdinalIgnoreCase)
            || !emergency.Contains("Rivera", StringComparison.Ordinal))
            throw new InvalidOperationException("Emergency call script is missing the caller or location.");

        var withoutGps = EmergencySsml("Alex Rivera", "512-555-0142", null, null, null);
        if (withoutGps.Contains("latitude", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Emergency call without GPS should not speak a location.");

        var family = FamilySsml("Alex Rivera", "Jordan Rivera");
        if (!family.Contains("family call", StringComparison.OrdinalIgnoreCase)
            || !family.Contains("Jordan", StringComparison.Ordinal))
            throw new InvalidOperationException("Family call script is missing the user or family member.");
    }

    public static string EmergencySsml(string? name, string? phone, double? latitude, double? longitude, double? accuracy)
    {
        var spokenName = NameSpeaker.Ssml(name, null);
        return $"""
            <speak xml:lang="en-US">
              <prosody rate="92%">
                Hi, {spokenName}.
                <break time="180ms"/>
                It's Medical Manager. This is your emergency call.
                <break time="160ms"/>
                Your phone number is {PhoneSsml(phone)}.
                <break time="140ms"/>
                {LocationSentence(latitude, longitude, accuracy)}
                <break time="200ms"/>
                Stay where you are if you can, and keep your phone with you.
              </prosody>
            </speak>
            """;
    }

    public static string FamilySsml(string? userName, string? familyName)
    {
        var spokenName = NameSpeaker.Ssml(userName, null);
        var family = string.IsNullOrWhiteSpace(familyName)
            ? "your family contact"
            : NameSpeaker.Ssml(familyName, null);
        return $"""
            <speak xml:lang="en-US">
              <prosody rate="92%">
                Hi, {spokenName}.
                <break time="180ms"/>
                It's Medical Manager. This is your family call with {family}.
                <break time="200ms"/>
                Please stay on the line.
              </prosody>
            </speak>
            """;
    }

    private static string PhoneSsml(string? phone)
    {
        var e164 = PhoneNumbers.ToE164(phone);
        return e164 is null
            ? "not on your profile"
            : $"""<say-as interpret-as="telephone">{WebUtility.HtmlEncode(e164)}</say-as>""";
    }

    private static string LocationSentence(double? latitude, double? longitude, double? accuracy)
    {
        if (latitude is null || longitude is null)
            return "Your GPS location is not available yet.";

        var sentence = $"Your GPS location is latitude {SpokenCoordinate(latitude.Value)}, longitude {SpokenCoordinate(longitude.Value)}.";
        if (accuracy is null)
            return sentence;

        var meters = Math.Max(1, (int)Math.Round(accuracy.Value)).ToString(CultureInfo.InvariantCulture);
        return sentence + $" It is accurate to about {meters} meters.";
    }

    private static string SpokenCoordinate(double value)
    {
        var digits = Math.Abs(value).ToString("0.000000", CultureInfo.InvariantCulture)
            .Replace(".", " point ", StringComparison.Ordinal);
        return value < 0 ? "minus " + digits : digits;
    }
}
