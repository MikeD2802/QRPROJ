using System.Text;
using QrGuard.Core;
using QrGuard.Decoder;

namespace QrGuard.Contracts;

internal static class Program
{
    private static int _passed;
    private static readonly Guid Session = Guid.NewGuid();
    private static FrameIdentity Identity(long id, long time = 1000) => new(Session, 1, 1, id, time);

    public static int Main()
    {
        Check("native QR decoder preserves original UTF-8 payload", () =>
        {
            using var frame = Scene(SyntheticFixtures.PayloadA, 240, 160);
            using var decoder = new ZxingQrDecoder(); using DecodeBatch decoded = decoder.Decode(frame);
            Assert(decoded.Observations.Count == 1 && decoded.Observations[0].Decoded);
            Assert(decoded.Observations[0].Payload.SequenceEqual(Encoding.UTF8.GetBytes(SyntheticFixtures.PayloadA)));
            Assert(decoded.Observations[0].Bounds.IsInside(frame.Width, frame.Height));
            Assert(decoded.Observations[0].Bounds.X >= 240 && decoded.Observations[0].Bounds.Y >= 160);
        });
        Check("moving QR retains track identity and revises geometry", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A"); using var b = Batch(90, 50, "A");
            Apply(store, 1, a); long id = store.Snapshot[0].TrackId; Apply(store, 2, b);
            Assert(store.Snapshot[0].TrackId == id && store.Snapshot[0].GeometryRevision == 2);
        });
        Check("stationary payload replacement invalidates previous revision", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A"); using var b = Batch(20, 20, "B");
            Apply(store, 1, a); Apply(store, 2, b);
            Assert(store.Snapshot[0].PayloadRevision == 2 && store.PayloadChanges == 1);
        });
        Check("fresh absence retires mask immediately", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A"); using var empty = new DecodeBatch([], false);
            Apply(store, 1, a); Apply(store, 2, empty);
            Assert(store.Snapshot.Count == 0 && store.RetiredTracks == 1);
        });
        Check("occluded and removed synthetic scenes contain no decodable QR", () =>
        {
            using var decoder = new ZxingQrDecoder(); using var frame = Scene(null, 0, 0);
            using var result = decoder.Decode(frame); Assert(result.Observations.Count == 0);
        });
        Check("out-of-order result cannot resurrect a retired mask", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A"); using var empty = new DecodeBatch([], false);
            Apply(store, 2, empty);
            Assert(!store.Apply(Identity(1), a, 640, 480, 1000, 1000)); Assert(store.Snapshot.Count == 0);
        });
        Check("old session, monitor and display generation are rejected", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A");
            Assert(!store.Apply(Identity(1) with { SessionId = Guid.NewGuid() }, a, 640, 480, 1000, 1000));
            Assert(!store.Apply(Identity(1) with { MonitorId = 2 }, a, 640, 480, 1000, 1000));
            Assert(!store.Apply(Identity(1) with { DisplayGeneration = 2 }, a, 640, 480, 1000, 1000));
        });
        Check("late results and future timestamps are rejected", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A");
            Assert(!store.Apply(Identity(1), a, 640, 480, 1501, 1000));
            Assert(!store.Apply(Identity(2, 1500), a, 640, 480, 1000, 1000));
        });
        Check("capture stall bounds mask lifetime", () =>
        {
            using var store = Store(); using var a = Batch(20, 20, "A"); Apply(store, 1, a);
            Assert(!store.Expire(1750, 1000)); Assert(store.Expire(1751, 1000)); Assert(store.Snapshot.Count == 0);
        });
        Check("invalid geometry, overflow and oversized payloads are rejected", () =>
        {
            using var store = Store(); using var invalid = new DecodeBatch([new(new(int.MaxValue, 0, int.MaxValue, 10), [], false)], false);
            Throws<ArgumentOutOfRangeException>(() => Apply(store, 1, invalid));
            using var oversized = new DecodeBatch([new(new(0, 0, 20, 20), new byte[4097], true)], false);
            Throws<ArgumentOutOfRangeException>(() => Apply(store, 1, oversized));
        });
        Check("observation count is bounded", () =>
        {
            using var store = Store(); using var batch = new DecodeBatch(Enumerable.Range(0, 17).Select(_ => new QrObservation(new(0, 0, 10, 10), [], false)).ToArray(), true);
            Throws<ArgumentOutOfRangeException>(() => Apply(store, 1, batch));
        });
        Check("same bytes in multiple QRs produce separate tracks", () =>
        {
            using var store = Store(); using var batch = new DecodeBatch([new(new(20, 20, 30, 30), [1], true), new(new(200, 20, 30, 30), [1], true)], false);
            Apply(store, 1, batch); Assert(store.Snapshot.Select(t => t.TrackId).Distinct().Count() == 2);
            Apply(store, 2, batch); Assert(store.Snapshot.Count == 2);
        });
        Check("reliable undecodable geometry is masked with disabled payload state", () =>
        {
            using var store = Store(); using var batch = new DecodeBatch([new(new(20, 20, 30, 30), [], false)], false);
            Apply(store, 1, batch); Assert(store.Snapshot.Count == 1 && !store.Snapshot[0].Decoded);
        });
        Check("margin covers QR and clips to physical frame bounds", () =>
        {
            Assert(new PixelRect(5, 5, 100, 100).WithMargin(640, 480) == new PixelRect(0, 0, 117, 117));
            Assert(new PixelRect(600, 450, 40, 30).WithMargin(640, 480).IsInside(640, 480));
        });
        Check("default, collapsed and crossed decoder corners never create a mask", () =>
        {
            Assert(!QrGeometry.TryBounds([default, default, default, default], 640, 480, out _));
            Assert(!QrGeometry.TryBounds([new(10, 10), new(50, 10), new(50, 10), new(10, 10)], 640, 480, out _));
            Assert(!QrGeometry.TryBounds([new(10, 10), new(50, 50), new(50, 10), new(10, 50)], 640, 480, out _));
        });
        Check("rotated valid corners yield bounded physical geometry", () =>
        {
            Assert(QrGeometry.TryBounds([new(50, 10), new(90, 50), new(50, 90), new(10, 50)], 640, 480, out var bounds));
            Assert(bounds == new PixelRect(10, 10, 81, 81));
            Assert(!QrGeometry.TryBounds([new(-1, 0), new(50, 0), new(50, 50), new(-1, 50)], 640, 480, out _));
        });
        Check("latest-slot replacement, closing and consumption dispose exactly once", () =>
        {
            using var slot = new LatestSlot<Owned>(); var first = new Owned(); var second = new Owned();
            slot.Publish(first); slot.Publish(second); Assert(first.Disposals == 1);
            Assert(ReferenceEquals(slot.Take(), second)); Assert(slot.Take() is null); second.Dispose();
            slot.Dispose(); var late = new Owned(); slot.Publish(late); Assert(late.Disposals == 1 && second.Disposals == 1);
        });
        Check("frame pixels and observation bytes are zeroed on release", () =>
        {
            byte[] bytes = [1, 2, 3, 4]; var frame = new PixelFrame(Identity(1), 1, 1, 4, bytes); frame.Dispose(); Assert(bytes.All(b => b == 0));
            byte[] payload = [3, 2, 1]; using (var observation = new QrObservation(new(0, 0, 1, 1), payload, true)) { }
            Assert(payload.All(b => b == 0));
        });
        Check("frame allocation arithmetic and envelope are bounded", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new PixelFrame(Identity(1), int.MaxValue, 1, 4, []));
            Throws<ArgumentOutOfRangeException>(() => new PixelFrame(Identity(1), 1, 1, 3, new byte[4]));
        });
        Check("synthetic sensitive QR is decoded without text logging", () =>
        {
            using var frame = Scene(SyntheticFixtures.Sensitive, 100, 100); using var decoder = new ZxingQrDecoder();
            using var result = decoder.Decode(frame); Assert(result.Observations.Count == 1 && result.Observations[0].Decoded);
        });
        Check("full-frame discovery finds QR crossing an arbitrary tile boundary", () =>
        {
            using var frame = Scene(SyntheticFixtures.PayloadB, 250, 150); using var decoder = new ZxingQrDecoder();
            using var result = decoder.Decode(frame); Assert(result.Observations.Count == 1);
            Assert(result.Observations[0].Bounds.X < 320 && result.Observations[0].Bounds.Right > 320);
        });
        Check("native decoding feeds stationary replacement and disappearance through tracking", () =>
        {
            using var decoder = new ZxingQrDecoder(); using var store = Store();
            using var a = Scene(SyntheticFixtures.PayloadA, 100, 100, 1);
            using var b = Scene(SyntheticFixtures.PayloadB, 100, 100, 2);
            using var removed = Scene(null, 0, 0, 3);
            using var decodedA = decoder.Decode(a); using var decodedB = decoder.Decode(b); using var decodedEmpty = decoder.Decode(removed);
            Apply(store, 1, decodedA); Apply(store, 2, decodedB);
            Assert(store.PayloadChanges == 1 && store.Snapshot.Count == 1);
            Apply(store, 3, decodedEmpty); Assert(store.Snapshot.Count == 0);
        });
        Check("QR-only reader ignores a valid non-QR barcode", () =>
        {
            using var creator = new ZXingCpp.CreatorOptions(ZXingCpp.BarcodeFormat.DataMatrix);
            using var barcode = new ZXingCpp.Barcode("synthetic", creator);
            using var writer = new ZXingCpp.WriterOptions { Scale = 6, AddQuietZones = true };
            using var image = barcode.ToImage(writer);
            byte[] gray = image.ToArray(); byte[] pixels = new byte[gray.Length * 4];
            for (int i = 0; i < gray.Length; i++)
            { pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = gray[i]; pixels[i * 4 + 3] = 255; }
            using var frame = new PixelFrame(Identity(1), image.Width, image.Height, image.Width * 4, pixels);
            using var decoder = new ZxingQrDecoder(); using var result = decoder.Decode(frame);
            Assert(result.Observations.Count == 0);
        });
        Console.WriteLine($"PASS: {_passed} P0 portable contracts. Windows compositor, physical phone and sharing were not tested.");
        return 0;
    }

    private static TrackStore Store() => new(Session, 1, 1);
    private static void Apply(TrackStore store, long id, DecodeBatch batch) => Assert(store.Apply(Identity(id), batch, 640, 480, 1000, 1000));
    private static DecodeBatch Batch(int x, int y, string payload) => new([new(new(x, y, 100, 100), Encoding.UTF8.GetBytes(payload), true)], false);
    private static void Assert(bool condition) { if (!condition) throw new InvalidOperationException("Contract failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected rejection was absent."); }
    private static void Check(string label, Action action) { action(); _passed++; Console.WriteLine($"PASS {label}"); }
    private sealed class Owned : IDisposable { public int Disposals; public void Dispose() => Disposals++; }

    private static PixelFrame Scene(string? payload, int x, int y, long id = 1)
    {
        const int width = 640, height = 480, stride = width * 4;
        byte[] pixels = Enumerable.Repeat((byte)255, stride * height).ToArray();
        if (payload is not null)
        {
            var fixture = SyntheticFixtures.Create(payload);
            Assert(x + fixture.Width <= width && y + fixture.Height <= height);
            for (int row = 0; row < fixture.Height; row++)
                for (int col = 0; col < fixture.Width; col++)
                {
                    int offset = (y + row) * stride + (x + col) * 4;
                    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = fixture.Pixels[row * fixture.Width + col];
                }
        }
        return new(Identity(id), width, height, stride, pixels);
    }
}
