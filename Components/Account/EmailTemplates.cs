using System.Net;
using MedicalManager.Data;

namespace MedicalManager.Components.Account;

internal static class EmailTemplates
{
    private const string BrandColor = "#0d9488";
    private const string TextColor = "#334155";
    private const string MutedColor = "#64748b";

    public static (string Html, string PlainText) Welcome(string fullName, bool forDoctor = false)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim();
        var encodedName = WebUtility.HtmlEncode(name);
        var lead = forDoctor
            ? "Your doctor account is ready. Open the patient list, choose a chart, and review the same dashboard, vitals, medications, labs, and appointments that patient uses."
            : "We're glad you joined Medical Manager. Your account is ready — here's how we help you take better care of yourself and your loved ones.";

        var plainText = forDoctor
            ? $"""
            Hi {name},

            Welcome to Medical Manager. Your doctor account is ready.

            Open the patient list, choose a chart, and review the same dashboard, vitals, medications, labs, and appointments that patient uses.

            Sign in anytime. If you have questions, reply to this email.

            Warm regards,
            The Medical Manager Team
            """
            : $"""
            Hi {name},

            Welcome to Medical Manager — we're glad you're here.

            Medical Manager helps you stay on top of your health in one calm, organized place:

            • Track blood pressure, glucose, and other vitals over time
            • Manage medications with schedules and reminders
            • Keep appointments and care-team details in one view
            • Use To Do reminders so nothing important slips through
            • See your dashboard at a glance — mobile-friendly wherever you are

            Our goal is simple: less stress, more clarity, and peace of mind for you and your loved ones.

            Sign in anytime to get started. If you have questions, reply to this email — we're here to help.

            Warm regards,
            The Medical Manager Team
            """;

