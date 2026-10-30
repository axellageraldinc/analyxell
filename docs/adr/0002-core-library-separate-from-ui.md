# 0002. Analysis lives in a UI-free core library

- Status: Accepted
- Date: 2026-10-30

## Context

Correctness of the numbers and of the diagnosis is the product. UI code is slow to test and easy to break; analysis code is pure and testable.

## Decision

All parsing, statistics, detection, rules, AI message building and settings live in `Analyxell.Core` (plain `net10.0`, no UI references). The WinUI project only presents results.

## Consequences

- The core has fast unit tests (synthetic data, no UI, no Windows App SDK) and was validated against CapFrameX's own numbers for real captures.
- UI code stays thin: view models format results, they do not compute them.
- The core could be reused by a CLI or a different front end.
- A rule for contributors: if you are computing something in a view model, it belongs in the core.
