# QR Guard: MVP and enterprise architecture

Version: 1.0 · 2026-10-01 · Status: proposed design, ready for a feasibility spike

This is an implementation brief, not evidence that the Windows behavior has been tested. All performance numbers are proposed acceptance targets. Start with `AGENTS.md` and `execution-plan.yaml` when implementation is requested.

| Companion file | Purpose |
|---|---|
| `AGENTS.md` | Development-agent loop, invariants, evidence rules, clarification format |
| `execution-plan.yaml` | Phase dependencies, scope, exit criteria, and gates |
| `policy.schema.json` | Strict local-policy body schema; semantic validation is also required |
| `policy.example.json` | Reserved-domain fixtures illustrating allow rules and deny precedence |

## 1. Product contract

Detect QR codes displayed on a supported Windows desktop, cover their visible pixels, decode their payload locally, and present a readable destination with a policy state. Company administrators can approve legitimate destinations and explicitly deny others.

The first release is a local QR visibility and policy control. It does not determine whether an unknown website is malicious. The enterprise edition adds management and optional reputation services without moving screen processing into the cloud.

Three separate concepts must stay separate in code and UI:

| Concept | Meaning |
|---|---|
| Coverage | Whether capture and masking currently work on this display/session |
| Policy decision | Approved, unverified, blocked, unsupported, or sensitive |
| Reputation evidence | Optional provider observations, including unavailable or no match |

An approved destination is approved by company policy; it is not a guarantee of safety. No match in a reputation service is not a clean bill of health.

### Boundaries that affect the design

- A desktop overlay covers the local display; it does not edit the document, webpage, original image, or printed QR.
- Detection happens after content is rendered. There is a measurable interval before the mask appears. This cannot promise that a QR is never displayed or never scanned.
- Tiny, cropped, obscured, blurred, animated, protected, or unsupported content may escape detection. Publish the tested coverage envelope rather than promising every QR under every condition.
- Secure desktops, lock screens, protected surfaces, exclusive fullscreen, and remote/virtual desktop configurations require separate support decisions. Never bypass Windows protection to capture them.
- Screenshot and screen-sharing behavior is a separate contract. The baseline protects the physical local display. See the mandatory capture/overlay feasibility gate below.

## 2. Local MVP

### Scope and defaults

| Item | MVP decision |
|---|---|
| Platform | Windows 11 x64, interactive local user session |
| Display scope | One explicitly selected monitor; first acceptance fixture is 1920×1080 |
| Applications | Visible content across applications on that monitor; no app-specific scanning |
| Runtime | C# on the supported .NET 10 LTS line, pinned SDK and package versions |
| UI | Small Win32 opaque mask windows; WPF tray and an explicitly opened details panel |
| Capture | Windows Graphics Capture through Win32 monitor interop, subject to P0 verification |
| Decode | Pinned ZXing-C++ .NET binding, QR reading only; no computer-vision model or OpenCV dependency initially |
| Policy | Local versioned JSON, strict schema, explicit allow/deny rules and expiry |
| Network | No background network traffic, DNS lookup, URL fetch, redirects, or TI calls |
| Link action | Explicit user action opens validated HTTP(S) in the default browser |
| Unknown link | Mask retained; show “Unverified — not checked”; warn before opening |
| Approved link | Mask retained; show “Company approved”; explicit Open action |
| Denied link | Mask retained; Open and Copy actions disabled |
| Sensitive/non-URL QR | Mask retained; no automatic launch; secrets are not displayed or logged |
| Diagnostics | Bounded local counters and redacted events; no screenshots or full payload persistence |
| Installation | Manual launch on a test workstation; no administrator rights or service required |

The framework and decoder are recommendations, not an invitation to add multiple frameworks. Verify binding support and pin a stable release during P0. The .NET support and decoder sources are listed in section 6.

### Components

One executable contains separate modules with narrow interfaces. Split processes only where a verified enterprise requirement justifies it.

| Module | Responsibility | Output/boundary |
|---|---|---|
| Capture adapter | Acquire current monitor frames, detect display/session changes, release native buffers | Latest bounded frame and capture health |
| Scheduler | Coalesce updates; select work within CPU and latency budgets | Work item with frame and display generation |
| QR decoder | Locate and decode QR regions in local memory | Geometry, raw bytes, decode result |
| Tracker | Associate observations across fresh frames and invalidate changed content | Track ID, geometry revision, payload revision |
| Policy evaluator | Validate payload and evaluate a policy snapshot deterministically | Decision, rule ID, reason, version |
| Mask renderer | Cover geometry without taking focus; provide a compact state label | Opaque local overlay |
| Details/action controller | Show readable host and state; revalidate explicit Open requests | Default-browser launch or refusal |
| Diagnostics | Count coverage failures, timing, decisions, and resource use | Redacted, bounded local records |

