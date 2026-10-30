# 0009. AI is optional, and there is a way to use any AI without connecting it

- Status: Accepted
- Date: 2026-10-30

## Context

AI is the headline feature, but not everyone wants to connect one: some distrust sending anything to a model, some already have a favourite chat they pay for, and some cannot run a local model. Making AI mandatory would exclude them, and making it the only way to get advice would make the app useless without it.

## Decision

- AI is **off until the user turns it on** and configures an endpoint and a model. Without it, the app is fully useful: analysis, findings with suggestions, a ranked "What you can do" list, comparison, system and background checks.
- The AI panel stays the most prominent part of the page, and says plainly that AI is optional. It offers two routes: **connect an AI** (Set up AI) or **export an analysis pack** for the user's own AI.
- The export pack is a single file: instructions, the exact measured data, and how to check the answer. It contains the same facts the built-in AI would see and nothing else.
- Settings explain the API key honestly, including its limits, and offer "memory only" so no key is ever written to disk.

## Consequences

- Privacy-minded and cost-conscious users are served, and trust is easier to earn.
- The AI feature needs a clear first-run explanation, since many users will see "set up AI" before using it.
- The pack's instructions must be kept in step with the built-in prompt (both forbid questions, require citations and require hypotheses to be labelled).

## Success looks like

A user with no AI configured can still get a list of concrete things to try, and can hand the same data to any AI in under a minute.
