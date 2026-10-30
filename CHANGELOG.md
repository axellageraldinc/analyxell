# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

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
