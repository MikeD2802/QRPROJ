using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace QrGuard.Core;

public enum PayloadKind { HttpUrl, Sensitive, Unsupported, Undecodable }
public enum DecisionState { CompanyApproved, Unverified, Blocked, Sensitive, Unsupported, UnableToDecode }

// Only owned bytes survive an observation. Parsed URL strings exist only during evaluation/actions.
internal sealed class TransientPayload : IDisposable
{
    private readonly byte[] _bytes;
    private bool _disposed;
    public TransientPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > Limits.MaxPayloadBytes) throw new ArgumentOutOfRangeException(nameof(bytes));
        _bytes = bytes.ToArray();
    }
    public ReadOnlySpan<byte> Bytes => _disposed ? throw new ObjectDisposedException(nameof(TransientPayload)) : _bytes;
    public void Dispose() { CryptographicOperations.ZeroMemory(_bytes); _disposed = true; }
}

// Construction is restricted to the validator; a launcher never receives an arbitrary string/command.
public sealed class ValidatedHttpTarget
{
    public string OriginalText { get; }
    public string AsciiHost { get; }
    public string Scheme { get; }
    public int EffectivePort { get; }
    internal ValidatedHttpTarget(string text, string host, string scheme, int port)
    { OriginalText = text; AsciiHost = host; Scheme = scheme; EffectivePort = port; }
    public override string ToString() => AsciiHost;

    public string RedactedDisplay()
    {
        int start = OriginalText.IndexOf("://", StringComparison.Ordinal) + 3;
        int path = OriginalText.IndexOfAny(['/', '?', '#'], start);
        string suffix = path < 0 ? "" : OriginalText[path..];
        int query = suffix.IndexOf('?'), fragment = suffix.IndexOf('#');
        // A '?' inside a fragment is not a query delimiter.
        if (fragment >= 0 && query > fragment) query = -1;
        int end = query < 0 ? (fragment < 0 ? suffix.Length : fragment) : query;
        string authority = AsciiHost + (EffectivePort == (Scheme == "https" ? 443 : 80) ? "" : $":{EffectivePort}");
        return Scheme + "://" + authority + DisplaySafety.Ascii(suffix[..end])
            + (query < 0 ? "" : "?[query redacted]") + (fragment < 0 ? "" : "#[fragment redacted]");
    }
}

public sealed record TypedPayload(PayloadKind Kind, ValidatedHttpTarget? Target = null);

public static class DisplaySafety
{
    // ASCII rendering prevents both explicit bidi controls and implicit RTL reordering in the details UI.
    public static string Ascii(string value)
    {
        var output = new StringBuilder(value.Length);
        foreach (char c in value) output.Append(c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}");
        return output.ToString();
    }
}

public static class PayloadValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true };
    private static readonly string[] SensitivePrefixes =
    ["otpauth:", "otpauth-migration:", "wifi:", "dpp:", "fido:", "openid4vp:", "openid-vc:",
        "ms-device:", "bluetooth:", "begin:vcard", "mecard:"];

    public static TypedPayload Validate(ReadOnlySpan<byte> bytes, bool decoded = true)
    {
        if (!decoded || bytes.IsEmpty || bytes.Length > Limits.MaxPayloadBytes) return new(PayloadKind.Undecodable);
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return new(PayloadKind.Unsupported); }
        if (SensitivePrefixes.Any(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return new(PayloadKind.Sensitive);
        return TryHttp(text, out ValidatedHttpTarget? target) ? new(PayloadKind.HttpUrl, target) : new(PayloadKind.Unsupported);
    }

    public static bool TryHttp(string text, out ValidatedHttpTarget? target)
    {
        target = null;
        if (text.Length is 0 or > Limits.MaxPayloadBytes) return false;
        try { if (StrictUtf8.GetByteCount(text) > Limits.MaxPayloadBytes) return false; }
        catch (EncoderFallbackException) { return false; }
        foreach (char c in text)
            if (char.IsControl(c) || char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format || c == '\\') return false;
        // Uri accepts some malformed escapes and normalizes surprising authority spellings. Check them first.
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '%') continue;
            if (i + 2 >= text.Length || !byte.TryParse(text.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)
                || b < 32 || b == 127 || b == 92) return false;
            i += 2;
        }
        int split = text.IndexOf("://", StringComparison.Ordinal);
        if (split < 0) return false;
        string scheme = text[..split].ToLowerInvariant();
        if (scheme is not ("http" or "https")) return false;
        int authorityStart = split + 3, authorityEnd = text.IndexOfAny(['/', '?', '#'], authorityStart);
        string authority = authorityEnd < 0 ? text[authorityStart..] : text[authorityStart..authorityEnd];
        if (authority.Length == 0 || authority.Contains('@') || authority.Contains('%')) return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || !uri.IsWellFormedOriginalString()
            || uri.Scheme != scheme || uri.UserInfo.Length != 0 || uri.Port is < 1 or > 65535) return false;

        string host, rawHost, portText = "";
        if (authority.StartsWith('['))
        {
            int close = authority.IndexOf(']');
            if (close < 0) return false;
            rawHost = authority[1..close];
            if (!IPAddress.TryParse(rawHost, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return false;
            host = "[" + ip.ToString().ToLowerInvariant() + "]";
            if (close + 1 < authority.Length)
            {
                if (authority[close + 1] != ':') return false;
                portText = authority[(close + 2)..];
                if (portText.Length == 0) return false;
            }
        }
        else
        {
            int colon = authority.IndexOf(':');
            rawHost = colon < 0 ? authority : authority[..colon];
            if (colon >= 0) { portText = authority[(colon + 1)..]; if (portText.Length == 0) return false; }
            if (uri.HostNameType == UriHostNameType.IPv4)
            {
                if (!IPAddress.TryParse(rawHost, out var ip) || rawHost != ip.ToString()) return false;
                host = rawHost;
            }
            else
            {
                if (!TryAsciiHost(rawHost, out host)) return false;
                string lastLabel = host.Split('.')[^1];
                // Browsers can treat an all-numeric (or hexadecimal) last label as an IPv4 authority.
                if (lastLabel.All(c => c is >= '0' and <= '9') || (lastLabel.StartsWith("0x", StringComparison.Ordinal)
                    && lastLabel[2..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))) return false;
            }
            if (!string.Equals(host, uri.IdnHost, StringComparison.OrdinalIgnoreCase)) return false;
        }
        if (portText.Length > 0 && (portText.Any(c => c is < '0' or > '9') || portText.Length > 5
            || portText[0] == '0' || !int.TryParse(portText, out int port) || port != uri.Port)) return false;
        target = new(text, host, scheme, uri.Port); return true;
    }

    public static bool TryAsciiHost(string raw, out string ascii)
    {
        ascii = "";
        if (raw.Length is 0 or > 253 || raw.EndsWith('.')) return false;
        try
        {
            var labels = new List<string>();
            foreach (string label in raw.Split('.'))
            {
                string a = Idn.GetAscii(label).ToLowerInvariant();
                if (a.Length is 0 or > 63 || a[0] == '-' || a[^1] == '-'
                    || a.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                    || Idn.GetAscii(Idn.GetUnicode(a)).ToLowerInvariant() != a) return false;
                labels.Add(a);
            }
            ascii = string.Join('.', labels);
            return ascii.Length <= 253;
        }
        catch (ArgumentException) { return false; }
    }
}
