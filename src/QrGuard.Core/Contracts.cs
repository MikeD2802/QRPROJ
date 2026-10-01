using System.Security.Cryptography;

namespace QrGuard.Core;

public static class Limits
{
    public const int MaxWidth = 1920;
    public const int MaxHeight = 1080;
    public const int MaxObservations = 16;
    public const int MaxPayloadBytes = 4096;
    public const int MaxFrameAgeMs = 500;
    public const int MaskLifetimeMs = 750;
    public const int DiscoveryIntervalMs = 200;
}

public readonly record struct FrameIdentity(
    Guid SessionId, long MonitorId, int DisplayGeneration, long FrameId, long CapturedTicks);

public readonly record struct PixelPoint(int X, int Y);

public static class QrGeometry
{
    // QRCode positions must describe a nondegenerate convex quadrilateral, not a default (0,0) point.
    public static bool TryBounds(ReadOnlySpan<PixelPoint> corners, int width, int height, out PixelRect bounds)
    {
        bounds = default;
        if (corners.Length != 4 || width is <= 0 or > Limits.MaxWidth || height is <= 0 or > Limits.MaxHeight) return false;
        int left = width, top = height, right = 0, bottom = 0;
        long sign = 0;
        for (int i = 0; i < 4; i++)
        {
            PixelPoint a = corners[i], b = corners[(i + 1) % 4], c = corners[(i + 2) % 4];
            if (a.X < 0 || a.X >= width || a.Y < 0 || a.Y >= height) return false;
            left = Math.Min(left, a.X); top = Math.Min(top, a.Y); right = Math.Max(right, a.X); bottom = Math.Max(bottom, a.Y);
            long cross = ((long)b.X - a.X) * ((long)c.Y - b.Y) - ((long)b.Y - a.Y) * ((long)c.X - b.X);
            if (cross == 0 || (sign != 0 && Math.Sign(cross) != Math.Sign(sign))) return false;
            sign = cross;
        }
        if (right - left + 1 < 21 || bottom - top + 1 < 21) return false;
        bounds = new(left, top, right - left + 1, bottom - top + 1); return true;
    }
}

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public long Right => (long)X + Width;
    public long Bottom => (long)Y + Height;
    public bool IsInside(int width, int height) => X >= 0 && Y >= 0 && Width > 0 && Height > 0
        && Right <= width && Bottom <= height;

    public PixelRect WithMargin(int frameWidth, int frameHeight)
    {
        if (!IsInside(frameWidth, frameHeight)) throw new ArgumentOutOfRangeException(nameof(frameWidth));
        int margin = Math.Max(12, (int)Math.Ceiling(Math.Max(Width, Height) * 0.08));
        int left = Math.Max(0, X - margin), top = Math.Max(0, Y - margin);
        int right = (int)Math.Min(frameWidth, Right + margin), bottom = (int)Math.Min(frameHeight, Bottom + margin);
        return new(left, top, right - left, bottom - top);
    }

    public double Overlap(PixelRect other)
    {
        long intersection = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(X, other.X))
            * Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
        long union = (long)Width * Height + (long)other.Width * other.Height - intersection;
        return union > 0 ? (double)intersection / union : 0;
    }
}

// Exclusive ownership: decoded pixels are cleared on every success, replacement, and failure path.
public sealed class PixelFrame : IDisposable
{
    public FrameIdentity Identity { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public PixelFrame(FrameIdentity identity, int width, int height, int stride, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width is <= 0 or > Limits.MaxWidth || height is <= 0 or > Limits.MaxHeight
            || stride != checked(width * 4) || pixels.Length != checked(stride * height))
            throw new ArgumentOutOfRangeException(nameof(width), "Frame outside the P0 envelope.");
        Identity = identity; Width = width; Height = height; Stride = stride; Pixels = pixels;
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(Pixels);
}

public sealed class QrObservation(PixelRect bounds, byte[] payload, bool decoded) : IDisposable
{
    public PixelRect Bounds { get; } = bounds;
    public byte[] Payload { get; } = payload;
    public bool Decoded { get; } = decoded;
    public void Dispose() => CryptographicOperations.ZeroMemory(Payload);
}

public sealed class DecodeBatch(IReadOnlyList<QrObservation> observations, bool saturated, int unlocatedCount = 0) : IDisposable
{
    public IReadOnlyList<QrObservation> Observations { get; } = observations;
    public bool Saturated { get; } = saturated;
    public int UnlocatedCount { get; } = unlocatedCount;
    public void Dispose() { foreach (var observation in Observations) observation.Dispose(); }
}

public interface IQrDecoder : IDisposable { DecodeBatch Decode(PixelFrame frame); }
public interface ICaptureSource : IDisposable { bool TryCapture(out PixelFrame? frame); }
public interface IMaskRenderer : IDisposable
{
    void Render(IReadOnlyList<TrackSnapshot> tracks, int frameWidth, int frameHeight);
    void Clear();
}

public readonly record struct TrackSnapshot(
    long TrackId, int GeometryRevision, int PayloadRevision, PixelRect Bounds, bool Decoded);
