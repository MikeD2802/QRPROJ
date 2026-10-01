using System.Security.Cryptography;

namespace QrGuard.Core;

public sealed class TrackStore(Guid sessionId, long monitorId, int generation) : IDisposable
{
    private sealed class Track(long id, PixelRect bounds, byte[] digest, bool decoded)
    {
        public long Id = id;
        public PixelRect Bounds = bounds;
        public byte[] Digest = digest;
        public bool Decoded = decoded;
        public int GeometryRevision = 1, PayloadRevision = 1;
        public TrackSnapshot Snapshot => new(Id, GeometryRevision, PayloadRevision, Bounds, Decoded);
        public void Clear() => CryptographicOperations.ZeroMemory(Digest);
    }

    private List<Track> _tracks = [];
    private long _lastFrameId, _lastAcceptedTicks, _nextId;
    public IReadOnlyList<TrackSnapshot> Snapshot => _tracks.Select(t => t.Snapshot).ToArray();
    public long AcceptedFrames { get; private set; }
    public long RejectedFrames { get; private set; }
    public long PayloadChanges { get; private set; }
    public long RetiredTracks { get; private set; }

    public bool Apply(FrameIdentity identity, DecodeBatch batch, int width, int height,
        long nowTicks, long ticksPerSecond)
    {
        if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        if (identity.SessionId != sessionId || identity.MonitorId != monitorId
            || identity.DisplayGeneration != generation || identity.FrameId <= _lastFrameId
            || nowTicks < identity.CapturedTicks
            || (double)(nowTicks - identity.CapturedTicks) / ticksPerSecond * 1000 > Limits.MaxFrameAgeMs)
        { RejectedFrames++; return false; }

        if (width is <= 0 or > Limits.MaxWidth || height is <= 0 or > Limits.MaxHeight)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (batch.Observations.Count > Limits.MaxObservations
            || batch.Observations.Any(o => !o.Bounds.IsInside(width, height) || o.Payload.Length > Limits.MaxPayloadBytes))
            throw new ArgumentOutOfRangeException(nameof(batch));

        var available = new List<Track>(_tracks);
        var next = new List<Track>();
        foreach (QrObservation observation in batch.Observations)
        {
            byte[] digest = SHA256.HashData(observation.Payload);
            Track? match = available.OrderByDescending(t => t.Bounds.Overlap(observation.Bounds))
                .FirstOrDefault(t => t.Bounds.Overlap(observation.Bounds) >= 0.25);
            match ??= available.FirstOrDefault(t => t.Decoded == observation.Decoded
                && CryptographicOperations.FixedTimeEquals(t.Digest, digest)
                && Math.Abs((long)t.Bounds.X - observation.Bounds.X) <= Math.Max(64, observation.Bounds.Width * 2L)
                && Math.Abs((long)t.Bounds.Y - observation.Bounds.Y) <= Math.Max(64, observation.Bounds.Height * 2L));
            if (match is null) { match = new Track(++_nextId, observation.Bounds, digest, observation.Decoded); }
            else
            {
                available.Remove(match);
                if (match.Bounds != observation.Bounds) { match.Bounds = observation.Bounds; match.GeometryRevision++; }
                if (match.Decoded != observation.Decoded || !CryptographicOperations.FixedTimeEquals(match.Digest, digest))
                {
                    match.Clear(); match.Digest = digest; match.Decoded = observation.Decoded;
                    match.PayloadRevision++; PayloadChanges++;
                }
                else CryptographicOperations.ZeroMemory(digest);
            }
            next.Add(match);
        }
        foreach (Track retired in available) { retired.Clear(); RetiredTracks++; }
        _tracks = next;
        _lastFrameId = identity.FrameId; _lastAcceptedTicks = identity.CapturedTicks; AcceptedFrames++;
        return true;
    }

    public bool Expire(long nowTicks, long ticksPerSecond)
    {
        if (_tracks.Count == 0 || (double)(nowTicks - _lastAcceptedTicks) / ticksPerSecond * 1000 <= Limits.MaskLifetimeMs)
            return false;
        Clear(); return true;
    }

    public void Clear()
    {
        foreach (Track track in _tracks) { track.Clear(); RetiredTracks++; }
        _tracks.Clear();
    }
    public void Dispose() => Clear();
}
