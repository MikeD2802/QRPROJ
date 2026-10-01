using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QrGuard.Core;

public enum UnknownAction { WarnThenOpen, Block }
public enum RuleEffect { Allow, Deny }
public enum PolicyFailure { None, Size, Schema, Semantics, Read }
public abstract record RuleMatch;
public sealed record ExactUrlMatch(string Value) : RuleMatch;
public sealed record ExactHostMatch(string Host, string Scheme, int EffectivePort) : RuleMatch;
public sealed record PayloadHashMatch(string Value) : RuleMatch;
public sealed record PolicyRule(string Id, RuleEffect Effect, DateTimeOffset ExpiresAt, RuleMatch Match);

public sealed class PolicySnapshot
{
    // Token is a snapshot identity, never the editable numeric revision. It is not persisted in diagnostics.
    public Guid Token { get; } = Guid.NewGuid();
    public string PolicyId { get; }
    public long Revision { get; }
    public UnknownAction UnknownAction { get; }
    public ImmutableArray<PolicyRule> Rules { get; }
    public bool IsBaseline { get; }
    internal PolicySnapshot(string id, long revision, UnknownAction unknown, ImmutableArray<PolicyRule> rules, bool baseline = false)
    { PolicyId = id; Revision = revision; UnknownAction = unknown; Rules = rules; IsBaseline = baseline; }
    internal static PolicySnapshot Baseline() => new("built-in-mask-and-warn", 0, UnknownAction.WarnThenOpen, [], true);
}

