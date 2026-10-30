# Contributing to daignostic

Thanks for helping. This project values accuracy and trust above feature count, so contributions are reviewed for correctness, tests and honesty of wording.

By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md). Contributions are licensed under the [MIT license](LICENSE).

## Ways to help

- **Report a problem** with a capture that is analysed wrongly. Describe what you expected; *do not attach real captures publicly* (they contain device details). Share the numbers you saw, or reproduce with a synthetic capture.
- **Add a game reader** so findings can use that game's settings ([guide](docs/architecture.md#extending)).
- **Improve or add rules** that explain a common cause of stutter.
- **Improve docs**, translations, accessibility, or the UI.

For anything larger than a small fix, open an issue first so we agree on the approach.

## Ground rules

1. **Read-only.** The app and the AI never change a user's system without an explicit action ([ADR 0006](docs/adr/0006-read-only-by-design.md)).
2. **Every claim is traceable.** A finding must cite facts that exist ([ADR 0004](docs/adr/0004-deterministic-analysis-ai-explains.md)).
3. **No guessing labels.** If you are not sure what a game value means, keep it as a raw id ([ADR 0010](docs/adr/0010-game-config-readers.md)).
4. **No secrets, no personal data, no real captures** in code, tests, issues or screenshots. Use synthetic data.
5. **Logic goes in `Daignostic.Core`**, with tests. Keep the WinUI project thin.

## Setup

See [docs/development.md](docs/development.md). In short:

```powershell
git clone https://github.com/axellageraldinc/daignostic
cd daignostic
git config core.hooksPath .githooks
dotnet test tests\Daignostic.Core.Tests
```

## Making a change

1. Fork and create a branch (`feature/short-name` or `fix/short-name`).
2. Make the change with tests. A bug fix starts with a failing test.
3. Run, and make sure these pass:
   ```powershell
   dotnet test tests\Daignostic.Core.Tests
   powershell -File scripts\check-secrets.ps1
   powershell -File tests\ui\smoke.ps1      # if you touched the app
   ```
4. Update docs and `CHANGELOG.md` (under *Unreleased*). If you made a technical or product decision, add an [ADR](docs/adr/README.md) or [BDR](docs/bdr/README.md).
5. Open a pull request using the template. Keep it focused; explain *why*.

## Style

- Match the code around you: naming, comment density, idioms. Comments explain *why*, not *what*.
- C#: nullable on, `partial` observable properties, no reflection-based serialization, ASCII-only source (use `\uXXXX` escapes).
- XAML: `x:Bind` with an explicit `Mode`, `AutomationProperties.AutomationId` on interactive controls, theme resources instead of hard-coded colours.
- User-facing wording: plain, calm, specific. Say what was measured; label hypotheses as hypotheses.

## Reviews

Maintainers look for: correctness against real behaviour, tests for both the firing and the not-firing case, privacy impact, and whether wording could mislead. Expect questions rather than silence.

## Reporting security issues

Please follow [SECURITY.md](SECURITY.md); do not open a public issue for a vulnerability.
