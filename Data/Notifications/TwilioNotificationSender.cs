using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data.Notifications;

public sealed class TwilioNotificationSender(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<TwilioNotificationSender> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _checkedAt = DateTime.MinValue;
    private bool _ready;
    private string? _fromNumber;
    private string? _accountSid;
    private int _notReadyLogged;

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (Fresh())
            return _ready;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh())
                return _ready;

            var twilio = options.Value.Twilio;
            if (string.IsNullOrWhiteSpace(twilio.ApiKeySid) || string.IsNullOrWhiteSpace(twilio.ApiKeySecret))
            {
                Remember(false, null, null);
                LogOnce("Reminders are ready. Add the Twilio API key to send texts and calls.");
                return false;
            }

            var accountSid = await ResolveAccountSidAsync(twilio, cancellationToken);
            if (string.IsNullOrWhiteSpace(accountSid))
            {
                Remember(false, null, null);
                LogOnce("Twilio rejected the API key, so texts and calls are paused.");
                return false;
            }

            var from = PhoneNumbers.ToE164(twilio.FromNumber);
            if (string.IsNullOrWhiteSpace(from))
                from = await FirstSendingNumberAsync(accountSid, twilio, cancellationToken);

            if (string.IsNullOrWhiteSpace(from))
            {
                var emptyBalance = await BalanceAsync(accountSid, twilio, cancellationToken);
                Remember(false, accountSid, null);
                LogOnce(emptyBalance is <= 0m
                    ? "Twilio is connected. This account has a $0 balance and no phone number, so texts and calls wait until both are in place."
                    : "Twilio is connected, but this account has no phone number for texts and calls.");
                return false;
            }

            var balance = await BalanceAsync(accountSid, twilio, cancellationToken);
            if (balance is <= 0m)
            {
                Remember(false, accountSid, from);
                LogOnce("Twilio is connected, but the account balance is $0, so texts and calls cannot be sent yet.");
                return false;
            }

            Remember(true, accountSid, from);
            logger.LogInformation("Twilio is ready to send reminder texts and calls.");
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Remember(false, _accountSid, _fromNumber);
            LogOnce("Twilio could not be reached, so texts and calls are paused.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<OutboundResult> CallAsync(
        string to,
        string ssml,
        string pollyVoice,
        CancellationToken cancellationToken) =>
        SendAsync(to, voice: true, ssml, pollyVoice, cancellationToken);

    public Task<OutboundResult> TextAsync(string to, string body, CancellationToken cancellationToken) =>
        SendAsync(to, voice: false, body, pollyVoice: null, cancellationToken);

    private async Task<OutboundResult> SendAsync(
        string to,
        bool voice,
        string payload,
        string? pollyVoice,
        CancellationToken cancellationToken)
    {
        if (!await EnsureReadyAsync(cancellationToken) || _accountSid is null || _fromNumber is null)
            return new OutboundResult(false, false, null, "Twilio is not ready to send.");

        var destination = PhoneNumbers.ToE164(to);
        if (destination is null)
            return new OutboundResult(false, true, null, "The phone number is not a valid US number.");

        var twilio = options.Value.Twilio;
        var fields = new Dictionary<string, string>
        {
            ["To"] = destination,
            ["From"] = _fromNumber
        };

        if (voice)
        {
            var voiceName = string.IsNullOrWhiteSpace(pollyVoice)
                ? options.Value.Voice.PollyVoice
                : pollyVoice;
            fields["Twiml"] = ToTwiml(payload, voiceName, options.Value.Voice.Language);
        }
        else
        {
            fields["Body"] = payload;
        }

        var path = voice ? "Calls.json" : "Messages.json";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/{path}")
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = Basic(twilio);

        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var sid = ReadString(json, "sid");
            return new OutboundResult(true, false, sid, null);
        }

        var code = ReadInt(json, "code");
        var permanent = code is 21211 or 21217 or 21408 or 21610 or 21614 or 21608 or 21212;
        logger.LogWarning("Twilio {Channel} was not accepted ({Code}).", voice ? "call" : "text", code);
        return new OutboundResult(false, permanent, null, code is null ? "Twilio did not accept the message." : $"Twilio {code}");
    }

    private async Task<string?> ResolveAccountSidAsync(TwilioOptions twilio, CancellationToken cancellationToken)
    {
        var configured = twilio.AccountSid?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twilio.com/2010-04-01/Accounts.json");
        request.Headers.Authorization = Basic(twilio);
        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("accounts", out var accounts))
            return null;

        foreach (var account in accounts.EnumerateArray())
        {
            var sid = account.TryGetProperty("sid", out var value) ? value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(sid))
                return sid;
        }

        return null;
    }

    private async Task<string?> FirstSendingNumberAsync(
        string accountSid,
        TwilioOptions twilio,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/IncomingPhoneNumbers.json?PageSize=20");
        request.Headers.Authorization = Basic(twilio);
        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("incoming_phone_numbers", out var numbers))
            return null;

        string? smsOnly = null;
        foreach (var number in numbers.EnumerateArray())
        {
            var phone = number.TryGetProperty("phone_number", out var phoneValue) ? phoneValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(phone))
                continue;

            var voice = HasCapability(number, "voice");
            var sms = HasCapability(number, "sms");
            if (voice && sms)
                return phone;
            if (sms && smsOnly is null)
                smsOnly = phone;
        }

        return smsOnly;
    }

    private async Task<decimal?> BalanceAsync(
        string accountSid,
        TwilioOptions twilio,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Balance.json");
        request.Headers.Authorization = Basic(twilio);
        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var raw = ReadString(await response.Content.ReadAsStringAsync(cancellationToken), "balance");
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private bool Fresh() => _checkedAt > DateTime.UtcNow.AddMinutes(-2);

    private void Remember(bool ready, string? accountSid, string? fromNumber)
    {
        _ready = ready;
        _accountSid = accountSid;
        _fromNumber = fromNumber;
        _checkedAt = DateTime.UtcNow;
    }

    private void LogOnce(string message)
    {
        if (Interlocked.Exchange(ref _notReadyLogged, 1) == 0)
            logger.LogInformation("{Message}", message);
    }

    private static AuthenticationHeaderValue Basic(TwilioOptions twilio)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilio.ApiKeySid}:{twilio.ApiKeySecret}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static bool HasCapability(JsonElement number, string name) =>
        number.TryGetProperty("capabilities", out var capabilities)
        && capabilities.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static string? ReadString(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadInt(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(name, out var value))
                return null;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code) ? code : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToTwiml(string ssml, string pollyVoice, string language)
    {
        var inner = ssml.Trim();
        if (inner.StartsWith("<speak", StringComparison.OrdinalIgnoreCase))
        {
            var start = inner.IndexOf('>');
            var end = inner.LastIndexOf("</speak>", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
                inner = inner[(start + 1)..end].Trim();
        }

        var voice = pollyVoice.StartsWith("Polly.", StringComparison.OrdinalIgnoreCase)
            ? pollyVoice
            : "Polly." + pollyVoice.Trim();
        var spokenLanguage = string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim();
        return $"""<Response><Say voice="{Xml(voice)}" language="{Xml(spokenLanguage)}">{inner}</Say></Response>""";
    }

    private static string Xml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);
}
