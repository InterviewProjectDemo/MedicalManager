using System.ComponentModel.DataAnnotations;

namespace MedicalManager.Data;

public enum ScheduleSeatStatus
{
    PendingDoctor = 1,
    PendingPatient = 2,
    Locked = 3,
    Closed = 4
}

public enum ScheduleSeatViewStatus
{
    Open,
    Held,
    Locked,
    Closed
}

public enum ScheduleNoticeLane
{
    NeedsYou,
    Waiting,
    Locked
}

/// <summary>
/// One hold on a doctor's calendar. Open time is the absence of a row.
/// A seat locks only after the patient and the doctor have both confirmed.
/// </summary>
public class ScheduleSeat
{
    public int Id { get; set; }

    [Required]
    public string DoctorUserId { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public ScheduleSeatStatus Status { get; set; }

    public string? PatientUserId { get; set; }

    public bool PatientConfirmed { get; set; }

    public bool DoctorConfirmed { get; set; }

    [MaxLength(200)]
    public string? Purpose { get; set; }

    public int? AppointmentId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class ScheduleGrid
{
    public static readonly TimeOnly DayStart = new(8, 0);
    public static readonly TimeOnly LastStart = new(16, 30);
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan HoldLifetime = TimeSpan.FromHours(48);

    public static IReadOnlyList<TimeOnly> Times { get; } = BuildTimes();

    public static DateOnly WeekStart(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-offset);
    }

    public static IReadOnlyList<DateOnly> WeekDays(DateOnly anyDay)
    {
        var monday = WeekStart(anyDay);
        return Enumerable.Range(0, 5).Select(offset => monday.AddDays(offset)).ToArray();
    }

    public static bool TryNormalize(DateTime value, out DateTime start, out DateTime end, out string? error)
    {
        start = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0);
        end = start.Add(Length);
        var time = TimeOnly.FromDateTime(start);
        if (start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            error = "The calendar is open Monday through Friday.";
            return false;
        }

        if (time < DayStart || time > LastStart || start.Minute is not (0 or 30))
        {
            error = "Choose one of the open seats.";
            return false;
        }

        if (start <= DateTime.Now)
        {
            error = "That seat is in the past.";
            return false;
        }

        error = null;
        return true;
    }

    private static TimeOnly[] BuildTimes()
    {
        var times = new List<TimeOnly>();
        for (var time = DayStart; time <= LastStart; time = time.Add(Length))
            times.Add(time);
        return times.ToArray();
    }
}

public sealed record ScheduleDoctorOption(
    string Id,
    string Name,
    string Specialty,
    string Clinic,
    bool Approved);

public sealed record ScheduleActionResult(bool Ok, string Message, bool Texted = false);

public sealed record ScheduleSeatView(
    DateTime StartsAt,
    DateTime EndsAt,
    int? SeatId,
    ScheduleSeatViewStatus Status,
    bool IsPast,
    bool CanOpen,
    bool ShowIdentity,
    bool AwaitingViewer,
    bool ViewerStarted,
    string Label,
    string? PersonName,
    string? PhotoUrl,
    string? Initials,
    string? Purpose,
    string AriaLabel,
    int? AppointmentId,
    bool CanCancel);

public sealed record ScheduleNotice(
    int SeatId,
    string DoctorUserId,
    string DoctorName,
    string PatientName,
    string? PhotoUrl,
    string? Initials,
    DateTime StartsAt,
    DateTime EndsAt,
    string? Purpose,
    ScheduleSeatStatus Status,
    ScheduleNoticeLane Lane,
    string Summary,
    int? AppointmentId);

public sealed record ScheduleBoard(
    string DoctorUserId,
    string DoctorName,
    string Specialty,
    string Clinic,
    DateOnly WeekStart,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<ScheduleSeatView> Seats);
