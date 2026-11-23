using System.Collections.Concurrent;

namespace MedicalManager.Data;

/// <summary>
/// Wakes open calendars when a seat is held, locked, or released.
/// </summary>
public sealed class ScheduleLiveBoard
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<Task>>> _channels = new(StringComparer.Ordinal);

    public IDisposable Listen(IEnumerable<string> channels, Func<Task> handler)
    {
        var seats = new List<(string Channel, Guid Id)>();
        foreach (var channel in channels.Where(channel => !string.IsNullOrWhiteSpace(channel)).Distinct(StringComparer.Ordinal))
        {
            var id = Guid.NewGuid();
            _channels.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, Func<Task>>()).TryAdd(id, handler);
            seats.Add((channel, id));
        }

        return new Subscription(() =>
        {
            foreach (var (channel, id) in seats)
            {
                if (_channels.TryGetValue(channel, out var bag))
                    bag.TryRemove(id, out _);
            }
        });
    }

    public void Publish(string? doctorUserId, string? patientUserId)
    {
        if (!string.IsNullOrWhiteSpace(doctorUserId))
            Fanout("doctor:" + doctorUserId);
        if (!string.IsNullOrWhiteSpace(patientUserId))
            Fanout("user:" + patientUserId);
    }

    private void Fanout(string channel)
    {
        if (!_channels.TryGetValue(channel, out var bag))
            return;

        foreach (var handler in bag.Values.ToArray())
            _ = Invoke(handler);
    }

    private static async Task Invoke(Func<Task> handler)
    {
        try
        {
            await handler();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
