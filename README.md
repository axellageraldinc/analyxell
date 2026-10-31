<p align="center">
  <img src="assets/logo.png" alt="analyxell logo" width="120">
</p>

<h1 align="center">analyxell</h1>

<p align="center">
  AI-assisted game performance diagnostics for Windows.<br>
  Open a <a href="https://github.com/CXWorld/CapFrameX">CapFrameX</a> capture, see what happened, and get a plain-language explanation you can trust.
</p>

> **Status: 1.4.0.** The app works and is tested, but it has not been tried on many PCs yet, so please report what breaks.

https://github.com/user-attachments/assets/385479e1-dacd-4d12-bb48-9569cb61646c

## What it does

You played a game and it stuttered. A capture tells you *that* it did; analyxell helps you work out *why*.

- **Analyze a capture.** Average FPS, 1% and 0.1% lows, stutters, freezes, and whether the GPU or CPU is the limit. Charts for frame time, FPS, load, temperatures, clocks, memory, power and latency, with every freeze marked.
- **Tell you what to do.** Findings cite the exact measurements behind them and come with concrete things to try, starting with a single "Start here" step. It covers freezes, low FPS, heat, memory and VRAM pressure, tearing conditions, latency, pacing, and more ([the rules](docs/rules.md)). No AI needed.
- **Look beyond the capture.** The PC's specs, which drive the game is on (SSD or HDD, NVMe or SATA, how full), Windows gaming settings, the GPU shader cache, and which other programs are competing for CPU, disk and GPU.
- **Open or get CapFrameX.** It detects whether CapFrameX is installed, opens it for you, or sends you to the official download page.
- **Compare two runs.** Changed a setting? See, measure by measure, whether it helped.
- **Ask an AI, safely, or not at all.** Optionally have an AI explain it and rank the fixes for your data, or export one file and give it to any AI yourself. You see the exact message first, the AI has no tools and never asks questions back, and any claim that cites no evidence is thrown away.
- **Game library.** See which games analyxell knows, what it knows about their settings, and add your own.
- **Know your game's settings.** Built in for Black Myth: Wukong. For any other game, type or paste them, or let your own AI pull them out of the config file, with every value checked against the file.

| Analyze | Compare |
|---|---|
| ![Analyze page](docs/images/analyze.png) | ![Compare page](docs/images/compare.png) |
| ![Findings tab](docs/images/analyze-details.png) | |

## Safe by design

- **Read-only.** analyxell reads capture files and game config files. It never edits game settings, starts captures, or runs commands. Anything it would write (a report) goes where *you* choose, through a save dialog.
- **Private by default.** Analysis is local. The AI step is optional, points at an endpoint you configure (a local gateway works), and never receives file paths or device names. Your API key is stored encrypted and is sent only to that endpoint.
- **Honest.** Hypotheses are labelled as hypotheses, and "inconclusive" is a valid answer.

Details: [docs/privacy.md](docs/privacy.md) and [ADR 0006](docs/adr/0006-read-only-by-design.md).

## Install

Pre-built downloads are on the [Releases](../../releases) page:

| Download | Use it when |
|---|---|
| `analyxell-<version>-win-x64.msix` | You want a normal install. Needs the signing certificate trusted once; see [docs/releasing.md](docs/releasing.md). |
| `analyxell-<version>-portable-win-x64.zip` | You want no installation. Unzip anywhere and run `Analyxell.exe`. |

Requires Windows 10 (1809) or newer, x64. Nothing else needs to be installed.

**Updating:** in **Settings > Updates**, press **Check for updates**. If a newer version exists, **Download update** gets the right file for how you installed it (the installer or the portable zip). Nothing is checked until you press the button.

## Quick start

1. Record a gameplay session with CapFrameX (it saves a `.json` capture).
2. Start analyxell and drop the capture on the window, or choose **Open capture...**.
3. Read the verdict at the top, then **Start here**. For an AI explanation open the **Ask AI** tab, check **Preview exactly what will be sent**, then press **Ask AI**.
4. Change one setting in the game, record again, and use **Compare** to see if it helped.

More in the [user guide](docs/user-guide.md).

## AI setup (optional)

AI is off until you turn it on, and everything else works without it. To use one from inside analyxell, open **Settings**, turn on the AI switch, and enter the endpoint address of any OpenAI-compatible service (a hosted one, or a local or self-hosted gateway such as [xellter](https://github.com/axellageraldinc/xellter)), the **model name** exactly as your provider spells it (**List models** shows what the endpoint offers), and an API key if required. The key is stored encrypted for your Windows account, never in the settings file, and can be kept in memory only. Prefer your own chat? **Export for my own AI** saves one file with the instructions and the measured data.

## Build from source

```powershell
git clone https://github.com/axellageraldinc/analyxell
cd analyxell
dotnet test tests\Analyxell.Core.Tests          # unit tests
winapp run src\Analyxell                        # build, register and launch the app
```

You need the .NET 10 SDK, [WinApp CLI](https://github.com/microsoft/winappCli) 0.7+ and Windows Developer Mode. See [docs/development.md](docs/development.md) for UI tests and packaging.

## Documentation

| | |
|---|---|
| [User guide](docs/user-guide.md) | Using the app, reading the results |
| [Architecture](docs/architecture.md) | How the code is organised |
| [Diagnostic rules](docs/rules.md) | Every rule, its threshold and its advice |
| [ADRs](docs/adr/README.md) | Technical decisions and why |
| [BDRs](docs/bdr/README.md) | Product decisions and why |
| [Privacy](docs/privacy.md) | Exactly what is read, stored and sent |
| [Development](docs/development.md) | Build, test, package |
| [Releasing](docs/releasing.md) | Release checklist, going public |
| [Contributing](CONTRIBUTING.md) | How to help |

## License

[MIT](LICENSE). analyxell is an independent project and is not affiliated with CapFrameX, NVIDIA or any game publisher.
