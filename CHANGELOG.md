# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

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
