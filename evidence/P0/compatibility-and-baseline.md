# P0 compatibility and resource baseline

**All actual Windows/share/performance results are pending.** Existing architecture numbers remain targets, not measurements.

Record sharing output using controlled synthetic windows only. Keep the physical-display phone view separate from the captured output. Do not modify existing tool settings to pass a scenario.

| Capture/share path | Mask visible to viewer? | Original QR visible/decodable? | Existing tool still works? | Tested version / result |
|---|---|---|---|---|
| Local physical display | Pending | Pending | Pending | Pending |
| Windows screenshot / Snipping Tool | Pending | Pending | Pending | Pending |
| Teams entire-desktop share | Pending | Pending | Pending | Pending |
| Teams fixture/source-window share | Pending | Pending | Pending | Pending |
| Zoom entire-desktop share | Pending | Pending | Pending | Pending |
| Zoom fixture/source-window share | Pending | Pending | Pending | Pending |
| Existing screen recording tool | Pending | Pending | Pending | Pending |
| Remote support / remote desktop / VDI | Unverified; outside P0 claim | Unverified | Unverified | Not tested |

Design hypothesis: affinity exclusion may expose the original QR in supported capture paths. It is global capture exclusion, not exclusion limited to this agent. Source-window sharing is a distinct path. No screenshot/share protection is claimed. If protection of remote viewers becomes required, stop at G02 before additional product work.

| Measurement | Workload / method | Target | Actual |
|---|---|---|---|
| CPU idle | 10 minutes of unchanged controlled desktop; process CPU delta / wall time / logical CPUs | Mean ≤0.5% total capacity | Pending |
| CPU browsing/scrolling | 10 minutes, fixed synthetic QR workload; same method | Mean ≤2% total capacity | Pending |
| Working set | 1-second process samples during each workload; distinguish peak and steady state | Steady state ≤150 MiB | Pending |
| Static render-to-visible-mask | External phone video at known frame rate; at least 30 appearance trials | p95 ≤500 ms | Pending |
| Removal after confirmed absence | External phone video; at least 30 removal trials | p95 ≤500 ms | Pending |
| Motion exposure | Phone video; count and duration of visible gaps | Record all gaps and failures | Pending |
| Focus / unrelated input | Scripted typing and click scenarios | Zero unexpected changes/interceptions | Pending |

The lab's explicit JSON export reports process CPU (including the lab and fixture), peak sampled working set, accepted/rejected frame counts, payload-change/retirement counters, average decode time and the most recent 2048 **capture-timestamp-to-mask-API** samples. It does not measure compositor presentation, source-render-to-mask latency, physical-phone readability, or mask-removal p95. Pair it with external physical observations. Export while each workload is active, or immediately after Stop; timing is frozen at Stop. A fresh Start resets the workload counters.

The prototype's fixed 5-pass/sec full-frame loop establishes a P0 baseline. Adaptive cadence, mixed DPI/multiple monitors, lifecycle recovery, accessible details and production packaging are later phases. If resource or interaction requirements fail, preserve the measured result and resolve G07 before a material tradeoff. There is no automatic API fallback, retry loop, or security-control exclusion.
