# 0006. Honesty over confidence in every diagnosis

- Status: Accepted
- Date: 2026-10-30

## Context

A diagnostic that sounds sure but is wrong sends users on wasted or harmful experiments. While this project's first manual analyses were done, a cause was stated before the data supported it and had to be retracted; a quality setting was also nearly described with a name it did not have.

## Decision

- Every claim shows the measurements behind it.
- Hypotheses are labelled as hypotheses and paired with a test (for example "record another run with ray tracing off and compare").
- "Inconclusive" is a first-class answer, for the rules and for the AI.
- Unknown values are shown as raw ids, not guessed labels.
- Statements about settings carry the caveat that the config file reflects the last save.
- The app compares metrics against CapFrameX's definitions and the project verifies this on real captures.

## Consequences

- Fewer dramatic verdicts; more "here is what the data shows and one thing to try".
- Rules need guard conditions and tests for both firing and staying quiet.
- Users can trust a finding more because it can be checked.

## Success looks like

When a diagnosis turns out wrong, the evidence shown beside it made the reason obvious and the next step was an experiment, not a leap of faith.
