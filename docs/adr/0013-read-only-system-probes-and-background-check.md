# 0013. Read-only system probes and an on-demand background check

- Status: Accepted
- Date: 2026-10-30

## Context

A capture lacks much of what explains a problem: which kind of drive the game runs from, how full it is, Windows gaming settings, the display mode, shader cache state, and which other programs compete for CPU, disk and GPU. Vendor settings such as G-SYNC state or driver options cannot be read safely without vendor APIs or admin rights.

## Decision

1. **System probes** read, never write: a few registry values (Game Mode, hardware GPU scheduling, windowed variable refresh, active power plan), the primary display mode, the number of monitors, the size of the GPU shader cache folders, and the game's drive type and free space. Drive type and connection come from two read-only storage queries (does the drive seek, and its bus type), made on the volume with no data access. The game's install folder comes from the user, or from Steam's own library manifests when it can be found there. All of it sits behind an `ISystemProbe` interface so tests use a fake.
2. **Things that cannot be read** (G-SYNC or FreeSync state, driver options such as shader cache size or power management mode, monitor model and maximum refresh) are asked from the user in an optional setup form. They are labelled "entered by you", used as given, and never presented as verified.
3. **Background check** is on demand, never automatic. For 10 seconds it samples every process's CPU time, disk I/O counters, memory and (when Windows exposes it) GPU use, using query-only process access. It never closes, suspends or changes anything. Results are matched against a small curated list of programs that commonly hurt games (overlays, recorders, browsers, launchers, cloud sync, security scans, vendor software, VMs).
4. **Privacy of names.** Process names are shown on the user's screen. AI messages receive only anonymous totals and category names (for example "Browser, Overlay") unless the user turns on sharing names in Settings. Findings that mention names are marked local-only and are excluded from AI messages and exports.
5. The result reflects the moment the check ran, and the UI and findings say so.

## Consequences

- Much richer and more accurate findings (an HDD install, a nearly full drive, an empty shader cache, a Power saver plan, a recorder eating the GPU encoder) without any write access or elevated rights.
- Some signals are indirect or approximate: variable refresh is inferred from the refresh rate moving; per-process GPU use depends on Windows' performance counters and is shown as "n/a" when unavailable.
- Reads that look sensitive (process list, registry) are documented in [privacy](../privacy.md), and nothing is collected until the user asks for it.