// Executable schema-v1 validation mirrors policy.schema.json without a schema resolver or network dependency.
// Rejects duplicate object keys in addition to JSON Schema's required/type/enum/oneOf/additionalProperties constraints.
public static class PolicyValidator
{
    public const int MaxFileBytes = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex Identifier = new("\\A[a-zA-Z0-9._-]+\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Hash = new("\\A[a-f0-9]{64}\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Utc = new("\\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.[0-9]+)?Z\\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool TryValidate(ReadOnlySpan<byte> bytes, out PolicySnapshot? snapshot, out PolicyFailure failure)
    {
        snapshot = null; failure = PolicyFailure.Schema;
        if (bytes.IsEmpty || bytes.Length > MaxFileBytes) { failure = PolicyFailure.Size; return false; }
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (!Object(root, "schema_version", "policy_id", "revision", "unknown_action", "rules")
                || !Integer(root.GetProperty("schema_version"), 1, 1, out _)
                || !Id(root.GetProperty("policy_id")) || !Integer(root.GetProperty("revision"), 1, long.MaxValue, out long revision)
                || !Choice(root.GetProperty("unknown_action"), "warn_then_open", "block")
                || root.GetProperty("rules").ValueKind != JsonValueKind.Array || root.GetProperty("rules").GetArrayLength() > 1000) return false;
            var rules = ImmutableArray.CreateBuilder<PolicyRule>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement rule in root.GetProperty("rules").EnumerateArray())
            {
                if (!Object(rule, "id", "effect", "owner", "reason", "expires_at", "match") || !Id(rule.GetProperty("id"))
                    || !Choice(rule.GetProperty("effect"), "allow", "deny") || !Text(rule.GetProperty("owner"), 1, 128)
                    || !Text(rule.GetProperty("reason"), 1, 512) || !Text(rule.GetProperty("expires_at"), 1, 128)) return false;
                string id = rule.GetProperty("id").GetString()!;
                string expiry = rule.GetProperty("expires_at").GetString()!;
                if (!Utc.IsMatch(expiry)) return false;
                if (!ids.Add(id) || !DateTimeOffset.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset expires)
                    || expires.Offset != TimeSpan.Zero) { failure = PolicyFailure.Semantics; return false; }
                JsonElement match = rule.GetProperty("match");
                if (match.ValueKind != JsonValueKind.Object || !match.TryGetProperty("kind", out JsonElement kind)
                    || !Text(kind, 1, 32)) return false;
                RuleMatch definition;
                switch (kind.GetString())
                {
                    case "exact_url":
                        if (!Object(match, "kind", "value") || !Text(match.GetProperty("value"), 1, 4096)) return false;
                        string url = match.GetProperty("value").GetString()!;
                        if (!(url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal))) return false;
                        if (!PayloadValidator.TryHttp(url, out _)) { failure = PolicyFailure.Semantics; return false; }
                        definition = new ExactUrlMatch(url); break;
                    case "exact_host":
                        if (!Object(match, "kind", "host", "scheme", "effective_port") || !Text(match.GetProperty("host"), 1, 253)
                            || !Choice(match.GetProperty("scheme"), "http", "https")
                            || !Integer(match.GetProperty("effective_port"), 1, 65535, out long port)) return false;
                        string host = match.GetProperty("host").GetString()!;
                        if (host.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.'))
                            || !PayloadValidator.TryAsciiHost(host, out string ascii) || ascii != host) return false;
                        // Apply the same authority contract as live URLs (including noncanonical numeric hosts).
                        if (!PayloadValidator.TryHttp($"{match.GetProperty("scheme").GetString()}://{host}:{port}/", out _))
                        { failure = PolicyFailure.Semantics; return false; }
                        definition = new ExactHostMatch(host, match.GetProperty("scheme").GetString()!, (int)port); break;
                    case "payload_sha256":
                        if (!Object(match, "kind", "value") || !Text(match.GetProperty("value"), 64, 64)
                            || !Hash.IsMatch(match.GetProperty("value").GetString()!)) return false;
                        definition = new PayloadHashMatch(match.GetProperty("value").GetString()!); break;
                    default: return false;
                }
                rules.Add(new(id, rule.GetProperty("effect").GetString() == "deny" ? RuleEffect.Deny : RuleEffect.Allow, expires, definition));
            }
            snapshot = new(root.GetProperty("policy_id").GetString()!, revision,
                root.GetProperty("unknown_action").GetString() == "block" ? UnknownAction.Block : UnknownAction.WarnThenOpen, rules.ToImmutable());
            failure = PolicyFailure.None; return true;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or EncoderFallbackException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private static bool Object(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) return false;
        return found.Count == names.Length;
    }
    private static bool Text(JsonElement value, int min, int max)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        string text = value.GetString()!;
        // Schema string lengths count Unicode scalar values, not UTF-16 code units.
        int length = text.EnumerateRunes().Count();
        return length >= min && length <= max && !text.Contains('\uFFFD');
    }
    private static bool Id(JsonElement value) => Text(value, 1, 64) && Identifier.IsMatch(value.GetString()!);
    private static bool Choice(JsonElement value, params string[] values) => value.ValueKind == JsonValueKind.String && values.Contains(value.GetString(), StringComparer.Ordinal);
    private static bool Integer(JsonElement value, long min, long max, out long number)
    {
        number = 0;
        if (value.ValueKind != JsonValueKind.Number) return false;
        if (!value.TryGetInt64(out number))
        {
            if (!value.TryGetDecimal(out decimal integral) || decimal.Truncate(integral) != integral || integral < min || integral > max) return false;
            number = (long)integral;
        }
        return number >= min && number <= max;
    }
}

public interface IPolicyProvider
{
    PolicySnapshot Current { get; }
    T UseCurrent<T>(Func<PolicySnapshot, T> action);
}

public sealed class LocalPolicyProvider(IEventSink events, IClock clock) : IPolicyProvider
{
    private readonly object _gate = new();
    private PolicySnapshot _current = PolicySnapshot.Baseline();
    private byte[]? _contentDigest;
    public PolicyFailure LastFailure { get; private set; }
    public PolicySnapshot Current { get { lock (_gate) return _current; } }
    public T UseCurrent<T>(Func<PolicySnapshot, T> action) { lock (_gate) return action(_current); }

