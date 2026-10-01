# QR Guard

P1 is a manually launched **offline MVP** built on the P0 Windows capture/opaque masks, including reviewed affinity fix `e93450ad2806b5b70ac1d091e0a9a28bad24d214`. It decodes locally, evaluates local policy and offers deliberate Open/Copy in user-opened details.

**P0 remains blocked by G01: workstation and physical-phone acceptance are missing.** D07 records the user's 2026-10-01 authorization to develop P1 before that acceptance. This does not establish P0 passed. P1 is for review; real UI/input, egress and workstation resources remain pending. P2–P4 are pending. See `execution-state.yaml` and `evidence/P1/`.

## Controlled Windows run

Use Windows 11 x64, SDK **10.0.401**, one landscape monitor no larger than **1920×1080**, initially at **100% DPI**, and synthetic fixtures only. No service or administrator rights are required.

```powershell
.\scripts\run-p1.ps1 -ExpectedCommit '<full reviewed P1 head SHA>'
```

The runner requires a clean checkout, restores locked dependencies, builds, runs P0/P1 portable contracts and six native mask API checks, records source identity/DLL hashes and opens the app. API failure stops before capture acceptance. Each run has a UTC transcript/environment report in ignored `lab-local/<commit>/<UTC-run>/`. Restore requires NuGet access, separate from runtime egress acceptance. Keep existing execution policy and security controls; individual commands below are also available.

1. Open the fixture and verify the uncovered QR scans with a phone. Select the monitor and start capture.
2. Test Payload A (Company approved), Payload B at the same location (Blocked), Sensitive, Unverified, Unsupported, IDNA, movement, removal and occlusion. Policy work never delays opaque mask creation.
3. Click a mask, or select a QR and use **View selected QR details** by keyboard. Rendering never opens details. The ASCII host is prominent; query/fragment values are hidden and non-ASCII path text is escaped to prevent bidi reordering.
4. **Open** uses the original validated HTTP(S) target. Unverified requires a separate warning with No selected by default. A confirmation must follow an issued warning and is consumed once. **Copy original link** is a separate explicit action. Restricted states refuse both actions in the controller.
5. Load a local JSON policy. The bundled policy contains reserved-domain demo approvals only. Edits are checked every second during capture and again at Open/Copy. Invalid edits retain the last validated snapshot; without one, use the built-in mask-and-warn baseline. Changed validated content creates a new snapshot identity even if its numeric revision is reused. Identical bytes retain identity. Refresh details after track/policy changes.
6. Export redacted measurements for each defined workload. **Stop / remove masks**, then close the app. Stop before locking, changing displays or switching sessions; recovery is P2 work.

Screenshots/remote viewers may see the original QR because masks use capture exclusion. Under-mask WGC, physical protection and actual input behavior remain unverified until the controlled acceptance in `evidence/P0/` and `evidence/P1/windows-acceptance.md`. An unlocated QR has no mask. Failures report degraded/unavailable coverage and retire masks, with no fullscreen block. Retry after a fault is deliberate Stop/Start, with no infinite automatic loop.

## Policy and payloads

Executable schema v1 validation adds semantics: ≤1 MiB, ≤1000 rules, unique IDs, real UTC expiries, valid match definitions, strict URI parsing and duplicate-property rejection. Only local fixed/removable files are read; UNC/network drives and reparse traversal are refused. User-editable policy is a lab control, not managed enforcement.

Precedence: structural/sensitive handling → active deny → active allow → **Unverified**. Expired rules are ignored. `payload_sha256` hashes original decoder bytes; `exact_url` compares complete ordinal text; `exact_host` compares exact lowercase ASCII/IDNA host, scheme and effective port. Hashes prove neither issuer identity nor replay resistance. `unknown_action: block` disables Unverified navigation; deliberate Copy of a validated Unverified URL remains available. Blocked, sensitive, unsupported and undecodable states disable both actions.

Strict UTF-8/URI validation rejects userinfo, control/format characters, whitespace, backslashes, malformed escapes, encoded ASCII controls and ambiguous authorities. Trailing-dot hosts, noncanonical IPv4, alternate Unicode dot separators, leading-zero ports and IPv6 zone identifiers are refused. Standard IPv4/IPv6 and IDNA hosts are supported. Known authenticator, Wi-Fi, DPP, FIDO, credential-presentation, Bluetooth and contact-card formats are Sensitive and hidden before policy. The classifier cannot recognize every proprietary enrollment format or secret in arbitrary HTTP paths; unknown non-URLs are non-launchable. Real enrollment/pairing workflows still require G03 before a pilot.

## Bounds

Keep two WGC pool buffers, one reusable staging texture, one active CPU pixel frame and one replaceable pending result. Complete-frame hashing every nominal 200 ms coalesces byte-identical observations, with full native discovery every 1000 ms while fresh frames arrive. No sampled/downscaled pixels, tile blind spots or periodic unmasking. Source identity/dimensions prevent cross-generation reuse.

Accept at most 16 observations/tracks and 4096 original bytes per payload. Tracker bytes are independently owned and cleared on replacement, retirement, stop/Clear and disposal. Cached/discarded batches and frames clear on release. Native/GPU memory uses supported disposal; forensic erasure is not claimed. Actions require observations ≤500 ms old; masks retire at 750 ms or fresh absence. Diagnostics retain 128 fixed-code events and 2048 timing samples, excluding URLs, payloads, hashes, rule text, titles and pixels.

## Reproduction

```text
dotnet restore tests/QrGuard.Contracts/QrGuard.Contracts.csproj --locked-mode
dotnet build tests/QrGuard.Contracts/QrGuard.Contracts.csproj -c Release --no-restore
dotnet run --project tests/QrGuard.Contracts -c Release --no-build --no-restore
dotnet restore tests/QrGuard.P1.Contracts/QrGuard.P1.Contracts.csproj --locked-mode
dotnet build tests/QrGuard.P1.Contracts/QrGuard.P1.Contracts.csproj -c Release --no-restore
dotnet run --project tests/QrGuard.P1.Contracts -c Release --no-build --no-restore
dotnet restore src/QrGuard.Windows/QrGuard.Windows.csproj --locked-mode
dotnet build src/QrGuard.Windows/QrGuard.Windows.csproj -c Release --no-restore
dotnet restore tests/QrGuard.Windows.Contracts/QrGuard.Windows.Contracts.csproj --locked-mode
dotnet build tests/QrGuard.Windows.Contracts/QrGuard.Windows.Contracts.csproj -c Release --no-restore
```

On this Linux authoring host add `-m:1 -nr:false -p:BuildInParallel=false` to restore/build. On Windows also run `dotnet run --project tests/QrGuard.Windows.Contracts -c Release --no-build --no-restore`. Hosted API checks are separate from real pointer/keyboard input, WGC, phone resistance, egress and performance.

No backend, TI, URL fetch, DNS lookup, email/document scanning, service, hooks, driver or security-setting changes were added. The browser adapter is reachable only from explicit current Open actions. Stop after P1 review; do not start P2 or roll out to real users.
