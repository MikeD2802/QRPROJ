using System.Security.Cryptography;

namespace QrGuard.Core;

public sealed class TrackStore(Guid sessionId, long monitorId, int generation) : ITrackStore
{
    private sealed class Track(long id, PixelRect bounds, byte[] digest, QrObservation observation)
    {
        public long Id = id;
        public PixelRect Bounds = bounds;
        public byte[] Digest = digest;
        public bool Decoded = observation.Decoded;
        public TransientPayload Payload = new(observation.Payload);
        public long ObservedTicks;
        public Guid EvaluatedPolicy;
        public int EvaluatedPayloadRevision;
        public DateTimeOffset ReevaluateAt;
        public DateTimeOffset EvaluatedAt;
        public DecisionState State = observation.Decoded ? DecisionState.Unverified : DecisionState.UnableToDecode;
        public int GeometryRevision = 1, PayloadRevision = 1;
        public TrackSnapshot Snapshot => new(Id, GeometryRevision, PayloadRevision, Bounds, Decoded, State);
        public void Clear() { Payload.Dispose(); CryptographicOperations.ZeroMemory(Digest); }
    }

    private List<Track> _tracks = [];
    private readonly object _gate = new();
    private bool _disposed;
    private long _lastFrameId, _lastAcceptedTicks, _nextId;
    public IReadOnlyList<TrackSnapshot> Snapshot { get { lock (_gate) return _tracks.Select(t => t.Snapshot).ToArray(); } }
    public long AcceptedFrames { get; private set; }
    public long RejectedFrames { get; private set; }
    public long PayloadChanges { get; private set; }
    public long RetiredTracks { get; private set; }

    public bool Apply(FrameIdentity identity, DecodeBatch batch, int width, int height,
        long nowTicks, long ticksPerSecond)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (identity.SessionId != sessionId || identity.MonitorId != monitorId
                || identity.DisplayGeneration != generation || identity.FrameId <= _lastFrameId
                || nowTicks < identity.CapturedTicks || identity.CapturedTicks < _lastAcceptedTicks
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
                if (match is null) { match = new Track(++_nextId, observation.Bounds, digest, observation); }
                else
                {
                    available.Remove(match);
                    if (match.Bounds != observation.Bounds) { match.Bounds = observation.Bounds; match.GeometryRevision++; }
                    if (match.Decoded != observation.Decoded || !CryptographicOperations.FixedTimeEquals(match.Digest, digest))
                    {
                        match.Clear(); match.Digest = digest; match.Decoded = observation.Decoded;
                        match.Payload = new(observation.Payload);
                        match.State = observation.Decoded ? DecisionState.Unverified : DecisionState.UnableToDecode;
                        match.PayloadRevision++; PayloadChanges++;
                    }
                    else CryptographicOperations.ZeroMemory(digest);
                }
                match.ObservedTicks = identity.CapturedTicks;
                next.Add(match);
            }
            foreach (Track retired in available) { retired.Clear(); RetiredTracks++; }
            _tracks = next;
            _lastFrameId = identity.FrameId; _lastAcceptedTicks = identity.CapturedTicks; AcceptedFrames++;
            return true;
        }
    }

    public bool Expire(long nowTicks, long ticksPerSecond)
    {
        lock (_gate)
        {
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (_tracks.Count == 0 || (double)(nowTicks - _lastAcceptedTicks) / ticksPerSecond * 1000 <= Limits.MaskLifetimeMs)
                return false;
            Clear(); return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (Track track in _tracks) { track.Clear(); RetiredTracks++; }
            _tracks.Clear();
        }
    }
    public void Dispose() { lock (_gate) { Clear(); _disposed = true; } }

    public bool Evaluate(IPolicyEvaluator evaluator, PolicySnapshot policy, DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            bool changed = false;
            DateTimeOffset nextExpiry = policy.Rules.Where(r => r.ExpiresAt > utcNow).Select(r => r.ExpiresAt).DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            foreach (Track track in _tracks)
            {
                if (track.EvaluatedPolicy == policy.Token && track.EvaluatedPayloadRevision == track.PayloadRevision && utcNow >= track.EvaluatedAt && utcNow < track.ReevaluateAt) continue;
                DecisionState previous = track.State;
                track.State = evaluator.Evaluate(PayloadValidator.Validate(track.Payload.Bytes, track.Decoded), track.Payload.Bytes, policy, utcNow).State;
                track.EvaluatedPolicy = policy.Token; track.EvaluatedPayloadRevision = track.PayloadRevision; track.ReevaluateAt = nextExpiry;
                track.EvaluatedAt = utcNow;
                changed |= previous != track.State;
            }
            return changed;
        }
    }

    internal delegate T CurrentReader<T>(TrackIdentity identity, TypedPayload payload, ReadOnlySpan<byte> bytes);
    // Runs under the same lock as replacement/retirement. Snapshot replacement holds the policy lock first.
    internal T ReadCurrent<T>(long id, TrackIdentity? expected, IClock clock, CurrentReader<T> read, Func<T> unavailable)
    {
        lock (_gate)
        {
            Track? track = _tracks.FirstOrDefault(t => t.Id == id);
            if (_disposed || track is null || clock.TicksPerSecond <= 0 || clock.MonotonicTicks < track.ObservedTicks
                || (double)(clock.MonotonicTicks - track.ObservedTicks) / clock.TicksPerSecond * 1000 > Limits.MaxFrameAgeMs) return unavailable();
            TrackIdentity identity = new(sessionId, monitorId, generation, track.Id, track.GeometryRevision, track.PayloadRevision);
            if (expected is not null && expected.Value != identity) return unavailable();
            return read(identity, PayloadValidator.Validate(track.Payload.Bytes, track.Decoded), track.Payload.Bytes);
        }
    }
}