    public bool TryReplace(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > PolicyValidator.MaxFileBytes) { Reject(PolicyFailure.Size); return false; }
        byte[] owned = bytes.ToArray();
        try
        {
            byte[] digest = SHA256.HashData(owned);
            lock (_gate)
            {
                if (_contentDigest is not null && CryptographicOperations.FixedTimeEquals(digest, _contentDigest))
                { LastFailure = PolicyFailure.None; CryptographicOperations.ZeroMemory(digest); return true; }
            }
            if (!PolicyValidator.TryValidate(owned, out PolicySnapshot? snapshot, out PolicyFailure failure))
            { CryptographicOperations.ZeroMemory(digest); Reject(failure); return false; }
            lock (_gate)
            {
                LastFailure = PolicyFailure.None;
                if (_contentDigest is not null) CryptographicOperations.ZeroMemory(_contentDigest);
                _contentDigest = digest; _current = snapshot!;
                events.Record(new(EventCode.PolicyAccepted, clock.MonotonicTicks)); return true;
            }
        }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }

    // Reads at most MaxFileBytes + 1, also for streams whose length changes while being read.
    public bool TryLoad(Stream stream)
    {
        byte[] buffer = new byte[PolicyValidator.MaxFileBytes + 1];
        try
        {
            int length = 0, read;
            while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) != 0) length += read;
            return TryReplace(buffer.AsSpan(0, length));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { Reject(PolicyFailure.Read); return false; }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    public void Reject(PolicyFailure failure)
    { lock (_gate) { LastFailure = failure; events.Record(new(EventCode.PolicyRejected, clock.MonotonicTicks)); } }
}

public readonly record struct PolicyDecision(DecisionState State)
{
    public string Label => State switch
    {
        DecisionState.CompanyApproved => "Company approved", DecisionState.Unverified => "Unverified",
        DecisionState.Blocked => "Blocked", DecisionState.Sensitive => "Sensitive",
        DecisionState.Unsupported => "Unsupported", _ => "Unable to decode"
    };
    public bool CopyEnabled => State is DecisionState.CompanyApproved or DecisionState.Unverified;
    public bool OpenEnabled(PolicySnapshot policy) => State == DecisionState.CompanyApproved
        || (State == DecisionState.Unverified && policy.UnknownAction == UnknownAction.WarnThenOpen);
}

public interface IPolicyEvaluator
{
    PolicyDecision Evaluate(TypedPayload payload, ReadOnlySpan<byte> originalBytes, PolicySnapshot policy, DateTimeOffset utcNow);
}

public sealed class PolicyEvaluator : IPolicyEvaluator
{
    public PolicyDecision Evaluate(TypedPayload payload, ReadOnlySpan<byte> originalBytes, PolicySnapshot policy, DateTimeOffset utcNow)
    {
        if (payload.Kind != PayloadKind.HttpUrl || payload.Target is null)
            return new(payload.Kind switch { PayloadKind.Sensitive => DecisionState.Sensitive,
                PayloadKind.Undecodable => DecisionState.UnableToDecode, _ => DecisionState.Unsupported });
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(originalBytes, hash);
        try
        {
            // All active denies precede every allow, regardless of file ordering.
            foreach (RuleEffect effect in new[] { RuleEffect.Deny, RuleEffect.Allow })
                foreach (PolicyRule rule in policy.Rules)
                    if (rule.Effect == effect && rule.ExpiresAt > utcNow && Matches(rule.Match, payload.Target, hash))
                        return new(effect == RuleEffect.Deny ? DecisionState.Blocked : DecisionState.CompanyApproved);
            return new(DecisionState.Unverified);
        }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }
    private static bool Matches(RuleMatch match, ValidatedHttpTarget target, ReadOnlySpan<byte> hash) => match switch
    {
        ExactUrlMatch url => string.Equals(url.Value, target.OriginalText, StringComparison.Ordinal),
        ExactHostMatch host => host.Host == target.AsciiHost && host.Scheme == target.Scheme && host.EffectivePort == target.EffectivePort,
        PayloadHashMatch payload => string.Equals(payload.Value, Convert.ToHexStringLower(hash), StringComparison.Ordinal),
        _ => false
    };
}
