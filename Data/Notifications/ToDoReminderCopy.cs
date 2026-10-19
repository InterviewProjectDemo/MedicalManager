using System.Globalization;
using System.Net;
using System.Text;
using MedicalManager.Data;

namespace MedicalManager.Data.Notifications;

public static class ToDoReminderCopy
{
    public static void SelfCheck()
    {
        var when = new DateTime(2026, 10, 15, 9, 0, 0);
        var dayBefore = BuildSms("Alex Rivera", "Pick up the lab form", when, AppointmentReminderKind.DayBefore);
        var dueSoon = BuildSms("Alex Rivera", "Pick up the lab form", when, AppointmentReminderKind.HourBefore);
        if (!dayBefore.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)
            || !dayBefore.Contains("Pick up the lab form", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Day-before to-do text is missing the task.");
        }

        if (!dueSoon.Contains("one hour", StringComparison.OrdinalIgnoreCase)
            || dueSoon.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Hour-before to-do text is wrong.");
        }

        var dayCall = BuildVoiceSsml("Alex Rivera", null, "Pick up the lab form", when, AppointmentReminderKind.DayBefore);
        var hourCall = BuildVoiceSsml("Alex Rivera", null, "Pick up the lab form", when, AppointmentReminderKind.HourBefore);
        if (!dayCall.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)
            || !dayCall.Contains("Pick up the lab form", StringComparison.Ordinal)
            || !hourCall.Contains("one hour", StringComparison.OrdinalIgnoreCase)
            || hourCall.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("To-do reminder call is wrong.");
        }
    }

    public static string BuildSms(string fullName, string description, DateTime finishBy, AppointmentReminderKind kind)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim();
        var task = string.IsNullOrWhiteSpace(description) ? "a to-do on your list" : description.Trim();
        var date = finishBy.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
        var time = finishBy.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        builder.Append("Hello ").Append(name).Append(", this is Medical Manager. ");
        if (kind == AppointmentReminderKind.HourBefore)
            builder.Append("Your to-do is due in about one hour, at ").Append(time).Append(": ").Append(task).Append('.');
        else
            builder.Append("A kind reminder: tomorrow, ").Append(date).Append(", by ").Append(time).Append(": ").Append(task).Append('.');

        var text = builder.ToString();
        return text.Length <= 480 ? text : text[..477] + "...";
    }

    public static string BuildVoiceSsml(
        string fullName,
        string? pronunciation,
        string description,
        DateTime finishBy,
        AppointmentReminderKind kind)
    {
        var name = NameSpeaker.Ssml(fullName, pronunciation);
        var task = Xml(string.IsNullOrWhiteSpace(description) ? "a to-do on your list" : description.Trim());
        var date = Xml(finishBy.ToString("dddd, MMMM d", CultureInfo.InvariantCulture));
        var time = TimeSsml(finishBy);
        var detail = kind == AppointmentReminderKind.HourBefore
            ? $"Your to-do is due in about one hour, at {time}: {task}."
            : $"I wanted to kindly remind you about a to-do for tomorrow, {date}, by {time}: {task}.";
        var body = $"""
            Hi, {name}.
            <break time="180ms"/>
            It's Medical Manager. {detail}
            <break time="160ms"/>
            Take care.
            """;
        return $"""
            <speak xml:lang="en-US">
              <prosody rate="92%">{body}</prosody>
            </speak>
            """;
    }

    private static string TimeSsml(DateTime value)
    {
        var clock = value.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
        return $"""<say-as interpret-as="time" format="hms12">{Xml(clock)}</say-as>""";
    }

    private static string Xml(string value) => WebUtility.HtmlEncode(value);
}
