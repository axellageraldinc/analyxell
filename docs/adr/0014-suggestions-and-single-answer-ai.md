# 0014. Suggestions are deterministic; the AI is optional, answers once, and never asks questions

- Status: Accepted
- Date: 2026-10-30

## Context

Pointing out problems is not enough: users want to know what to do. Fixed, well-understood advice does not need a language model, and AI should not be required to get it. Separately, the AI call is a one-shot request: there is no conversation, so a model that ends with a question wastes the answer. The model also has no access to the user's PC.

## Decision

1. **Every finding above Info carries suggestions** written in the rule itself: in-game settings or simple, reversible checks, always ending with "change one thing, record again, compare". A "What you can do" list ranks the most valuable ones across findings. A test enforces that no finding above Info is without a suggestion.
2. **The AI builds on the suggestions** rather than inventing advice: it ranks them, explains them for this data, drops ones that clearly do not apply, and may add only in-game settings or simple checks. It still cannot suggest commands, registry edits, BIOS changes, downloads or deleting files.
3. **Single answer.** The system prompt states that this is one question and one answer, forbids asking the user anything, and says the model has no way to look anything up. When information is missing, it must state its assumption, give guidance that holds either way ("if X, then Y; otherwise Z"), and list what it could not determine in an `unknowns` field, which the app shows as "What the AI could not tell from this data". The app collects the data a model would otherwise ask for (setup, game settings, background check) itself.
4. **AI is optional.** With no AI configured, everything above still works. The Analyze page can **export an analysis pack**: one Markdown file with instructions (the same rules, in a conversational form), the exact data, and how to check the answer, so the user can give it to any AI they like. The pack contains measurements only.
5. The model is told to use a **model name** exactly as the endpoint expects it; the app sends it unchanged and can list the models an endpoint offers.

## Consequences

- Useful advice with no AI and no network; AI adds personalisation and prioritisation.
- The AI cannot loop the user into a conversation that the app cannot hold, and cannot pretend to have inspected the PC.
- The export pack lets privacy-minded or cost-conscious users use any model without configuring anything in the app.
- Suggestions are general guidance: they are phrased as things to try and verify, not guaranteed fixes.
