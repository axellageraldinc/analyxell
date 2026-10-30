# 0003. Local-first and private; AI is optional and user-controlled

- Status: Accepted
- Date: 2026-10-30

## Context

Captures describe a user's hardware and play sessions. Many gamers are wary of tools that upload data, and AI services vary widely in privacy and cost.

## Decision

All analysis runs on the user's PC. AI is opt-in: the user supplies the endpoint (a local or self-hosted gateway is the model case), sees the exact message before it is sent, and nothing is sent until they press a button. No telemetry, no accounts, no hosted backend run by this project.

## Consequences

- Users keep control and can verify what leaves their machine ([docs/privacy.md](../privacy.md)).
- Quality of the AI part depends on the model the user chooses; the design compensates with citations and discarding uncited claims ([ADR 0004](../adr/0004-deterministic-analysis-ai-explains.md)).
- We cannot offer "one-click AI" without the user configuring an endpoint. That is an accepted trade-off.

## Success looks like

Privacy-conscious users trust the tool enough to use it, and a reviewer can confirm the claims by reading the code and the tests.
