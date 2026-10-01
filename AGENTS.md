# QR Guard implementation harness

This file is intended for the root of the future implementation repository. Read `architecture.md`, `execution-plan.yaml`, and the policy example/schema before editing. It governs the development agent; the endpoint product does not require an AI agent at runtime.

## Authorization and current mode

The current request is architecture first. This package authorizes designing and refining the brief, not launching a deployment or starting every implementation phase. When the user asks to build, record the authorized phase in the execution state and start there. Do not request authorization already present in the session.

Suggested first implementation instruction:

> Implement P0 only using this harness. Verify capture, QR decoding, an opaque mask, and under-mask tracking on a real Windows workstation. Produce evidence and record limitations. Stop if the supported APIs cannot meet the stated local-display contract. Do not add a backend or TI integration.

## Execution loop

1. Read repository instructions and existing code. Preserve unrelated changes. This harness does not authorize modifying other products, endpoint controls, or organizational settings.
2. Read the requested phase and its dependencies in `execution-plan.yaml`. Do not jump to a dependent phase while a gate is blocked.
3. Maintain `execution-state.yaml` with the current phase, decisions, completed criteria, evidence paths, and blockers. Create it when implementation starts; never replace prior decisions with guessed answers after compaction.
4. Implement the smallest vertical slice that satisfies that phase. Keep interfaces narrow; pin dependencies. Local refactors, synthetic fixtures, and appropriate tests are routine work.
5. Verify the change using the stated acceptance criteria. Distinguish source review, compilation, portable tests, Windows execution, physical phone validation, and enterprise load tests. None is a substitute for another.
6. Record evidence and limitations. Mark a criterion complete only when its required evidence exists. If a performance target fails, preserve the measurement and investigate before proposing a change.
7. Continue through authorized phases whose dependencies are satisfied. Otherwise report the completed result and the specific unresolved gate. Never quietly build a mockup in place of the endpoint application.

Do not delegate to subagents unless the user or applicable project instructions explicitly authorize delegation. Routine architectural review can be done within the active agent's turn.

## Requirements that remain invariant

- Screen capture, QR decoding, masking, and immediate policy decisions run locally.
- The offline MVP makes no background network calls. A deliberate user Open action is the only intended navigation.
- Unknown is Unverified, not Safe. Policy approval and reputation evidence remain distinct.
- Deny rules take precedence over allow rules. Exact payload hashes do not establish issuer identity or resist replay.
- No screenshots, video, raw payloads, secret URIs, or URL query/fragment values in persistent logs or outbound diagnostics.
- No automatic launch, shell command interpolation, clipboard overwrite, or hidden programmatic user interaction.
- No DLL injection, drivers, global input hooks, process-memory scanning, TLS interception, bypass of capture protections, or broad EDR exclusions.
- No scanning emails, senders, attachments, or document files.
- No focus stealing or input blocking outside the actual mask. No full-screen fail-closed surface during an outage.
- No periodic unmasking to inspect a covered code.
- No assumption that screenshots or remote viewers are protected. Capture/share modes need distinct evidence.
- No claim of a protected QR when the observation has no verified geometry. Unsupported coverage is reported honestly.
- No infinite retry, unbounded frame queue, unbounded telemetry spool, or forced update of other tools.
- Legitimate authenticator, pairing, Wi-Fi, and authorization QR workflows require a product decision before a real-user pilot.

## When to ask, and when to keep working

Proceed with routine implementation choices within the agreed scope. Use the architecture defaults as provisional decisions and record them. Ask only when the answer changes the contract, prevents required verification, authorizes deployment/data sharing, or resolves a material compatibility/security tradeoff.

Required gates:

| Gate | Trigger | Stop-dependent work |
|---|---|---|
| G01 | No real supported Windows test environment or physical phone validation | Do not claim P0 completed; portable contracts can proceed |
| G02 | Cannot observe beneath the mask without exposure; screenshot/share protection is required | Stop capture-architecture-dependent product work; preserve spike evidence |
| G03 | Real enrollment/pairing workflow would be obscured | Stop real-user rollout until an exception/preservation decision is recorded |
| G04 | Managed deployment policy for unknown links is unspecified | Ask warn-and-open versus block-unapproved before rollout; demo uses warn-and-open |
| G05 | Starting enterprise implementation | Resolve tenancy, region/provider, device authentication, offline-policy lifetime |
| G06 | Adding external metadata/TI submission | Resolve approved fields, retention, provider, and submission scope before egress |
| G07 | Supported APIs fail resource, focus/input, or masking requirements | Present measured options; never silently change scope or add an intrusive mechanism |

Within a technical spike, make at most two materially different bounded attempts at a blocked capture approach before requesting an architectural decision. Debug ordinary errors normally; this limit prevents an endless search for a fundamentally different product.

Use this clarification format:

```text
Blocked gate: Gxx — <concrete decision>.
Evidence: <observed result and evidence file>.
Impact: <what requirement or next phase depends on it>.
Recommendation: <one proposed choice and why>.
Choices: <two or three concrete alternatives>.
Continuing independently: <work that does not require the answer>.
```

Ask one focused question per decision; combine at most three closely related choices. Do not ask the user to decide class names, internal folder organization, or routine refactoring. When a required decision is pending, do not interpret elapsed time, silence, or a guessed preference as approval. If the user already decided, cite that decision and proceed.

## Verification and evidence

Use synthetic QR fixtures first. Record fixture seed/source, exact payload bytes, expected policy decision, display geometry, and scenario. Never decode a live private enrollment code merely to populate a sample.

Portable checks must exercise policy precedence/expiry, URI parsing, strict matching, payload changes, stale callbacks, action-time revalidation, schemas, queue bounds, and redaction. Windows checks must observe actual compositor behavior, geometry, motion, occlusion, input/focus, lock/unlock, monitor changes, and capture/share compatibility. Performance reports must include workload, hardware, interval, measurement method, percentile/count, and failures.

Keep physical-phone evidence separate from agent capture evidence; the latter may exclude the mask. Avoid uploading screen recordings containing real desktop information. Use controlled synthetic windows and locally held evidence.

Suggested evidence location is `evidence/<phase>/`. Suggested state file shape:

```yaml
phase: P0
status: in_progress # pending, in_progress, blocked, completed
authorized_scope: P0
decisions: [] # id, choice, user/source, timestamp
criteria: [] # criterion, status, evidence_path, environment
blockers: [] # gate, evidence, requested_decision
next_action: Prove underlying pixels remain observable while the physical mask is visible.
```

## Handoff

Every phase report states what changed, what was actually tested, which criteria passed, known coverage limits, and the next authorized action. Keep implementation evidence distinct from architectural targets. Enterprise pilot approval must refer to a concrete signed build, tested policy, device cohort, rollback method, and compatibility report.

If blocked, leave reproducible evidence and a precise decision request. Do not fill the gap by inventing successful Windows tests, silently weakening the user's requirements, or asking a blanket permission question.
