using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data.Notifications;

public sealed record FamilyTalkSetup(bool Ok, string? Token, string? Ticket, string? Error);

public sealed class FamilyVoiceCallService(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    IMemoryCache cache,
    IServiceScopeFactory scopes,
    TwilioNotificationSender sender,
    ILogger<FamilyVoiceCallService> logger)
{
    private const string AppName = "Medical Manager Family";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _appSid;
    private string? _appVoiceUrl;

    public async Task<FamilyTalkSetup> PrepareAsync(string userId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var access = scope.ServiceProvider.GetRequiredService<PatientAccessService>();
        var contact = await access.GetFamilyContactAsync(userId);
        var destination = PhoneNumbers.ToE164(contact.Phone);
        if (destination is null)
            return new FamilyTalkSetup(false, null, null, "Add a 10-digit phone number for this family contact.");

        return await IssueAsync(userId, destination, cancellationToken);
    }

    public async Task<FamilyTalkSetup> PrepareDirectoryCallAsync(
        string userId,
        string kind,
        string name,
        CancellationToken cancellationToken)
    {
        var destination = await ResolveDirectoryNumberAsync(userId, kind, name);
        if (destination is null)
            return new FamilyTalkSetup(false, null, null, "I can't find a phone number for that person.");

        return await IssueAsync(userId, destination, cancellationToken);
    }

    private async Task<string?> ResolveDirectoryNumberAsync(string userId, string kind, string name)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (string.Equals(kind, "doctor", StringComparison.OrdinalIgnoreCase))
        {
            var doctors = scope.ServiceProvider.GetRequiredService<DoctorService>();
            var matches = (await doctors.GetDoctorsAsync(userId))
                .Where(doctor => CallGuide.NameMatches(name, doctor.Name))
                .Select(doctor => PhoneNumbers.ToE164(doctor.PhoneNumber))
                .Where(phone => phone is not null)
                .Distinct()
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        var access = scope.ServiceProvider.GetRequiredService<PatientAccessService>();
        var contact = await access.GetFamilyContactAsync(userId);
        if (contact.Name is null || !CallGuide.NameMatches(name, contact.Name))
            return null;

        return PhoneNumbers.ToE164(contact.Phone);
    }

    private async Task<FamilyTalkSetup> IssueAsync(string userId, string destination, CancellationToken cancellationToken)
    {
        var route = await sender.GetVoiceRouteAsync(cancellationToken);
        var twilio = options.Value.Twilio;
        if (route.AccountSid is null || route.FromNumber is null
            || string.IsNullOrWhiteSpace(twilio.ApiKeySid)
            || string.IsNullOrWhiteSpace(twilio.ApiKeySecret))
            return new FamilyTalkSetup(false, null, null, "Calling is not available right now.");

        var voiceUrl = VoiceUrl();
        var appSid = await EnsureAppAsync(route.AccountSid, twilio, voiceUrl, cancellationToken);
        if (appSid is null)
            return new FamilyTalkSetup(false, null, null, "Calling is not available right now.");

        var ticket = Guid.NewGuid().ToString("N");
        cache.Set(TicketKey(ticket), new FamilyCallTicket(destination, route.FromNumber), TimeSpan.FromMinutes(3));
        var token = CreateToken(route.AccountSid, twilio.ApiKeySid, twilio.ApiKeySecret, VoiceIdentity(userId), appSid);
        return new FamilyTalkSetup(true, token, ticket, null);
    }

    public string ClaimTwiml(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket)
            || !cache.TryGetValue(TicketKey(ticket), out FamilyCallTicket? call)
            || call is null)
        {
            return """
                <?xml version="1.0" encoding="UTF-8"?>
                <Response><Say>This call is no longer available.</Say></Response>
                """;
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
              <Dial callerId="{Xml(call.CallerId)}" answerOnBridge="true">
                <Number>{Xml(call.To)}</Number>
              </Dial>
            </Response>
            """;
    }

    private string VoiceUrl()
    {
        var baseUrl = options.Value.Twilio.VoiceBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://www.medicalmgr.com";
        return baseUrl.Trim().TrimEnd('/') + "/voice/family";
    }

    private async Task<string?> EnsureAppAsync(
        string accountSid,
        TwilioOptions twilio,
        string voiceUrl,
        CancellationToken cancellationToken)
    {
        if (_appSid is not null && string.Equals(_appVoiceUrl, voiceUrl, StringComparison.Ordinal))
            return _appSid;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_appSid is not null && string.Equals(_appVoiceUrl, voiceUrl, StringComparison.Ordinal))
                return _appSid;

            var existing = await FindAppAsync(accountSid, twilio, cancellationToken);
            if (existing is null)
            {
                var created = await CreateAppAsync(accountSid, twilio, voiceUrl, cancellationToken);
                if (created is null)
                    return null;
                _appSid = created;
                _appVoiceUrl = voiceUrl;
                return created;
            }

            if (!string.Equals(existing.Value.VoiceUrl, voiceUrl, StringComparison.Ordinal))
            {
                if (!await UpdateAppAsync(accountSid, twilio, existing.Value.Sid, voiceUrl, cancellationToken))
                    return null;
            }

            _appSid = existing.Value.Sid;
            _appVoiceUrl = voiceUrl;
            return _appSid;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Sid, string? VoiceUrl)?> FindAppAsync(
        string accountSid,
        TwilioOptions twilio,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Applications.json?PageSize=50");
        request.Headers.Authorization = Basic(twilio);
        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twilio family calling app could not be listed ({Status}).", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("applications", out var applications))
            return null;

        foreach (var application in applications.EnumerateArray())
        {
            var name = application.TryGetProperty("friendly_name", out var nameValue) ? nameValue.GetString() : null;
            if (!string.Equals(name, AppName, StringComparison.Ordinal))
                continue;

            var sid = application.TryGetProperty("sid", out var sidValue) ? sidValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(sid))
                continue;

            var url = application.TryGetProperty("voice_url", out var urlValue) ? urlValue.GetString() : null;
            return (sid, url);
        }

        return null;
    }

    private async Task<string?> CreateAppAsync(
        string accountSid,
        TwilioOptions twilio,
        string voiceUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Applications.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["FriendlyName"] = AppName,
                ["VoiceUrl"] = voiceUrl,
                ["VoiceMethod"] = "POST"
            })
        };
        request.Headers.Authorization = Basic(twilio);
        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Twilio family calling app could not be created ({Status}).", (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null;
    }

    private async Task<bool> UpdateAppAsync(
        string accountSid,
        TwilioOptions twilio,
        string appSid,
        string voiceUrl,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Applications/{appSid}.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["VoiceUrl"] = voiceUrl,
                ["VoiceMethod"] = "POST"
            })
        };
        request.Headers.Authorization = Basic(twilio);
        using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Twilio family calling app could not be updated ({Status}).", (int)response.StatusCode);
        return response.IsSuccessStatusCode;
    }

    private static string CreateToken(string accountSid, string apiKeySid, string apiKeySecret, string identity, string appSid)
    {
        var header = Base64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT","cty":"twilio-fpa;v=1"}"""));
        var expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["jti"] = $"{apiKeySid}-{Guid.NewGuid():N}",
            ["iss"] = apiKeySid,
            ["sub"] = accountSid,
            ["exp"] = expires,
            ["grants"] = new Dictionary<string, object>
            {
                ["identity"] = identity,
                ["voice"] = new Dictionary<string, object>
                {
                    ["outgoing"] = new Dictionary<string, string>
                    {
                        ["application_sid"] = appSid
                    }
                }
            }
        });
        var payload = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        var signature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiKeySecret), Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    private static string VoiceIdentity(string userId)
    {
        var identity = new string(userId.Where(char.IsLetterOrDigit).ToArray());
        if (identity.Length == 0)
            identity = "patient";
        return identity.Length <= 121 ? identity : identity[..121];
    }

    private static string TicketKey(string ticket) => "family-call:" + ticket;

    private static string Xml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static AuthenticationHeaderValue Basic(TwilioOptions twilio)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilio.ApiKeySid}:{twilio.ApiKeySecret}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private sealed record FamilyCallTicket(string To, string CallerId);
}
