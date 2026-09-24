using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data.Notifications;

public sealed record OutboundResult(bool Ok, bool Permanent, string? Sid, string? Detail);

public sealed class TwilioNotificationSender(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<TwilioNotificationSender> logger)
{
    public Task<OutboundResult> CallAsync(string to, string twiml, CancellationToken cancellationToken) =>
        PostAsync("Calls", to, new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = options.Value.Twilio.FromNumber.Trim(),
            ["Twiml"] = twiml
        }, cancellationToken);

    public Task<OutboundResult> TextAsync(string to, string body, CancellationToken cancellationToken) =>
        PostAsync("Messages", to, new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = options.Value.Twilio.FromNumber.Trim(),
            ["Body"] = body
        }, cancellationToken);

    private async Task<OutboundResult> PostAsync(
        string resource,
        string to,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var twilio = options.Value.Twilio;
        if (!twilio.IsConfigured)
            return new OutboundResult(false, false, null, "Twilio is not configured.");

        var sid = twilio.AccountSid.Trim();
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/{resource}.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{twilio.AuthToken.Trim()}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        var client = httpClientFactory.CreateClient("Twilio");
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var messageSid = ReadString(payload, "sid");
            logger.LogInformation("Sent appointment {Resource} reminder.", resource);
            return new OutboundResult(true, false, messageSid, null);
        }

        var code = ReadString(payload, "code");
        var message = ReadString(payload, "message");
        var detail = string.IsNullOrWhiteSpace(code) ? "Twilio request failed." : $"Twilio {code}";
        if (!string.IsNullOrWhiteSpace(message))
            detail = TrimDetail($"{detail}: {message}");

        logger.LogWarning("Appointment {Resource} reminder was not accepted. {Detail}", resource, detail);
        var permanent = code is "21211" or "21214" or "21614" or "21408" or "13223" or "13224";
        return new OutboundResult(false, permanent, null, detail);

        static string TrimDetail(string value) => value.Length <= 280 ? value : value[..280];
    }

    private static string? ReadString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(property, out var value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