Use injected interfaces for capture, decode, policy, rendering, clock, and browser launch. Production behavior must run without an LLM, agent framework, Python runtime, or cloud connection. “Agent harness” in this package refers to the development agent's execution instructions.

### State and interface contracts

Every observation carries `sessionId`, `monitorId`, `displayGeneration`, `frameId`, and a monotonic capture timestamp. Geometry is in physical pixels, with an explicit transform for monitor origin, rotation, and DPI. Never mix physical coordinates with WPF device-independent units. Enterprise monitor origins can be negative.

Every track carries `trackId`, `geometryRevision`, `payloadRevision`, and an ephemeral original payload buffer. Each asynchronous operation captures this identity tuple plus `policyRevision`; an outdated result is discarded, never applied to a new QR at the same coordinates. Payload changes at a stationary location invalidate the previous decision immediately.

Required interfaces: `ICaptureSource` yields frames and coverage; `IQrDecoder` yields bounded observations; `ITrackStore` associates/invalidate observations; `IPolicyEvaluator` consumes a typed payload and immutable snapshot; `IMaskRenderer` consumes track geometry/state; `ILinkLauncher` receives only action-time-validated HTTP(S) targets. Add `IPolicyProvider`, `IEventSink`, and `IReputationProvider` as local/no-op abstractions; they are not background network calls in the MVP.

Track lifecycle: `observed` → `masked/evaluating` → `approved | unverified | blocked | sensitive | unsupported` → `retired`. Coverage lifecycle is separate: `starting | active | degraded | paused | stopped`. Candidate geometry, if reliable but undecodable, uses `masked/undecodable`. Bounds on observation count, image dimensions, payload size, and native buffer arithmetic must be checked before allocation or interop calls.

### Processing sequence

1. Acquire a fresh frame without retaining an unbounded history.
2. Locate/decode QR regions locally. The initial implementation may obtain location and payload together from the decoder. Do not claim pre-decode masking unless the selected API actually exposes reliable candidate geometry.
3. Create an opaque mask as soon as a validated QR region is available. URL evaluation must not delay masking.
4. Validate/classify the payload and evaluate the current local policy.
5. Update the compact label. Show details in a separate panel only after the user asks.
6. Continue tracking the underlying content. Remove or reposition masks using fresh observations, including window moves, scrolls, and occlusion.
7. Recheck the current track, payload revision, and policy on every Open/Copy action. Never act on a stale UI callback.

If a decoder cannot locate an undecodable QR, the product cannot mask that QR through that decoder. Record this as a coverage limitation; do not invent a location or hide arbitrary image regions.

### Overlay and interaction rules

- Cover the complete QR bounds plus a conservative margin, including the quiet zone where practical. Start with `max(12 physical pixels, 8% of the larger dimension)` and validate against physical phone cameras. Tune from evidence.
- The mask itself is fully opaque. Transparency outside mask rectangles is permitted, but no transparent hole may expose QR pixels.
- Use tool-window/topmost/no-activate behavior. Showing or moving a mask must not steal focus, intercept typing, or change the foreground application.
- Constrain mouse interception to the mask. Do not let a click on the visible mask activate an invisible underlying control. Clicking the mask may explicitly open accessible details.
- Put buttons, long URLs, and keyboard navigation in the user-opened details window. Small QR masks must not become large obstructive banners.
- Avoid full-screen input-blocking surfaces and global input hooks. Verify actual hit testing; a transparency style alone is not evidence of correct click behavior.
- No automatic link launch, automatic clipboard write, or injected user interaction. Disabled actions must remain disabled in the action controller, not just in the UI.
- Masking is an availability tradeoff: stale masks must be retired promptly when the source changes. During uncertain tracking, report degraded coverage rather than covering unrelated desktop areas indefinitely.

### Critical P0 gate: capture while the QR is covered

