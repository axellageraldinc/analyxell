# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- Game settings now belong to one capture. A game's config file only holds its latest settings, so an older capture was being judged by newer settings (and so was the AI). A config saved after the capture was recorded is no longer used; you can say "these are the settings I played with" or enter them yourself, and they are kept for that capture only.
- Charts with several lines: press a name in the legend to hide or show that line (at least one stays visible); the scale follows what is shown.
- The AI can ask for what it is missing without asking you a question: it names checks from a short list daignostic offers (background programs, PC setup, game settings), they appear as buttons you press, daignostic runs them read-only, and the answer is marked out of date until you ask again. Anything the AI names that was not offered is dropped.
- The AI is told what a capture cannot show (driver VSync, actual tearing, why a spike happened) so it reads the findings the way the rules do.
- CPU bottlenecks are now spelled out: "The CPU is holding the GPU back", whether one thread is the limit, and stretches where the CPU limited an otherwise GPU-bound run.
- New diagnoses: performance that declines over a run (with the memory or heat growth that fits), hitches that fade (shader and loading), single-channel or slow memory, Resizable BAR, Game Mode, background recording, desktop-compositor presentation and Memory integrity. See docs/problems-and-causes.md.
- Your VSync setup now counts: VSync set in the GPU driver cannot be seen in a capture, so the tearing findings believe what you entered.
- Analyze page redesigned to show less at once: a one-line verdict, the metric cards, then tabs (Overview, Ask AI, Findings, PC and setup, Background programs). Findings that need attention come first and the rest are folded away; the overview shows the top three steps.
- Compare page: pick the metric to overlay (frame time, FPS, latency, GPU load, temperature, clock, memory, power); the two runs are now yellow (A) and blue (B), named in the legend, with bold one-second averages over faint per-frame lines.
- Asking an AI shows progress (elapsed time, what it is doing, hints when slow) with Cancel, and failures explain what went wrong and what to try (endpoint, key, model, timeout, unreadable answer) with Try again and a shortcut to Settings. Requests give up after 2 minutes.

- The chart can mark more than freezes: stutters, FPS drops (seconds well below the typical frame rate), dropped frames and where tearing was possible. Switch each on or off under the chart; freezes are on by default.
- Test AI connection shows how long it has been waiting, when it gives up, and has Cancel.
- The UI tests set your real settings aside and restore them, so running them never touches your AI setup.

### Fixed

- Chart legend swatches were not centred on their text.

- A blank button in the page header where the CapFrameX button should be.

### Added

- CapFrameX detection with Open CapFrameX / Get CapFrameX (official GitHub releases) and a way to point at the portable version.
- The AI endpoint can be given with or without /v1 (providers document both), and 404/405 errors now say what to check.
- Settings problems are shown under the field they are about instead of at the top of the page.
- PC setup is reachable from the page header and from the PC details.
- New logo.
- Findings now come with concrete suggestions and a ranked "What you can do" list (deterministic, no AI needed).
- More diagnoses: low FPS, GPU/CPU/VRAM heat, system memory pressure, tearing conditions and display sync, input latency, motion pacing, game on a hard disk, nearly full drive, small shader cache, Power saver plan, display below its maximum refresh.
- Chart views for frame time, FPS, GPU and CPU load, temperatures, clocks, memory, power and latency.
- PC specs: a one-line summary with full details on demand (graphics, processor, memory, display, board and Windows, game drive).
- Read-only system probes (Windows gaming settings, display mode, drive type and free space, shader cache size) and an optional setup form for what cannot be read (G-SYNC/FreeSync, driver options, monitor, install folder).
- Background check: which other programs use CPU, disk and GPU, and well-known interferers, on demand.
- Game settings for any game: type, paste, or import from a config file with your own AI, with every value checked against the file.
- AI is optional and the main feature of the page; export an analysis pack for your own AI; the AI answers once and never asks questions, and reports what it could not tell.
- Settings: model name (with List models), API key assurance and memory-only option, CapFrameX MCP tutorial with plain-language connection diagnosis.
- Analyze page: FPS statistics (average, P1, 1% and 0.1% low averages, matching CapFrameX), stutter and freeze detection, GPU/CPU/thermal/power bottleneck verdict, and a frame-time chart with a one-second average and freeze markers.
- Rule-based findings that cite the measurements behind them, including what the sensors did around each freeze (GPU load, drive activity, memory).
- Compare page: run-to-run comparison with scored and context-only measures and an overlay chart.
- Optional AI explanation through any OpenAI-compatible endpoint: exact-message preview, no tools, citation checking, and discarding of uncited claims.
- Game settings reader for Black Myth: Wukong (read-only).
- Optional read-only CapFrameX MCP integration for listing captures.
- Markdown report export through a save dialog.
- Drag and drop, command-line capture paths, and a recent-captures list.
- API key stored with Windows DPAPI; settings file holds no secrets.
- Installer (MSIX) and portable (zip) packaging scripts.
- Unit tests, a UI smoke test with a mock AI endpoint, secret scanning, and CI.
- Documentation: user guide, architecture, privacy, 11 ADRs, 8 BDRs, contributing, security policy, code of conduct.

[Unreleased]: https://github.com/axellageraldinc/daignostic/commits/main
