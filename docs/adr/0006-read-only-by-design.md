# 0006. The app and the AI are read-only; writes need explicit user action

- Status: Accepted
- Date: 2026-10-30

## Context

The tool inspects another program's data and a game's configuration, and talks to an AI. Users must be able to trust that it will not change their system, their games or CapFrameX, and that an AI cannot do so on its behalf.

## Decision

daignostic never modifies game settings, CapFrameX settings or any file it did not create, never starts captures, and never executes commands. The only writes are:

| Write | Trigger | Where |
|---|---|---|
| App preferences | The user presses **Save settings** | The app's own data folder |
| API key | The user enters one and saves | The app's own data folder, encrypted ([ADR 0007](0007-secrets-and-secret-scanning.md)) |
| Setup and game settings the user enters | The user presses Save in the setup or game-settings dialog | The app's own data folder |
| A Markdown report or an AI analysis pack | The user presses **Export** and picks a location in a save dialog | Where the user chooses |

The AI is given no tools ([ADR 0005](0005-openai-compatible-ai-without-tools.md)), and its advice is text that a person applies by hand. Any future feature that writes (for example applying a graphics preset) must ask the user explicitly each time and be added through a new ADR.

## Consequences

- A clear promise for users and contributors, shown in the UI and docs.
- Game config readers must open files with read-only sharing (a unit test checks the file is unchanged after reading).
- The MCP allow-list blocks CapFrameX tools that write.
- Convenience features that edit configs are out of scope for the MVP.
