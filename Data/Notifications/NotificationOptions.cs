namespace MedicalManager.Data.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 60;

    public PinpointOptions Pinpoint { get; set; } = new();

    public TwilioOptions Twilio { get; set; } = new();

    public VoiceOptions Voice { get; set; } = new();

    public TravelOptions Travel { get; set; } = new();
}

public sealed class PinpointOptions
{
    public string Region { get; set; } = "us-east-1";

    public string AccessKeyId { get; set; } = "";

    public string SecretAccessKey { get; set; } = "";

    /// <summary>
    /// Phone number, phone number id, or pool that places calls and texts.
    /// When empty, the app uses an active number already on the AWS account.
    /// </summary>
    public string OriginationIdentity { get; set; } = "";
}

public sealed class TwilioOptions
{
    public string AccountSid { get; set; } = "";

    public string ApiKeySid { get; set; } = "";

    public string ApiKeySecret { get; set; } = "";

    /// <summary>
    /// E.164 number that places the texts and calls. When empty, the app uses
    /// the first Twilio number on the account that can text.
    /// </summary>
    public string FromNumber { get; set; } = "";

    /// <summary>
    /// Public site Twilio calls when a family conversation starts on this device.
    /// </summary>
    public string VoiceBaseUrl { get; set; } = "https://www.medicalmgr.com";
}

public sealed class VoiceOptions
{
    /// <summary>
    /// Caring US English voice for reminder calls. Twilio speaks Ruth-Generative
    /// as a warm, conversational voice.
    /// </summary>
    public string PollyVoice { get; set; } = "Ruth-Generative";

    public string Language { get; set; } = "en-US";
}

public sealed class TravelOptions
{
    public bool Enabled { get; set; } = true;
}
