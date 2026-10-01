# P1 offline contracts — 2026-10-01

## Authorization and identity

Base: merged P0 `1f6fdf33021f1cb36541729a63be02d796735541`, containing affinity fix `e93450ad2806b5b70ac1d091e0a9a28bad24d214`. Branch: `codex/p1-offline-mvp`. D07 in the plan/state authorizes P1 development before P0 workstation/phone acceptance. P0 remains blocked by G01; P2–P4 remain pending.

The handoff SHA is the draft PR head. `source-hashes.sha256` records tested source bytes; `verification.txt` records actual commands/output. Missing workstation evidence is not inferred from compilation or portable tests.

## Available verification

Ubuntu 24.04 x64; pinned .NET SDK 10.0.401 / runtime 10.0.12. All four projects restored in locked mode and built Release with warnings as errors. No production package versions changed and no new production dependency was introduced.

| Check | Actual evidence | Result |
|---|---|---|
| P0 native QR/lifecycle regressions | `verification.txt` | 23 contracts passed on Linux |
| P1 payload/policy/actions/bounds | `verification.txt` | 67 contracts passed on Linux |
| Windows app and native API executable | `verification.txt` | Compiled; zero warnings/errors; not executed on Linux |
| Hosted Windows API/portable execution | `ci-results.md` / PR CI | Passed: 23 P0 + 67 P1 portable; six native mask API checks; PowerShell syntax; exact CI checkout recorded |
| Real Windows capture/WPF/input | `windows-acceptance.md` | Pending |
| Runtime egress and workstation resources | `egress-and-resource-report.md` | Pending |

Optional `dotnet format whitespace` could not connect to its MSBuild pipe on this runner (`SocketException`, permission denied). Locked builds work with single-node flags; lock-body indentation was adjusted directly. This authoring-tool limitation is separate from endpoint API behavior.

## Architecture example matrix

Clock: **2026-10-01T00:00:00Z**, before example expiry. Every row is independently exercised.

| Original UTF-8 payload | Outcome |
|---|---|
| `https://benefits.corp.example/enroll` | Company approved |
| `https://benefits.corp.example/enroll?token=demo` | Unverified |
| `https://help.corp.example/docs` | Company approved |
| `https://help.corp.example/deny-test` | Blocked; deny overrides host allow |
| `https://help.corp.example.evil.test/docs` | Unverified |
| `http://help.corp.example/docs` | Unverified |
| `https://help.corp.example:8443/docs` | Unverified |
| `https://user:demo@help.corp.example/docs` | Unsupported; no Open/Copy |
| `javascript:alert(1)` | Unsupported; no Open/Copy |
| `otpauth://totp/Demo?secret=SYNTHETICONLY` | Sensitive; hidden; no Open/Copy |

## Tested invariants

- Strict complete URL text: path/escape case, query order, fragments, scheme case and explicit default ports are not normalized for exact matching. Host rules use ASCII/IDNA, label boundaries, scheme and effective port. Original-byte SHA-256 matches the example digest independently; canonical URI strings are not substituted.
- Structural/sensitive handling precedes all rules. Hash allows cannot enable sensitive, unsupported, invalid-UTF-8 or undecodable payloads. Query/fragment values are hidden and path Unicode is ASCII-escaped; the ASCII host is separate.
- Schema validates required/additional fields, supported match shapes, enums/types/bounds/integer semantics, duplicate keys, rule count and file size. Semantics validate unique IDs, rule URLs, IDNA and UTC expiry. Invalid replacements retain the last validated identity; without a snapshot the built-in baseline warns.
- Accepted-content snapshot tokens are independent of numeric revision. Identical bytes retain identity; edits that reuse a revision invalidate actions. Windows Open/Copy re-read the selected local file at action time. Stream reads are capped at 1 MiB + 1 detection byte.
- Actions check session, monitor, generation, track ID, geometry/payload revisions, freshness and snapshot under policy/track locks. Payload/policy, including expiry, are reevaluated immediately before adapter dispatch. Windows also checks current monitor bounds and capture fault/cancellation. One pending warning ticket is issued before confirmation; cancellation, reuse and changed/stale tickets refuse navigation.
- Replacement, motion, retirement, old callbacks, stop/disposal and stale observations refuse old actions. Identical fresh observations retain current tickets. Fake browser/clipboard adapters prove deliberate effects and original-target delivery without real navigation/clipboard writes.
- Owned bytes survive observation disposal and clear at lifecycle boundaries. Maximum live track bytes, latest-slot bursts, complete-frame coalescing, periodic discovery, source/dimension invalidation and cache clearing are tested. Diagnostics have bounded fixed-code events without arbitrary strings or exception messages.

## Reproduction

Use the SDK and commands in `README.md`. P1 entry point: `tests/QrGuard.P1.Contracts/Program.cs`; synthetic fixtures/fixed clocks print fixed test labels only. The original P0 portable harness is unchanged. CI runs both harnesses on Linux and Windows, plus four retained P0 and two P1 native mask API checks on Windows. Synthetic Win32 messages are not real input acceptance.
