# QR Guard

P0 contains a Windows feasibility application: Windows Graphics Capture monitor interop → local ZXing-C++ QR decoding → bounded tracking → persistent opaque native mask windows. It runs manually as the signed-in user, without a service or administrator rights.

**P0 is blocked on real Windows and physical-phone validation.** A successful build and portable QR tests do not prove compositor behavior, capture beneath masks, phone resistance, focus, sharing, or Windows performance. P1–P4 remain pending. See `execution-state.yaml` and `evidence/P0/`.

## Run the P0 lab

Use a supported Windows 11 x64 workstation, .NET SDK **10.0.401**, and one landscape monitor at **1920×1080 and 100% DPI** for the first acceptance run. The prototype refuses monitors larger than 1920×1080 or in portrait orientation. Other DPI settings, rotation, display changes, remote sessions, and lock/unlock recovery are unverified.

```powershell
.\scripts\run-p0.ps1 -ExpectedCommit '<full reviewed P0 commit SHA>'
```

Use a clean checkout of the reviewed commit. The script verifies that commit, restores locked dependencies, builds, runs portable contracts and the Windows mask API checks, then opens the lab. It records a separate transcript and environment report for each run in `lab-local/<commit>/<UTC-run>/`, including the source SHA and built DLL hashes. API failure stops the run before capture acceptance. Keep measurements and controlled phone evidence with that run and add trial counts/results to the acceptance matrices.

Dependency restore needs NuGet access; the running application makes no network requests. If your PowerShell policy prevents a script, run the individual commands below according to your existing policy, and record the commit/build identity separately.

1. Open the synthetic fixture, position it on the chosen monitor, and first confirm its uncovered QR scans with a phone.
2. Select that monitor and click **Start selected monitor**.
3. Test static masking, same-location Payload A/B replacement, movement, removal, and occlusion using the acceptance procedure in `evidence/P0/capture-overlay-results.md`.
4. Record physical-phone results separately from screenshots/capture. Capture exclusion may omit masks from screenshots and sharing.
5. Click **Export measurements** during each defined workload; keep any local lab captures in the ignored `lab-local/` directory. Export contains fixed counters and timings, never pixels or decoded payloads.
6. Click **Stop / remove masks**. No retry or autostart is scheduled. Close the app to end the lab.

Stop the prototype before locking, disconnecting a display, or switching sessions. Capture/display failures retire masks and report degraded coverage; they do not protect an unsupported desktop. Keep real enrollment, authenticator, pairing, Wi-Fi and authorization QR workflows out of this lab.

## Build and portable verification

```text
dotnet restore tests/QrGuard.Contracts/QrGuard.Contracts.csproj --locked-mode
dotnet build tests/QrGuard.Contracts/QrGuard.Contracts.csproj -c Release --no-restore
dotnet run --project tests/QrGuard.Contracts -c Release --no-build --no-restore
dotnet restore src/QrGuard.Windows/QrGuard.Windows.csproj --locked-mode
dotnet build src/QrGuard.Windows/QrGuard.Windows.csproj -c Release --no-restore
dotnet restore tests/QrGuard.Windows.Contracts/QrGuard.Windows.Contracts.csproj --locked-mode
dotnet build tests/QrGuard.Windows.Contracts/QrGuard.Windows.Contracts.csproj -c Release --no-restore
```

On Windows, also run `dotnet run --project tests/QrGuard.Windows.Contracts -c Release --no-build --no-restore`. Its four real HWND checks exercise the production renderer's opaque alpha, affinity readback, movement and cleanup. They acquire no screen pixels and perform no input simulation. See `evidence/P0/affinity-review.md` for the source/API concern and evidence limits.

On this Linux authoring host, MSBuild needs `-m:1 -nr:false -p:BuildInParallel=false` on build/restore commands. The Windows executables can be compiled here, but they must be run on Windows. GitHub Actions checks compilation and synthetic native QR contracts on Linux and Windows, and native mask APIs on its Windows runner; hosted results do not establish workstation/phone acceptance.

## Scope

The lab uses two WGC pool buffers, one reusable GPU staging texture, one active CPU pixel frame, and one replaceable pending decoded result. Every 200 ms it performs full-frame QR discovery, without periodically hiding a mask. There are at most 16 masks, at most 4096 payload bytes per observation, and 2048 retained timing samples. No raw capture pixels are written to disk; managed frame/payload buffers are cleared on release. Native decoder memory is released using its own disposal API; this is not a claim of forensic memory erasure.

Masks are layered windows with constant alpha 255 and no color key, preserving full opacity while satisfying the affinity readback API contract. They use `WDA_EXCLUDEFROMCAPTURE`, verified before showing, with tool-window, topmost, and no-activate styles. Clicks are consumed within the mask rectangle. A fresh missing observation retires the mask; a 750 ms freshness timeout also retires it and reports degraded coverage. API readback does not prove underlying pixels remain visible to WGC: that is a required Windows experiment.

P0 labels every decoded QR **Unverified · P0**, or **Unable to decode** when reliable candidate geometry is available. It provides no policy evaluation, Open/Copy action, TI, backend, email/document scanning, hooks, drivers, or security-tool configuration changes. The fixture writer is used only for controlled synthetic tests. The supplied policy files are reserved for P1.

Read `AGENTS.md`, `architecture.md`, and `execution-plan.yaml` before further work. Stop after this phase for review.
