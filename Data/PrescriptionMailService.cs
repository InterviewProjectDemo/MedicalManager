using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using MedicalManager.Components.Account;

namespace MedicalManager.Data;

public sealed record PrescriptionSendResult(bool Sent, string Message);

public sealed class PrescriptionMailService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppEmailSender email,
    ILogger<PrescriptionMailService> logger)
{
    public async Task<PrescriptionSendResult> SendToPharmacistAsync(
        string actorUserId,
        string pharmacistEmail,
        IReadOnlyCollection<int> medicationIds)
    {
        if (!email.IsConfigured)
        {
            logger.LogWarning("Email is not configured. A prescription email was not sent.");
            return new PrescriptionSendResult(false, "Email is not configured, so the prescription was not sent.");
        }

        var pharmacist = (pharmacistEmail ?? "").Trim();
        if (!IsEmail(pharmacist))
            return new PrescriptionSendResult(false, "Enter a valid pharmacist email.");

        await using var db = await dbFactory.CreateDbContextAsync();
        var patient = await db.Users.AsNoTracking().FirstOrDefaultAsync(user => user.Id == actorUserId);
        if (patient is null)
            return new PrescriptionSendResult(false, "The prescription could not be sent.");

        var medications = await db.Medications.AsNoTracking()
            .Include(item => item.Schedules)
            .Where(item => item.UserId == actorUserId)
            .ToListAsync();

        List<Medication> chosen;
        if (medicationIds.Count == 0)
        {
            chosen = medications.Where(item => item.IsActive).ToList();
            if (chosen.Count == 0)
                return new PrescriptionSendResult(false, "There is no current medication to send. Select a medication or add one first.");
        }
        else
        {
            var wanted = medicationIds.ToHashSet();
            chosen = medications.Where(item => wanted.Contains(item.Id)).ToList();
            if (chosen.Count == 0)
                return new PrescriptionSendResult(false, "Choose medications from your list.");
        }

        chosen = chosen.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var profile = await db.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == actorUserId);
        var doctorIds = chosen
            .Select(item => item.PrescribingDoctorUserId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        var doctors = doctorIds.Count == 0
            ? new Dictionary<string, ApplicationUser>()
            : await db.Users.AsNoTracking()
                .Where(user => doctorIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id);

        var changeIds = chosen.Select(item => item.Id).ToList();
        var changes = await db.MedicationChanges.AsNoTracking()
            .Where(change => changeIds.Contains(change.MedicationId))
            .OrderByDescending(change => change.ChangedAt)
            .ThenByDescending(change => change.Id)
            .ToListAsync();
        var latest = new Dictionary<int, string>();
        foreach (var change in changes)
        {
            if (!latest.ContainsKey(change.MedicationId))
                latest[change.MedicationId] = change.Summary;
        }

        var clinicians = new List<PrescriptionClinician>();
        foreach (var doctorId in doctorIds)
        {
            if (doctors.TryGetValue(doctorId, out var doctor))
            {
                clinicians.Add(new PrescriptionClinician(
                    doctor.DisplayName,
                    Blank(doctor.Specialty),
                    Blank(doctor.ClinicAddress),
                    License(doctor)));
            }
            else
            {
                var named = chosen.FirstOrDefault(item => item.PrescribingDoctorUserId == doctorId)?.PrescribedBy;
                clinicians.Add(new PrescriptionClinician(
                    string.IsNullOrWhiteSpace(named) ? "Prescribing doctor" : named.Trim(),
                    "Not on file",
                    "Not on file",
                    "Not on file"));
            }
        }

        var letter = new PrescriptionLetter(
            DateTime.Now.ToString("MMM d, yyyy h:mm tt"),
            clinicians,
            chosen.Any(item => string.IsNullOrWhiteSpace(item.PrescribingDoctorUserId)),
            new PrescriptionRecipient(
                patient.DisplayName,
                Blank(profile?.HomeAddress),
                profile?.DateOfBirth?.ToString("MMMM d, yyyy") ?? "Not on file",
                Blank(FirstPhone(profile, patient))),
            chosen.Select(item => new PrescriptionLine(
                item.Name,
                Blank(item.Dosage),
                MedicationHistoryText.TimeLabel(item.Frequency, item.Days, item.Schedules),
                string.IsNullOrWhiteSpace(item.Purpose) ? "—" : item.Purpose.Trim(),
                item.StartDate.ToString("MMM d, yyyy"),
                Prescriber(item, doctors),
                latest.GetValueOrDefault(item.Id) ?? "No recorded changes.")).ToList());

        var (html, plain) = EmailTemplates.Prescription(letter);
        try
        {
            var delivered = await email.TrySendPrescriptionAsync(pharmacist, html, plain);
            if (!delivered)
                return new PrescriptionSendResult(false, "Email is not configured, so the prescription was not sent.");
        }
        catch (Exception)
        {
            logger.LogError("A prescription email could not be sent.");
            return new PrescriptionSendResult(false, "The prescription could not be sent. Nothing was delivered.");
        }

        var patientEmail = patient.Email?.Trim();
        if (string.IsNullOrWhiteSpace(patientEmail))
            return new PrescriptionSendResult(true, "Sent to the pharmacist. Your account has no email address, so a copy was not sent to you.");

        if (string.Equals(patientEmail, pharmacist, StringComparison.OrdinalIgnoreCase))
            return new PrescriptionSendResult(true, "Sent to the pharmacist. That address is also your account email, so you already have the copy.");

        try
        {
            var copy = await email.TrySendPrescriptionAsync(patientEmail, html, plain);
            if (!copy)
                return new PrescriptionSendResult(true, "Sent to the pharmacist. A copy could not be sent to you because email is not configured.");
        }
        catch (Exception)
        {
            logger.LogError("The patient copy of a prescription email could not be sent.");
            return new PrescriptionSendResult(true, "Sent to the pharmacist. The copy to you could not be sent.");
        }

        return new PrescriptionSendResult(true, "Sent to the pharmacist, and a copy was sent to you.");
    }

    private static string Prescriber(Medication medication, IReadOnlyDictionary<string, ApplicationUser> doctors)
    {
        if (!string.IsNullOrWhiteSpace(medication.PrescribingDoctorUserId)
            && doctors.TryGetValue(medication.PrescribingDoctorUserId, out var doctor))
            return doctor.DisplayName;

        if (!string.IsNullOrWhiteSpace(medication.PrescribingDoctorUserId))
            return string.IsNullOrWhiteSpace(medication.PrescribedBy) ? "Prescribing doctor" : medication.PrescribedBy.Trim();

        return "Entered by the patient";
    }

    private static string? FirstPhone(PatientProfile? profile, ApplicationUser patient) =>
        string.IsNullOrWhiteSpace(profile?.Phone) ? patient.PhoneNumber : profile.Phone;

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not on file" : value.Trim();

    private static string License(ApplicationUser doctor)
    {
        var state = doctor.LicenseState?.Trim();
        var number = doctor.LicenseNumber?.Trim();
        if (string.IsNullOrWhiteSpace(state) && string.IsNullOrWhiteSpace(number))
            return "Not on file";
        if (string.IsNullOrWhiteSpace(state))
            return number!;
        if (string.IsNullOrWhiteSpace(number))
            return state!;
        return $"{state} {number}";
    }

    private static bool IsEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return address.Address.Contains('@', StringComparison.Ordinal)
                && !address.Address.StartsWith('@');
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
