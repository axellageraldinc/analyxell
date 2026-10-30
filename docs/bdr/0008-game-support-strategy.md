# 0008. Generic analysis first, per-game settings readers second

- Status: Accepted
- Date: 2026-10-30

## Context

Frame-time analysis works for every game. Understanding a game's *settings* needs per-game knowledge, which is costly and must be exactly right.

## Decision

Every game gets the full analysis. Settings-aware findings are added game by game through small readers, starting with Black Myth: Wukong because the maintainer plays it and it exercises ray tracing and DLSS. Readers are accepted into the project only with tests, only label values whose meaning is verified, and are community-contributable.

## Consequences

- No game is unsupported for the core value.
- Quality of settings advice grows with contributions rather than guesswork.
- The README lists supported games so expectations are clear.

## Success looks like

A contributor can add a reader for their favourite game in a single, small, reviewed pull request.
