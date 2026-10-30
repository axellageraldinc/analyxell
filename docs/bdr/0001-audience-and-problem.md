# 0001. Build for gamers who feel stutter but cannot explain it

- Status: Accepted
- Date: 2026-10-30

## Context

PC gamers notice when a game hitches, but finding the cause means reading frame-time graphs, sensor logs and forum threads, and it is easy to reach a wrong conclusion. Capture tools show data; they do not say what it means. Experts can interpret it; most players cannot.

## Decision

Target players who already (or can easily) record captures and want a plain-language answer to "what is wrong and what should I try?". Optimise for clarity over exhaustive detail: a few cards, a short list of findings with their evidence, and one suggested change at a time.

## Consequences

- Findings are written for non-experts and always show the measurements behind them.
- Power-user features (custom metrics, scripting, large-scale benchmarking) are not priorities.
- The experience must work for someone with a single capture and no AI configured.

## Success looks like

A player with a stuttering game can open a capture and, within a minute, know whether to look at graphics settings, background load or hardware limits, and can verify a fix with a second capture.
