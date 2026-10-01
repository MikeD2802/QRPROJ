using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QrGuard.Core;

namespace QrGuard.P1.Contracts;

internal static class Program
{
    private static int _passed;
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Example = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "policy.example.json"));
    private static readonly PolicyEvaluator Evaluator = new();

    public static int Main()
    {
        (string Payload, DecisionState State)[] fixtures =
        [
            ("https://benefits.corp.example/enroll", DecisionState.CompanyApproved),
            ("https://benefits.corp.example/enroll?token=demo", DecisionState.Unverified),
            ("https://help.corp.example/docs", DecisionState.CompanyApproved),
            ("https://help.corp.example/deny-test", DecisionState.Blocked),
            ("https://help.corp.example.evil.test/docs", DecisionState.Unverified),
            ("http://help.corp.example/docs", DecisionState.Unverified),
            ("https://help.corp.example:8443/docs", DecisionState.Unverified),
            ("https://user:demo@help.corp.example/docs", DecisionState.Unsupported),
            ("javascript:alert(1)", DecisionState.Unsupported),
            ("otpauth://totp/Demo?secret=SYNTHETICONLY", DecisionState.Sensitive)
        ];
        for (int i = 0; i < fixtures.Length; i++)
        {
            var fixture = fixtures[i];
            Check($"architecture fixture {i + 1:00}", () => Assert(Decision(fixture.Payload, Example) == fixture.State));
        }
        Check("all six mask labels are truthful", () =>
        {
            Assert(Enum.GetValues<DecisionState>().Select(s => new PolicyDecision(s).Label).SequenceEqual(
                new[] { "Company approved", "Unverified", "Blocked", "Sensitive", "Unsupported", "Unable to decode" }));
        });
        Check("deny precedes allow in either file order", () =>
        {
            var allow = Rule(Host("help.corp.example"), "allow", "a");
            var deny = Rule(Url("https://help.corp.example/deny-test"), "deny", "d");
            foreach (var policy in new[] { Policy(allow, deny), Policy(deny, allow) })
                Assert(Decision("https://help.corp.example/deny-test", policy) == DecisionState.Blocked);
        });
        Check("expiry ignores rules at the exact UTC boundary", () =>
        {
            byte[] policy = Policy(Rule(Host("help.corp.example"), "allow", expires: "2026-10-01T00:00:00Z"));
            Assert(Decision("https://help.corp.example/docs", policy) == DecisionState.Unverified);
            Assert(Decision("https://help.corp.example/docs", policy, Now.AddTicks(-1)) == DecisionState.CompanyApproved);
        });
        Check("expired deny no longer overrides an active allow", () =>
        {
            byte[] policy = Policy(Rule(Host("help.corp.example"), "allow", "a"), Rule(Host("help.corp.example"), "deny", "d", "2026-10-01T00:00:00Z"));
            Assert(Decision("https://help.corp.example/docs", policy) == DecisionState.CompanyApproved);
        });
        Check("exact URL does not normalize complete original text", () =>
        {
            const string url = "https://benefits.corp.example/Enroll/%2F?a=1&b=2#Frag";
            byte[] policy = Policy(Rule(Url(url)));
            Assert(Decision(url, policy) == DecisionState.CompanyApproved);
            foreach (string variant in new[] { url.Replace("Enroll", "enroll"), url.Replace("%2F", "%2f"), url.Replace("a=1&b=2", "b=2&a=1"),
                url.Replace("#Frag", ""), url.Replace("#Frag", "#frag"), url.Replace("https://", "HTTPS://"), url.Replace("example/", "example:443/") })
                Assert(Decision(variant, policy) == DecisionState.Unverified);
            Assert(Decision("https://benefits.corp.example", Policy(Rule(Url("https://benefits.corp.example/")))) == DecisionState.Unverified);
        });
        Check("exact host respects label boundaries and effective scheme/port", () =>
        {
            foreach (string url in new[] { "https://help.corp.example/docs", "https://HELP.CORP.EXAMPLE/docs", "https://help.corp.example:443/docs" })
                Assert(Decision(url, Example) == DecisionState.CompanyApproved);
            foreach (string url in new[] { "https://help.corp.example.evil.test/docs", "https://x.help.corp.example/docs", "https://nothelp.corp.example/docs", "http://help.corp.example/docs", "https://help.corp.example:8443/docs" })
                Assert(Decision(url, Example) == DecisionState.Unverified);
            Assert(Decision("http://help.corp.example:443/docs", Policy(Rule(Host("help.corp.example", "http", 443)))) == DecisionState.CompanyApproved);
        });
        Check("IDNA host matches canonical ASCII only and displays it prominently", () =>
        {
            byte[] policy = Policy(Rule(Host("xn--bcher-kva.example")));
            Assert(Decision("https://bücher.example/docs", policy) == DecisionState.CompanyApproved);
            Assert(Decision("https://xn--bcher-kva.example/docs", policy) == DecisionState.CompanyApproved);
            Assert(PayloadValidator.Validate(Bytes("https://bücher.example/docs")).Target!.AsciiHost == "xn--bcher-kva.example");
            Assert(!Valid(Policy(Rule(Host("bücher.example")))));
            Assert(!Valid(Policy(Rule(Host("xn--.example")))));
        });
        Check("payload hash uses original bytes, never a canonical URI", () =>
        {
            const string url = "https://benefits.corp.example/enroll";
            string hash = Convert.ToHexStringLower(SHA256.HashData(Bytes(url)));
            Assert(hash == "c715fd175f63ca1c1e9f590c8ba91de1801c237d691d9bc958f1adf2cd3eb163");
            byte[] policy = Policy(Rule(Hash(url)));
            Assert(Decision(url, policy) == DecisionState.CompanyApproved);
            foreach (string variant in new[] { url + "?token=demo", url.Replace("https", "HTTPS"), url.Replace("example/", "example:443/"), url.Replace("enroll", "%65nroll") })
                Assert(Decision(variant, policy) == DecisionState.Unverified);
        });
        Check("UTF-8 is strict, including overlong/truncated/surrogate encodings", () =>
        {
            foreach (byte[] bytes in new byte[][] { [0xff], [0xc0, 0xaf], [0xe2, 0x82], [0xed, 0xa0, 0x80], [0xef, 0xbb, 0xbf, 104, 116, 116, 112] })
                Assert(PayloadValidator.Validate(bytes).Kind == PayloadKind.Unsupported);
        });
        Check("unpaired UTF-16 in a policy URL is rejected without throwing", () =>
        {
            Assert(!PayloadValidator.TryHttp("https://example.test/\ud800", out _));
            byte[] escaped = Bytes(Encoding.UTF8.GetString(Example).Replace("synthetic-demo-only", "\\ud800", StringComparison.Ordinal));
            Assert(!Valid(escaped));
        });
        Check("malformed and ambiguous HTTP URLs never launch", () =>
        {
            foreach (string url in new[] { "https://", "https:///help.corp.example", "https:help.corp.example", "//help.corp.example", "https://help.corp.example\\evil",
                "https://help.corp.example/%GG", "https://help.corp.example/%", "https://help.corp.example/%00", "https://help.corp.example/%0a", "https://help.corp.example/%5c",
                "https://help.corp.example/a\nb", "https://help.corp.example/ a", "https://u:p@help.corp.example", "https://@help.corp.example", "https://help%2Ecorp.example",
                "https://127.1/", "https://2130706433/", "https://0x7f000001/", "https://9999999999999999999999/", "https://host.123/", "https://host.0x123/", "https://help.corp.example:0443/", "https://help.corp.example:0/", "https://help.corp.example:65536/",
                "https://help.corp.example./", "https://help。corp.example/", "https://help.corp.example/\u202Etxt", "https://[::1]evil/", "https://help.corp.example:/" })
                Assert(PayloadValidator.Validate(Bytes(url)).Kind == PayloadKind.Unsupported);
        });
        Check("standard IPv4 and IPv6 URLs remain launchable", () =>
        {
            foreach (string url in new[] { "https://127.0.0.1:8443/path", "http://[::1]/path", "https://[2001:db8::1]:8443/path" })
                Assert(PayloadValidator.Validate(Bytes(url)).Kind == PayloadKind.HttpUrl);
        });
        Check("non-URL and unsupported schemes remain unsupported", () =>
        {
            foreach (string value in new[] { "plain text", "file:///C:/demo", "mailto:a@example.test", "data:text/html,demo", "javascript:alert(1)", "https+custom://example.test/" })
                Assert(PayloadValidator.Validate(Bytes(value)).Kind == PayloadKind.Unsupported);
        });
        Check("sensitive URI and structured payload types are classified first", () =>
        {
            foreach (string value in new[] { "otpauth://totp/Demo?secret=SYNTHETICONLY", "WIFI:T:WPA;S:demo;P:demo;;", "DPP:K:DEMO;;", "FIDO:/demo", "BEGIN:VCARD\nFN:Demo\nEND:VCARD", "openid4vp://demo" })
                Assert(PayloadValidator.Validate(Bytes(value)).Kind == PayloadKind.Sensitive);
        });
        Check("schema enforces required root fields and rejects additional properties", () =>
        {
            foreach (string name in new[] { "schema_version", "policy_id", "revision", "unknown_action", "rules" })
            { JsonObject node = JsonNode.Parse(Example)!.AsObject(); node.Remove(name); Assert(!Valid(Serialize(node))); }
            JsonObject extra = JsonNode.Parse(Example)!.AsObject(); extra["extra"] = true; Assert(!Valid(Serialize(extra)));
        });
        Check("schema rejects wrong root types, enums, bounds and duplicate properties", () =>
        {
            foreach (string text in new[] { "[]", "null", "{}", "{\"schema_version\":1,\"schema_version\":1,\"policy_id\":\"x\",\"revision\":1,\"unknown_action\":\"block\",\"rules\":[]}" })
                Assert(!Valid(Bytes(text)));
            foreach (var mutation in new (string Key, JsonNode? Value)[] { ("schema_version", JsonValue.Create(2)), ("revision", JsonValue.Create(0)),
                ("revision", JsonValue.Create(1.5)), ("revision", JsonValue.Create("1")), ("policy_id", JsonValue.Create("bad id")),
                ("policy_id", JsonValue.Create(new string('a', 65))), ("unknown_action", JsonValue.Create("safe")), ("rules", JsonValue.Create("rules")) })
            { JsonObject node = JsonNode.Parse(Example)!.AsObject(); node[mutation.Key] = mutation.Value?.DeepClone(); Assert(!Valid(Serialize(node))); }
        });
        Check("rule schema validates every required field and additional properties", () =>
        {
            foreach (string name in new[] { "id", "effect", "owner", "reason", "expires_at", "match" })
            { var rule = Rule(Url("https://example.test/")); rule.Remove(name); Assert(!Valid(Policy(rule))); }
            var extra = Rule(Url("https://example.test/")); extra["extra"] = "secret"; Assert(!Valid(Policy(extra)));
            foreach (var change in new (string Key, JsonNode? Value)[] { ("id", JsonValue.Create("")), ("effect", JsonValue.Create("permit")),
                ("owner", JsonValue.Create("")), ("owner", JsonValue.Create(new string('a', 129))), ("reason", JsonValue.Create(new string('a', 513))), ("match", null) })
            { var rule = Rule(Url("https://example.test/")); rule[change.Key] = change.Value?.DeepClone(); Assert(!Valid(Policy(rule))); }
        });
        Check("integral JSON numbers obey schema integer semantics", () =>
        {
            string json = Encoding.UTF8.GetString(Policy());
            Assert(Valid(Bytes(json.Replace("\"revision\":1", "\"revision\":1.0", StringComparison.Ordinal))));
            Assert(Valid(Bytes(json.Replace("\"revision\":1", "\"revision\":1e0", StringComparison.Ordinal))));
        });
        Check("rule IDs must be unique", () => Assert(!Valid(Policy(Rule(Host("example.test")), Rule(Url("https://example.test/"))))));
        Check("expiry must be a real UTC date-time ending in Z", () =>
        {
            foreach (string expiry in new[] { "2027-02-29T00:00:00Z", "2026-13-01T00:00:00Z", "2027-10-01", "2027-10-01T00:00:00+00:00", "2027-10-01T00:00:00-04:00", "2027-10-01T24:00:00Z", "notZ" })
                Assert(!Valid(Policy(Rule(Host("example.test"), expires: expiry))));
            Assert(Valid(Policy(Rule(Host("example.test"), expires: "2028-02-29T01:02:03.1234567Z"))));
        });
        Check("match definitions enforce exactly one supported match shape", () =>
        {
            foreach (JsonObject match in new[] { new JsonObject { ["kind"] = "suffix", ["value"] = "example.test" },
                new JsonObject { ["kind"] = "exact_url", ["value"] = "https://example.test/", ["host"] = "example.test" },
                new JsonObject { ["kind"] = "exact_host", ["host"] = "Example.test", ["scheme"] = "https", ["effective_port"] = 443 },
                Host("example.test", "ftp"), Host("example.test", port: 0), Host("example.test", port: 65536),
                new JsonObject { ["kind"] = "payload_sha256", ["value"] = new string('A', 64) },
                new JsonObject { ["kind"] = "payload_sha256", ["value"] = new string('a', 63) } })
                Assert(!Valid(Policy(Rule(match))));
        });
        Check("rule URLs also require semantic validation", () =>
        {
            foreach (string value in new[] { "https://u:p@example.test/", "https://", "https://example.test/%ZZ", "https://example.test/a\nb", "javascript:alert(1)" })
                Assert(!Valid(Policy(Rule(Url(value)))));
        });
        Check("file bytes and total rule count are bounded before acceptance", () =>
        {
            Assert(!Valid(new byte[PolicyValidator.MaxFileBytes + 1]));
            Assert(!Valid([])); Assert(!Valid([0xff]));
            Assert(!Valid(Policy(Enumerable.Range(0, 1001).Select(i => Rule(Host("example.test"), id: "r" + i)).ToArray())));
            Assert(Valid(Policy(Enumerable.Range(0, 1000).Select(i => Rule(Host("example.test"), id: "r" + i)).ToArray())));
        });
        Check("invalid replacement retains the last validated snapshot", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Example)); Guid token = rig.Provider.Current.Token;
            Assert(!rig.Provider.TryReplace(Bytes("{broken"))); Assert(rig.Provider.Current.Token == token && !rig.Provider.Current.IsBaseline);
            Assert(Decision("https://help.corp.example/docs", rig.Provider.Current) == DecisionState.CompanyApproved);
        });
        Check("no valid policy uses the built-in Unverified warn baseline", () =>
        {
            using var rig = new Rig(); Assert(!rig.Provider.TryReplace(Bytes("[]"))); rig.Apply("https://example.test/");
            Assert(rig.Provider.Current.IsBaseline && rig.Details.Label == "Unverified" && rig.Details.CanOpen);
            Assert(rig.Actions.Open(rig.Details.Ticket) == ActionOutcome.WarningRequired);
        });
        Check("bounded stream loader rejects oversize even without a reported length", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Example)); Guid token = rig.Provider.Current.Token;
            using var stream = new CountingStream(PolicyValidator.MaxFileBytes + 100);
            Assert(!rig.Provider.TryLoad(stream)); Assert(stream.ReadBytes == PolicyValidator.MaxFileBytes + 1 && rig.Provider.Current.Token == token);
            using var valid = new MemoryStream(Example); Assert(rig.Provider.TryLoad(valid));
        });
        Check("details and policy evaluation have no automatic browser or clipboard effects", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Example)); rig.Apply("https://help.corp.example/docs");
            _ = rig.Details; rig.Tracks.Evaluate(Evaluator, rig.Provider.Current, Now);
            Assert(rig.Adapter.Opens == 0 && rig.Adapter.Copies == 0);
        });
        Check("explicit approved Open and Copy receive the original validated HTTP target", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Policy(Rule(Host("example.test")))));
            const string url = "https://EXAMPLE.test:443/A%2f?token=DEMO#private"; rig.Apply(url);
            ActionTicket ticket = rig.Details.Ticket;
            Assert(rig.Actions.Open(ticket) == ActionOutcome.Completed && rig.Adapter.Last == url && rig.Adapter.Opens == 1);
            Assert(rig.Actions.Copy(ticket) == ActionOutcome.Completed && rig.Adapter.Last == url && rig.Adapter.Copies == 1);
        });
        Check("Unverified Open requires warning confirmation; Copy requires its own action", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            Assert(rig.Actions.Open(ticket) == ActionOutcome.WarningRequired && rig.Adapter.Opens == 0);
            Assert(rig.Actions.Copy(ticket) == ActionOutcome.Completed && rig.Adapter.Copies == 1 && rig.Adapter.Opens == 0);
            Assert(rig.Actions.Open(ticket, true) == ActionOutcome.Completed && rig.Adapter.Opens == 1);
        });
        Check("warning confirmation must follow an issued warning and can be used only once", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            Assert(rig.Actions.Open(ticket, true) == ActionOutcome.Refused);
            Assert(rig.Actions.Open(ticket) == ActionOutcome.WarningRequired);
            rig.Actions.CancelWarning(ticket); Assert(rig.Actions.Open(ticket, true) == ActionOutcome.Refused);
            Assert(rig.Actions.Open(ticket) == ActionOutcome.WarningRequired && rig.Actions.Open(ticket, true) == ActionOutcome.Completed);
            Assert(rig.Actions.Open(ticket, true) == ActionOutcome.Refused && rig.Adapter.Opens == 1);
        });
        Check("unknown_action block prevents navigation even with confirmation", () =>
        {
            using var rig = new Rig(); JsonObject node = JsonNode.Parse(Policy())!.AsObject(); node["unknown_action"] = "block";
            Assert(rig.Provider.TryReplace(Serialize(node))); rig.Apply("https://example.test/");
            Assert(rig.Details.Label == "Unverified" && !rig.Details.CanOpen);
            Assert(rig.Actions.Open(rig.Details.Ticket, true) == ActionOutcome.Refused && rig.Adapter.Opens == 0);
        });
        Check("blocked actions are refused in the controller, not just disabled buttons", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Example)); rig.Apply("https://help.corp.example/deny-test");
            Assert(!rig.Details.CanOpen && !rig.Details.CanCopy);
            Assert(rig.Actions.Open(rig.Details.Ticket, true) == ActionOutcome.Refused && rig.Actions.Copy(rig.Details.Ticket) == ActionOutcome.Refused);
            Assert(rig.Adapter.Opens == 0 && rig.Adapter.Copies == 0);
        });
        Check("hash allows never enable sensitive, unsupported or undecodable actions", () =>
        {
            foreach (string value in new[] { "otpauth://totp/Demo?secret=SYNTHETICONLY", "WIFI:S:DEMO;P:SECRET;;", "javascript:alert(1)", "plain text", "https://u:p@example.test/" })
            {
                using var rig = new Rig(); Assert(rig.Provider.TryReplace(Policy(Rule(Hash(value))))); rig.Apply(value);
                Assert(!rig.Details.CanOpen && !rig.Details.CanCopy && rig.Details.Destination == "Payload hidden");
                Assert(rig.Actions.Open(rig.Details.Ticket, true) == ActionOutcome.Refused && rig.Actions.Copy(rig.Details.Ticket) == ActionOutcome.Refused);
                Assert(rig.Adapter.Opens == 0 && rig.Adapter.Copies == 0);
            }
            using var missing = new Rig(); missing.Apply("https://example.test/", decoded: false);
            Assert(missing.Details.Label == "Unable to decode" && missing.Actions.Copy(missing.Details.Ticket) == ActionOutcome.Refused);
        });
        Check("invalid UTF-8 remains non-launchable despite a matching allow hash", () =>
        {
            using var rig = new Rig(); byte[] bytes = [0xff, 0xfe];
            Assert(rig.Provider.TryReplace(Policy(Rule(new JsonObject { ["kind"] = "payload_sha256", ["value"] = Convert.ToHexStringLower(SHA256.HashData(bytes)) }))));
            rig.Apply(bytes); Assert(rig.Details.Label == "Unsupported");
            Assert(rig.Actions.Open(rig.Details.Ticket, true) == ActionOutcome.Refused && rig.Actions.Copy(rig.Details.Ticket) == ActionOutcome.Refused);
        });
        Check("default details redact query/fragment values and neutralize bidi", () =>
        {
            using var rig = new Rig(); rig.Apply("https://bücher.example/שלום?token=HIDDEN_QUERY#HIDDEN_FRAGMENT");
            Assert(rig.Details.AsciiHost == "xn--bcher-kva.example" && rig.Details.Destination.All(c => c is >= ' ' and <= '~'));
            Assert(!rig.Details.Destination.Contains("HIDDEN_QUERY") && !rig.Details.Destination.Contains("HIDDEN_FRAGMENT"));
            Assert(DisplaySafety.Ascii("a\u202Eb") == "a\\u202Eb");
            Assert(PayloadValidator.Validate(Bytes("https://example.test/path#HIDDEN?query=HIDDEN")).Target!.RedactedDisplay() == "https://example.test/path#[fragment redacted]");
        });
        Check("same-location replacement invalidates pending warning, Open and Copy", () =>
        {
            using var rig = new Rig(); rig.Apply("https://old.example/"); ActionTicket old = rig.Details.Ticket;
            Assert(rig.Actions.Open(old) == ActionOutcome.WarningRequired);
            rig.Apply("https://new.example/"); Assert(!rig.Actions.IsCurrent(old));
            Assert(rig.Actions.Open(old, true) == ActionOutcome.ChangedOrStale && rig.Actions.Copy(old) == ActionOutcome.ChangedOrStale);
            Assert(rig.Details.AsciiHost == "new.example" && rig.Adapter.Opens == 0 && rig.Adapter.Copies == 0);
        });
        Check("geometry changes invalidate pending actions even when payload is identical", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket old = rig.Details.Ticket;
            rig.Apply("https://example.test/", bounds: new(50, 40, 100, 100));
            Assert(rig.Actions.Open(old, true) == ActionOutcome.ChangedOrStale);
        });
        Check("retirement and stale callbacks cannot launch an earlier destination", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket old = rig.Details.Ticket;
            using var empty = new DecodeBatch([], false); Assert(rig.Tracks.Apply(rig.Identity(++rig.Frame), empty, 640, 480, 1000, 1000));
            using var late = Batch(Bytes("https://example.test/")); Assert(!rig.Tracks.Apply(rig.Identity(1), late, 640, 480, 1000, 1000));
            Assert(rig.Actions.Open(old, true) == ActionOutcome.ChangedOrStale && rig.Actions.Copy(old) == ActionOutcome.ChangedOrStale);
        });
        Check("session, monitor, generation, track and revision tuples are all rechecked", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            foreach (TrackIdentity changed in new[] { ticket.Track with { SessionId = Guid.NewGuid() }, ticket.Track with { MonitorId = 2 },
                ticket.Track with { DisplayGeneration = 2 }, ticket.Track with { TrackId = 999 }, ticket.Track with { GeometryRevision = 2 }, ticket.Track with { PayloadRevision = 2 } })
                Assert(rig.Actions.Open(ticket with { Track = changed }, true) == ActionOutcome.ChangedOrStale);
            Assert(rig.Actions.Copy(ticket with { PolicyToken = Guid.NewGuid() }) == ActionOutcome.ChangedOrStale && rig.Adapter.Opens == 0);
        });
        Check("actions require fresh observations even before masks reach their retirement limit", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            rig.Clock.MonotonicTicks = 1501; Assert(!rig.Tracks.Expire(1501, 1000));
            Assert(rig.Actions.Open(ticket, true) == ActionOutcome.ChangedOrStale && rig.Actions.Copy(ticket) == ActionOutcome.ChangedOrStale);
            rig.Clock.MonotonicTicks = 999; Assert(!rig.Actions.IsCurrent(ticket));
        });
        Check("policy edits invalidate pending actions even when the numeric revision is reused", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Policy(Rule(Host("example.test"))))); rig.Apply("https://example.test/");
            ActionTicket old = rig.Details.Ticket; long revision = rig.Provider.Current.Revision;
            Assert(rig.Provider.TryReplace(Policy(Rule(Host("example.test"), "deny"))) && rig.Provider.Current.Revision == revision);
            Assert(!rig.Actions.IsCurrent(old) && rig.Actions.Open(old) == ActionOutcome.ChangedOrStale && rig.Actions.Copy(old) == ActionOutcome.ChangedOrStale);
            Assert(rig.Details.Label == "Blocked" && rig.Adapter.Opens == 0);
        });
        Check("identical reload retains snapshot identity; invalid edit does not replace it", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Example)); rig.Apply("https://help.corp.example/docs");
            ActionTicket old = rig.Details.Ticket; Assert(rig.Provider.TryReplace(Example) && rig.Provider.Current.Token == old.PolicyToken);
            Assert(!rig.Provider.TryReplace(Bytes("{}")) && rig.Actions.IsCurrent(old));
            Assert(rig.Actions.Open(old) == ActionOutcome.Completed);
        });
        Check("action-time expiry removes approvals and requires a warning", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Policy(Rule(Host("example.test"), expires: "2026-10-01T00:00:01Z"))));
            rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            Assert(rig.Details.Label == "Company approved"); rig.Clock.UtcNow = Now.AddSeconds(1);
            Assert(rig.Actions.Open(ticket) == ActionOutcome.WarningRequired && rig.Adapter.Opens == 0);
        });
        Check("expired approval under unknown_action block cannot be opened", () =>
        {
            using var rig = new Rig(); var policy = JsonNode.Parse(Policy(Rule(Host("example.test"), expires: "2026-10-01T00:00:01Z")))!.AsObject();
            policy["unknown_action"] = "block"; Assert(rig.Provider.TryReplace(Serialize(policy)));
            rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket; rig.Clock.UtcNow = Now.AddSeconds(1);
            Assert(rig.Actions.Open(ticket) == ActionOutcome.Refused && rig.Adapter.Opens == 0);
        });
        Check("stationary identical observations keep actions current but never bypass warnings", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            rig.Clock.MonotonicTicks = 1400; rig.Apply("https://example.test/"); rig.Clock.MonotonicTicks = 1600;
            Assert(rig.Actions.IsCurrent(ticket) && rig.Actions.Open(ticket) == ActionOutcome.WarningRequired);
        });
        Check("mask labels reevaluate rule expiry and UTC clock rollback", () =>
        {
            using var rig = new Rig(); Assert(rig.Provider.TryReplace(Policy(Rule(Host("example.test"), expires: "2026-10-01T00:00:01Z"))));
            rig.Apply("https://example.test/"); rig.Tracks.Evaluate(Evaluator, rig.Provider.Current, Now);
            Assert(rig.Tracks.Snapshot[0].State == DecisionState.CompanyApproved);
            rig.Tracks.Evaluate(Evaluator, rig.Provider.Current, Now.AddSeconds(1)); Assert(rig.Tracks.Snapshot[0].State == DecisionState.Unverified);
            rig.Tracks.Evaluate(Evaluator, rig.Provider.Current, Now); Assert(rig.Tracks.Snapshot[0].State == DecisionState.CompanyApproved);
        });
        Check("stop/clear and disposal invalidate every pending action", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); ActionTicket ticket = rig.Details.Ticket;
            rig.Tracks.Clear(); Assert(rig.Actions.Open(ticket, true) == ActionOutcome.ChangedOrStale);
            rig.Apply("https://example.test/"); ticket = rig.Details.Ticket; rig.Tracks.Dispose();
            Assert(rig.Actions.Copy(ticket) == ActionOutcome.ChangedOrStale);
        });
        Check("owned original bytes survive observation release and are cleared on every lifecycle boundary", () =>
        {
            foreach (string boundary in new[] { "replace", "retire", "clear", "dispose", "expire" })
            {
                using var rig = new Rig(); byte[] original = Bytes("https://example.test/?token=TRANSIENT"); rig.Apply(original);
                byte[] owned = OwnedTrackBytes(rig.Tracks); Assert(owned.SequenceEqual(original) && !ReferenceEquals(owned, original));
                Array.Clear(original); Assert(owned.Any(b => b != 0));
                if (boundary == "replace") rig.Apply("https://replacement.test/");
                else if (boundary == "retire") { using var empty = new DecodeBatch([], false); rig.Tracks.Apply(rig.Identity(++rig.Frame), empty, 640, 480, 1000, 1000); }
                else if (boundary == "dispose") rig.Tracks.Dispose();
                else if (boundary == "expire") rig.Tracks.Expire(1751, 1000);
                else rig.Tracks.Clear();
                Assert(owned.All(b => b == 0));
            }
        });
        Check("latest-result slot clears discarded original payload buffers", () =>
        {
            using var slot = new LatestSlot<DecodeBatch>(); byte[] a = [1, 2, 3], b = [4, 5, 6];
            slot.Publish(Batch(a)); slot.Publish(Batch(b)); Assert(a.All(v => v == 0));
            slot.Dispose(); Assert(b.All(v => v == 0)); byte[] c = [7]; slot.Publish(Batch(c)); Assert(c[0] == 0);
        });
        Check("track payload and observation limits bound live owned memory", () =>
        {
            using var rig = new Rig();
            using var batch = new DecodeBatch(Enumerable.Range(0, Limits.MaxObservations).Select(i => new QrObservation(new(i * 30, 0, 21, 21), new byte[Limits.MaxPayloadBytes], true)).ToArray(), false);
            Assert(rig.Tracks.Apply(rig.Identity(++rig.Frame), batch, 640, 480, 1000, 1000));
            Assert(rig.Tracks.Snapshot.Count == Limits.MaxObservations);
            Assert(OwnedTrackBuffers(rig.Tracks).Sum(b => b.Length) == Limits.MaxObservations * Limits.MaxPayloadBytes);
        });
        Check("byte-identical full frames coalesce; periodic full discovery remains required", () =>
        {
            using var cache = new BoundedDiscovery(); using var decoder = new FakeDecoder();
            using var frame = Pixels(); using var a = cache.Process(frame, decoder, 1000, 1000, out bool reused);
            Assert(!reused); using var b = cache.Process(frame, decoder, 1200, 1000, out reused); Assert(reused && decoder.Calls == 1);
            using var c = cache.Process(frame, decoder, 2000, 1000, out reused); Assert(!reused && decoder.Calls == 2);
            frame.Pixels[0] = 1; using var changed = cache.Process(frame, decoder, 2200, 1000, out reused); Assert(!reused && decoder.Calls == 3);
        });
        Check("coalesced batch ownership is independent and cached payloads clear on replacement/disposal", () =>
        {
            using var cache = new BoundedDiscovery(); using var decoder = new FakeDecoder(); using var frame = Pixels();
            using var first = cache.Process(frame, decoder, 1000, 1000, out _); byte[] cached = CachedBytes(cache);
            first.Dispose(); Assert(cached.Any(b => b != 0));
            using var reused = cache.Process(frame, decoder, 1200, 1000, out _); Assert(reused.Observations[0].Payload.SequenceEqual(cached));
            frame.Pixels[0] = 1; using var next = cache.Process(frame, decoder, 1300, 1000, out _); Assert(cached.All(b => b == 0));
            cached = CachedBytes(cache); cache.Dispose(); Assert(cached.All(b => b == 0));
        });
        Check("coalescing never reuses geometry across session/display identities or dimensions", () =>
        {
            using var cache = new BoundedDiscovery(); using var decoder = new FakeDecoder(); using var frame = Pixels();
            using var first = cache.Process(frame, decoder, 1000, 1000, out _);
            using var generation = new PixelFrame(frame.Identity with { DisplayGeneration = 2 }, 100, 100, 400, new byte[40000]);
            using var second = cache.Process(generation, decoder, 1200, 1000, out bool reused); Assert(!reused);
            using var changed = new PixelFrame(generation.Identity, 200, 50, 800, new byte[40000]);
            using var third = cache.Process(changed, decoder, 1300, 1000, out reused); Assert(!reused);
        });
        Check("stop clears cached bytes promptly while decoder work finishes and discards the late payload", () =>
        {
            using var cache = new BoundedDiscovery(); using var initial = new FakeDecoder(); using var frame = Pixels();
            using var first = cache.Process(frame, initial, 1000, 1000, out _); byte[] cached = CachedBytes(cache);
            using var blocked = new BlockingDecoder(); frame.Pixels[0] = 1;
            Task work = Task.Run(() => { using var late = cache.Process(frame, blocked, 1200, 1000, out _); });
            try
            {
                Assert(blocked.Started.Wait(1000)); cache.Dispose(); Assert(cached.All(b => b == 0));
            }
            finally { blocked.Resume.Set(); }
            bool disposed = false;
            try { work.GetAwaiter().GetResult(); } catch (ObjectDisposedException) { disposed = true; }
            Assert(disposed && blocked.Payload.All(b => b == 0));
        });
        Check("latest-result bursts retain one pending item and clear all replacements", () =>
        {
            using var slot = new LatestSlot<DecodeBatch>(); byte[]? prior = null;
            for (int i = 0; i < 1000; i++)
            {
                byte[] current = [1, 2, 3]; slot.Publish(Batch(current));
                if (prior is not null) Assert(prior.All(b => b == 0)); prior = current;
            }
            using var only = slot.Take(); Assert(only is not null && slot.Take() is null && only.Observations.Count == 1);
            only!.Dispose(); Assert(prior!.All(b => b == 0));
        });
        Check("diagnostics have a fixed redacted schema and a bounded event queue", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/?token=NEVER_LOG#NEVER_LOG");
            rig.Actions.Copy(rig.Details.Ticket); Assert(!rig.Provider.TryReplace(Bytes("{NEVER_LOG")));
            for (int i = 0; i < 1000; i++) rig.Events.Record(new(EventCode.PolicyRejected, i));
            Assert(rig.Events.Snapshot.Count == BoundedDiagnostics.Capacity && rig.Events.Dropped >= 872);
            string report = JsonSerializer.Serialize(rig.Events.Snapshot);
            Assert(!report.Contains("NEVER_LOG") && !report.Contains("example.test") && !report.Contains("token="));
            Assert(typeof(DiagnosticEvent).GetProperties().All(p => p.PropertyType != typeof(string)));
        });
        Check("adapter exceptions remain redacted and never retry automatically", () =>
        {
            using var rig = new Rig(); rig.Apply("https://example.test/"); rig.Adapter.Throw = true;
            Assert(rig.Actions.Open(rig.Details.Ticket) == ActionOutcome.WarningRequired);
            Assert(rig.Actions.Open(rig.Details.Ticket, true) == ActionOutcome.AdapterFailed && rig.Adapter.Opens == 1);
            Assert(!JsonSerializer.Serialize(rig.Events.Snapshot).Contains("SECRET"));
        });
        Console.WriteLine($"PASS: {_passed} P1 portable contracts. Windows UI/input, compositor, egress and workstation resources were not tested.");
        return 0;
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] Serialize(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node);
    private static bool Valid(byte[] bytes) => PolicyValidator.TryValidate(bytes, out _, out _);
    private static JsonObject Url(string value) => new() { ["kind"] = "exact_url", ["value"] = value };
    private static JsonObject Host(string host, string scheme = "https", int port = 443) => new() { ["kind"] = "exact_host", ["host"] = host, ["scheme"] = scheme, ["effective_port"] = port };
    private static JsonObject Hash(string value) => new() { ["kind"] = "payload_sha256", ["value"] = Convert.ToHexStringLower(SHA256.HashData(Bytes(value))) };
    private static JsonObject Rule(JsonObject match, string effect = "allow", string id = "rule", string expires = "2027-10-01T00:00:00Z") => new()
    { ["id"] = id, ["effect"] = effect, ["owner"] = "synthetic", ["reason"] = "synthetic", ["expires_at"] = expires, ["match"] = match.DeepClone() };
    private static byte[] Policy(params JsonObject[] rules) => Serialize(new JsonObject
    { ["schema_version"] = 1, ["policy_id"] = "fixture", ["revision"] = 1, ["unknown_action"] = "warn_then_open", ["rules"] = new JsonArray(rules.Select(r => r.DeepClone()).ToArray()) });
    private static DecisionState Decision(string text, byte[] bytes, DateTimeOffset? utc = null)
    { Assert(PolicyValidator.TryValidate(bytes, out var policy, out _)); return Decision(text, policy!, utc); }
    private static DecisionState Decision(string text, PolicySnapshot policy, DateTimeOffset? utc = null)
    { byte[] bytes = Bytes(text); return Evaluator.Evaluate(PayloadValidator.Validate(bytes), bytes, policy, utc ?? Now).State; }
    private static DecodeBatch Batch(byte[] bytes) => new([new(new(20, 20, 100, 100), bytes, true)], false);
    private static void Assert(bool value) { if (!value) throw new InvalidOperationException("P1 contract failed."); }
    private static void Check(string label, Action action) { action(); _passed++; Console.WriteLine("PASS " + label); }
    private static byte[][] OwnedTrackBuffers(TrackStore store)
    {
        var tracks = (IEnumerable)typeof(TrackStore).GetField("_tracks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        return tracks.Cast<object>().Select(t =>
        {
            object payload = t.GetType().GetField("Payload")!.GetValue(t)!;
            return (byte[])payload.GetType().GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(payload)!;
        }).ToArray();
    }
    private static byte[] OwnedTrackBytes(TrackStore store) => OwnedTrackBuffers(store).Single();
    private static byte[] CachedBytes(BoundedDiscovery cache) => ((DecodeBatch)typeof(BoundedDiscovery).GetField("_cached", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!).Observations[0].Payload;
    private static PixelFrame Pixels() => new(new(Guid.NewGuid(), 1, 1, 1, 1000), 100, 100, 400, new byte[40000]);

    private sealed class FakeClock : IClock { public long MonotonicTicks { get; set; } = 1000; public long TicksPerSecond => 1000; public DateTimeOffset UtcNow { get; set; } = Now; }
    private sealed class FakeAdapter : ILinkLauncher, IClipboardAdapter
    {
        public int Opens, Copies; public string? Last; public bool Throw;
        public void Open(ValidatedHttpTarget target) { Opens++; Last = target.OriginalText; if (Throw) throw new InvalidOperationException("SECRET"); }
        public void Copy(ValidatedHttpTarget target) { Copies++; Last = target.OriginalText; if (Throw) throw new InvalidOperationException("SECRET"); }
    }
    private sealed class Rig : IDisposable
    {
        public Guid Session = Guid.NewGuid(); public long Frame;
        public FakeClock Clock = new(); public FakeAdapter Adapter = new(); public BoundedDiagnostics Events = new();
        public TrackStore Tracks; public LocalPolicyProvider Provider; public ActionController Actions;
        public Rig() { Tracks = new(Session, 1, 1); Provider = new(Events, Clock); Actions = new(Tracks, Provider, Evaluator, Clock, Adapter, Adapter, Events); }
        public FrameIdentity Identity(long frame) => new(Session, 1, 1, frame, Clock.MonotonicTicks);
        public void Apply(string text, bool decoded = true, PixelRect? bounds = null) => Apply(Bytes(text), decoded, bounds);
        public void Apply(byte[] bytes, bool decoded = true, PixelRect? bounds = null)
        {
            using var batch = new DecodeBatch([new(bounds ?? new(20, 20, 100, 100), bytes.ToArray(), decoded)], false);
            Assert(Tracks.Apply(Identity(++Frame), batch, 640, 480, Clock.MonotonicTicks, Clock.TicksPerSecond));
        }
        public DetailsView Details => Actions.Details(Tracks.Snapshot.Single().TrackId)!;
        public void Dispose() => Tracks.Dispose();
    }
    private sealed class FakeDecoder : IQrDecoder
    { public int Calls; public DecodeBatch Decode(PixelFrame _) { Calls++; return new([new(new(0, 0, 25, 25), Bytes("https://example.test/"), true)], false); } public void Dispose() { } }
    private sealed class BlockingDecoder : IQrDecoder
    {
        public ManualResetEventSlim Started = new(), Resume = new();
        public byte[] Payload = Bytes("https://example.test/?token=TRANSIENT");
        public DecodeBatch Decode(PixelFrame _) { Started.Set(); if (!Resume.Wait(2000)) throw new InvalidOperationException("bounded_fake_timeout"); return new([new(new(0, 0, 25, 25), Payload, true)], false); }
        public void Dispose() { Started.Dispose(); Resume.Dispose(); }
    }
    private sealed class CountingStream(int length) : Stream
    {
        public int ReadBytes;
        public override int Read(byte[] buffer, int offset, int count) { int n = Math.Min(count, length - ReadBytes); Array.Fill(buffer, (byte)' ', offset, n); ReadBytes += n; return n; }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
