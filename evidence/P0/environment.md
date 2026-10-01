# P0 authoring and verification environment

Status: **G01 blocked**. No Windows workstation, desktop session, GPU compositor, or physical phone is attached to this execution environment. No Windows capture attempt was performed here.

| Field | Observed value |
|---|---|
| Host OS | Ubuntu 24.04, Linux x86_64 |
| Kernel | 6.18.44 |
| SDK | .NET 10.0.401 |
| Host runtime | .NET 10.0.12 |
| Source base | `5fdbf2a11c3f1a3e4ddf6de608b2d065b56675d0` |
| Windows target | `net10.0-windows10.0.19041.0`, runtime `win-x64` |
| Windows execution | Not performed |
| Phone model / camera app | Not available |
| Windows CPU, RAM, GPU, driver, power plan, DPI | Not available |

The Windows target compilation and native Linux QR contracts are separate evidence types. Neither satisfies G01.

On the test workstation use a clean checkout and `scripts/run-p0.ps1 -ExpectedCommit '<full reviewed P0 commit SHA>'`. It calls `scripts/collect-p0-environment.ps1` after building and saves a separate UTC run directory. The environment report records the exact commit, clean-tree status and lab/test DLL hashes. Add the chosen display resolution, DPI percentage, OS support status, phone model/app, meeting-tool versions, workload and trial counts. The environment collector excludes user names, computer names, serial numbers and tenant/device identifiers. Keep local synthetic camera recordings in `lab-local/`; do not upload a real desktop recording.
