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
        "I agree to receive recurring text messages from Medical Manager at this mobile number. Texts are one-time chart-access approval codes and appointment reminders. Message frequency varies. Message and data rates may apply. Reply STOP to opt out. Reply HELP for help.";

    public const string OptOutTrailer = " Reply STOP to opt out. Reply HELP for help.";

    public static string Finish(string body) => body.TrimEnd() + OptOutTrailer;
}
