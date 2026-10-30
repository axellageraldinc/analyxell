# Architecture Decision Records

Each ADR records one technical decision: the context, the choice, and what follows from it. They are short and permanent: to change a decision, add a new ADR that supersedes the old one rather than editing history.

Format: **Status** (Accepted, Superseded by NNNN), **Context**, **Decision**, **Consequences**, and sometimes **Alternatives considered**. Copy [template.md](template.md).

| # | Decision | Status |
|---|---|---|
| [0001](0001-winui3-dotnet.md) | WinUI 3 on .NET 10 for the desktop app | Accepted |
| [0002](0002-core-library-separate-from-ui.md) | Analysis lives in a UI-free core library | Accepted |
| [0003](0003-capture-files-first-mcp-optional.md) | Read capture files directly; CapFrameX MCP is optional and read-only | Accepted |
| [0004](0004-deterministic-analysis-ai-explains.md) | Deterministic analysis measures; the AI only explains, with citations | Accepted |
| [0005](0005-openai-compatible-ai-without-tools.md) | AI through an OpenAI-compatible endpoint, with no tools | Accepted |
| [0006](0006-read-only-by-design.md) | The app and the AI are read-only; writes need explicit user action | Accepted |
| [0007](0007-secrets-and-secret-scanning.md) | Secrets: DPAPI storage, never in the repo, scanned in CI | Accepted |
| [0008](0008-custom-chart-control.md) | A small custom chart control instead of a charting dependency | Accepted |
| [0009](0009-no-trimming-self-contained-deployment.md) | No trimming; self-contained MSIX and portable builds | Accepted |
| [0010](0010-game-config-readers.md) | Game settings come from read-only per-game readers | Accepted |
| [0011](0011-release-artifacts-and-signing.md) | Release as MSIX installer and portable zip | Accepted |
| [0012](0012-user-provided-and-ai-assisted-game-settings.md) | Game settings can be user-provided or extracted by the user's AI, and are always checked | Accepted |
| [0013](0013-read-only-system-probes-and-background-check.md) | Read-only system probes and an on-demand background check | Accepted |
| [0014](0014-suggestions-and-single-answer-ai.md) | Suggestions are deterministic; the AI is optional, answers once, never asks questions | Accepted |
