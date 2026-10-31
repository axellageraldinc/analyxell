# 0010. Findings must say what to do, not just what is wrong

- Status: Accepted
- Date: 2026-10-30

## Context

A list of problems leaves a non-expert stuck. The most valuable thing a diagnosis can give is the next step, in a sensible order, that can be tried and verified.

## Decision

Every finding above Info comes with concrete suggestions, and the page leads with a ranked "Start here" step. Suggestions are limited to in-game settings and simple, reversible checks, are phrased as things to try, and always point back to the verification loop: change one thing, record again, Compare. The app also looks for causes outside the capture (drive type, Windows settings, background programs, display setup) because many real fixes live there.

## Consequences

- The advice must be reviewed like code: it is tested, and kept to guidance that is safe for anyone to try.
- Where a cause is a hypothesis, the suggestion is a test of it, not a claim ([BDR 0006](0006-honesty-over-confidence.md)).
- The scope stays read-only ([BDR 0005](0005-mvp-scope-and-non-goals.md)): the app tells people what to change, it does not change it.

## Success looks like

A user with a stuttering game gets at least one specific, safe thing to try within a minute of opening a capture, and a way to prove it worked.
