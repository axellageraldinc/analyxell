# Architecture

daignostic is a WinUI 3 desktop app on top of a UI-free analysis library.

```
 CapFrameX capture (.json) --+
 Game config (.ini) ---------+--> Daignostic.Core --> facts + findings --> Daignostic (WinUI app)
 CapFrameX MCP (list only) --+         |                                        |
                                       +-- AI prompt (facts only) --> AI endpoint (optional)
```

## Projects

| Project | Role |
|---|---|
| `src/Daignostic.Core` | All logic. Plain .NET 10, no UI types, fully unit tested. |
| `src/Daignostic` | WinUI 3 app: windows, pages, view models, chart control. No analysis logic. |
| `tests/Daignostic.Core.Tests` | xUnit tests with synthetic captures. |
| `tests/ui` | PowerShell UI smoke test driven by UI Automation, with a mock AI endpoint. |
| `scripts` | Icon generation, packaging, secret scanning. |

## Core library

| Namespace | Responsibility |
|---|---|
| `Capture` | `CaptureReader` parses a capture into frames and sensor series (only what the analysis needs; machine identifiers are dropped). |
| `Analysis` | `FrameMetrics` (FPS statistics, matching CapFrameX's definitions), `Detectors` (stutters and freezes), `BottleneckAnalyzer`, `FreezeContext` (what the sensors did around each freeze), `Rules` (findings), `CaptureAnalyzer` (orchestrates and builds the *facts* table), `RunComparer`. |
| `Config` | `IGameConfigReader` and the Wukong implementation: read-only, maps only values whose meaning is certain. |
| `Ai` | `OpenAiCompatibleClient`, the system prompt, the message builder, and `AiResponseParser` which enforces citations. |
| `Mcp` | `CapFrameXMcpClient` with a read-only tool allow-list; `CaptureCatalog` finds captures. |
| `Settings` | Preferences and DPAPI secret storage. |
| `Reporting` | Markdown export. |

### The facts table

`CaptureAnalyzer` reduces a capture to a flat dictionary of named facts (`fps.p1`, `freeze.count`, `freeze_ctx.gpu_load.at_freeze_avg`, ...). Rules cite fact keys, the AI message contains only these facts and the findings, and the AI must cite them back. This is what makes every statement traceable and every AI claim checkable. See [ADR 0004](adr/0004-deterministic-analysis-ai-explains.md).

## App

MVVM with CommunityToolkit.Mvvm. `AppServices` composes dependencies by hand; `AppState` holds the captures loaded in this session. `FrameTimeChart` draws frame times with XAML shapes, reducing 45,000 frames to one min/max pair per pixel column ([ADR 0008](adr/0008-custom-chart-control.md)). Commands are `[RelayCommand]` methods; pages bind with `x:Bind`.

## Data flow

1. Open: `AppState.LoadAsync` parses the file on a worker thread, reads game settings if a reader matches, and runs `CaptureAnalyzer.Analyze`.
2. `AnalyzeViewModel` turns the result into cards, findings and chart data.
3. Ask AI: `AiPanelViewModel` builds the exact message (shown in the preview), sends it, parses the reply and drops uncited claims.

## Extending

- **New game:** implement `IGameConfigReader`, register it in `GameConfigReaders.CreateDefaults`, add tests with a temp config file. Label a value only when you are certain what it means; otherwise pass it through as a raw id.
- **New finding:** add a rule in `Rules.cs` that cites fact keys that exist (a test enforces this), plus tests for both firing and not firing.
- **New sensor logic:** sensors are looked up by `name` and `type` as logged by CapFrameX; keep lookups tolerant of a missing sensor.
