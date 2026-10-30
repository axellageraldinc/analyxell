# 0010. Game settings come from read-only per-game readers

- Status: Accepted
- Date: 2026-10-30

## Context

Findings are far more useful when they know the game's settings ("ray tracing is on while the game hitches"). Games store settings in their own formats, and the numbers in those files are often internal ids, not the names shown in the game's menu. Early in this project a quality level was mistaken for a preset name, which would have produced wrong advice.

## Decision

Each supported game has an `IGameConfigReader` that locates its config file, parses it **read-only**, and returns `GameSetting` items. A value is labelled in plain words only when its meaning is certain (for example a 0/1 switch known to mean off/on). Everything else is returned as a *raw id* with `IsRaw = true`, shown as "(raw id)", and never described as a preset name. The reader also reports when the file was last written, and the UI states that the file reflects the last save, not necessarily the captured session.

The first reader supports Black Myth: Wukong. Unsupported games simply show no settings section.

## Consequences

- No wrong labels; honest gaps instead.
- Adding a game is a small, testable class; see [architecture](../architecture.md#extending).
- Rules that use settings (currently ray tracing) are written as hypotheses to test with a comparison run, not as conclusions.
