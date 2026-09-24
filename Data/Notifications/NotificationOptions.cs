namespace MedicalManager.Data.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 60;

    public TwilioOptions Twilio { get; set; } = new();

    public VoiceOptions Voice { get; set; } = new();

    public TravelOptions Travel { get; set; } = new();
}

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";

    public string AuthToken { get; set; } = "";

    public string FromNumber { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(FromNumber);
}

public sealed class VoiceOptions
{
    /// <summary>
    /// Warm conversational Amazon Polly voice used by Twilio Say.
    /// </summary>
    public string PollyVoice { get; set; } = "Polly.Ruth-Neural";

    public string Language { get; set; } = "en-US";
}

public sealed class TravelOptions
{
    public bool Enabled { get; set; } = true;
}
