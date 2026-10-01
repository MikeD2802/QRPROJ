# Hosted CI evidence — 2026-10-01

[Run #7](https://github.com/MikeD2802/QRPROJ/actions/runs/36916090510) completed successfully on Linux and Windows Server 2025. Both jobs restored/build all four projects and passed the **23 P0 + 67 P1** portable contracts with zero build warnings/errors. The Windows job also passed PowerShell parser checks and **four P0 + two P1** native HWND/synthetic-message contracts.

Implementation head: `2800926eb9734245990d7ef412b9fe86c1f0c5c8`. Actual pull-request checkout: `29136899d5f46592e9bd02564319fe7a3dabd45b`, merging that head into unchanged base `1f6fdf33021f1cb36541729a63be02d796735541`. The locally tested/published implementation tree was `abb42bed2ab68e22a0563975861f099eff0ee365`. Any following evidence-only handoff commit retains the same tested source hashes; verify `source-hashes.sha256` and inspect the PR's latest checks separately.

Windows image: `windows-2025-vs2026`, version `20260925.250.1`. Linux image: Ubuntu 24.04, version `20260927.320.1`. SDK: 10.0.401.

**This is API/portable execution evidence, not Windows 11 workstation WGC, WPF accessibility, actual pointer/keyboard input, physical opacity/phone resistance, sharing, runtime egress or resource evidence.** All those acceptance rows remain pending, and G01 remains visible. No endpoint security settings were changed.

Selected actual log lines follow. Full authoritative logs remain linked to each job.

### contracts (windows-latest)

Job: [110550289248](https://github.com/MikeD2802/QRPROJ/actions/runs/36916090510/job/110550289248) · success. Head metadata: `2800926eb9734245990d7ef412b9fe86c1f0c5c8`.

```text
2026-10-01T19:41:15.9346149Z HEAD is now at 2913689 Merge 2800926eb9734245990d7ef412b9fe86c1f0c5c8 into 1f6fdf33021f1cb36541729a63be02d796735541
2026-10-01T19:41:42.8351839Z     0 Warning(s)
2026-10-01T19:41:42.8352160Z     0 Error(s)
2026-10-01T19:41:44.8409974Z PASS: 23 P0 portable contracts. Windows compositor, physical phone and sharing were not tested.
2026-10-01T19:41:48.7838996Z     0 Warning(s)
2026-10-01T19:41:48.7839303Z     0 Error(s)
2026-10-01T19:41:50.7987734Z PASS: 67 P1 portable contracts. Windows UI/input, compositor, egress and workstation resources were not tested.
2026-10-01T19:41:59.1560911Z     0 Warning(s)
2026-10-01T19:41:59.1561624Z     0 Error(s)
2026-10-01T19:42:03.1417437Z     0 Warning(s)
2026-10-01T19:42:03.1417796Z     0 Error(s)
2026-10-01T19:42:05.1169778Z PASS: visible mask has layered/no-activate/tool/topmost styles, alpha 255, no color key, and affinity 0x11.
2026-10-01T19:42:05.1204895Z PASS: six policy states expose fixed window labels and retain opacity/affinity; render never requests details.
2026-10-01T19:42:05.1208178Z PASS: synthetic Win32 mouse messages preserve no-activate handling and request only current track details after down/up.
2026-10-01T19:42:05.1218367Z PASS: movement reuses the mask window and retains opaque alpha and affinity readback.
2026-10-01T19:42:05.1239170Z PASS: retirement destroys the native mask window.
2026-10-01T19:42:05.1281838Z PASS: disposal destroys the recreated mask window.
2026-10-01T19:42:05.1283164Z PASS: 4 P0 and 2 P1 Windows mask API contracts. Synthetic messages are not real UI/input evidence. Capture, phone resistance, real input, sharing and performance were not tested.
```

### contracts (ubuntu-latest)

Job: [110550289346](https://github.com/MikeD2802/QRPROJ/actions/runs/36916090510/job/110550289346) · success. Head metadata: `2800926eb9734245990d7ef412b9fe86c1f0c5c8`.

```text
2026-10-01T19:41:10.4623307Z HEAD is now at 2913689 Merge 2800926eb9734245990d7ef412b9fe86c1f0c5c8 into 1f6fdf33021f1cb36541729a63be02d796735541
2026-10-01T19:41:18.4301800Z     0 Warning(s)
2026-10-01T19:41:18.4302179Z     0 Error(s)
2026-10-01T19:41:19.4019530Z PASS: 23 P0 portable contracts. Windows compositor, physical phone and sharing were not tested.
2026-10-01T19:41:22.6517941Z     0 Warning(s)
2026-10-01T19:41:22.6518401Z     0 Error(s)
2026-10-01T19:41:23.7388241Z PASS: 67 P1 portable contracts. Windows UI/input, compositor, egress and workstation resources were not tested.
2026-10-01T19:41:28.6242153Z     0 Warning(s)
2026-10-01T19:41:28.6242506Z     0 Error(s)
2026-10-01T19:41:31.2248093Z     0 Warning(s)
2026-10-01T19:41:31.2248432Z     0 Error(s)
```

