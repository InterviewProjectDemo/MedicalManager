namespace MedicalManager.Data;

/// <summary>
/// Words that the public website, the Profile checkbox, and outbound texts share.
/// </summary>
public static class SmsCopy
{
    public const string Brand = "Medical Manager";
    public const string BusinessName = "Business Intelligence and Technology Solutions Inc.";
    public const string SupportEmail = "contact@bite-us.com";
    public const string HomeUrl = "https://www.medicalmgr.com";
    public const string PrivacyUrl = "https://www.medicalmgr.com/privacy";
    public const string TermsUrl = "https://www.medicalmgr.com/terms";
    public const string ConsentUrl = "https://www.medicalmgr.com/sms";

    public const string CheckboxLead =
        "I agree to receive text messages from Medical Manager at this mobile number. Texts are one-time approval codes and one-time monitor codes. Message frequency varies. Message and data rates may apply. Reply STOP to opt out. Reply HELP for help.";

    public const string OptOutTrailer = " Reply STOP to opt out. Reply HELP for help.";

    public static string Finish(string body) => body.TrimEnd() + OptOutTrailer;

    public static string ApprovalCode(string code) =>
        Finish($"Medical Manager: Your one-time approval code is {code}. It expires in 15 minutes.");

    public static string MonitorCode(string code) =>
        Finish($"Medical Manager: Your one-time monitor code is {code}. It expires in 15 minutes.");

    public static void SelfCheck()
    {
        const string approval = "Medical Manager: Your one-time approval code is 123456. It expires in 15 minutes. Reply STOP to opt out. Reply HELP for help.";
        const string monitor = "Medical Manager: Your one-time monitor code is 654321. It expires in 15 minutes. Reply STOP to opt out. Reply HELP for help.";
        if (!string.Equals(ApprovalCode("123456"), approval, StringComparison.Ordinal)
            || !string.Equals(MonitorCode("654321"), monitor, StringComparison.Ordinal))
            throw new InvalidOperationException("One-time code texts do not match the approved 2FA samples.");

        foreach (var sample in new[] { ApprovalCode("123456"), MonitorCode("654321") })
        {
            if (sample.Contains("http", StringComparison.OrdinalIgnoreCase)
                || sample.Contains("www.", StringComparison.OrdinalIgnoreCase)
                || sample.Contains("appointment", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("One-time code texts must stay free of links and appointment wording.");
        }
    }
}
