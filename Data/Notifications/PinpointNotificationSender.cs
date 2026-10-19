using Amazon;
using Amazon.PinpointSMSVoiceV2;
using Amazon.PinpointSMSVoiceV2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data.Notifications;

public sealed record OutboundResult(bool Ok, bool Permanent, string? Sid, string? Detail);

public sealed class PinpointNotificationSender(
    IOptions<NotificationOptions> options,
    ILogger<PinpointNotificationSender> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAmazonPinpointSMSVoiceV2? _client;
    private Routes? _routes;
    private DateTime _routesCheckedAt = DateTime.MinValue;
    private int _missingCredentialsLogged;
    private int _subscriptionLogged;
    private int _missingNumberLogged;
    private int _accessLogged;

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!HasCredentialSource())
        {
            LogOnce(
                ref _missingCredentialsLogged,
                "Appointment reminders are ready. Amazon Pinpoint credentials are not available.");
            return false;
        }

        try
        {
            var routes = await RoutesAsync(cancellationToken);
            if (routes.Voice is null && routes.Sms is null)
            {
                var configuredNumber = options.Value.Pinpoint.OriginationIdentity?.Trim();
                if (string.IsNullOrWhiteSpace(configuredNumber))
                {
                    LogOnce(
                        ref _missingNumberLogged,
                        "Amazon Pinpoint is connected, but this account has no active phone number for reminder calls or texts.");
                }
                else
                {
                    LogOnce(
                        ref _missingNumberLogged,
                        "The reminder number {Number} is connected. Texts and calls start once AWS marks it active.",
                        configuredNumber);
                }

                return false;
            }

            return true;
        }
        catch (AmazonServiceException ex) when (IsSubscriptionError(ex))
        {
            LogOnce(
                ref _subscriptionLogged,
                "Amazon Pinpoint SMS and voice is not turned on for this AWS account yet.");
            return false;
        }
        catch (AmazonServiceException ex)
        {
            LogOnce(ref _accessLogged, "Amazon Pinpoint could not be reached ({Code}).", ex.ErrorCode);
            return false;
        }
    }

    public Task<OutboundResult> CallAsync(
        string to,
        string ssml,
        string pollyVoice,
        CancellationToken cancellationToken) =>
        SendAsync(to, voice: true, ssml, pollyVoice, cancellationToken);

    public Task<OutboundResult> TextAsync(string to, string body, CancellationToken cancellationToken) =>
        SendAsync(to, voice: false, body, pollyVoice: "", cancellationToken);

    public void Dispose()
    {
        _client?.Dispose();
        _gate.Dispose();
    }

    private async Task<OutboundResult> SendAsync(
        string to,
        bool voice,
        string body,
        string pollyVoice,
        CancellationToken cancellationToken)
    {
        Routes routes;
        try
        {
            routes = await RoutesAsync(cancellationToken);
        }
        catch (AmazonServiceException ex)
        {
            return Failed(ex.ErrorCode);
        }

        var from = voice ? routes.Voice : routes.Sms;
        if (string.IsNullOrWhiteSpace(from))
        {
            return new OutboundResult(
                false,
                voice,
                null,
                voice
                    ? "No Amazon Pinpoint number can place calls."
                    : "No Amazon Pinpoint number can send texts.");
        }

        try
        {
            var client = Client();
            if (voice)
            {
                var response = await client.SendVoiceMessageAsync(new SendVoiceMessageRequest
                {
                    DestinationPhoneNumber = to,
                    OriginationIdentity = from,
                    MessageBody = body,
                    MessageBodyTextType = VoiceMessageBodyTextType.SSML,
                    VoiceId = PollyVoiceId(pollyVoice)
                }, cancellationToken);
                logger.LogInformation("Sent an appointment call reminder.");
                return new OutboundResult(true, false, response.MessageId, null);
            }

            var text = await client.SendTextMessageAsync(new SendTextMessageRequest
            {
                DestinationPhoneNumber = to,
                OriginationIdentity = from,
                MessageBody = body,
                MessageType = MessageType.TRANSACTIONAL
            }, cancellationToken);
            logger.LogInformation("Sent an appointment text reminder.");
            return new OutboundResult(true, false, text.MessageId, null);
        }
        catch (AmazonServiceException ex)
        {
            logger.LogWarning(
                "Appointment {Channel} reminder was not accepted ({Code}).",
                voice ? "call" : "text",
                ex.ErrorCode);
            return Failed(ex.ErrorCode);
        }
    }

    private async Task<Routes> RoutesAsync(CancellationToken cancellationToken)
    {
        if (_routes is not null && DateTime.UtcNow - _routesCheckedAt < TimeSpan.FromMinutes(2))
            return _routes;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_routes is not null && DateTime.UtcNow - _routesCheckedAt < TimeSpan.FromMinutes(2))
                return _routes;

            var listed = await ListActiveNumbersAsync(cancellationToken);
            var configured = options.Value.Pinpoint.OriginationIdentity?.Trim();
            var active = listed.Where(number => number.IsActive).ToList();
            Routes routes;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var match = listed.FirstOrDefault(number => SameIdentity(configured, number));
                if (match is { IsActive: false })
                    routes = new Routes(null, null);
                else if (match is null)
                    routes = new Routes(configured, configured);
                else
                    routes = new Routes(
                        match.Voice ? match.Identity : null,
                        match.Sms ? match.Identity : null);
            }
            else
            {
                var both = active.FirstOrDefault(number => number.Sms && number.Voice);
                var sms = both ?? active.FirstOrDefault(number => number.Sms);
                var voice = both ?? active.FirstOrDefault(number => number.Voice);
                routes = new Routes(voice?.Identity, sms?.Identity);
            }

            _routes = routes;
            _routesCheckedAt = DateTime.UtcNow;
            return routes;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<OriginationNumber>> ListActiveNumbersAsync(CancellationToken cancellationToken)
    {
        var numbers = new List<OriginationNumber>();
        string? token = null;
        do
        {
            var page = await Client().DescribePhoneNumbersAsync(new DescribePhoneNumbersRequest
            {
                NextToken = token,
                MaxResults = 100
            }, cancellationToken);

            foreach (var number in page.PhoneNumbers ?? [])
            {
                if (string.IsNullOrWhiteSpace(number.PhoneNumber))
                    continue;

                var capabilities = number.NumberCapabilities ?? [];
                numbers.Add(new OriginationNumber(
                    number.PhoneNumber.Trim(),
                    number.PhoneNumberId?.Trim() ?? "",
                    number.Status?.ToString() ?? "",
                    capabilities.Contains("SMS", StringComparer.OrdinalIgnoreCase),
                    capabilities.Contains("VOICE", StringComparer.OrdinalIgnoreCase)));
            }

            token = page.NextToken;
        }
        while (!string.IsNullOrWhiteSpace(token));

        return numbers;
    }

    private IAmazonPinpointSMSVoiceV2 Client()
    {
        if (_client is not null)
            return _client;

        var pinpoint = options.Value.Pinpoint;
        var regionName = string.IsNullOrWhiteSpace(pinpoint.Region) ? "us-east-1" : pinpoint.Region.Trim();
        var config = new AmazonPinpointSMSVoiceV2Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(regionName)
        };

        _client = !string.IsNullOrWhiteSpace(pinpoint.AccessKeyId)
            && !string.IsNullOrWhiteSpace(pinpoint.SecretAccessKey)
            ? new AmazonPinpointSMSVoiceV2Client(pinpoint.AccessKeyId.Trim(), pinpoint.SecretAccessKey.Trim(), config)
            : new AmazonPinpointSMSVoiceV2Client(config);
        return _client;
    }

    private bool HasCredentialSource()
    {
        var pinpoint = options.Value.Pinpoint;
        if (!string.IsNullOrWhiteSpace(pinpoint.AccessKeyId)
            && !string.IsNullOrWhiteSpace(pinpoint.SecretAccessKey))
            return true;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI")))
            return true;

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"))
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));
    }

    private void LogOnce(ref int gate, string message, params object?[] args)
    {
        if (Interlocked.Exchange(ref gate, 1) == 0)
            logger.LogInformation(message, args);
    }

    private static bool IsSubscriptionError(AmazonServiceException ex) =>
        ex.ErrorCode is "SubscriptionRequiredException" or "OptInRequired" or "OptInRequiredException";

    private static OutboundResult Failed(string? code)
    {
        var detail = string.IsNullOrWhiteSpace(code) ? "Amazon Pinpoint did not accept the reminder." : $"Pinpoint {code}";
        return new OutboundResult(false, false, null, detail.Length <= 280 ? detail : detail[..280]);
    }

    private static string PollyVoiceId(string configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "Joanna" : configured.Trim();
        if (value.StartsWith("Polly.", StringComparison.OrdinalIgnoreCase))
            value = value["Polly.".Length..];
        foreach (var suffix in new[] { "-Neural", "-Generative", "-Standard" })
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                value = value[..^suffix.Length];
        }

        if (!SupportedVoices.Contains(value))
            value = "Joanna";
        return value.ToUpperInvariant();
    }

    private static readonly HashSet<string> SupportedVoices = new(StringComparer.OrdinalIgnoreCase)
    {
        "AMY", "ASTRID", "BIANCA", "BRIAN", "CAMILA", "CARLA", "CARMEN", "CELINE", "CHANTAL",
        "CONCHITA", "CRISTIANO", "DORA", "EMMA", "ENRIQUE", "EWA", "FILIZ", "GERAINT", "GIORGIO",
        "GWYNETH", "HANS", "INES", "IVY", "JACEK", "JAN", "JOANNA", "JOEY", "JUSTIN", "KARL",
        "KENDRA", "KIMBERLY", "LEA", "LIV", "LOTTE", "LUCIA", "LUPE", "MADS", "MAJA", "MARLENE",
        "MATHIEU", "MATTHEW", "MAXIM", "MIA", "MIGUEL", "MIZUKI", "NAJA", "NICOLE", "PENELOPE",
        "RAVEENA", "RICARDO", "RUBEN", "RUSSELL", "SALLI", "SEOYEON", "TAKUMI", "TATYANA",
        "VICKI", "VITORIA", "ZEINA", "ZHIYU"
    };

    private static bool SameIdentity(string configured, OriginationNumber number)
    {
        if (string.Equals(configured, number.Identity, StringComparison.OrdinalIgnoreCase)
            || string.Equals(configured, number.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(number.Id)
            && configured.EndsWith(number.Id, StringComparison.OrdinalIgnoreCase))
            return true;

        var left = PhoneNumbers.ToE164(configured);
        var right = PhoneNumbers.ToE164(number.Identity);
        return left is not null && left == right;
    }

    private sealed record Routes(string? Voice, string? Sms);

    private sealed record OriginationNumber(string Identity, string Id, string Status, bool Sms, bool Voice)
    {
        public bool IsActive => string.Equals(Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }
}
