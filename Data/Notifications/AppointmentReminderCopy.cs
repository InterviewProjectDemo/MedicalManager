using System.Globalization;
using System.Net;
using System.Text;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public sealed record ReminderFacts(
    string FullName,
    string? NamePronunciation,
    DateTime StartsAt,
    DateTime Now,
    AppointmentReminderKind Kind,
    string? Purpose,
    string? ProviderName,
    string? Location,
    string? TravelSentence);

public static class AppointmentReminderCopy
{
    public static void SelfCheck()
    {
        var when = new DateTime(2026, 9, 25, 14, 30, 0);
        var facts = new ReminderFacts(
            "Alex Rivera",
            null,
            when,
            when.AddDays(-1),
            AppointmentReminderKind.DayBefore,
            "Cardiology follow-up",
            "Dr. Morgan Chen",
            "Main Clinic, Room 214",
            "From your home, it is about 15 minutes by car, roughly about 6 miles away.");
        var twiml = BuildTwiml(facts, "Polly.Ruth-Neural", "en-US");
        if (twiml.Length > 3900)
            throw new InvalidOperationException("Reminder call script is too long for Twilio.");
        if (!twiml.Contains("phoneme", StringComparison.Ordinal) || !twiml.Contains("Rivera", StringComparison.Ordinal))
            throw new InvalidOperationException("Reminder call does not pronounce the full name.");
        if (!twiml.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)
            || !twiml.Contains("Cardiology follow-up", StringComparison.Ordinal)
            || !twiml.Contains("Main Clinic", StringComparison.Ordinal)
            || !twiml.Contains("15 minutes", StringComparison.Ordinal))
            throw new InvalidOperationException("Day-before call is missing the visit details.");

