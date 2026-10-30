# 0005. MVP scope and explicit non-goals

- Status: Accepted
- Date: 2026-10-30

## Context

A diagnostic tool can grow without end. A small, trustworthy first version beats a broad, shaky one.

## Decision

**In the MVP (0.1.0):**

1. Load and chart a CapFrameX capture (frame-time chart, FPS statistics, stutters, freezes, bottleneck).
2. Compare two runs.
3. AI findings through a user-configured OpenAI-compatible endpoint, with preview, citations and discarding of uncited claims.
4. Read the game's settings (Black Myth: Wukong first).
5. Read-only guarantee: nothing is changed without the user explicitly asking.
6. Installer and portable builds.

**Non-goals:**

- Recording captures or monitoring games live.
- Changing game, driver or system settings, including applying graphics presets.
- Automatic tuning or "one-click optimise".
- Cloud accounts, telemetry or a hosted service.
- Non-Windows platforms.

## Consequences

- A focused app that can be verified completely.
- Requests for auto-tuning are answered with the safe alternative: a clear suggestion the user applies and then verifies with **Compare**.
- Non-goals can be revisited only through a new BDR and, for writes, a new ADR ([ADR 0006](../adr/0006-read-only-by-design.md)).

## Success looks like

Each MVP item has an automated check (unit or UI test) and a documented user path.
