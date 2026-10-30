# Privacy

analyxell is local-first. This page lists everything it reads, stores and can send. It has no server, no account, no telemetry and no auto-update ping.

## What it reads

| Source | Why | Notes |
|---|---|---|
| CapFrameX capture files you open | Analysis | Read-only. The computer's name is never read. The motherboard name is shown to you but never sent or exported. |
| A game's config file | Show settings next to results | Read-only. Built in for Black Myth: Wukong; for other games only when you pick a file. |
| A few Windows settings | Explain performance | Read from the registry, read-only: Game Mode, hardware GPU scheduling, variable refresh for windowed games, active power plan. Also the primary display's resolution and refresh rate, and the number of monitors. |
| Where CapFrameX is installed | Offer to open it, or to download it if missing | Checks the usual install folders and Windows' list of installed apps (read-only), whether a CapFrameX process is running, and a path you may choose. analyxell starts CapFrameX only when you press **Open CapFrameX**. |
| The game's drive | Find out if it is on an SSD or HDD, NVMe or SATA, and how full it is | Two read-only storage queries on the volume, no file access. The game's folder comes from you, or from Steam's own library files if it is a Steam game. |
| The GPU shader cache folders | An empty cache means shaders compile during play | Only their total size and file count are read, never the contents. |
| Running programs (only when you press the background check) | Find programs competing with the game | For 10 seconds: each process's name, CPU time, disk I/O counters, memory and GPU use. Query-only: nothing is closed or changed. |
| CapFrameX MCP server (optional) | List captures | Only tools on a read-only allow-list can be called; writing tools are blocked in code and tests. |

## What it stores

| Data | Where | Protection |
|---|---|---|
| Preferences (folders, AI endpoint and model, toggles) | `%LOCALAPPDATA%\Analyxell\settings.json` (packaged installs: the app's private data folder) | Contains no secrets. |
| AI API key (only if you enter one and allow saving) | `...\Analyxell\secrets\aikey.bin` | Encrypted with Windows DPAPI for your user account. Not readable text; useless on another account or PC. Choose "memory only" and nothing is written. |
| Setup you enter (G-SYNC state, driver options, monitor) | `...\Analyxell\setup.json` | Plain local file you can open and delete. |
| Game settings you enter or import, and the game's install folder | `...\Analyxell\games\<process>.json` | Plain local file. |

Loaded captures and background-check results are held in memory only.

## What can leave your PC

Only when you press **Ask AI** (or **Send** in the settings import), and only to the endpoint in Settings. You can see the exact message first.

**Ask AI** sends:

- measured facts: frame-rate statistics, counts, sensor summaries, GPU and CPU model names, driver version, resolution, graphics API, memory size, whether ReBAR, GPU scheduling and Game Mode are on, display refresh range, latency and pacing numbers;
- the rule-based findings and their suggestions;
- the game settings you have, and the setup you entered (never the monitor model or drive model);
- Windows facts above (power plan, drive type and free space, shader cache size);
- anonymous totals from the background check (CPU, disk and GPU used by other programs) and category names such as "Browser, Overlay";
- your optional note;
- your API key, as a standard `Authorization: Bearer` header.

It **never** includes file paths, your computer's name, the motherboard, your Windows user name, or the names of other running programs. (Program names are sent only if you switch that on in Settings.) Findings that mention program names are marked local-only and excluded from the message.

**Import settings with my AI** sends the text of one config file you choose, after removing lines that look like secrets and anonymising user paths. You see that exact text first.

The model receives text only. It is offered no tools or functions, so it cannot read files, run commands or change settings, and it is told it cannot look anything up. A test (`tests/ui/smoke.ps1`) verifies a real request carries no tools and no identifiers.

## Export for my own AI

**Export for my own AI** writes a Markdown file where you choose. It contains the same facts and findings as above, with instructions. It is up to you what you do with it; it holds measurements only, no paths, computer name or keys.

## About the API key

- Encrypted with Windows DPAPI for your account; never written to the settings file, logs, reports, exports or an AI message.
- Sent only to the endpoint address you entered, in the Authorization header.
- You can see the stored file (it is encrypted data), delete it, or choose memory-only.
- Honest limit: DPAPI protects the key from other users and from someone copying your disk. A program already running as you could still ask Windows to decrypt it, as with any app's saved secret. Use a key with a spending limit, or a local gateway.

## If you use a hosted AI

Whatever you send is handled under that provider's terms. Prefer a local or self-hosted gateway if that matters to you.

## Reports

**Export report...** writes a Markdown file where you choose, with metrics, findings, game settings and AI output. It contains no paths or identifiers.

## For contributors

- Never commit real captures: they contain machine details. Tests use synthetic data.
- `scripts/check-secrets.ps1` scans for keys, tokens, personal paths and e-mail addresses. It runs as a pre-commit hook and in CI.

## Looking a game up

The only thing analyxell sends to the internet by itself is a lookup you start: "Look this game up online" sends the game's name to pcgamingwiki.com (a community-written wiki) through its public API, with a User-Agent that names analyxell. Nothing else is sent: no capture, path, device name or key. The answer is shown to you before anything is saved, and what is saved stays on this PC.
