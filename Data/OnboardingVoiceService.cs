using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace MedicalManager.Data;

/// <summary>
/// Speaks onboarding and walkthrough lines with a neural human voice
/// (Microsoft Jenny, friendly style) instead of the browser's robotic reader.
/// </summary>
public sealed record OnboardingSpeechRequest(string? Text);

public sealed class OnboardingVoiceService(IMemoryCache cache, ILogger<OnboardingVoiceService> logger)
{
    public const int MaxCharacters = 800;

    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string ChromiumVersion = "1-143.0.3650.96";
    private const string VoiceName = "en-US-JennyNeural";

    public async Task<byte[]?> SynthesizeAsync(string? text, CancellationToken cancellationToken = default)
    {
        var spoken = Sanitize(text);
        if (spoken is null)
            return null;

        var key = "onboarding-voice:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(spoken)));
        if (cache.TryGetValue(key, out byte[]? cached) && cached is { Length: > 0 })
            return cached;

        byte[]? audio = null;
        foreach (var styled in new[] { true, false })
        {
            audio = await TrySynthesizeAsync(spoken, styled, cancellationToken);
            if (audio is { Length: > 200 })
                break;
        }

        if (audio is not { Length: > 200 })
        {
            logger.LogWarning("Human onboarding voice could not be synthesized.");
            return null;
        }

        cache.Set(key, audio, TimeSpan.FromHours(12));
        return audio;
    }

    private async Task<byte[]?> TrySynthesizeAsync(string text, bool friendlyStyle, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0");

        var connectionId = Guid.NewGuid().ToString("N");
        var gec = CreateSecMsGec();
        var url =
            "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
            $"?TrustedClientToken={TrustedClientToken}" +
            $"&ConnectionId={connectionId}" +
            $"&Sec-MS-GEC={gec}" +
            $"&Sec-MS-GEC-Version={ChromiumVersion}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            await socket.ConnectAsync(new Uri(url), timeout.Token);
            await SendTextAsync(socket, SpeechConfig(), timeout.Token);
            await SendTextAsync(socket, SsmlRequest(text, friendlyStyle), timeout.Token);
            return await ReadAudioAsync(socket, timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Neural voice attempt failed.");
            return null;
        }
    }

    private static string? Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            if (ch is '\r' or '\n' or '\t')
            {
                builder.Append(' ');
                continue;
            }

            if (char.IsControl(ch))
                continue;

            builder.Append(ch);
        }

        var spoken = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (spoken.Length is 0 or > MaxCharacters)
            return null;

        return spoken;
    }

    private static string SpeechConfig() =>
        "X-Timestamp:" + Timestamp() + "\r\n" +
        "Content-Type:application/json; charset=utf-8\r\n" +
        "Path:speech.config\r\n" +
        "\r\n" +
        """{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}""";

    private static string SsmlRequest(string text, bool friendlyStyle)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var body = friendlyStyle ? StyledSsml(text) : PlainSsml(text);
        return
            "X-RequestId:" + requestId + "\r\n" +
            "Content-Type:application/ssml+xml\r\n" +
            "X-Timestamp:" + Timestamp() + "\r\n" +
            "Path:ssml\r\n" +
            "\r\n" +
            body;
    }

    private static string StyledSsml(string text) =>
        $"""
        <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xmlns:mstts="https://www.w3.org/2001/mstts" xml:lang="en-US">
          <voice name="{VoiceName}">
            <mstts:express-as style="friendly">
              <prosody rate="-6%">{Xml(text)}</prosody>
            </mstts:express-as>
          </voice>
        </speak>
        """;

    private static string PlainSsml(string text) =>
        $"""
        <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">
          <voice name="{VoiceName}">
            <prosody rate="-6%">{Xml(text)}</prosody>
          </voice>
        </speak>
        """;

    private static async Task<byte[]?> ReadAudioAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var audio = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return audio.Length > 200 ? audio.ToArray() : null;

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var payload = message.ToArray();
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var text = Encoding.UTF8.GetString(payload);
                if (text.Contains("Path:turn.end", StringComparison.Ordinal))
                    break;
                continue;
            }

            if (payload.Length < 2)
                continue;

            var headerLength = (payload[0] << 8) | payload[1];
            if (headerLength < 0 || headerLength + 2 > payload.Length)
                continue;

            var header = Encoding.UTF8.GetString(payload, 2, headerLength);
            if (!header.Contains("Path:audio", StringComparison.Ordinal))
                continue;

            audio.Write(payload, 2 + headerLength, payload.Length - 2 - headerLength);
        }

        return audio.Length > 200 ? audio.ToArray() : null;
    }

    private static Task SendTextAsync(ClientWebSocket socket, string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string Timestamp() =>
        DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Edge's public neural endpoint expects a SHA-256 of the Windows clock rounded to 5 minutes.
    /// </summary>
    private static string CreateSecMsGec()
    {
        var ticks = (long)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 11644473600L;
        ticks *= 10_000_000L;
        ticks -= ticks % 3_000_000_000L;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken));
        return Convert.ToHexString(hash);
    }

    private static string Xml(string value) => System.Net.WebUtility.HtmlEncode(value);
}
