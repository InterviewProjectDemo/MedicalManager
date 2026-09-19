using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public enum ToDoPriority
{
    High,
    Medium,
    Low
}

public class ToDoItem
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public DateTime FinishBy { get; set; } = DateTime.Now.AddDays(1).Date.AddHours(9);

    public ToDoPriority Priority { get; set; } = ToDoPriority.Medium;

    [MaxLength(400)]
    public string? Notes { get; set; }

    public bool IsDone { get; set; }
}

public static class ToDoRules
{
    public const int AttentionHorizonDays = 3;

    public static bool IsOverdue(ToDoItem item, DateTime now) =>
        !item.IsDone && item.FinishBy < now;

    public static bool IsNearTermHigh(ToDoItem item, DateTime now)
    {
        if (item.IsDone || item.Priority != ToDoPriority.High) return false;
        var due = DateOnly.FromDateTime(item.FinishBy);
        var start = DateOnly.FromDateTime(now);
        return due >= start && due <= start.AddDays(AttentionHorizonDays);
    }

    public static string AttentionCountLabel(int count) => count == 1
        ? "1 high-priority task in the next 3 days"
        : $"{count} high-priority tasks in the next 3 days";

    public static int DaysUntilFinish(DateTime finishBy, DateTime now) =>
        DateOnly.FromDateTime(finishBy).DayNumber - DateOnly.FromDateTime(now).DayNumber;

    public static string DaysRemainingPhrase(DateTime finishBy, DateTime now)
    {
        var days = DaysUntilFinish(finishBy, now);
        return days switch
        {
            <= 0 => "due today",
            1 => "due in 1 day",
            _ => $"{days} days remaining"
        };
    }

    public static string AttentionPriorityLabel(ToDoItem item) => item.Priority.ToString();

    public static string AttentionBody(ToDoItem item, DateTime now) =>
        $"{item.Description} · {DaysRemainingPhrase(item.FinishBy, now)}";

    public static string AttentionLine(ToDoItem item, DateTime now) =>
        $"{AttentionPriorityLabel(item)} · {AttentionBody(item, now)}";

    public static string PriorityCss(ToDoPriority priority) => priority switch
    {
        ToDoPriority.High => "status-danger",
        ToDoPriority.Medium => "status-warn",
        ToDoPriority.Low => "status-ok",
        _ => "status-muted"
    };
}
