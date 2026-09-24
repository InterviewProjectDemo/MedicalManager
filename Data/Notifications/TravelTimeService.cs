using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MedicalManager.Data.Notifications;

public sealed record TravelEstimate(double DistanceMeters, double DurationSeconds)
{
    public string Sentence
    {
        get
        {
            var minutes = Math.Max(1, (int)Math.Round(DurationSeconds / 60.0, MidpointRounding.AwayFromZero));
            var miles = DistanceMeters / 1609.344;
            if (miles < 0.25)
                return $"From your home, it is {TimePhrase(minutes)} by car, less than a quarter of a mile away.";

            return $"From your home, it is {TimePhrase(minutes)} by car, roughly {DistancePhrase(miles)} away.";
        }
    }

    private static string TimePhrase(int minutes)
    {
        if (minutes < 60)
            return minutes == 1 ? "about a minute" : $"about {minutes} minutes";

        var hours = minutes / 60;
        var rest = minutes % 60;
        var hourText = hours == 1 ? "about 1 hour" : $"about {hours} hours";
        if (rest < 5)
            return hourText;

        var minuteText = rest == 1 ? "1 minute" : $"{rest} minutes";
        return $"{hourText} and {minuteText}";
    }

    private static string DistancePhrase(double miles)
    {
        if (miles < 1.5)
            return "a mile";

        var rounded = miles < 10
            ? miles.ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Round(miles).ToString("0", CultureInfo.InvariantCulture);
        return $"{rounded} miles";
    }
}

public sealed class TravelTimeService(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<TravelTimeService> logger)
{
    private static readonly ConcurrentDictionary<string, GeoPoint?> Geocodes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim NominatimGate = new(1, 1);
    private static DateTime _nextNominatimCall = DateTime.MinValue;

    public async Task<TravelEstimate?> TryEstimateAsync(string? homeAddress, string? destination, CancellationToken cancellationToken)
    {
        if (!options.Value.Travel.Enabled)
            return null;

        if (string.IsNullOrWhiteSpace(homeAddress) || string.IsNullOrWhiteSpace(destination))
            return null;

        try
        {
            var origin = await GeocodeAsync(homeAddress, cancellationToken);
            var place = await GeocodeAsync(destination, cancellationToken);
            if (origin is null || place is null)
                return null;

            if (origin.Value.DistanceTo(place.Value) < 80)
                return null;

            var client = httpClientFactory.CreateClient("Travel");
            var url =
                "https://router.project-osrm.org/route/v1/driving/" +
                $"{origin.Value.Lon.ToString(CultureInfo.InvariantCulture)},{origin.Value.Lat.ToString(CultureInfo.InvariantCulture)};" +
                $"{place.Value.Lon.ToString(CultureInfo.InvariantCulture)},{place.Value.Lat.ToString(CultureInfo.InvariantCulture)}" +
                "?overview=false";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
                return null;

            var route = routes[0];
            var meters = route.GetProperty("distance").GetDouble();
            var seconds = route.GetProperty("duration").GetDouble();
            if (meters <= 0 || seconds <= 0 || double.IsNaN(meters) || double.IsNaN(seconds))
                return null;

            return new TravelEstimate(meters, seconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Drive time could not be calculated for an appointment reminder.");
            return null;
        }
    }

    private async Task<GeoPoint?> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var key = address.Trim();
        if (Geocodes.TryGetValue(key, out var cached))
            return cached;

        await NominatimGate.WaitAsync(cancellationToken);
        try
        {
            if (Geocodes.TryGetValue(key, out cached))
                return cached;

            var wait = _nextNominatimCall - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);
            _nextNominatimCall = DateTime.UtcNow.AddSeconds(1.1);

            var client = httpClientFactory.CreateClient("Travel");
            var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=" +
                      Uri.EscapeDataString(key);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Geocodes[key] = null;
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            {
                Geocodes[key] = null;
                return null;
            }

            var hit = document.RootElement[0];
            if (!double.TryParse(hit.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(hit.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                Geocodes[key] = null;
                return null;
            }

            var point = new GeoPoint(lat, lon);
            Geocodes[key] = point;
            return point;
        }
        finally
        {
            NominatimGate.Release();
        }
    }

    private readonly record struct GeoPoint(double Lat, double Lon)
    {
        public double DistanceTo(GeoPoint other)
        {
            const double earth = 6371000;
            var dLat = DegreesToRadians(other.Lat - Lat);
            var dLon = DegreesToRadians(other.Lon - Lon);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(DegreesToRadians(Lat)) * Math.Cos(DegreesToRadians(other.Lat))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return earth * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
