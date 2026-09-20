using System.Net;

namespace MedicalManager.Components.Account;

internal static class EmailTemplates
{
    private const string BrandColor = "#0d9488";
    private const string TextColor = "#334155";
    private const string MutedColor = "#64748b";

    public static (string Html, string PlainText) Welcome(string fullName)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim();
        var encodedName = WebUtility.HtmlEncode(name);

        var plainText = $"""
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
                          <p style="margin:0 0 16px;font-size:16px;line-height:1.6;">We're glad you joined Medical Manager. Your account is ready — here's how we help you take better care of yourself and your loved ones.</p>
                          <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="margin:20px 0;">
                            <tr><td style="padding:8px 0;font-size:15px;line-height:1.5;">&#8226; Track <strong>vitals</strong> like blood pressure and glucose over time</td></tr>
                            <tr><td style="padding:8px 0;font-size:15px;line-height:1.5;">&#8226; Manage <strong>medications</strong> with schedules and reminders</td></tr>
                            <tr><td style="padding:8px 0;font-size:15px;line-height:1.5;">&#8226; Keep <strong>appointments</strong> and care-team details organized</td></tr>
                            <tr><td style="padding:8px 0;font-size:15px;line-height:1.5;">&#8226; Stay on top of tasks with <strong>To Do</strong> reminders</td></tr>
                            <tr><td style="padding:8px 0;font-size:15px;line-height:1.5;">&#8226; View your <strong>dashboard at a glance</strong> — mobile-friendly, wherever you are</td></tr>
                          </table>
                          <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">Our goal is less stress, more clarity, and peace of mind. Sign in anytime to get started.</p>
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
}
