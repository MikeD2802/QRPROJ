# P0 capture and mask acceptance

**Actual Windows results: pending.** The following is a reproducible experiment, not a completed test report. Source review and portable contracts establish bounded state behavior; they do not establish compositor behavior.

Use one Windows 11 x64 landscape display at 1920×1080 / 100% DPI. Launch the lab manually as a standard user. Do not change EDR, DLP, capture protections, meeting-tool settings, or organizational policy to make a test pass.

Use a clean checkout and `scripts/run-p0.ps1 -ExpectedCommit '<full reviewed P0 commit SHA>'`. Before launching the capture lab, its four Windows API checks must pass for layered alpha 255, exclusion-affinity readback, movement and cleanup. These are API checks, not phone or under-mask-capture evidence. The run's `environment.json` records the exact source commit and DLL hashes; record its run directory with every acceptance result.

| Scenario | Procedure and pass evidence | Actual result |
|---|---|---|
| Uncovered positive control | Open synthetic Payload A. Phone must decode it before capture starts. Clear any cached phone scan popup. | Pending |
| Static opaque mask | Start the selected monitor. Confirm full QR and quiet-zone coverage with an external phone camera after the mask appears. Record initial exposure duration separately. | Pending |
| Observe beneath persistent mask | While the mask stays visible continuously, replace A with B at the same position. Lab payload-change count must increase without a mask gap. Repeat A/B 30 times; phone video must show no periodic hiding. | Pending |
| Static freshness | Leave the covered fixture unchanged for 10 minutes. Check whether WGC still yields fresh frames; any mask expiry/degraded status is a failure to satisfy static coverage, not a pass. | Pending |
| Motion / scrolling | Use fixture Move / stop, move its window, then scroll a synthetic QR page. Record every visible gap and p95 mask delay. Do not infer zero gaps from eventual tracking. | Pending |
| Removal | Use Remove QR and close the fixture. Observe removal on the first accepted empty observation; measure p95 over 30 removals. | Pending |
| Occlusion | Cover the fixture with an ordinary opaque window and remove that window. Check retirement, re-detection and any obstruction of unrelated controls. | Pending |
| Focus / typing | Focus the fixture text box or another ordinary app. Keep typing during initial mask, movement, A/B change and retirement. Zero unexpected focus changes or missing/intercepted keystrokes. | Pending |
| Mouse hit testing | Put an ordinary control adjacent to the QR and a controlled clickable target under it. Outside clicks work normally; mask clicks never activate the hidden target or steal focus. | Pending |
| Stop / exit | Stop capture, close the lab, and terminate only the test process. No surviving blocking HWNDs; coverage loss is recorded. | Pending |
| Display / capture error | In a controlled lab only, exercise capture denial or a display change. Stale masks retire and the UI reports degraded coverage; no fallback/hook or infinite retry. | Pending |

Record each row with OS/GPU/DPI, tested commit, trial count, observation method, failures and evidence reference. Agent captures can exclude the mask, so physical video and captured output must be kept as distinct evidence.

The prototype checks `SetWindowDisplayAffinity` / `GetWindowDisplayAffinity` before showing masks. This is API configuration, not proof that WGC returns original pixels. If WGC returns black/masked pixels, stops yielding fresh static observations, or cannot track under a persistent mask, retain the result and evaluate G02/G07. Do not hide masks to rescan. Make at most two materially different bounded capture attempts before requesting an architectural decision.

P0 currently performs full-frame decode every 200 ms, rather than a separate high-rate tracking pass. Its 750 ms freshness timeout favors desktop availability during a stall; it is not an accepted removal-latency target. Small, partial, blurred, very fast, protected, or geometry-free observations may remain undetected/unmasked. Native QR candidates without a nondegenerate in-frame quadrilateral are reported as unavailable, not assigned a guessed mask.