`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` is a candidate mechanism for keeping the agent's own mask out of supported capture paths. This may let the agent see the original pixels while the person sees an opaque mask. It is not scoped only to the agent's capture. Microsoft documents this affinity as removing the window from supported capture output [S2].

**Design inference to test:** supported screenshots or screen sharing may also omit the mask and show the original QR. Other capture paths may behave differently. This is not an acceptable basis for claiming that remote viewers see a protected QR.

Before building additional product features, prove these together on a real Windows workstation:

1. A phone camera cannot decode the covered QR on the physical display.
2. The agent can still inspect the underlying QR without momentarily hiding the mask.
3. The mask follows movement, scrolling, and occlusion; it disappears when the QR disappears.
4. Screenshot and screen-sharing output is recorded for desktop sharing and source-window sharing separately.
5. Existing capture/meeting software keeps working without settings changes.

Never use periodic unmasking to rescan: it exposes the QR and can flicker. Never silently add DLL injection, application hooks, a display driver, capture-policy bypasses, or a framebuffer replacement to solve this gate.

If the selected capture path cannot satisfy the physical-display contract, stop and request an architectural choice. A per-window capture approach is an alternative to evaluate, not a proven equivalent: desktop composition, occlusion, source-window sharing, and application coverage change. If protected screenshots or remote viewers are required, stop before committing to monitor capture plus global affinity exclusion.

### Policy semantics

Matching is deliberately conservative:

- `payload_sha256`: SHA-256 of the decoder's original payload bytes; never hash a lossy display string. It is a matching key, not proof of issuer identity or protection against replay.
- `exact_url`: strict, ordinal equality of the complete validated UTF-8 URL text. Do not reorder query parameters, decode escapes, discard fragments, lowercase paths, or expand redirects for matching.
- `exact_host`: compare the parsed lowercase ASCII/IDNA host with an explicitly listed scheme and effective port. This approves the destination host broadly; it does not approve an eventual redirect or all content hosted there. Prefer exact URLs initially.
- MVP has no suffix wildcard, regex, or path-prefix rule. Future suffix rules must match DNS label boundaries; `company.example.evil.test` must not match `company.example`.
- Reject absolute URLs with credentials/userinfo, control characters, ambiguous parsing, invalid encoding, or an unsupported scheme. A URI parser is required; string searches are insufficient.
- Show the ASCII host prominently, isolate bidi display effects, and redact query/fragment values in diagnostics and the default details view. The original validated target remains ephemeral in memory for an explicit browser action.

Precedence: sensitive-payload handling and structural validation → active deny rules → active allow rules → unverified default. An allow rule cannot turn an unsupported launch scheme into HTTP(S), and an expired rule is ignored.

The provided example policy is for synthetic fixtures only. The MVP policy file is editable by the test user and is not a managed enforcement boundary. Enterprise adds authenticated distribution and protected storage.

Initial contract fixtures, evaluated with the example policy before rule expiry:

| Payload | Expected policy outcome |
|---|---|
| `https://benefits.corp.example/enroll` | Approved by exact URL/payload rules |
| `https://benefits.corp.example/enroll?token=demo` | Unverified: complete URL/payload differs |
| `https://help.corp.example/docs` | Approved by exact host, HTTPS, port 443 |
| `https://help.corp.example/deny-test` | Blocked: exact deny overrides host allow |
| `https://help.corp.example.evil.test/docs` | Unverified: host is different |
| `http://help.corp.example/docs` | Unverified: approved scheme is HTTPS |
| `https://help.corp.example:8443/docs` | Unverified: effective port differs |
| `https://user:demo@help.corp.example/docs` | Unsupported launch: userinfo is rejected |
| `javascript:alert(1)` | Unsupported launch; never execute |
| `otpauth://totp/Demo?secret=SYNTHETICONLY` | Sensitive; mask without exposing or exporting payload |

Add an expired-rule clock fixture and a same-location/different-payload observation. Schema validation alone is insufficient: use a proper parser to validate rule URLs, prohibit userinfo/control characters, enforce unique IDs and supported rule semantics, and bound total policy size before accepting it.

### Failure behavior

