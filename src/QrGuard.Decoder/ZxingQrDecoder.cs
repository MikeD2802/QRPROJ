using System.Security.Cryptography;
using QrGuard.Core;
using ZXingCpp;

namespace QrGuard.Decoder;

public sealed class ZxingQrDecoder : IQrDecoder
{
    private readonly BarcodeReader _reader = new()
    {
        Formats = BarcodeFormat.QRCode,
        MaxNumberOfSymbols = Limits.MaxObservations + 1,
        TryInvert = true,
        ReturnErrors = true
    };

    public unsafe DecodeBatch Decode(PixelFrame frame)
    {
        var observations = new List<QrObservation>();
        int unlocated = 0;
        fixed (byte* pixels = frame.Pixels)
        {
            // ImageView is non-owning. Pin for its entire native use (the span overload does not own a pin).
            var view = new ImageView((nint)pixels, frame.Width, frame.Height, ImageFormat.BGRA, frame.Stride);
            Barcode[] barcodes = _reader.From(view);
            try
            {
                foreach (Barcode barcode in barcodes.Take(Limits.MaxObservations))
                {
                    Position p = barcode.Position;
                    PixelPoint[] corners = [new(p.TopLeft.X, p.TopLeft.Y), new(p.TopRight.X, p.TopRight.Y),
                        new(p.BottomRight.X, p.BottomRight.Y), new(p.BottomLeft.X, p.BottomLeft.Y)];
                    if (!QrGeometry.TryBounds(corners, frame.Width, frame.Height, out PixelRect bounds))
                    { unlocated++; continue; }
                    byte[] payload = barcode.IsValid ? barcode.Bytes : [];
                    if (payload.Length > Limits.MaxPayloadBytes)
                    { CryptographicOperations.ZeroMemory(payload); payload = []; }
                    observations.Add(new(bounds, payload, barcode.IsValid && payload.Length > 0));
                }
                return new(observations, barcodes.Length > Limits.MaxObservations, unlocated);
            }
            catch { foreach (var observation in observations) observation.Dispose(); throw; }
            finally { foreach (Barcode barcode in barcodes) barcode.Dispose(); GC.KeepAlive(view); }
        }
    }
    public void Dispose() => _reader.Dispose();
}
