using System.Collections.Concurrent;

namespace MedicalManager.Data;

public sealed record VideoSignal(string Kind, string Payload);

/// <summary>
/// Relays WebRTC offer, answer, and ICE between the doctor and patient circuits.
/// Media is not relayed.
/// </summary>
public sealed class VideoSignalBroker
{
    private readonly ConcurrentDictionary<int, Room> _rooms = new();

    public async Task<bool> JoinAsync(int visitId, string userId, bool isDoctor, Func<VideoSignal, Task> deliver)
    {
        var room = _rooms.GetOrAdd(visitId, _ => new Room());
        List<VideoSignal> backlog;
        bool peerPresent;
        Seat? doctor;
        lock (room)
        {
            var seat = new Seat(userId, deliver);
            if (isDoctor)
                room.Doctor = seat;
            else
                room.Patient = seat;

            var box = isDoctor ? room.ForDoctor : room.ForPatient;
            backlog = box.ToList();
            box.Clear();
            peerPresent = isDoctor ? room.Patient is not null : room.Doctor is not null;
            doctor = room.Doctor;
        }

        foreach (var signal in backlog)
            await DeliverSafe(signal, deliver);

        if (!isDoctor && peerPresent && doctor is not null)
            await DeliverSafe(new VideoSignal("peer-ready", ""), doctor.Deliver);

        return isDoctor && peerPresent;
    }

    public async Task RelayAsync(int visitId, string fromUserId, bool fromDoctor, VideoSignal signal)
    {
        var room = _rooms.GetOrAdd(visitId, _ => new Room());
        Seat? target;
        lock (room)
        {
            target = fromDoctor ? room.Patient : room.Doctor;
            if (target is null || target.UserId == fromUserId)
            {
                var box = fromDoctor ? room.ForPatient : room.ForDoctor;
                if (box.Count < 64)
                    box.Add(signal);
                return;
            }
        }

        await DeliverSafe(signal, target.Deliver);
    }

    public async Task LeaveAsync(int visitId, string userId)
    {
        if (!_rooms.TryGetValue(visitId, out var room))
            return;

        Seat? other;
        lock (room)
        {
            other = room.Doctor?.UserId == userId
                ? room.Patient
                : room.Patient?.UserId == userId ? room.Doctor : null;
            if (room.Doctor?.UserId == userId)
                room.Doctor = null;
            if (room.Patient?.UserId == userId)
                room.Patient = null;
            if (room.Doctor is null && room.Patient is null)
                _rooms.TryRemove(visitId, out _);
        }

        if (other is not null)
            await DeliverSafe(new VideoSignal("peer-left", ""), other.Deliver);
    }

    private static async Task DeliverSafe(VideoSignal signal, Func<VideoSignal, Task> deliver)
    {
        try
        {
            await deliver(signal);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private sealed class Room
    {
        public Seat? Doctor;
        public Seat? Patient;
        public List<VideoSignal> ForDoctor { get; } = [];
        public List<VideoSignal> ForPatient { get; } = [];
    }

    private sealed class Seat(string userId, Func<VideoSignal, Task> deliver)
    {
        public string UserId { get; } = userId;
        public Func<VideoSignal, Task> Deliver { get; } = deliver;
    }
}
