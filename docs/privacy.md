# Privacy

daignostic is local-first. This page lists everything it reads, stores and can send.

## What it reads

| Source | Why | Notes |
|---|---|---|
| CapFrameX capture files you open | Analysis | Read-only. The device name and motherboard fields in the file are parsed away and never used or shown. |
| A game's config file (currently Black Myth: Wukong's `GameUserSettings.ini`) | Show settings next to results | Read-only; the file is never modified. |
| CapFrameX MCP server (optional) | List captures | Only tools on a read-only allow-list can be called. Writing tools are blocked in code and covered by tests. |

## What it stores

| Data | Where | Protection |
|---|---|---|
| Preferences (folder, endpoint, preset, toggles) | `%LOCALAPPDATA%\Daignostic\settings.json` (packaged installs: the app's private data folder) | Contains no secrets. |
| AI API key (if you enter one) | `...\Daignostic\secrets\aikey.bin` | Encrypted with Windows DPAPI for your user account. Useless on another account or PC. Never in settings, logs, reports or the repository. |

daignostic collects no telemetry and makes no network calls except the ones you configure.

## What can leave your PC

Only when you press **Ask AI**, and only to the endpoint in Settings:

- the measured facts (frame-rate statistics, counts, sensor summaries, GPU and CPU model names, driver version, resolution, graphics API);
- the rule-based findings;
- the game settings read from the config file;
- your optional note;
- your API key, as a standard `Authorization: Bearer` header.

It **never** includes file paths, the device name, the motherboard, your user name or any capture file contents beyond the summary above. The **Preview** panel shows the exact message, and a test (`tests/ui/smoke.ps1`) verifies a real request contains none of those.

The model receives text only. It is offered no tools or functions, so it cannot read files, run commands or change settings.

## If you use a hosted AI

Whatever you send is handled under that provider's terms. Prefer a local or self-hosted gateway if that matters to you.

## Reports

**Export report...** writes a Markdown file where you choose. It contains metrics, findings, game settings and AI output, with no paths or identifiers.

## For contributors

- Never commit real captures: they contain machine details. Tests use synthetic data.
- `scripts/check-secrets.ps1` scans for keys, tokens, personal paths and e-mail addresses. It runs as a pre-commit hook and in CI.
