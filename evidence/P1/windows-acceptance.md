# P1 Windows acceptance — pending

No supported Windows 11 workstation or phone is attached. Every row below is **pending** with no inferred pass or trial count. Hosted native HWND/synthetic-message CI is separate API evidence. Continuous physical masking and under-mask WGC remain unverified.

| Scenario | Expected behavior | Status / evidence |
|---|---|---|
| P0 phone control/covered QR | Control decodes; continuous mask resists defined phone trials | Pending — `evidence/P0/physical-phone-results.md` |
| P0 under-mask capture/motion/removal/occlusion | Observe while masks stay present; follow and retire promptly | Pending — `evidence/P0/capture-overlay-results.md` |
| P0 screenshot and both sharing modes | Characterize each; no remote-protection claim | Pending — `evidence/P0/compatibility-and-baseline.md` |
| All policy labels | Truthful states; mask remains opaque through evaluation | Pending |
| Deliberate details | No panel on render/move; click or keyboard entry opens one accessible panel | Pending |
| IDNA/bidi/default privacy | Prominent ASCII host; values/secrets hidden; no bidi reordering | Pending |
| Approved Open | Deliberate action opens original validated target | Pending — controlled target only |
| Unverified warn-and-open | No/cancel never navigates; issued warning/Yes required once | Pending |
| Unknown block | Navigation disabled/refused; Copy remains a separate explicit validated-URL action | Pending |
| Restricted states | Blocked/sensitive/unsupported/undecodable Open/Copy disabled and refused | Pending |
| Same-location replacement during warning | Earlier destination cannot launch/copy; stale details invalidate | Pending |
| Motion/removal/stale action | Old ticket refused; 500 ms action freshness; prompt retirement | Pending |
| Policy edit during warning | Same numeric revision still invalidates; invalid edit retains snapshot | Pending |
| Policy edit between timer ticks | Action-time reload observes new accepted content before dispatch | Pending |
| Actual focus/hit testing | Rendering never activates; unrelated typing/clicks work; covered clicks consumed | Pending |
| Keyboard/screen reader | Main selection and details have names and normal tab/keyboard access | Pending |
| Saturation/unlocated/capture fault | Coverage is degraded/unavailable separately from policy; no invented mask/fullscreen block | Pending |
| Stop/exit | Masks removed, actions invalid, resources released; stuck worker honestly reported | Pending |

## Controlled procedure

1. Clean checkout of the reviewed P1 head on Windows 11 x64: `scripts/run-p1.ps1 -ExpectedCommit '<40-character head SHA>'`. Keep UTC transcript, source/DLL hashes and hardware/build/driver/power/display/DPI fields. An API failure stops before capture acceptance.
2. Use synthetic reserved-domain fixtures only. Verify the uncovered phone control first. Exercise Payload A/B, Sensitive, Unverified, Unsupported and IDNA. Keep real enrollment/pairing codes out. Unlocatable fixtures are coverage misses, not protected QR codes.
3. Type in the fixture while masks change/move/remove. Test mask clicks, unrelated controls, main-window keyboard entry and details tab order/screen-reader names. Record actual foreground and input results; styles and synthetic messages are insufficient.
4. Leave an Unverified warning open while replacing/removing/moving the QR and editing a separate local policy copy without increasing `revision`. Confirm: earlier targets must not launch. Test invalid JSON retention and unknown block. Do not edit the clean source checkout during acceptance.
5. Separate phone recordings of the physical display from agent screenshots/share output. Keep controlled evidence locally under ignored `lab-local/`; record summaries/trial counts here after review. Fill egress/resources separately and verify no masks survive Stop/exit.

No G02/G07 contract failure was observed in this Linux authoring session. Missing experiments are not success or failure. A measured failure requiring a new capture API/contract or resource/safety requirement needs one focused decision with retained evidence. P2 display/session recovery and rollout are outside scope.
