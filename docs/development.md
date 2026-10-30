# Development

## Prerequisites

| Tool | Version | Install |
|---|---|---|
| Windows | 10 1809 or newer (11 recommended) | |
| .NET SDK | 10.0 | `winget install Microsoft.DotNet.SDK.10` |
| WinApp CLI | 0.7 or newer | `winget install Microsoft.WinAppCli` |
| Developer Mode | on | Settings > System > For developers |
| Git | any recent | `winget install Git.Git` |

Optional: the [win-dev-skills](https://github.com/microsoft/win-dev-skills) plugins for Claude Code or GitHub Copilot (`winui`, `winappcli`) if you develop with an AI assistant.

```powershell
git clone https://github.com/axellageraldinc/analyxell
cd analyxell
git config core.hooksPath .githooks        # enables the secret-scan pre-commit hook
```

## Build and run

```powershell
winapp run src\Analyxell --arch x64                               # build, register, launch
winapp run src\Analyxell --arch x64 --args "C:\path\capture.json"  # launch with a capture
```

Use `winapp run`, not the raw `.exe`: the app is packaged and needs its identity. `dotnet build src\Analyxell -p:Platform=x64` builds without launching.

## Tests

| Layer | Command | Notes |
|---|---|---|
| Unit | `dotnet test tests\Analyxell.Core.Tests` | Synthetic data only; runs in a second. |
| Real-capture check | `$env:ANALYXELL_SAMPLE_CAPTURE = "C:\path\real.json"` then run the unit tests | Optional. Parses your own capture and sanity-checks the numbers. Never commit real captures. |
| UI smoke | `powershell -File tests\ui\smoke.ps1` | Builds and launches the app, drives it with UI Automation, uses a mock AI endpoint. Screenshots go to `artifacts\ui-test`. Needs an unlocked desktop. |

The UI smoke test checks the user paths in the MVP (open, analyze, compare, settings, AI) and the privacy guarantees (no tools offered, no identifiers sent, uncited claims dropped).

## Packaging

```powershell
powershell -File scripts\package.ps1 -Version 0.1.0
```

Creates `artifacts\analyxell-<version>-portable-win-x64.zip` and `artifacts\analyxell-<version>-win-x64.msix`. Without `-CertPath` it generates a development certificate in `artifacts\certs` (git-ignored). To try the MSIX on your PC you must trust that certificate once (`winapp cert install`, needs admin); the portable zip needs nothing.

## Icons

`assets\logo.svg` is the source. `powershell -File scripts\build-icons.ps1` renders `assets\logo.png` and `assets\AppIcon.ico` with headless Edge. To refresh the packaged assets after changing the logo: `winapp manifest update-assets assets\logo.png --manifest src\Analyxell\Package.appxmanifest`, then copy `assets\AppIcon.ico` and `assets\logo.png` into `src\Analyxell\Assets`.

## Conventions

- Match the surrounding code. Keep logic in `Analyxell.Core` ([ADR 0002](adr/0002-core-library-separate-from-ui.md)).
- Nullable enabled; warnings are errors in the core.
- Use `partial` observable properties (`[ObservableProperty] public partial string X { get; set; }`), `x:Bind` with an explicit `Mode`, and give interactive controls an `AutomationProperties.AutomationId`.
- Don't use reflection-based serialization; use `JsonDocument` or source-generated JSON.
- Non-ASCII characters in source: use escapes (`"\u00B7"`) so editors and scripts cannot corrupt them.
- New behaviour needs tests. New decisions need an ADR or BDR.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| App silently exits when started from Explorer or a script | It is packaged: launch with `winapp run`. |
| Portable build crashes at start-up | Trimming was turned on; keep `PublishTrimmed=false` ([ADR 0009](adr/0009-no-trimming-self-contained-deployment.md)). |
| `winapp ui` cannot find a control | Add an `AutomationId`; target it by id. |
| Developer Mode error | Enable it in Windows settings. |