| Situation | Required behavior |
|---|---|
| Unknown or policy-unavailable URL | Mask and show Unverified; never label Safe |
| Invalid edited policy | Retain last validated in-memory snapshot; emit redacted error; if none exists, use a built-in mask-and-warn baseline |
| Decode failure with reliable location | Mask and show Unable to decode; disable Open |
| Decode failure without location | Mark the observation unavailable; do not claim it was covered |
| Capture/graphics failure | Retire stale masks, show coverage degraded, retry with bounded backoff |
| Screen lock/session switch | Release frames and pause; rebuild capture and tracks on return |
| Overload | Drop obsolete work, throttle discovery, report degraded coverage; never build a growing frame backlog |
| Tracking ambiguity | Invalidate prior link actions; bound mask lifetime; do not obscure unrelated controls indefinitely |
| Agent exits/crashes | Its masks disappear; no orphaned blocking windows; coverage is lost and must be reported after restart |
| Network outage | No effect on MVP; enterprise continues with permitted cached policy |

The QR decision can be restrictive while desktop failure handling yields to availability. A capture outage cannot provide a fail-closed QR security guarantee without a substantially more intrusive design.

### Resource targets and acceptance envelope

Provisional reference workstation: Windows 11 x64, at least 8 logical CPU cores, 16 GB RAM, integrated graphics, one 1920×1080 monitor. Record exact OS build, hardware, GPU driver, power mode, and tested DPI before interpreting numbers.

| Measure | Initial target, not a measured result |
|---|---|
| Discovery cadence | Adaptive 2–5 passes/sec while content changes |
| Tracking cadence | Up to 10–15 lightweight geometry checks/sec for existing masks, if within budget |
| Static QR render-to-mask latency | p95 ≤500 ms on the supported fixture corpus |
| Mask removal after confirmed absence | p95 ≤500 ms |
| CPU, unchanged desktop | Average ≤0.5% of total system capacity over 10 min |
| CPU, normal scrolling/browsing | Average ≤2% of total system capacity over 10 min |
| Process working set | ≤150 MiB steady state on the reference single-monitor workload |
| Frame buffering | At most two retained capture buffers and one replaceable pending work item per monitor |
| Pixel persistence/networking | None; release frames promptly; do not include pixels in crash/support uploads |
| Focus theft and unrelated input interception | Zero in the scripted acceptance scenarios |

These discovery targets are not a zero-exposure tracking guarantee. Measure visible gaps during motion as well as steady-state latency. If targets fail, propose a scoped change with measurements; do not sacrifice recognition by indiscriminate downsampling or weaken safety labels.

Use frame-change coalescing and a latest-frame-wins scheduler. Periodic complete discovery is required so tile boundaries and missed change notifications do not become blind spots. QR codes can span changed-region boundaries. Expand/merge regions before decode and test large codes crossing tiles. In the MVP, bounded full-frame decode can be simpler than implementing a custom finder-pattern engine prematurely.

## 3. Enterprise architecture

### Keep the fast path on the endpoint

Capture → decode → mask → local policy must remain independent of backend latency and availability. Management and enrichment run asynchronously. No backend stores or processes desktop video.

```mermaid
flowchart TD
    subgraph Endpoint["Managed endpoint"]
        U["User-session agent"] --> L["Local policy evaluator"]
        U --> M["Opaque QR masks"]
        B["Management broker"] -->|"Validated snapshot"| L
        U -->|"Redacted events"| B
    end
    B -->|"Outbound authenticated HTTPS"| A["Control-plane API"]
    A --> P["Policy artifacts and tenant data"]
    A --> Q["Bounded durable work queue"]
    Q --> W["Background workers"]
    W --> R["Optional reputation adapters"]
    W --> S["Optional SIEM export"]
```

### Endpoint responsibilities and privileges

| Process | Runs as | Owns |
|---|---|---|
| User-session agent | Logged-in user, standard integrity | Capture, QR decoding, tracking, UI, local policy evaluation, explicit browser launch |
| Management broker | Dedicated service identity with only required permissions | Device authentication, verified policy cache, health, bounded telemetry spool |
| Installer/update mechanism | Existing authorized software-management system | Signed package installation and updates |

Windows services and the user's desktop must be separated [S4]. Do not run capture or UI as SYSTEM to bypass desktop restrictions. Do not add a custom updater if Intune or an existing software-distribution system can handle signed packages.

Use local named-pipe IPC with explicit ACLs for the broker service identity and the intended session user. Authenticate the connecting Windows token/session; do not trust a client-supplied PID or tenant ID. Version messages, cap their size, validate schemas, and reject unsupported message types. Do not expose a local TCP listener or generic “execute command” RPC.

