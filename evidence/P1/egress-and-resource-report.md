# P1 egress/resources — 2026-10-01

**Windows runtime egress and reference-workstation resource acceptance are pending.** Source inspection and portable ownership tests are separate evidence, without invented measurements.

## Egress

| Evidence | Actual result |
|---|---|
| Source inspection | No runtime HTTP/DNS/TI/backend client or URL/redirect fetch. Policy reads reject UNC/network drives/reparse traversal. Browser navigation and clipboard writes use explicit action adapters only. |
| Portable fake adapters | Evaluation/details have no effects; restricted/stale actions refuse; Unverified requires issued warning/confirmation. No real navigation/clipboard use in tests. |
| Observed Windows process/DNS/network egress | Pending; no suitable workstation. Inspection is not network telemetry. |
| Development activity | SDK/NuGet/Git/GitHub/authoring validation downloads are separate from runtime acceptance. NuGet audit remains enabled. |

After building, observe the exact application and browser with existing approved local process/network tools; change no firewall/EDR settings. Record tool/version, filters, source/binary identity, interval, counts and confidence limits. Run 10-minute stopped, static, moving/occluded, invalid-policy/reload and details workloads **without Open**. Separate unrelated OS/framework/host traffic. Then perform one deliberate Open to a controlled HTTP(S) endpoint and attribute intentional browser egress separately. Keep only synthetic local evidence; upload no desktop/private URLs to external analyzers.

## Bounded design, verified in contracts/source

| Resource | Bound | Evidence |
|---|---|---|
| Capture | Two WGC buffers, one staging texture, one active CPU frame; max 1920×1080 BGRA | P0 source/build; workstation behavior pending |
| Pending result | One replaceable item; discarded bytes clear | P0 and P1 1000-publication burst contracts |
| Discovery cache | One bounded batch + 32-byte full-frame digest; native discovery each 1000 ms while frames arrive | P1 cache/clearing/source identity tests |
| Original bytes | 16 tracks ×4096 bytes; cached/active/pending batches each separately bounded likewise | P0 envelope tests; P1 owned-memory/clearing tests |
| Policy | ≤1 MiB, ≤1000 rules, ≤1 MiB +1 detection-byte stream read | P1 validation/stream tests |
| Actions | One pending warning; observation age ≤500 ms; retirement at 750 ms or absence | P1 actions, P0 expiry tests |
| Diagnostics | 128 fixed-code events; 2048 timings; bounded drop accounting | P1 redaction/burst tests and report source |

Bounds do not establish working set. Native/GPU disposal is not forensic erasure. No periodic unmasking, global hooks, fullscreen block, intrusive capture fallback or security-setting change was added.

## Windows measurement matrix

Reference: Windows 11 x64, ≥8 logical cores, ≥16 GB RAM, integrated graphics, one 1920×1080 monitor at 100% DPI initially. Fill exact hardware/build/driver/power, clean commit and DLL hashes before interpreting results.

| Measure/workload | Method / acceptance target | Actual |
|---|---|---|
| Unchanged desktop CPU | 10 min; CPU seconds / elapsed / logical processors ×100; average ≤0.5% total-system capacity | Pending |
| Normal scrolling/browsing CPU | 10 min controlled movement/occlusion and approved synthetic browsing; average ≤2% | Pending |
| Steady working set | 10 min samples; ≤150 MiB; state whether fixture included; separate GPU/native if available | Pending |
| Render-to-visible mask | Controlled physical-display timestamp trials; p95 ≤500 ms; sample count/misses | Pending |
| Absence-to-physical removal | Controlled trials; p95 ≤500 ms; distinguish 750 ms stall expiry | Pending |
| Motion visible gaps | Physical phone/video observation; count/duration/failures | Pending |
| Real focus/unrelated input | Typing/click/keyboard scenarios; zero unintended interception | Pending |
| Stop/exit/restart | Repeated trials; no orphan HWNDs; worker/capture resources released | Pending |

Export contains process CPU, one-second sampled peak working set, bounded capture-timestamp-to-mask-API p95, accepted/rejected frames, payload changes, retirement, full/coalesced passes and fixed events. That p95 is **not** render-to-visible, phone or absence-to-physical-removal evidence. Record those externally; distinguish peak from steady memory. No measured G07 deviation exists yet. Preserve/investigate a failure before proposing any resource/safety contract change.
