using System.Collections.Concurrent;
using MedicalManager.Data.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedicalManager.Data;

public sealed class ScheduleService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> users,
    ChartAccessService chartAccess,
    DoctorService doctors,
    ProfilePhotoService photos,
    ScheduleLiveBoard live,
    TwilioNotificationSender twilio,
    ILogger<ScheduleService> logger)
{
    private const string AskDoctor = "Medical Manager: a patient wants to book a time on your calendar. Open Calendar to confirm or decline.";
    private const string AskPatient = "Medical Manager: a doctor offered you a visit time. Open Calendar to accept or decline.";
    private const string LockedText = "Medical Manager: a visit time is confirmed. Open Calendar to see it.";
    private const string DeclinedText = "Medical Manager: a visit request was cancelled and that time is open again.";
    private const string CancelledText = "Medical Manager: a confirmed visit was cancelled and that time is open again.";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<ScheduleDoctorOption>> ListDoctorsAsync(string patientUserId)
    {
        var doctorsInRole = await users.GetUsersInRoleAsync(AppRoles.Doctor);
        await using var db = await dbFactory.CreateDbContextAsync();
        var approved = (await db.ChartAccessGrants.AsNoTracking()
            .Where(grant => grant.PatientUserId == patientUserId && grant.RevokedAt == null)
            .Select(grant => grant.DoctorUserId)
            .ToListAsync()).ToHashSet(StringComparer.Ordinal);

        return doctorsInRole
            .OrderByDescending(doctor => approved.Contains(doctor.Id))
            .ThenBy(doctor => doctor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(doctor => new ScheduleDoctorOption(
                doctor.Id,
                doctor.DisplayName,
                doctor.Specialty?.Trim() ?? "",
                doctor.ClinicAddress?.Trim() ?? "",
                approved.Contains(doctor.Id)))
            .ToList();
    }

    public async Task<ScheduleBoard?> GetBoardAsync(string actorUserId, string doctorUserId, DateOnly anyDay)
    {
        var actor = await users.FindByIdAsync(actorUserId);
        var doctor = await users.FindByIdAsync(doctorUserId);
        if (actor is null || doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor))
            return null;

        var actorIsDoctor = await users.IsInRoleAsync(actor, AppRoles.Doctor);
        if (actorIsDoctor && !string.Equals(actor.Id, doctor.Id, StringComparison.Ordinal))
            return null;
        if (!actorIsDoctor && !await users.IsInRoleAsync(actor, AppRoles.Patient))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync();
        var expired = await ExpireAsync(db, doctorUserId);
        var days = ScheduleGrid.WeekDays(anyDay);
        var windowStart = days[0].ToDateTime(TimeOnly.MinValue).AddHours(-12);
        var windowEnd = days[^1].ToDateTime(new TimeOnly(23, 59));
        var seats = await db.ScheduleSeats.AsNoTracking()
            .Where(seat => seat.DoctorUserId == doctorUserId && seat.StartsAt >= windowStart && seat.StartsAt <= windowEnd)
            .ToListAsync();
        var blockers = await LoadBlockersAsync(db, doctor, windowStart, windowEnd);
        var people = await LoadPeopleAsync(db, seats.Select(seat => seat.PatientUserId).Append(doctorUserId).Concat(blockers.Select(block => block.PatientUserId)));
        var grants = actorIsDoctor
            ? (await db.ChartAccessGrants.AsNoTracking()
                .Where(grant => grant.DoctorUserId == doctorUserId && grant.RevokedAt == null)
                .Select(grant => grant.PatientUserId)
                .ToListAsync()).ToHashSet(StringComparer.Ordinal)
            : [];

        var views = new List<ScheduleSeatView>();
        foreach (var day in days)
        {
            foreach (var time in ScheduleGrid.Times)
            {
                var start = day.ToDateTime(time);
                var end = start.Add(ScheduleGrid.Length);
                var seat = seats.FirstOrDefault(item => SameMinute(item.StartsAt, start));
                var blocker = seat is null
                    ? blockers.FirstOrDefault(block => Hits(block.StartsAt, block.EndsAt, start, end))
                    : null;
                views.Add(ToView(actor.Id, actorIsDoctor, start, end, seat, blocker, people, grants));
            }
        }

        if (expired)
            live.Publish(doctorUserId, null);

        return new ScheduleBoard(
            doctor.Id,
            doctor.DisplayName,
            doctor.Specialty?.Trim() ?? "",
            doctor.ClinicAddress?.Trim() ?? "",
            days[0],
            days,
            views);
    }

    public async Task<IReadOnlyList<ScheduleNotice>> GetNoticesAsync(string actorUserId, bool actorIsDoctor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var expiredDoctors = await ExpireAllAsync(db);
        var now = DateTime.Now;
        var query = db.ScheduleSeats.AsNoTracking()
            .Where(seat => seat.PatientUserId != null
                && (seat.Status == ScheduleSeatStatus.PendingDoctor
                    || seat.Status == ScheduleSeatStatus.PendingPatient
                    || seat.Status == ScheduleSeatStatus.Locked));
        query = actorIsDoctor
            ? query.Where(seat => seat.DoctorUserId == actorUserId)
            : query.Where(seat => seat.PatientUserId == actorUserId);
        var seats = await query.OrderBy(seat => seat.StartsAt).ToListAsync();

        seats = seats
            .Where(seat => seat.Status != ScheduleSeatStatus.Locked || seat.StartsAt >= now)
            .Take(20)
            .ToList();

        var ids = seats.Select(seat => seat.DoctorUserId)
            .Concat(seats.Select(seat => seat.PatientUserId))
            .Append(actorUserId);
        var people = await LoadPeopleAsync(db, ids!);
        var notices = seats.Select(seat => ToNotice(actorUserId, actorIsDoctor, seat, people)).ToList();
        foreach (var doctorId in expiredDoctors)
            live.Publish(doctorId, null);
        return notices;
    }

    public Task<bool> SharesSeatAsync(string doctorUserId, string patientUserId) =>
        SharesSeatInternalAsync(doctorUserId, patientUserId);

    public async Task<ScheduleActionResult> RequestAsync(string actorUserId, string doctorUserId, DateTime startsAt, string? purpose)
    {
        var actor = await users.FindByIdAsync(actorUserId);
        var doctor = await users.FindByIdAsync(doctorUserId);
        if (actor is null || !await users.IsInRoleAsync(actor, AppRoles.Patient))
            return Fail("Only a patient account can hold a seat.");
        if (doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor) || doctor.Id == actor.Id)
            return Fail("Choose a doctor.");
        if (!ScheduleGrid.TryNormalize(startsAt, out var start, out var end, out var error))
            return Fail(error!);
        if (!TryPurpose(purpose, out var clean, out error))
            return Fail(error!);

        var gate = Gates.GetOrAdd(doctor.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            await ExpireAsync(db, doctor.Id);
            var conflict = await ConflictAsync(db, doctor, actor.Id, start, end, exceptSeatId: null);
            if (conflict is not null)
                return Fail(conflict);

            db.ScheduleSeats.Add(NewSeat(doctor.Id, actor.Id, start, end, clean, ScheduleSeatStatus.PendingDoctor, patientConfirmed: true, doctorConfirmed: false));
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return Fail("That seat was just taken. Pick another open one.");
            }
        }
        finally
        {
            gate.Release();
        }

        var texted = await TryTextAsync(doctor.Id, AskDoctor);
        live.Publish(doctor.Id, actor.Id);
        var message = $"Seat held for {When(start)}. {doctor.DisplayName} has been asked to confirm. Nobody else can take it while it is held.";
        return Ok(message, texted);
    }

    public async Task<ScheduleActionResult> OfferAsync(string actorUserId, string patientUserId, DateTime startsAt, string? purpose)
    {
        var doctor = await users.FindByIdAsync(actorUserId);
        var patient = await users.FindByIdAsync(patientUserId);
        if (doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor))
            return Fail("Only a doctor account can offer a seat.");
        if (patient is null || !await users.IsInRoleAsync(patient, AppRoles.Patient) || patient.Id == doctor.Id)
            return Fail("Choose a patient.");
        var affiliated = await chartAccess.ListAffiliatedPatientsAsync(doctor.Id);
        if (!affiliated.Any(item => item.PatientId == patient.Id))
            return Fail("Choose a patient you are already connected with.");
        if (!ScheduleGrid.TryNormalize(startsAt, out var start, out var end, out var error))
            return Fail(error!);
        if (!TryPurpose(purpose, out var clean, out error))
            return Fail(error!);

        var gate = Gates.GetOrAdd(doctor.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            await ExpireAsync(db, doctor.Id);
            var conflict = await ConflictAsync(db, doctor, patient.Id, start, end, exceptSeatId: null);
            if (conflict is not null)
                return Fail(conflict);

            db.ScheduleSeats.Add(NewSeat(doctor.Id, patient.Id, start, end, clean, ScheduleSeatStatus.PendingPatient, patientConfirmed: false, doctorConfirmed: true));
            try
            {
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return Fail("That seat was just taken. Pick another open one.");
            }
        }
        finally
        {
            gate.Release();
        }

        var texted = await TryTextAsync(patient.Id, AskPatient);
        live.Publish(doctor.Id, patient.Id);
        return Ok($"Request sent to {patient.DisplayName} for {When(start)}. The seat stays held until they accept.", texted);
    }

    public async Task<ScheduleActionResult> ConfirmAsync(string actorUserId, int seatId)
    {
        var actor = await users.FindByIdAsync(actorUserId);
        if (actor is null)
            return Fail("Sign in again to confirm this seat.");

        var preview = await FindSeatAsync(seatId);
        if (preview is null || preview.PatientUserId is null)
            return Fail("That request is no longer waiting.");

        var doctor = await users.FindByIdAsync(preview.DoctorUserId);
        if (doctor is null)
            return Fail("That doctor account is no longer available.");

        var gate = Gates.GetOrAdd(preview.DoctorUserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        string? patientId = null;
        var locked = false;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            var seat = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.Id == seatId);
            if (seat is null || seat.PatientUserId is null)
                return Fail("That request is no longer waiting.");

            var actorIsDoctor = seat.DoctorUserId == actor.Id;
            var actorIsPatient = seat.PatientUserId == actor.Id;
            if (!actorIsDoctor && !actorIsPatient)
                return Fail("This request belongs to someone else.");
            if (seat.Status is not (ScheduleSeatStatus.PendingDoctor or ScheduleSeatStatus.PendingPatient))
                return Fail("That seat is no longer waiting for an answer.");
            if (actorIsDoctor && seat.Status != ScheduleSeatStatus.PendingDoctor)
                return Fail("Waiting on the patient to accept.");
            if (actorIsPatient && seat.Status != ScheduleSeatStatus.PendingPatient)
                return Fail("Waiting on the doctor to confirm.");

            var conflict = await ConflictAsync(db, doctor, seat.PatientUserId, seat.StartsAt, seat.EndsAt, seat.Id);
            if (conflict is not null)
            {
                var releasedDoctor = seat.DoctorUserId;
                var releasedPatient = seat.PatientUserId;
                db.ScheduleSeats.Remove(seat);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                live.Publish(releasedDoctor, releasedPatient);
                return Fail(conflict);
            }

            if (actorIsDoctor)
                seat.DoctorConfirmed = true;
            else
                seat.PatientConfirmed = true;
            seat.UpdatedAt = DateTime.UtcNow;

            if (seat.PatientConfirmed && seat.DoctorConfirmed)
            {
                var appointment = BuildAppointment(seat.PatientUserId, doctor, seat);
                db.Appointments.Add(appointment);
                seat.Status = ScheduleSeatStatus.Locked;
                await db.SaveChangesAsync();
                seat.AppointmentId = appointment.Id;
                locked = true;
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            patientId = seat.PatientUserId;
        }
        finally
        {
            gate.Release();
        }

        if (locked && patientId is not null)
        {
            try
            {
                await doctors.SyncDiscoveredDoctorsAsync(patientId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Doctor list sync failed after a locked seat.");
            }
        }

        var otherId = actor.Id == preview.DoctorUserId ? patientId : preview.DoctorUserId;
        var texted = otherId is not null && await TryTextAsync(otherId, locked ? LockedText : AskPatient);
        live.Publish(preview.DoctorUserId, patientId);
        if (!locked)
            return Ok("Confirmed. Waiting on the other person.", texted);

        var mine = actor.Id == patientId;
        var message = mine
            ? $"You both confirmed {When(preview.StartsAt)}. The visit is on your Appointments list."
            : $"You both confirmed {When(preview.StartsAt)}. The seat is locked, and the visit is on the patient's appointment list.";
        return Ok(message, texted);
    }

    public async Task<ScheduleActionResult> DeclineAsync(string actorUserId, int seatId)
    {
        var seat = await FindSeatAsync(seatId);
        if (seat is null)
            return Fail("That request is already gone.");
        if (seat.DoctorUserId != actorUserId && seat.PatientUserId != actorUserId)
            return Fail("This request belongs to someone else.");
        if (seat.Status is not (ScheduleSeatStatus.PendingDoctor or ScheduleSeatStatus.PendingPatient))
            return Fail("That seat is no longer a request.");

        var gate = Gates.GetOrAdd(seat.DoctorUserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var current = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.Id == seatId);
            if (current is null)
                return Ok("The seat is open again.");
            if (current.Status is not (ScheduleSeatStatus.PendingDoctor or ScheduleSeatStatus.PendingPatient))
                return Fail("That seat is no longer a request.");
            db.ScheduleSeats.Remove(current);
            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }

        var otherId = actorUserId == seat.DoctorUserId ? seat.PatientUserId : seat.DoctorUserId;
        var texted = otherId is not null && await TryTextAsync(otherId, DeclinedText);
        live.Publish(seat.DoctorUserId, seat.PatientUserId);
        return Ok("The seat is open again.", texted);
    }

    public async Task<ScheduleActionResult> CloseAsync(string doctorUserId, DateTime startsAt)
    {
        var doctor = await users.FindByIdAsync(doctorUserId);
        if (doctor is null || !await users.IsInRoleAsync(doctor, AppRoles.Doctor))
            return Fail("Only the doctor can close a seat.");
        if (!ScheduleGrid.TryNormalize(startsAt, out var start, out var end, out var error))
            return Fail(error!);

        var gate = Gates.GetOrAdd(doctor.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await ExpireAsync(db, doctor.Id);
            if (await db.ScheduleSeats.AnyAsync(seat => seat.DoctorUserId == doctor.Id && seat.StartsAt == start))
                return Fail("That seat is already held or booked.");
            if ((await LoadBlockersAsync(db, doctor, start.AddHours(-12), end.AddHours(12)))
                .Any(block => Hits(block.StartsAt, block.EndsAt, start, end)))
                return Fail("That time is already booked.");

            db.ScheduleSeats.Add(new ScheduleSeat
            {
                DoctorUserId = doctor.Id,
                StartsAt = start,
                EndsAt = end,
                Status = ScheduleSeatStatus.Closed,
                UpdatedAt = DateTime.UtcNow
            });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Fail("That seat was just taken.");
            }
        }
        finally
        {
            gate.Release();
        }

        live.Publish(doctor.Id, null);
        return Ok($"Closed {When(start)}. Patients cannot select it.");
    }

    public async Task<ScheduleActionResult> ReopenAsync(string doctorUserId, int seatId)
    {
        var gate = Gates.GetOrAdd(doctorUserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var seat = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.Id == seatId && item.DoctorUserId == doctorUserId);
            if (seat is null || seat.Status != ScheduleSeatStatus.Closed)
                return Fail("That seat is not closed.");
            db.ScheduleSeats.Remove(seat);
            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }

        live.Publish(doctorUserId, null);
        return Ok("That seat is open.");
    }

    public async Task<ScheduleActionResult> ReleaseAsync(string actorUserId, int seatId)
    {
        var preview = await FindSeatAsync(seatId);
        if (preview is null)
            return Fail("That seat is already open.");
        if (preview.DoctorUserId != actorUserId && preview.PatientUserId != actorUserId)
            return Fail("This seat belongs to someone else.");
        if (preview.Status != ScheduleSeatStatus.Locked)
            return Fail("Only a locked seat can be released this way.");

        var gate = Gates.GetOrAdd(preview.DoctorUserId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var seat = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.Id == seatId);
            if (seat is null)
                return Ok("The seat is open again.");
            if (seat.Status != ScheduleSeatStatus.Locked)
                return Fail("That seat is no longer locked.");

            if (seat.AppointmentId is int appointmentId)
            {
                var appointment = await db.Appointments.FirstOrDefaultAsync(item => item.Id == appointmentId);
                if (appointment is not null && appointment.Status != AppointmentStatus.Scheduled)
                    return Fail("This visit is no longer scheduled, so the seat stays on the calendar.");
                if (appointment is not null)
                {
                    appointment.Status = AppointmentStatus.Cancelled;
                    appointment.Notes = AppendNote(appointment.Notes, "Cancelled from the calendar.");
                }
            }

            db.ScheduleSeats.Remove(seat);
            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }

        live.Publish(preview.DoctorUserId, preview.PatientUserId);
        var otherId = actorUserId == preview.DoctorUserId ? preview.PatientUserId : preview.DoctorUserId;
        var texted = otherId is not null && await TryTextAsync(otherId, CancelledText);
        return Ok("The appointment is cancelled and the seat is open again.", texted);
    }

    public async Task<ScheduleActionResult> CancelVisitAsync(string actorUserId, int appointmentId)
    {
        var actor = await users.FindByIdAsync(actorUserId);
        if (actor is null)
            return Fail("Sign in again.");

        await using var lookup = await dbFactory.CreateDbContextAsync();
        var appointment = await lookup.Appointments.AsNoTracking().FirstOrDefaultAsync(item => item.Id == appointmentId);
        if (appointment is null)
            return Fail("That appointment is already gone.");
        if (appointment.Status != AppointmentStatus.Scheduled)
            return Fail("That visit is no longer scheduled.");
        if (appointment.StartsAt <= DateTime.Now)
            return Fail("That time has already passed.");

        var actorIsDoctor = await users.IsInRoleAsync(actor, AppRoles.Doctor);
        var isPatient = string.Equals(appointment.UserId, actorUserId, StringComparison.Ordinal);
        var providerKey = NormalizePersonName(appointment.ProviderName);
        string? doctorId = null;
        if (actorIsDoctor && NormalizePersonName(actor.DisplayName) == providerKey)
            doctorId = actor.Id;
        else if (!isPatient)
            return Fail("This appointment belongs to someone else.");

        if (doctorId is null && providerKey.Length > 0)
        {
            var doctorsInRole = await users.GetUsersInRoleAsync(AppRoles.Doctor);
            doctorId = doctorsInRole.FirstOrDefault(doctor => NormalizePersonName(doctor.DisplayName) == providerKey)?.Id;
        }

        if (!isPatient && doctorId != actorUserId)
            return Fail("This appointment belongs to someone else.");

        var gateKey = doctorId ?? actorUserId;
        var gate = Gates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var current = await db.Appointments.FirstOrDefaultAsync(item => item.Id == appointmentId);
            if (current is null)
                return Ok("The seat is open again.");
            if (current.Status != AppointmentStatus.Scheduled)
                return Fail("That visit is no longer scheduled.");

            current.Status = AppointmentStatus.Cancelled;
            current.Notes = AppendNote(current.Notes, "Cancelled from the calendar.");
            var seat = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.AppointmentId == appointmentId);
            if (seat is not null)
                db.ScheduleSeats.Remove(seat);
            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }

        live.Publish(doctorId ?? actorUserId, appointment.UserId);
        var otherId = isPatient ? doctorId : appointment.UserId;
        var texted = otherId is not null && await TryTextAsync(otherId, CancelledText);
        return Ok("The appointment is cancelled and the seat is open again.", texted);
    }

    public async Task ReleaseSeatForAppointmentAsync(int appointmentId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var seat = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.AppointmentId == appointmentId);
        if (seat is null)
            return;

        var doctorId = seat.DoctorUserId;
        var patientId = seat.PatientUserId;
        db.ScheduleSeats.Remove(seat);
        await db.SaveChangesAsync();
        live.Publish(doctorId, patientId);
    }

    public async Task ReleaseSeatIfAppointmentChangedAsync(Appointment appointment)
    {
        if (appointment.Id == 0)
            return;

        await using var db = await dbFactory.CreateDbContextAsync();
        var seat = await db.ScheduleSeats.FirstOrDefaultAsync(item => item.AppointmentId == appointment.Id);
        if (seat is null)
            return;
        if (appointment.Status != AppointmentStatus.Cancelled && SameMinute(seat.StartsAt, appointment.StartsAt))
            return;

        var doctorId = seat.DoctorUserId;
        var patientId = seat.PatientUserId;
        db.ScheduleSeats.Remove(seat);
        await db.SaveChangesAsync();
        live.Publish(doctorId, patientId);
    }

    private async Task<bool> SharesSeatInternalAsync(string doctorUserId, string patientUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ScheduleSeats.AsNoTracking().AnyAsync(seat =>
            seat.DoctorUserId == doctorUserId
            && seat.PatientUserId == patientUserId
            && seat.Status != ScheduleSeatStatus.Closed);
    }

    private async Task<string?> ConflictAsync(
        ApplicationDbContext db,
        ApplicationUser doctor,
        string patientUserId,
        DateTime start,
        DateTime end,
        int? exceptSeatId)
    {
        var occupying = await db.ScheduleSeats.AsNoTracking().FirstOrDefaultAsync(seat =>
            seat.DoctorUserId == doctor.Id
            && seat.StartsAt == start
            && (exceptSeatId == null || seat.Id != exceptSeatId));
        if (occupying is not null)
            return occupying.Status == ScheduleSeatStatus.Closed
                ? "The doctor closed that seat."
                : "That seat was just taken. Pick another open one.";

        var pair = await db.ScheduleSeats.AsNoTracking().AnyAsync(seat =>
            seat.DoctorUserId == doctor.Id
            && seat.PatientUserId == patientUserId
            && (seat.Status == ScheduleSeatStatus.PendingDoctor || seat.Status == ScheduleSeatStatus.PendingPatient)
            && (exceptSeatId == null || seat.Id != exceptSeatId));
        if (pair)
            return "There is already an open request between you. Answer it or withdraw it before holding another seat.";

        var patientSeats = await db.ScheduleSeats.AsNoTracking()
            .Where(seat => seat.PatientUserId == patientUserId
                && seat.Status != ScheduleSeatStatus.Closed
                && (exceptSeatId == null || seat.Id != exceptSeatId))
            .Select(seat => new { seat.StartsAt, seat.EndsAt })
            .ToListAsync();
        if (patientSeats.Any(seat => Hits(seat.StartsAt, seat.EndsAt, start, end)))
            return "There is already a seat at this time.";

        var blockers = await LoadBlockersAsync(db, doctor, start.AddHours(-12), end.AddHours(12));
        if (blockers.Any(block => Hits(block.StartsAt, block.EndsAt, start, end)))
            return "That time is already booked.";

        var ownAppointments = await db.Appointments.AsNoTracking()
            .Where(item => item.UserId == patientUserId && item.Status != AppointmentStatus.Cancelled)
            .Where(item => item.StartsAt > start.AddHours(-12) && item.StartsAt < end.AddHours(12))
            .Select(item => new { item.StartsAt, item.EndsAt })
            .ToListAsync();
        if (ownAppointments.Any(item => Hits(item.StartsAt, item.EndsAt ?? item.StartsAt.Add(ScheduleGrid.Length), start, end)))
            return "There is already an appointment at this time.";

        return null;
    }

    private async Task<List<SlotBlock>> LoadBlockersAsync(
        ApplicationDbContext db,
        ApplicationUser doctor,
        DateTime windowStart,
        DateTime windowEnd)
    {
        var doctorKey = NormalizePersonName(doctor.DisplayName);
        if (doctorKey.Length == 0)
            return [];

        var rows = await db.Appointments.AsNoTracking()
            .Where(item => item.Status != AppointmentStatus.Cancelled
                && item.StartsAt >= windowStart
                && item.StartsAt <= windowEnd)
            .Select(item => new
            {
                item.Id,
                item.UserId,
                item.StartsAt,
                item.EndsAt,
                item.ProviderName,
                item.Title,
                item.Purpose
            })
            .ToListAsync();

        return rows
            .Where(item => NormalizePersonName(item.ProviderName) == doctorKey)
            .Select(item => new SlotBlock(
                item.Id,
                item.UserId,
                item.StartsAt,
                item.EndsAt ?? item.StartsAt.Add(ScheduleGrid.Length),
                item.Title,
                item.Purpose))
            .ToList();
    }

    private ScheduleSeatView ToView(
        string actorId,
        bool actorIsDoctor,
        DateTime start,
        DateTime end,
        ScheduleSeat? seat,
        SlotBlock? blocker,
        People people,
        HashSet<string> grants)
    {
        var isPast = start <= DateTime.Now;
        var patientId = seat?.PatientUserId ?? blocker?.PatientUserId;
        var fromSeat = seat is not null && seat.Status != ScheduleSeatStatus.Closed;
        var showIdentity = !string.IsNullOrWhiteSpace(patientId)
            && (actorIsDoctor || string.Equals(patientId, actorId, StringComparison.Ordinal));
        var person = showIdentity && patientId is not null ? people.Name(patientId) : null;
        var status = seat is null
            ? blocker is null ? ScheduleSeatViewStatus.Open : ScheduleSeatViewStatus.Locked
            : seat.Status switch
            {
                ScheduleSeatStatus.Closed => ScheduleSeatViewStatus.Closed,
                ScheduleSeatStatus.Locked => ScheduleSeatViewStatus.Locked,
                _ => ScheduleSeatViewStatus.Held
            };

        var awaiting = seat is not null && (
            (actorIsDoctor && seat.Status == ScheduleSeatStatus.PendingDoctor)
            || (!actorIsDoctor && seat.PatientUserId == actorId && seat.Status == ScheduleSeatStatus.PendingPatient));
        var started = seat is not null && (
            (actorIsDoctor && seat.DoctorConfirmed && !seat.PatientConfirmed)
            || (!actorIsDoctor && seat.PatientUserId == actorId && seat.PatientConfirmed && !seat.DoctorConfirmed));
        var party = actorIsDoctor || (patientId is not null && string.Equals(patientId, actorId, StringComparison.Ordinal));
        var canCancel = !isPast && party && status is ScheduleSeatViewStatus.Held or ScheduleSeatViewStatus.Locked;
        var canOpen = !isPast && status switch
        {
            ScheduleSeatViewStatus.Open => true,
            ScheduleSeatViewStatus.Held => party,
            ScheduleSeatViewStatus.Locked => party,
            ScheduleSeatViewStatus.Closed => actorIsDoctor,
            _ => false
        };

        var label = status switch
        {
            ScheduleSeatViewStatus.Open => isPast ? "Past" : "Open",
            ScheduleSeatViewStatus.Held => showIdentity ? person ?? "Held" : "Held",
            ScheduleSeatViewStatus.Locked => showIdentity ? person ?? "Taken" : "Taken",
            _ => "Closed"
        };
        var purpose = showIdentity || canCancel ? seat?.Purpose ?? blocker?.Purpose ?? blocker?.Title : null;
        var photo = patientId is not null && showIdentity && people.Photo(patientId) is DateTime updated
            && (actorId == patientId || (actorIsDoctor && (fromSeat || grants.Contains(patientId))))
            ? photos.GetPhotoUrl(patientId, updated)
            : null;
        var when = start.ToString("dddd MMM d h:mm tt");
        var aria = showIdentity && !string.IsNullOrWhiteSpace(person)
            ? $"{when}, {StatusWord(status)}, {person}"
            : $"{when}, {label}";

        return new ScheduleSeatView(
            start,
            end,
            seat?.Id,
            status,
            isPast,
            canOpen,
            showIdentity && status is not ScheduleSeatViewStatus.Open,
            awaiting,
            started,
            label ?? "Open",
            person,
            photo,
            showIdentity && !string.IsNullOrWhiteSpace(person) ? Initials(person) : null,
            string.IsNullOrWhiteSpace(purpose) ? null : purpose,
            aria,
            seat?.AppointmentId ?? blocker?.AppointmentId,
            canCancel);
    }

    private ScheduleNotice ToNotice(string actorId, bool actorIsDoctor, ScheduleSeat seat, People people)
    {
        var patientName = people.Name(seat.PatientUserId);
        var doctorName = people.Name(seat.DoctorUserId);
        var awaiting = actorIsDoctor
            ? seat.Status == ScheduleSeatStatus.PendingDoctor
            : seat.Status == ScheduleSeatStatus.PendingPatient;
        var lane = seat.Status == ScheduleSeatStatus.Locked
            ? ScheduleNoticeLane.Locked
            : awaiting ? ScheduleNoticeLane.NeedsYou : ScheduleNoticeLane.Waiting;
        var when = When(seat.StartsAt);
        var summary = lane switch
        {
            ScheduleNoticeLane.NeedsYou when actorIsDoctor => $"{patientName} asked for {when}. Confirm to lock this seat.",
            ScheduleNoticeLane.NeedsYou => $"{doctorName} offered {when}. Accept to add it to your appointments.",
            ScheduleNoticeLane.Waiting when actorIsDoctor => $"Waiting for {patientName} to accept {when}.",
            ScheduleNoticeLane.Waiting => $"Waiting for {doctorName} to confirm {when}.",
            _ when actorIsDoctor => $"{patientName} is booked {when}.",
            _ => $"You are booked with {doctorName} {when}."
        };
        var updated = people.Photo(seat.PatientUserId);
        var allowPhoto = updated is not null && seat.PatientUserId is not null && (seat.PatientUserId == actorId || actorIsDoctor);
        return new ScheduleNotice(
            seat.Id,
            seat.DoctorUserId,
            doctorName,
            patientName,
            allowPhoto ? photos.GetPhotoUrl(seat.PatientUserId!, updated) : null,
            Initials(actorIsDoctor || allowPhoto ? patientName : doctorName),
            seat.StartsAt,
            seat.EndsAt,
            seat.Purpose,
            seat.Status,
            lane,
            summary,
            seat.AppointmentId);
    }

    private async Task<People> LoadPeopleAsync(ApplicationDbContext db, IEnumerable<string?> ids)
    {
        var wanted = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
            return People.Empty;

        var accounts = await db.Users.AsNoTracking()
            .Where(user => wanted.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName, user.Email })
            .ToListAsync();
        var stamps = await db.PatientProfiles.AsNoTracking()
            .Where(profile => wanted.Contains(profile.UserId) && profile.ProfilePhotoData != null)
            .Select(profile => new { profile.UserId, profile.ProfilePhotoUpdatedAt })
            .ToListAsync();

        var names = accounts.ToDictionary(
            account => account.Id,
            account => string.IsNullOrWhiteSpace(account.FullName) ? account.Email ?? "Patient" : account.FullName.Trim(),
            StringComparer.Ordinal);
        var photoStamps = stamps.ToDictionary(stamp => stamp.UserId, stamp => stamp.ProfilePhotoUpdatedAt, StringComparer.Ordinal);
        return new People(names, photoStamps);
    }

    private async Task<ScheduleSeat?> FindSeatAsync(int seatId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ScheduleSeats.AsNoTracking().FirstOrDefaultAsync(seat => seat.Id == seatId);
    }

    private static async Task<bool> ExpireAsync(ApplicationDbContext db, string doctorUserId)
    {
        var cutoff = DateTime.UtcNow - ScheduleGrid.HoldLifetime;
        var now = DateTime.Now;
        var stale = await db.ScheduleSeats
            .Where(seat => seat.DoctorUserId == doctorUserId
                && (seat.Status == ScheduleSeatStatus.PendingDoctor || seat.Status == ScheduleSeatStatus.PendingPatient)
                && (seat.UpdatedAt < cutoff || seat.StartsAt <= now))
            .ToListAsync();
        if (stale.Count == 0)
            return false;

        db.ScheduleSeats.RemoveRange(stale);
        await db.SaveChangesAsync();
        return true;
    }

    private static async Task<List<string>> ExpireAllAsync(ApplicationDbContext db)
    {
        var cutoff = DateTime.UtcNow - ScheduleGrid.HoldLifetime;
        var now = DateTime.Now;
        var stale = await db.ScheduleSeats
            .Where(seat => (seat.Status == ScheduleSeatStatus.PendingDoctor || seat.Status == ScheduleSeatStatus.PendingPatient)
                && (seat.UpdatedAt < cutoff || seat.StartsAt <= now))
            .ToListAsync();
        if (stale.Count == 0)
            return [];

        var doctors = stale.Select(seat => seat.DoctorUserId).Distinct(StringComparer.Ordinal).ToList();
        db.ScheduleSeats.RemoveRange(stale);
        await db.SaveChangesAsync();
        return doctors;
    }

    private static ScheduleSeat NewSeat(
        string doctorUserId,
        string patientUserId,
        DateTime start,
        DateTime end,
        string? purpose,
        ScheduleSeatStatus status,
        bool patientConfirmed,
        bool doctorConfirmed) => new()
        {
            DoctorUserId = doctorUserId,
            PatientUserId = patientUserId,
            StartsAt = start,
            EndsAt = end,
            Purpose = purpose,
            Status = status,
            PatientConfirmed = patientConfirmed,
            DoctorConfirmed = doctorConfirmed,
            UpdatedAt = DateTime.UtcNow
        };

    private static Appointment BuildAppointment(string patientUserId, ApplicationUser doctor, ScheduleSeat seat)
    {
        var purpose = string.IsNullOrWhiteSpace(seat.Purpose) ? null : seat.Purpose.Trim();
        var title = string.IsNullOrWhiteSpace(purpose) ? "Office visit" : TrimTo(purpose, 120);
        var location = string.IsNullOrWhiteSpace(doctor.ClinicAddress) ? "Clinic" : TrimTo(doctor.ClinicAddress.Trim(), 160);
        return new Appointment
        {
            UserId = patientUserId,
            Title = title,
            ProviderName = TrimTo(doctor.DisplayName, 120),
            Location = location,
            Purpose = purpose,
            StartsAt = seat.StartsAt,
            EndsAt = seat.EndsAt,
            Status = AppointmentStatus.Scheduled,
            Notes = "Booked on the calendar after both sides confirmed."
        };
    }

    private async Task<bool> TryTextAsync(string userId, string body)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == userId);
            if (profile is null || string.IsNullOrWhiteSpace(profile.Phone) || !SmsConsent.IsGranted(profile, profile.Phone))
                return false;

            var result = await twilio.TextAsync(profile.Phone, body, CancellationToken.None);
            return result.Ok;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schedule text was not sent.");
            return false;
        }
    }

    private static bool TryPurpose(string? purpose, out string? clean, out string? error)
    {
        clean = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();
        if (clean is { Length: > 200 })
        {
            error = "Keep the reason under 200 characters.";
            clean = null;
            return false;
        }

        error = null;
        return true;
    }

    private static string AppendNote(string? notes, string extra)
    {
        var text = string.IsNullOrWhiteSpace(notes) ? extra : $"{notes.Trim()} {extra}";
        return TrimTo(text, 400);
    }

    private static string When(DateTime start) => start.ToString("ddd MMM d, h:mm tt");

    private static string StatusWord(ScheduleSeatViewStatus status) => status switch
    {
        ScheduleSeatViewStatus.Held => "held",
        ScheduleSeatViewStatus.Locked => "booked",
        ScheduleSeatViewStatus.Closed => "closed",
        _ => "open"
    };

    private static bool SameMinute(DateTime left, DateTime right) =>
        left.Year == right.Year
        && left.Month == right.Month
        && left.Day == right.Day
        && left.Hour == right.Hour
        && left.Minute == right.Minute;

    private static bool Hits(DateTime start, DateTime end, DateTime slotStart, DateTime slotEnd) =>
        start < slotEnd && end > slotStart;

    private static string TrimTo(string value, int max) => value.Length <= max ? value : value[..max];

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.Equals("Dr.", StringComparison.OrdinalIgnoreCase)
                && !part.Equals("Dr", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (parts.Count == 0)
            return "?";
        if (parts.Count == 1)
            return char.ToUpperInvariant(parts[0][0]).ToString();
        return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[^1][0]));
    }

    private static string NormalizePersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var letters = new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
        var parts = letters.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[0] is "dr" or "doctor")
            parts.RemoveAt(0);
        string[] suffixes = ["md", "do", "np", "pa", "rn", "rd", "phd", "fnp", "pac"];
        while (parts.Count > 1 && suffixes.Contains(parts[^1], StringComparer.Ordinal))
            parts.RemoveAt(parts.Count - 1);
        return string.Join(' ', parts);
    }

    private static ScheduleActionResult Fail(string message) => new(false, message);

    private static ScheduleActionResult Ok(string message, bool texted = false) =>
        new(true, texted ? message + " A text was sent too." : message, texted);

    private sealed record SlotBlock(
        int AppointmentId,
        string PatientUserId,
        DateTime StartsAt,
        DateTime EndsAt,
        string Title,
        string? Purpose);

    private sealed class People(Dictionary<string, string> names, Dictionary<string, DateTime?> photos)
    {
        public static People Empty { get; } = new(new Dictionary<string, string>(), new Dictionary<string, DateTime?>());

        public string Name(string? id) =>
            id is not null && names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "Patient";

        public DateTime? Photo(string? id) =>
            id is not null && photos.TryGetValue(id, out var stamp) ? stamp : null;
    }
}