Store verified policy under protected machine storage and deliver immutable snapshots to each session agent. The broker never needs screenshot data. Keep policy evaluation in the session agent so broker outages do not freeze the UI.

Per-user instances must not cross session boundaries. Multiple monitors receive independent capture/tracking state and a shared global resource budget. Device/session/display changes invalidate all old frame references and geometry. Native decoder process isolation is an optional later hardening step if fuzzing or crash evidence warrants it; do not add processes by habit.

Signing and service recovery support integrity and operability; they do not make the product tamper-proof against a local administrator. Distinguish process stopped, device offline, policy stale, and unsupported surface in fleet health.

### Lean control plane

Start with one modular ASP.NET Core application, one managed relational database, object storage for versioned policy artifacts, and a durable work queue. Run a small number of stateless API replicas and separate background worker replicas when workload requires it. No Kubernetes, Kafka, event mesh, or per-QR cloud function is required initially.

Azure is a practical candidate for the user's environment: managed application/container hosting, managed PostgreSQL, blob storage, a managed queue, and Key Vault signing keys. Hosting vendor, region, dedicated deployment versus SaaS tenancy, and authentication are decisions before enterprise implementation. Treat these as functional capabilities rather than mandatory SKU choices.

Logical modules within the single codebase:

1. Device enrollment and authentication.
2. Policy authoring, validation, publication, and audit.
3. Fleet health and bounded event ingestion.
4. Optional cached enrichment.
5. Optional export to the organization's SIEM.

These are module boundaries, not five required microservices. Split a module into a service only when a measured scaling, isolation, or ownership need exists.

### Device and tenant identity

- Prefer an individually enrolled device credential backed by the OS key store; TPM-backed keys where supported. Do not embed tenant-wide API secrets in the endpoint package.
- Candidate authentication is device certificates or a certificate-backed token flow. Validate compatibility with corporate proxies and TLS inspection before choosing. Do not silently create proxy bypasses or disable certificate validation.
- Derive tenant/device authorization from validated server-side enrollment. A submitted `tenant_id` is not an authorization boundary.
- Apply tenant isolation to database access, object paths, caches, queues, quotas, administration, and exports. Reputation cache entries that contain private tenant data cannot be shared across tenants.
- Protect administration with enterprise SSO, scoped roles, and an audit trail. Policy publisher and device reader are separate permissions.
- Revoke compromised device credentials and signing keys through documented procedures. Revocation cannot reach an offline endpoint immediately.

### Signed policy distribution

The MVP rule body remains reusable. Enterprise wraps it in a signed, versioned artifact containing tenant/audience, revision, issue time, validity bounds, key ID, and the compatible agent/schema version. Use an established signing envelope such as JWS with an explicitly allowed algorithm; no invented cryptography or arbitrary remote signing-key URLs.

The broker verifies signatures, audience, schema, bounds, and revision before an atomic cache replacement. Keep the last validated snapshot. Reject a lower revision; an intentional rollback republishes the previous rule body under a higher revision. Rotate signing keys with a documented trust anchor and overlap period.

Poll roughly every 15 minutes with randomized jitter and conditional requests/ETags; distribute versioned bytes from a cached artifact path. Avoid a database read for every endpoint poll. Long polling or push notification is optional if the business requires faster revocation; masking itself remains local.

Proposed stale-policy behavior: use a last validated enterprise policy within a configurable maximum age, initially seven days. After that age, retain local masking but remove stale approvals and use the baseline Unverified state; do not continue calling them Company approved. Confirm the allowed maximum age before a production pilot. Deny-cache retention must be explicitly specified with validity bounds rather than assuming it lasts forever.

### Optional reputation path

Only after the enterprise management path is stable:

1. Apply active local deny rules immediately and check local approvals.
2. For eligible unknown HTTP(S) destinations, enqueue a bounded asynchronous request according to the tenant's privacy policy.
3. Consult a tenant-scoped cache, consolidate identical in-flight requests, and rate-limit providers.
4. Apply the result only to the still-current payload revision; expiry or an outage yields Unverified, not Safe.
5. An authoritative malicious result can override a broad allow rule according to explicit enterprise precedence. An override must be a distinct, audited business decision.

Expose evidence and source freshness instead of inventing an uncalibrated numeric score. Newly seen domains, punycode, brand words, or a suspicious-looking path alone do not justify declaring a destination malicious.

