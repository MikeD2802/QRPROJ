# Available P0 verification

Final checks recorded on 2026-10-01 in the Linux environment described in `environment.md`.

| Check | Outcome | Evidence |
|---|---|---|
| Windows x64 target compilation | PASS, 0 warnings / 0 errors; executable not run | `windows-target-build.txt` |
| Portable project compilation | PASS, 0 warnings / 0 errors | `portable-build.txt` |
| Native QR and lifecycle contracts on Linux | PASS, 23 checks | `portable-contracts.txt` |
| Locked dependency restore | PASS for both contract and Windows projects | Reproducible with README commands / committed locks |
| JSON / YAML syntax | PASS for execution state, plan, workflow, fixtures and package locks | Parsed using Python JSON / YAML in authoring environment |
| Whitespace / diff checks | PASS | `git diff --check` |
| Source review | No network, shell launch, clipboard writes or global hook calls; Win32 imports are supported capture/window/paint APIs | Source inspection; this is not a packet capture or Windows execution test |
| Windows compositor and under-mask capture | NOT RUN | `capture-overlay-results.md` |
| Physical phone | NOT RUN | `physical-phone-results.md` |
| Focus/input and sharing | NOT RUN | Acceptance matrices |
| Windows resource and physical latency baseline | NOT RUN | `compatibility-and-baseline.md` |

The portable checks use the native ZXing-C++ library on Linux, not a mocked decoder. They cover original payload bytes, native decoding into tracking, stationary replacement, motion association, disappearance, stale/out-of-order/session/display results, bounded geometry and observation counts, reliable undecodable geometry, mask margins, slot disposal, zeroing, sensitive synthetic payloads, full-frame boundary discovery and QR-only filtering. Simulated occlusion/removal and rotated QR geometry are not tests of actual Windows occlusion or monitor rotation.

Policy evaluation, URI launch validation, schema semantics, policy-bound actions and runtime packet-capture proof belong to P1 and are not implemented or claimed here. P0 remains blocked by G01. Stop for review; do not start P1.
