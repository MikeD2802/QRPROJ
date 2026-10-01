using System.Security.Cryptography;

namespace QrGuard.Core;

// Coalesce byte-identical complete frames, with periodic full discovery. No sampled pixels or tile blind spots.
public sealed class BoundedDiscovery : IDisposable
{
    public const int FullDiscoveryIntervalMs = 1000;
    private byte[]? _digest;
    private DecodeBatch? _cached;
    private long _lastFullTicks;
    private FrameIdentity _source;
    private int _width, _height;
    private bool _disposed;
    private readonly object _gate = new();

    public DecodeBatch Process(PixelFrame frame, IQrDecoder decoder, long nowTicks, long frequency, out bool reused)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        byte[] digest = SHA256.HashData(frame.Pixels);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                reused = _cached is not null && _digest is not null && CryptographicOperations.FixedTimeEquals(digest, _digest)
                    && _source.SessionId == frame.Identity.SessionId && _source.MonitorId == frame.Identity.MonitorId
                    && _source.DisplayGeneration == frame.Identity.DisplayGeneration && _width == frame.Width && _height == frame.Height
                    && nowTicks >= _lastFullTicks && (double)(nowTicks - _lastFullTicks) / frequency * 1000 < FullDiscoveryIntervalMs;
                if (reused) return Clone(_cached!);
            }
            // Native decode is outside the cache lock: stop can clear cached bytes without waiting for native code.
            DecodeBatch batch = decoder.Decode(frame);
            try
            {
                if (batch.Observations.Count > Limits.MaxObservations || batch.Observations.Any(o => o.Payload.Length > Limits.MaxPayloadBytes
                    || !o.Bounds.IsInside(frame.Width, frame.Height))) throw new InvalidOperationException("decoder_bounds");
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    DecodeBatch copy = Clone(batch);
                    _cached?.Dispose(); _cached = copy;
                    if (_digest is not null) CryptographicOperations.ZeroMemory(_digest);
                    _digest = digest; digest = [];
                    _source = frame.Identity; _width = frame.Width; _height = frame.Height;
                    _lastFullTicks = nowTicks; return batch;
                }
            }
            catch { batch.Dispose(); throw; }
        }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    private static DecodeBatch Clone(DecodeBatch batch) => new(batch.Observations.Select(o =>
        new QrObservation(o.Bounds, o.Payload.ToArray(), o.Decoded)).ToArray(), batch.Saturated, batch.UnlocatedCount);
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true; _cached?.Dispose(); _cached = null;
            if (_digest is not null) CryptographicOperations.ZeroMemory(_digest);
            _digest = null;
        }
    }
}