If redirect expansion is added, implement it in an isolated backend worker with SSRF protection: restrict protocols; check every hop and actual resolved destination; block loopback, link-local, private/internal ranges and cloud metadata; limit redirects, body size, time, and credential use. Protect against DNS rebinding. Public expansion cannot inspect an organization's authenticated internal page or safely consume every one-time URL.

Do not send enrollment secrets, OAuth/device codes, tokenized URLs, or other private payloads to a third party by default. Host-only queries can reduce exposure but lose URL-specific precision. Exact-URL submission is a separate tenant-approved capability. Verify each provider's actual API, licensing, retention, and supported verdicts before integration; existing email products are not assumed to expose a generic synchronous URL-scanning API.

No email inspection, sender analysis, message parsing, mailbox connector, or document modification is part of this product.

### Events and observability

Default outbound fields: random event ID, enrolled device reference, time, agent version, policy revision, coverage state, decision, reason code, and latency bucket. Tenant context comes from device authentication. Destination host and observed application context are opt-in metadata; pixel data, raw payload bytes, query/fragment values, window titles, and document/email contents are excluded. Monitor capture alone does not establish which process owns every QR. A foreground process is context, not proven source attribution; label uncertainty and do not use it as a trusted-origin allow rule.

Use a short-lived in-memory fingerprint to deduplicate repeated observations. If fleet correlation needs a stable fingerprint, design a tenant-scoped keyed digest and confirm its privacy impact. A plain hash of a secret is not adequate redaction.

Send state changes and explicit user actions rather than an event on every frame. Separate operational metrics from security events. Proposed broker limits: encrypted local spool ≤10 MiB, age ≤48 hours, bounded batches, randomized retry, and backpressure. Drop low-priority repeated observations before high-priority decisions, record drop counts, and apply fairness so one endpoint cannot exhaust a tenant's quota.

Queue consumers are idempotent by event ID. Use a transactional outbox for database changes that also require queue delivery. Provider and SIEM export outages must not block ingestion, policy delivery, or desktop protection.

### Capacity example

These are sizing assumptions, not benchmark results. Model a fleet of 50,000 enrolled devices and validate with synthetic load before deployment.

| Operation | Assumption | Approximate average load |
|---|---|---|
| Policy poll | Every 15 min/device, jittered | 56 requests/sec |
| Health update | Every 15 min/device, separate request | 56 requests/sec |
| QR events | 8 events/device/day | 4.6 events/sec over 24 h |
| Full policy publication | 250 KiB/device without shared transfer caching | About 11.9 GiB for the whole fleet |

Real work-hour peaks and reboot/login waves exceed these averages. Test at least 10× average API arrival rates plus cold-cache publication and backlog recovery. Combine health with policy polls where practical, cache immutable policy bytes, and use conditional fetches. Enrichment load depends on unique eligible unknown destinations, not on frames or total QR sightings. Provider quotas and cost are independent bottlenecks.

Scale API replicas on measured latency and request load; scale workers on queue depth/age. Bound queues, caches, per-tenant throughput, and payload sizes. Start with shared tenancy-aware tables if SaaS is chosen; isolate a large tenant only when contract or measured load requires it.

### Coexistence requirements

- Existing EDR/DLP/web controls remain responsible for navigation and endpoint response. The overlay disables the product's own Open action; it cannot prevent a user from typing a URL elsewhere.
- No driver, DLL injection, process-memory inspection, TLS interception, packet filtering, app modification, global keyboard hook, or broad EDR exclusion.
- No file/email scanning. Pixel acquisition is limited to the active user's supported desktop and processed in transient memory.
- Capture availability, GPU use, power/battery impact, screen readers, DPI/rotation, Teams/Zoom sharing, recording, remote support, and VDI are explicit compatibility tests.
- Do not assume zero interoperability risk because public OS APIs are used. Capture APIs have resource and platform constraints. In particular, Desktop Duplication has documented concurrency limits [S3], so it is not an automatic silent fallback.
- Keep bounded crash recovery and an administrative disable/rollback path. Use deployment rings and signed packages; never change other security tools' settings to pass a test.
- Preserve legitimate enrollment, authenticator, pairing, Wi-Fi, and device-authorization workflows through a product decision before rollout. A URL replacement cannot substitute for every protocol QR. The default demo masks sensitive codes; a managed, time-limited exception or preservation workflow requires explicit approval and a defined trust basis. A browser process name alone does not prove page origin.

