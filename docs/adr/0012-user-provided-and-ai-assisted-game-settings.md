# 0012. Game settings can be user-provided or extracted by the user's AI, and are always checked

- Status: Accepted
- Date: 2026-10-30

## Context

[ADR 0010](0010-game-config-readers.md) gives each supported game a hand-written, read-only reader. That cannot cover every game, and the maintainers cannot verify every game's file format. Users want any game's graphics settings to inform the analysis.

## Decision

For games without a reader, the user can supply settings in two ways, both stored locally per game (`%LOCALAPPDATA%\Daignostic\games\<process>.json`):

1. **Type or paste** them: JSON, `key=value`, `key: value` or INI lines. No AI is involved.
2. **Import with the user's AI.** The user picks the game's config file. daignostic first removes lines that look like secrets (token, password, key, session, e-mail and similar words, long opaque strings) and anonymises user profile paths, shows the exact text, and only sends it when the user presses the button. The AI is asked to return only graphics and performance related keys exactly as written.

The AI's answer is not trusted. A setting is kept only if its key **and** value literally appear in the file text that was sent; everything else is discarded and counted. Imported and typed settings are always shown as **raw** values with their source ("entered by you", "AI import from file.ini, checked against the file"), never reinterpreted as friendly labels. The user reviews the list and presses Save.

If a built-in reader exists, its values win; user-provided items only add keys the reader did not report.

## Consequences

- Any game can contribute settings without new code, while keeping the principle of never guessing what a value means ([BDR 0006](../bdr/0006-honesty-over-confidence.md)).
- Using the AI import needs an AI to be configured, which stays optional ([BDR 0009](../bdr/0009-ai-is-optional.md)); typing or pasting always works.
- The redaction is pattern based, not perfect. The preview is the real protection: nothing leaves the PC without the user seeing it.
- Built-in readers remain the best experience for popular games and are still welcome contributions.