        var hour = facts with { Kind = AppointmentReminderKind.HourBefore, Now = when.AddHours(-1) };
        var hourTwiml = BuildTwiml(hour, "Polly.Ruth-Neural", "en-US");
        if (!hourTwiml.Contains("one hour", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hour-before call does not mention the hour.");
        if (hourTwiml.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Hour-before call should not say the visit is tomorrow.");

        var sms = BuildSms(facts);
        if (!sms.Contains("Alex Rivera", StringComparison.Ordinal) || !sms.Contains("Cardiology follow-up", StringComparison.Ordinal))
            throw new InvalidOperationException("Day-before text is missing the name or purpose.");
    }

    public static string BuildSms(ReminderFacts facts)
    {
        var name = string.IsNullOrWhiteSpace(facts.FullName) ? "there" : facts.FullName.Trim();
        var when = WhenClause(facts);
        var purpose = PurposeText(facts);
        var provider = Clean(facts.ProviderName);
        var location = Clean(facts.Location);
        var builder = new StringBuilder();
        builder.Append("Hello ").Append(name).Append(", this is Medical Manager. ");
        builder.Append("A kind reminder: ").Append(when).Append(" you have an appointment");
        builder.Append(" for ").Append(purpose);
        if (!string.IsNullOrWhiteSpace(provider))
            builder.Append(" with ").Append(provider);
        if (!string.IsNullOrWhiteSpace(location))
            builder.Append(" at ").Append(location);
        builder.Append('.');
        if (!string.IsNullOrWhiteSpace(facts.TravelSentence))
            builder.Append(' ').Append(facts.TravelSentence.Trim());
        builder.Append(" We hope your visit goes smoothly.");
        return builder.ToString();
    }

    public static string BuildTwiml(ReminderFacts facts, string pollyVoice, string language)
    {
        var voice = string.IsNullOrWhiteSpace(pollyVoice) ? "Polly.Ruth-Neural" : pollyVoice.Trim();
        var lang = string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim();
        var name = NameSpeaker.Ssml(facts.FullName, facts.NamePronunciation);
        var first = NameSpeaker.SsmlFirst(facts.FullName, facts.NamePronunciation);
        var body = facts.Kind == AppointmentReminderKind.HourBefore
            ? HourBeforeBody(facts, name, first)
            : DayBeforeBody(facts, name, first);

        return $"""
            <Response>
              <Say voice="{Xml(voice)}" language="{Xml(lang)}">
                <prosody rate="94%" pitch="+2%">{body}</prosody>
              </Say>
            </Response>
            """;
    }

    private static string DayBeforeBody(ReminderFacts facts, string name, string first)
    {
        var date = Xml(SpokenDate(facts.StartsAt));
        var time = TimeSsml(facts.StartsAt);
        var lead = RelativeDay(facts);
        var when = lead is "tomorrow" or "today"
            ? $"{Xml(Capitalize(lead))}, {date}, at {time}, you have an appointment."
            : $"{Xml(Capitalize(lead))}, at {time}, you have an appointment.";
        var details = DetailSentences(facts);
        var travel = TravelSsml(facts.TravelSentence);
        var closingName = string.IsNullOrWhiteSpace(first) ? "" : $""" <break time="200ms"/>{first}""";
        return $"""
            <break time="400ms"/>
            Hello, <break time="250ms"/>{name}.
            <break time="450ms"/>
            This is Medical Manager, calling with a kind reminder about your visit.
            <break time="350ms"/>
            {when}
            {details}
            {travel}
            <break time="400ms"/>
            Please give yourself a little extra time, so you can arrive calmly.
            We are wishing you a smooth and easy visit.
            Take care{closingName}.
            """;
    }

    private static string HourBeforeBody(ReminderFacts facts, string name, string first)
    {
        var date = Xml(SpokenDate(facts.StartsAt));
        var time = TimeSsml(facts.StartsAt);
        var details = DetailSentences(facts);
        var travel = TravelSsml(facts.TravelSentence);
        var closingName = string.IsNullOrWhiteSpace(first) ? "" : $""" <break time="200ms"/>{first}""";
        return $"""
            <break time="400ms"/>
            Hello, <break time="250ms"/>{name}.
            <break time="450ms"/>
            This is Medical Manager, with a gentle reminder.
            <break time="300ms"/>
            Your appointment is in about one hour, at {time} today, {date}.
            {details}
            {travel}
            <break time="400ms"/>
            This is a kind nudge to start getting ready.
            We hope the trip there is easy.
            Take care{closingName}.
            """;
    }

    private static string DetailSentences(ReminderFacts facts)
    {
        var purpose = Xml(PurposeText(facts));
        var provider = Clean(facts.ProviderName);
        var location = Clean(facts.Location);
        var builder = new StringBuilder();
        builder.Append("<break time=\"300ms\"/>It is for ").Append(purpose).Append('.');
        if (!string.IsNullOrWhiteSpace(provider))
        {
            builder.Append(" <break time=\"200ms\"/>You will be seeing ")
                .Append(Xml(provider))
                .Append('.');
        }

        builder.Append(" <break time=\"300ms\"/>");
        if (string.IsNullOrWhiteSpace(location))
        {
            builder.Append("The place for the visit is listed with your appointment.");
        }
        else
        {
            builder.Append("The visit is at ").Append(LocationSsml(location)).Append('.');
        }

        return builder.ToString();
    }

    private static string LocationSsml(string location)
    {
        var encoded = Xml(location);
        return location.Any(char.IsDigit)
            ? $"""<say-as interpret-as="address">{encoded}</say-as>"""
            : encoded;
    }

    private static string TravelSsml(string? travel)
    {
        if (string.IsNullOrWhiteSpace(travel))
            return "";

        return $"""<break time="300ms"/>{Xml(travel.Trim())}""";
    }

    private static string WhenClause(ReminderFacts facts)
    {
        var relative = RelativeDay(facts);
        var date = SpokenDate(facts.StartsAt);
        var time = facts.StartsAt.ToString("h:mm tt", CultureInfo.InvariantCulture);
        if (facts.Kind == AppointmentReminderKind.HourBefore)
            return $"today, {date}, at {time}, in about one hour,";

        return $"{relative}, {date}, at {time},";
    }

    private static string RelativeDay(ReminderFacts facts)
    {
        if (facts.Kind == AppointmentReminderKind.HourBefore)
            return "today";

        var day = DateOnly.FromDateTime(facts.StartsAt);
        var today = DateOnly.FromDateTime(facts.Now);
        if (day == today.AddDays(1))
            return "tomorrow";
        if (day == today)
            return "today";
        return "on " + facts.StartsAt.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
    }

    private static string SpokenDate(DateTime value) =>
        value.ToString("dddd, MMMM ", CultureInfo.InvariantCulture) + Ordinal(value.Day);

    private static string Ordinal(int day)
    {
        var suffix = (day % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (day % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };
        return day + suffix;
    }

    private static string TimeSsml(DateTime value)
    {
        var clock = value.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
        return $"""<say-as interpret-as="time" format="hms12">{Xml(clock)}</say-as>""";
    }

    private static string PurposeText(ReminderFacts facts)
    {
        var purpose = Clean(facts.Purpose);
        return string.IsNullOrWhiteSpace(purpose) ? "your scheduled visit" : purpose;
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.');

    private static string Xml(string value) => WebUtility.HtmlEncode(value);
}
