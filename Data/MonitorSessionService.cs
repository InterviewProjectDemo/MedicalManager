using System.Net.Http.Headers;
using System.Text.Json;
using MedicalManager.Data.Notifications;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data;

/// <summary>
/// Live family-watch session for one app instance. The camera stays in the
/// patient's browser. This only passes the connection details to a family
/// member who already entered the access code.
/// </summary>
public sealed class MonitorSessionService(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    TwilioNotificationSender twilio,
    ILogger<MonitorSessionService> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _stamps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);
    private IReadOnlyList<MonitorIceServer> _ice = FallbackIce();
    private DateTime _iceUntil = DateTime.MinValue;

    public void NoteCodeChanged(string token)
    {
        lock (_gate)
        {
            _stamps.TryGetValue(token, out var stamp);
            _stamps[token] = stamp + 1;
            _attempts.Remove(token);
            if (_rooms.TryGetValue(token, out var room))
                DropViewer(room);
        }
    }

    public CodeGate TryCode(string token, string? entered, string? expected)
    {
        var digits = new string((entered ?? "").Where(char.IsDigit).ToArray());
        lock (_gate)
        {
            if (_attempts.TryGetValue(token, out var attempt) && attempt.LockedUntil > DateTime.UtcNow)
            {
                return new CodeGate(false, "Too many tries. Wait five minutes, then use the code from their profile.", 0);
            }

            if (string.IsNullOrEmpty(expected) || digits.Length != expected.Length
                || !CryptographicEquals(digits, expected))
            {
                attempt ??= new Attempt();
                attempt.Failures += 1;
                if (attempt.Failures >= 8)
                {
                    attempt.Failures = 0;
                    attempt.LockedUntil = DateTime.UtcNow.AddMinutes(5);
                }

                _attempts[token] = attempt;
                return new CodeGate(false, "That code does not match. Ask them for the Monitor Access Code on their profile.", 0);
            }

            _attempts.Remove(token);
            if (!_stamps.TryGetValue(token, out var stamp))
                _stamps[token] = stamp = 1;

            return new CodeGate(true, null, stamp);
        }
    }

    public void GoLive(string userId, string token, string firstName)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(token, out var room) || !string.Equals(room.UserId, userId, StringComparison.Ordinal))
            {
                room = new Room(userId, firstName);
                _rooms[token] = room;
            }

            room.FirstName = firstName;
            room.Live = true;
            room.Generation += 1;
            room.OfferSdp = null;
            room.AnswerSdp = null;
            room.PatientIce.Clear();
            room.ViewerIce.Clear();
            room.NextIce = 1;
        }
    }

    public void GoOffline(string userId)
    {
        lock (_gate)
        {
            foreach (var room in _rooms.Values)
            {
                if (!string.Equals(room.UserId, userId, StringComparison.Ordinal) || !room.Live)
                    continue;

                room.Live = false;
                room.Generation += 1;
                room.OfferSdp = null;
                room.AnswerSdp = null;
                room.Frame = null;
                room.PatientIce.Clear();
                room.ViewerIce.Clear();
            }
        }
    }

    public PatientSignal ForPatient(string userId)
    {
        lock (_gate)
        {
            var room = _rooms.Values.FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.Ordinal) && item.Live);
            if (room is null)
                return new PatientSignal(false, false, 0, null, 0, []);

            var ice = room.ViewerIce.Select(item => item.Json).ToArray();
            return new PatientSignal(true, room.ViewerId is not null, room.Generation, room.AnswerSdp, room.ViewerIce.Count == 0 ? 0 : room.ViewerIce[^1].Id, ice);
        }
    }

    public void SetOffer(string userId, long generation, string sdp)
    {
        lock (_gate)
        {
            var room = _rooms.Values.FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.Ordinal));
            if (room is null || !room.Live || room.Generation != generation || string.IsNullOrWhiteSpace(sdp))
                return;

            room.OfferSdp = sdp;
            room.AnswerSdp = null;
            room.PatientIce.Clear();
        }
    }

    public void AddPatientIce(string userId, long generation, IReadOnlyList<string> candidates)
    {
        lock (_gate)
        {
            var room = _rooms.Values.FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.Ordinal));
            if (room is null || room.Generation != generation)
                return;

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    room.PatientIce.Add(new Ice(room.NextIce++, candidate));
            }
        }
    }

    public WatchSignal ForViewer(string token, string viewerId, long stamp)
    {
        lock (_gate)
        {
            if (!_stamps.TryGetValue(token, out var current))
                current = 1;

            if (stamp != current)
                return new WatchSignal(true, false, true, 0, null, []);

            if (!_rooms.TryGetValue(token, out var room))
                return new WatchSignal(true, false, false, 0, null, []);

            if (!string.Equals(room.ViewerId, viewerId, StringComparison.Ordinal))
            {
                room.ViewerId = viewerId;
                room.Generation += 1;
                room.OfferSdp = null;
                room.AnswerSdp = null;
                room.PatientIce.Clear();
                room.ViewerIce.Clear();
            }

            var ice = room.PatientIce.Select(item => item.Json).ToArray();
            return new WatchSignal(true, room.Live, false, room.Generation, room.OfferSdp, ice, room.FrameId, room.Frame);
        }
    }

    public void DetachViewer(string token, string viewerId)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(token, out var room) && string.Equals(room.ViewerId, viewerId, StringComparison.Ordinal))
                DropViewer(room);
        }
    }

    public void SetFrame(string userId, string? jpegBase64)
    {
        if (string.IsNullOrWhiteSpace(jpegBase64) || jpegBase64.Length > 48_000)
            return;

        lock (_gate)
        {
            var room = _rooms.Values.FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.Ordinal) && item.Live);
            if (room is null)
                return;

            room.Frame = jpegBase64;
            room.FrameId++;
        }
    }

    public void SetAnswer(string token, string viewerId, long generation, string sdp)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(token, out var room))
                return;
            if (!string.Equals(room.ViewerId, viewerId, StringComparison.Ordinal) || room.Generation != generation)
                return;
            if (string.IsNullOrWhiteSpace(sdp))
                return;

            room.AnswerSdp = sdp;
        }
    }

    public void Nudge(string userId)
    {
        lock (_gate)
        {
            var room = _rooms.Values.FirstOrDefault(item => string.Equals(item.UserId, userId, StringComparison.Ordinal) && item.Live);
            if (room is null)
                return;

            room.Generation += 1;
            room.OfferSdp = null;
            room.AnswerSdp = null;
            room.PatientIce.Clear();
            room.ViewerIce.Clear();
        }
    }

    public void AddViewerIce(string token, string viewerId, long generation, IReadOnlyList<string> candidates)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(token, out var room))
                return;
            if (!string.Equals(room.ViewerId, viewerId, StringComparison.Ordinal) || room.Generation != generation)
                return;

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                    room.ViewerIce.Add(new Ice(room.NextIce++, candidate));
            }
        }
    }

    public async Task<IReadOnlyList<MonitorIceServer>> IceServersAsync(CancellationToken cancellationToken)
    {
        if (_iceUntil > DateTime.UtcNow && _ice.Count > 0)
            return _ice;

        try
        {
            var route = await twilio.GetVoiceRouteAsync(cancellationToken);
            var twilioOptions = options.Value.Twilio;
            if (!string.IsNullOrWhiteSpace(route.AccountSid)
                && !string.IsNullOrWhiteSpace(twilioOptions.ApiKeySid)
                && !string.IsNullOrWhiteSpace(twilioOptions.ApiKeySecret))
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(route.AccountSid)}/Tokens.json");
                var key = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(
                    $"{twilioOptions.ApiKeySid}:{twilioOptions.ApiKeySecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", key);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Ttl"] = "3600" });
                using var response = await httpClientFactory.CreateClient("Twilio").SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var parsed = ParseIce(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (parsed.Count > 0)
                    {
                        _ice = parsed;
                        _iceUntil = DateTime.UtcNow.AddMinutes(45);
                        return _ice;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogInformation("Family monitor is using a public connection helper.");
        }

        _ice = FallbackIce();
        _iceUntil = DateTime.UtcNow.AddMinutes(10);
        return _ice;
    }

    private static void DropViewer(Room room)
    {
        room.ViewerId = null;
        room.Generation += 1;
        room.OfferSdp = null;
        room.AnswerSdp = null;
        room.PatientIce.Clear();
        room.ViewerIce.Clear();
    }

    private static List<MonitorIceServer> ParseIce(string json)
    {
        var list = new List<MonitorIceServer>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("ice_servers", out var servers) || servers.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var server in servers.EnumerateArray())
        {
            var username = server.TryGetProperty("username", out var user) ? user.GetString() : null;
            var credential = server.TryGetProperty("credential", out var secret) ? secret.GetString() : null;
            if (server.TryGetProperty("urls", out var urls))
                AddUrls(list, urls, username, credential);
            else if (server.TryGetProperty("url", out var url))
                AddUrls(list, url, username, credential);
        }

        return list;
    }

    private static void AddUrls(List<MonitorIceServer> list, JsonElement urls, string? username, string? credential)
    {
        if (urls.ValueKind == JsonValueKind.String)
        {
            var value = urls.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(new MonitorIceServer(value, username, credential));
            return;
        }

        if (urls.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in urls.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(new MonitorIceServer(value, username, credential));
        }
    }

    private static bool CryptographicEquals(string left, string right)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(left);
        var b = System.Text.Encoding.UTF8.GetBytes(right);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static IReadOnlyList<MonitorIceServer> FallbackIce() =>
        [new MonitorIceServer("stun:stun.l.google.com:19302", null, null)];

    private sealed class Room(string userId, string firstName)
    {
        public string UserId { get; } = userId;
        public string FirstName { get; set; } = firstName;
        public bool Live { get; set; }
        public long Generation { get; set; }
        public string? ViewerId { get; set; }
        public string? OfferSdp { get; set; }
        public string? AnswerSdp { get; set; }
        public string? Frame { get; set; }
        public long FrameId { get; set; }
        public long NextIce { get; set; } = 1;
        public List<Ice> PatientIce { get; } = [];
        public List<Ice> ViewerIce { get; } = [];
    }

    private sealed record Ice(long Id, string Json);

    private sealed class Attempt
    {
        public int Failures { get; set; }
        public DateTime? LockedUntil { get; set; }
    }
}

public sealed record MonitorIceServer(string Urls, string? Username, string? Credential);

public sealed record CodeGate(bool Ok, string? Message, long Stamp);

public sealed record PatientSignal(
    bool Live,
    bool ViewerWaiting,
    long Generation,
    string? AnswerSdp,
    long IceThrough,
    IReadOnlyList<string> ViewerIce);

public sealed record WatchSignal(
    bool Known,
    bool Live,
    bool NeedsCode,
    long Generation,
    string? OfferSdp,
    IReadOnlyList<string> PatientIce,
    long FrameId = 0,
    string? Frame = null);
