using System.Text;
using ZXingCpp;

namespace QrGuard.Decoder;

// The writer is used only for explicit synthetic lab fixtures and portable contract checks.
public static class SyntheticFixtures
{
    public const string PayloadA = "https://benefits.corp.example/enroll";
    public const string PayloadB = "https://help.corp.example/deny-test";
    public const string Sensitive = "otpauth://totp/Demo?secret=SYNTHETICONLY";

    public static (byte[] Pixels, int Width, int Height) Create(string text)
    {
        using var options = new CreatorOptions(BarcodeFormat.QRCode, "EcLevel=H");
        using var barcode = new Barcode(Encoding.UTF8.GetBytes(text), options);
        using var writer = new WriterOptions { Scale = 6, AddQuietZones = true };
        using Image image = barcode.ToImage(writer);
        return (image.ToArray(), image.Width, image.Height);
    }
}
