using System.Globalization;
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
}
