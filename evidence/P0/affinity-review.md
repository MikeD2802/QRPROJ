# P0 affinity readback review

Review input: PR #1 at `5991a32568a8080f357ad3741f43118bc06511e6`, 2026-10-01. P0 remains in review and blocked on G01; no dependent phase is authorized to start.

## Source/API concern and change

The reviewed renderer created a non-layered top-level mask but required `GetWindowDisplayAffinity` to succeed before display. Microsoft documents that readback succeeds only for layered windows while DWM composes the desktop. Failure destroyed the mask and cancelled processing. Compilation and portable QR contracts could not exercise this Win32 requirement.

The renderer now creates the mask with `WS_EX_LAYERED` in addition to its existing no-activate, tool-window and topmost styles. It calls `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` before setting exclusion affinity. Alpha 255 is opaque; the flags contain no color key, and the window uses no click-through style. The existing opaque GDI paint remains in use. Both affinity API calls and the exact `WDA_EXCLUDEFROMCAPTURE` value must still pass before display. Any initialization failure destroys the unshown HWND.

This resolves the source/API prerequisite; workstation compositing, actual physical opacity and WGC visibility remain hypotheses requiring execution evidence.

Primary documentation checked 2026-10-01:

- [GetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowdisplayaffinity): layered-window and DWM prerequisite.
- [SetLayeredWindowAttributes](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes): alpha 255 opacity and independent color-key flags.
- [Layered windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows): GDI redirection after setting attributes and shape/transparency hit testing.
- [SetWindowDisplayAffinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity): own top-level-window requirement, supported exclusion value and capture limitations.

## Regression checks and their limits

`tests/QrGuard.Windows.Contracts` links the production mask renderer and native declarations. On Windows it creates real HWNDs and checks:

1. Visible mask has layered/no-activate/tool/topmost styles, constant alpha 255, no color-key or click-through style, exact exclusion-affinity readback and expected bounds.
2. Movement reuses the same HWND, revises its bounds and preserves opacity/affinity.
3. Retirement destroys the mask.
4. Disposal destroys a recreated mask.

The test inspects only windows on its own thread. It does not acquire desktop pixels or simulate user input. Its per-monitor DPI manifest is the same one used by the lab. CI runs these API contracts only on the Windows runner and parses the PowerShell harness; Linux compiles the test without executing Win32 calls. Preserve the exact workflow SHA and job logs when interpreting a CI outcome. Hosted API tests are distinct from real workstation capture/focus/input and physical-phone evidence.

Linux build and 23 portable-contract rerun results are recorded in `affinity-validation.txt`. Windows API execution is not performed on the Linux authoring host. Actual acceptance remains pending in `capture-overlay-results.md`, `physical-phone-results.md` and `compatibility-and-baseline.md`.

## Exact-commit workstation collection

On a supported Windows 11 x64 workstation, use a clean checkout of the full reviewed P0 commit SHA and run:

```powershell
.\scripts\run-p0.ps1 -ExpectedCommit '<full reviewed P0 commit SHA>'
```

The script checks checkout identity, builds, records the source SHA and DLL hashes, and saves a timestamped local transcript/environment report. It stops before capture acceptance if the Windows mask API checks fail. Retain a failed report as evidence; do not retry indefinitely or remove the affinity check to make it pass.

Follow all acceptance rows, including continuous A/B replacement without hiding masks, 10-minute static freshness, physical-phone positive controls and covered trials, input/focus, separate desktop/source-window sharing, and Windows resource/physical-latency workloads. Associate each result with the same source SHA and run directory. Keep controlled phone footage and measurements local; do not upload real desktop content. No result in this file marks G01 complete.