## 4. Staged delivery and acceptance

| Phase | Build | Exit evidence |
|---|---|---|
| P0: feasibility | Minimal capture/decoder/mask vertical slice on real Windows | Under-mask tracking, phone test, movement/occlusion, screenshot/sharing behavior, basic resource measurements |
| P1: offline MVP | Local policy, classifications, details/actions, bounded scheduler, health | Contract tests, static/moving QR corpus, truthful UI states, no background egress, focus/input checks |
| P2: endpoint pilot readiness | Mixed-DPI multi-monitor support, lifecycle recovery, signed packaging, measured compatibility | Lab soak, resource report, known coverage limits, clean uninstall/rollback, legitimate QR workflow decision |
| P3: enterprise management | Broker, identity, signed policies, bounded telemetry, admin API | Tenant-isolation checks, stale-policy/rollback/signature tests, load/fault tests, authorized pilot |
| P4: optional enrichment | One verified provider adapter, cache and async decisions | Private-payload exclusions, provider outage behavior, stale-result handling, rate-limit and cost report |

Do not start P3 or P4 because P0 is blocked. A web mockup, a compiled Linux model, or synthetic screenshot decoding does not prove physical Windows masking. Performance work must retain a regression corpus, including codes that cross tiles, move, change payload at the same location, or are covered by a different window.

Minimum portable contract tests: deny-over-allow, expiry, strict URL matching, host label boundaries, invalid/sensitive URI handling, out-of-order result rejection, action-time revalidation, schema rejection, and queue bounds. Windows tests cover focus/input, actual compositing, capture recovery, DPI coordinate mapping, under-mask observation, and real phone readability. Enterprise tests add IPC authorization, device/tenant isolation, signature and rollback protection, offline staleness, outbox/idempotence, provider faults, and deployment recovery.

## 5. Agent decision and clarification gates

The companion `AGENTS.md` defines execution behavior. The following choices need a user decision at the phase where they become consequential; they do not block this architecture document.

| Gate | Ask when | Decision required |
|---|---|---|
| G01: test environment | Before claiming P0 passed | Access to a real supported Windows workstation and physical phone validation |
| G02: capture/overlay contract | P0 under-mask observation fails, or remote-view protection is requested | Accept physical-display scope, evaluate per-window capture, or change the architecture |
| G03: legitimate QR workflows | Before a pilot that affects real users | Which enrollment/pairing codes must remain usable; permitted temporary exceptions |
| G04: unknown links | Before a managed pilot | Warn and permit explicit navigation, or block unapproved destinations |
| G05: enterprise deployment | Before P3 implementation | SaaS versus dedicated tenant, hosting region/provider, device authentication, stale-policy budget |
| G06: private telemetry/TI | Before new data leaves endpoints or reaches a provider | Allowed metadata, retention, third-party submission scope, provider choice |
| G07: scope/security tradeoff | Required targets cannot be met with bounded supported APIs | Evidence-based target/scope change; no silent intrusive mechanism or weaker safety claim |

Routine implementation choices, refactoring, synthetic fixture creation, and portable testing do not require clarification. Ask once with evidence and a concrete recommendation; continue independent work while the answer is pending. Do not treat elapsed time as a required decision.

## 6. Primary sources and interpretation

Documentation checked on 2026-10-01. Sources establish API capabilities and restrictions; the architecture and resource budgets above are proposed engineering decisions.

- [S1: Windows monitor-capture interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createformonitor): supports selecting a monitor for a capture item.
- [S2: SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity): exclusion behavior and its limits. Test the consequences for every supported capture/share path.
- [S3: Desktop Duplication constraints](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutput1-duplicateoutput): concurrent duplication limits and failure conditions.
- [S4: Windows interactive services](https://learn.microsoft.com/en-us/windows/win32/services/interactive-services): service/session UI separation.
- [S5: Extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles): tool-window, topmost, and no-activate semantics; actual interaction still needs testing.
- [S6: .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core): .NET 10 is the supported LTS choice at the document date.
- [S7: ZXing-C++](https://github.com/zxing-cpp/zxing-cpp): native QR reader and .NET binding availability; pin and validate the selected binding.
- [S8: Windows Graphics Capture](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture): capture capability and visible indicators.
- [S9: Capture border consent](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired): borderless capture has capability/consent requirements. Keep standard indicators during the initial spike; do not promise invisible capture or bypass the prompt.
