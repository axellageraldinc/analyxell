# 0004. Deterministic analysis measures; the AI only explains, with citations

- Status: Accepted
- Date: 2026-10-30

## Context

Language models are good at explaining and poor at arithmetic and at admitting uncertainty. Early manual analysis in this project also showed how easy it is to state a wrong cause confidently (for example, claiming the GPU was idle during freezes when the data showed it was busy). Low-cost models make this worse.

## Decision

1. Plain code computes every number, detection and bottleneck verdict, and evaluates rules that produce *findings*. Every finding lists the fact keys it rests on.
2. The AI receives only the flat facts table and the findings, never raw frame data.
3. The system prompt forbids inventing numbers, requires a citation per claim (fact key or `finding:ID`), requires hypotheses to be labelled, allows "inconclusive", and limits advice to in-game settings and simple checks.
4. The reply is parsed strictly. Any claim whose citations do not exist in the sent data is discarded and counted ("N claims were discarded").

## Consequences

- The app is useful with no AI at all; the AI adds explanation and prioritisation.
- Cheap models work, because they interpret a short structured summary instead of reasoning over numbers.
- Hallucinated claims cannot reach the user unless they cite real evidence; fabricated citations are dropped.
- Adding a rule means adding facts it can cite; a test fails if a finding cites a fact that does not exist.
- Citations prove traceability, not truth: a model can still draw a poor conclusion from real facts. The UI therefore shows evidence beside every claim.
