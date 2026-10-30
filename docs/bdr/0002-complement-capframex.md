# 0002. Complement CapFrameX instead of replacing it

- Status: Accepted
- Date: 2026-10-30

## Context

CapFrameX is a respected, actively developed open-source capture and analysis tool. Recording captures accurately (PresentMon integration, overlays, hotkeys, sensor logging) is hard and already solved there. Its own analysis is chart-oriented rather than explanatory.

## Decision

daignostic does not record gameplay. It consumes CapFrameX captures and adds interpretation: rule-based findings, run-to-run comparison and optional AI explanation. Where CapFrameX offers a read-only integration (listing captures over MCP) we use it; we never drive its capture or settings.

## Consequences

- Much less to build and maintain, and accurate data from a trusted source.
- We depend on CapFrameX's file format; the reader is tolerant, tested, and isolated so format changes are cheap to follow.
- Metrics we show must match CapFrameX's definitions so users can cross-check (verified against real captures).
- Messaging is clear that we are independent and not affiliated.

## Success looks like

CapFrameX users see daignostic as a natural next step after recording, not a competitor.