        var patientDetails = forDoctor
            ? ""
            : "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"margin:20px 0;\">"
              + "<tr><td style=\"padding:8px 0;font-size:15px;line-height:1.5;\">&#8226; Track <strong>vitals</strong> like blood pressure and glucose over time</td></tr>"
              + "<tr><td style=\"padding:8px 0;font-size:15px;line-height:1.5;\">&#8226; Manage <strong>medications</strong> with schedules and reminders</td></tr>"
              + "<tr><td style=\"padding:8px 0;font-size:15px;line-height:1.5;\">&#8226; Keep <strong>appointments</strong> and care-team details organized</td></tr>"
              + "<tr><td style=\"padding:8px 0;font-size:15px;line-height:1.5;\">&#8226; Stay on top of tasks with <strong>To Do</strong> reminders</td></tr>"
              + "<tr><td style=\"padding:8px 0;font-size:15px;line-height:1.5;\">&#8226; View your <strong>dashboard at a glance</strong> — mobile-friendly, wherever you are</td></tr>"
              + "</table>"
              + "<p style=\"margin:0 0 16px;font-size:15px;line-height:1.6;\">Our goal is less stress, more clarity, and peace of mind. Sign in anytime to get started.</p>";

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(15,23,42,.08);">
                      <tr>
                        <td style="background:{BrandColor};padding:28px 32px;">
                          <p style="margin:0;font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:rgba(255,255,255,.85);">Medical Manager</p>
                          <h1 style="margin:8px 0 0;font-size:24px;line-height:1.3;color:#ffffff;font-weight:600;">Welcome, {encodedName}</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <p style="margin:0 0 16px;font-size:16px;line-height:1.6;">{WebUtility.HtmlEncode(lead)}</p>
                          {patientDetails}
                          <p style="margin:0;font-size:15px;line-height:1.6;">Warm regards,<br><strong>The Medical Manager Team</strong></p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px;background:#f8fafc;border-top:1px solid #e2e8f0;">
                          <p style="margin:0;font-size:12px;line-height:1.5;color:{MutedColor};">You received this email because you registered at Medical Manager. If this wasn't you, please contact us.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    public static (string Html, string PlainText) PasswordReset(string resetLink)
    {
        var encodedLink = WebUtility.HtmlEncode(resetLink);

        var plainText = $"""
            Reset your Medical Manager password

            We received a request to reset the password for your account. Open the link below to choose a new password:

            {resetLink}

            If you did not request this, you can safely ignore this email. Your password will not change until you use the link above.

            — The Medical Manager Team
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(15,23,42,.08);">
                      <tr>
                        <td style="background:{BrandColor};padding:24px 32px;">
                          <h1 style="margin:0;font-size:22px;color:#ffffff;font-weight:600;">Reset your password</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">We received a request to reset the password for your Medical Manager account. Click the button below to choose a new password.</p>
                          <p style="margin:24px 0;text-align:center;">
                            <a href="{encodedLink}" style="display:inline-block;background:{BrandColor};color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:12px 28px;border-radius:8px;">Reset password</a>
                          </p>
                          <p style="margin:0 0 8px;font-size:13px;line-height:1.5;color:{MutedColor};">Or copy and paste this link into your browser:</p>
                          <p style="margin:0;font-size:13px;line-height:1.5;word-break:break-all;"><a href="{encodedLink}" style="color:{BrandColor};">{encodedLink}</a></p>
                          <p style="margin:24px 0 0;font-size:14px;line-height:1.6;color:{MutedColor};">If you did not request a password reset, you can safely ignore this email.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    public static (string Html, string PlainText) EmailConfirmation(string confirmationLink)
    {
        var encodedLink = WebUtility.HtmlEncode(confirmationLink);

        var plainText = $"""
            Confirm your Medical Manager email

            Please confirm your account by opening this link:

            {confirmationLink}

            — The Medical Manager Team
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;">
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 16px;font-size:22px;color:{BrandColor};">Confirm your email</h1>
                          <p style="margin:0 0 24px;font-size:15px;line-height:1.6;">Please confirm your Medical Manager account by clicking the button below.</p>
                          <p style="margin:0 0 24px;text-align:center;">
                            <a href="{encodedLink}" style="display:inline-block;background:{BrandColor};color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:12px 28px;border-radius:8px;">Confirm email</a>
                          </p>
                          <p style="margin:0;font-size:13px;line-height:1.5;word-break:break-all;color:{MutedColor};"><a href="{encodedLink}" style="color:{BrandColor};">{encodedLink}</a></p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    public static (string Html, string PlainText) MonitorShare(string patientName, string link, string code)
    {
        var name = string.IsNullOrWhiteSpace(patientName) ? "Your family" : patientName.Trim();
        var encodedName = WebUtility.HtmlEncode(name);
        var encodedLink = WebUtility.HtmlEncode(link);
        var spaced = WebUtility.HtmlEncode(string.Join(' ', code.ToCharArray()));

        var plainText = $"""
            Medical Manager: {name} shared a family monitor.
            Code {code}.
            {link}

            Open the link and enter the code. The picture is on only while {name} leaves Monitor open.
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;">
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 16px;font-size:22px;color:{BrandColor};">Medical Manager family monitor</h1>
                          <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">Medical Manager: {encodedName} shared a family monitor.</p>
                          <p style="margin:0 0 8px;font-size:15px;line-height:1.6;">Code</p>
                          <p style="margin:0 0 24px;font-size:28px;font-weight:800;letter-spacing:0.08em;color:#134e4a;">{spaced}</p>
                          <p style="margin:0 0 24px;text-align:center;">
                            <a href="{encodedLink}" style="display:inline-block;background:{BrandColor};color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:12px 28px;border-radius:8px;">Open the monitor</a>
                          </p>
                          <p style="margin:0 0 16px;font-size:13px;line-height:1.5;word-break:break-all;color:{MutedColor};"><a href="{encodedLink}" style="color:{BrandColor};">{encodedLink}</a></p>
                          <p style="margin:0;font-size:14px;line-height:1.6;">The picture is on only while {encodedName} leaves Monitor open.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    public static (string Html, string PlainText) AccessApproval(string doctorName, string records, string code)
    {
        var doctor = string.IsNullOrWhiteSpace(doctorName) ? "Your doctor" : doctorName.Trim();
        var encodedDoctor = WebUtility.HtmlEncode(doctor);
        var encodedRecords = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(records) ? "Selected records" : records.Trim());
        var encodedCode = WebUtility.HtmlEncode(code);
        var link = SmsCopy.HomeUrl.TrimEnd('/') + "/access-requests";
        var encodedLink = WebUtility.HtmlEncode(link);

        var plainText = $"""
            {doctor} asked to open records in Medical Manager.

            Records: {records}

            Your one-time approval code is {code}. It expires in 15 minutes.

            Sign in and enter the code on Access requests:
            {link}

            The doctor does not see this code. If you did not expect this request, you can ignore this email.

            — The Medical Manager Team
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(15,23,42,.08);">
                      <tr>
                        <td style="background:{BrandColor};padding:28px 32px;">
                          <p style="margin:0;font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:rgba(255,255,255,.85);">Medical Manager</p>
                          <h1 style="margin:8px 0 0;font-size:22px;line-height:1.3;color:#ffffff;font-weight:600;">Approval request</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <p style="margin:0 0 16px;font-size:16px;line-height:1.6;">{encodedDoctor} asked to open records in Medical Manager.</p>
                          <p style="margin:0 0 16px;font-size:15px;line-height:1.6;"><strong>Records:</strong> {encodedRecords}</p>
                          <p style="margin:0 0 8px;font-size:15px;line-height:1.6;">Your one-time approval code is</p>
                          <p style="margin:0 0 8px;font-size:28px;font-weight:800;letter-spacing:0.18em;color:#134e4a;">{encodedCode}</p>
                          <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:{MutedColor};">It expires in 15 minutes.</p>
                          <p style="margin:0 0 24px;text-align:center;">
                            <a href="{encodedLink}" style="display:inline-block;background:{BrandColor};color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:12px 28px;border-radius:8px;">Open access requests</a>
                          </p>
                          <p style="margin:0;font-size:14px;line-height:1.6;">The doctor does not see this code. If you did not expect this request, you can ignore this email.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    public static (string Html, string PlainText) Prescription(PrescriptionLetter letter)
    {
        var patientName = Encode(letter.Patient.Name);
        var issued = Encode(letter.Issued);
        var doctorCard = letter.Clinicians.Count == 0
            ? """
              <p style="margin:0 0 8px;font-size:16px;font-weight:700;color:#0f3d5c;">Entered by the patient</p>
              <p style="margin:0;font-size:14px;line-height:1.5;color:#334155;">No prescribing doctor is linked to these medications. The patient name, address, date of birth, and phone are in the card beside this one.</p>
              """
            : string.Join("", letter.Clinicians.Select((clinician, index) => ClinicianBlock(clinician, index > 0)))
              + (letter.IncludesPatientEntered
                  ? "<p style=\"margin:12px 0 0;font-size:13px;line-height:1.45;color:#334155;\">Some medications were entered by the patient.</p>"
                  : "");

        var rows = string.Join("", letter.Lines.Select((line, index) => MedicationRow(line, index)));
        var plainDoctors = letter.Clinicians.Count == 0
            ? "Entered by the patient. No prescribing doctor is linked to these medications."
            : string.Join("\n\n", letter.Clinicians.Select(clinician =>
                $"""
                {clinician.Name}
                Specialty: {clinician.Specialty}
                Clinic: {clinician.ClinicAddress}
                License: {clinician.License}
                """));
        if (letter.IncludesPatientEntered && letter.Clinicians.Count > 0)
            plainDoctors += "\n\nSome medications were entered by the patient.";

        var plainMeds = string.Join("\n\n", letter.Lines.Select((line, index) =>
            $"""
            {index + 1}. {line.Name}
               Dose: {line.Dose}
               Time: {line.Schedule}
               Purpose: {line.Purpose}
               Started: {line.Started}
               Prescriber: {line.Prescriber}
               Latest change: {line.LatestChange}
            """));

        var plainText = $"""
            MEDICAL MANAGER
            Prescription
            For {letter.Patient.Name}
            Issued {letter.Issued}

            DOCTOR
            {plainDoctors}

            PATIENT
            Name: {letter.Patient.Name}
            Address: {letter.Patient.Address}
            Date of birth: {letter.Patient.DateOfBirth}
            Phone: {letter.Patient.Phone}

            MEDICATIONS
            {plainMeds}

            Sent by the patient from Medical Manager.
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#e8eef5;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#1e293b;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#e8eef5;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:680px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #d5dee8;">
                      <tr>
                        <td style="background:#0f3d5c;padding:22px 24px;">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                            <tr>
                              <td width="52" valign="middle" style="width:52px;">
                                <table role="presentation" cellspacing="0" cellpadding="0"><tr>
                                  <td width="44" height="44" align="center" valign="middle" style="width:44px;height:44px;background:#ffffff;color:#0f3d5c;font-weight:800;font-size:16px;border-radius:22px;">Rx</td>
                                </tr></table>
                              </td>
                              <td valign="middle" style="padding-left:12px;">
                                <p style="margin:0;font-size:11px;letter-spacing:.16em;font-weight:700;color:#99f6e4;">MEDICAL MANAGER</p>
                                <p style="margin:2px 0 0;font-size:26px;line-height:1.15;font-weight:700;color:#ffffff;">Prescription</p>
                                <p style="margin:6px 0 0;font-size:14px;color:#e2e8f0;">For {patientName}</p>
                              </td>
                              <td valign="bottom" align="right" style="font-size:12px;line-height:1.4;color:#cbd5e1;">Issued<br>{issued}</td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:18px 16px 8px;">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                            <tr>
                              <td valign="top" width="50%" style="width:50%;padding:6px;">
                                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f8fafc;border:1px solid #dbe3ee;border-top:4px solid #0f3d5c;border-radius:12px;">
                                  <tr><td style="padding:14px 14px 4px;font-size:11px;letter-spacing:.14em;font-weight:700;color:#64748b;">DOCTOR</td></tr>
                                  <tr><td style="padding:0 14px 14px;">{doctorCard}</td></tr>
                                </table>
                              </td>
                              <td valign="top" width="50%" style="width:50%;padding:6px;">
                                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f8fafc;border:1px solid #dbe3ee;border-top:4px solid #0d9488;border-radius:12px;">
                                  <tr><td style="padding:14px 14px 4px;font-size:11px;letter-spacing:.14em;font-weight:700;color:#64748b;">PATIENT</td></tr>
                                  <tr><td style="padding:0 14px 14px;">{PatientBlock(letter.Patient)}</td></tr>
                                </table>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:8px 22px 6px;">
                          <p style="margin:0;font-size:13px;letter-spacing:.12em;font-weight:700;color:#0f3d5c;">MEDICATIONS</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:0 16px 8px;">
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="border-collapse:collapse;">
                            <tr style="background:#0f3d5c;color:#ffffff;">
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Medication</th>
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Dose</th>
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Time</th>
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Purpose</th>
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Started</th>
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Prescriber</th>
                              <th align="left" style="padding:8px 10px;font-size:12px;font-weight:700;">Latest change</th>
                            </tr>
                            {rows}
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:16px 22px 20px;background:#f8fafc;border-top:1px solid #e2e8f0;">
                          <p style="margin:0;font-size:12px;line-height:1.5;color:#64748b;">Sent by the patient from Medical Manager. This copy is for the pharmacy, the prescriber, and the patient.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    private static string ClinicianBlock(PrescriptionClinician clinician, bool divided)
    {
        var border = divided ? "border-top:1px solid #e2e8f0;padding-top:10px;margin-top:10px;" : "";
        return "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"" + border + "\">"
            + Field("Name", clinician.Name, bold: true)
            + Field("Specialty", clinician.Specialty, bold: false)
            + Field("Clinic", clinician.ClinicAddress, bold: false)
            + Field("License", clinician.License, bold: false)
            + "</table>";
    }

    private static string PatientBlock(PrescriptionRecipient patient) =>
        "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\">"
        + Field("Name", patient.Name, bold: true)
        + Field("Address", patient.Address, bold: false)
        + Field("Date of birth", patient.DateOfBirth, bold: false)
        + Field("Phone", patient.Phone, bold: false)
        + "</table>";

    private static string Field(string label, string value, bool bold)
    {
        var weight = bold ? "700" : "600";
        return "<tr><td style=\"padding:3px 8px 3px 0;font-size:12px;color:#64748b;vertical-align:top;white-space:nowrap;\">"
            + Encode(label)
            + "</td><td style=\"padding:3px 0;font-size:14px;line-height:1.4;font-weight:"
            + weight
            + ";color:#0f172a;\">"
            + Encode(value)
            + "</td></tr>";
    }

    private static string MedicationRow(PrescriptionLine line, int index)
    {
        var background = index % 2 == 0 ? "#ffffff" : "#f1f5f9";
        string Cell(string value) =>
            "<td style=\"padding:8px 10px;font-size:13px;line-height:1.4;color:#1e293b;vertical-align:top;border-bottom:1px solid #e2e8f0;\">"
            + Encode(value)
            + "</td>";

        return "<tr style=\"background:" + background + ";\">"
            + Cell(line.Name)
            + Cell(line.Dose)
            + Cell(line.Schedule)
            + Cell(line.Purpose)
            + Cell(line.Started)
            + Cell(line.Prescriber)
            + Cell(line.LatestChange)
            + "</tr>";
    }

    public static (string Html, string PlainText) MedicalTranscript(
        string patientName,
        string doctorName,
        string when,
        string status,
        string body)
    {
        var name = string.IsNullOrWhiteSpace(patientName) ? "there" : patientName.Trim();
        var doctor = string.IsNullOrWhiteSpace(doctorName) ? "Your doctor" : doctorName.Trim();
        var text = body.Trim();
        var plainText = $"""
            Hi {name},

            {doctor} shared the medical transcript from your visit on {when}.
            Status: {status}

            {text}

            This transcript is for you. You can also open Medical Transcript in Medical Manager when your doctor has left view access on.
            """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f1f5f9;font-family:Segoe UI,Helvetica,Arial,sans-serif;color:{TextColor};">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border-radius:12px;overflow:hidden;">
                      <tr>
                        <td style="background:{BrandColor};padding:24px 28px;">
                          <p style="margin:0;font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:rgba(255,255,255,.85);">Medical Manager</p>
                          <h1 style="margin:8px 0 0;font-size:22px;line-height:1.3;color:#ffffff;font-weight:600;">Medical transcript</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:24px 28px;">
                          <p style="margin:0 0 12px;font-size:16px;line-height:1.6;">Hi {Encode(name)},</p>
                          <p style="margin:0 0 12px;font-size:15px;line-height:1.6;">{Encode(doctor)} shared the transcript from your visit on {Encode(when)}.</p>
                          <p style="margin:0 0 16px;font-size:14px;line-height:1.5;color:{MutedColor};">Status: {Encode(status)}</p>
                          <div style="margin:0;padding:14px 16px;background:#f8fafc;border-radius:10px;white-space:pre-wrap;font-size:15px;line-height:1.55;">{Encode(text)}</div>
                          <p style="margin:16px 0 0;font-size:14px;line-height:1.6;color:{MutedColor};">This transcript is for you. You can also open it in Medical Manager when your doctor has left view access on.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return (html, plainText);
    }

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value.Trim());
}

public sealed record PrescriptionLetter(
    string Issued,
    IReadOnlyList<PrescriptionClinician> Clinicians,
    bool IncludesPatientEntered,
    PrescriptionRecipient Patient,
    IReadOnlyList<PrescriptionLine> Lines);

public sealed record PrescriptionClinician(string Name, string Specialty, string ClinicAddress, string License);

public sealed record PrescriptionRecipient(string Name, string Address, string DateOfBirth, string Phone);

public sealed record PrescriptionLine(
    string Name,
    string Dose,
    string Schedule,
    string Purpose,
    string Started,
    string Prescriber,
    string LatestChange);

