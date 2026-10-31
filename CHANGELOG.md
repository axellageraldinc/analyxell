# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.0] - 2026-10-31

### Added

- **Drive activity in Compare.** Compare now shows each drive's peak activity in both runs, and the AI gets the drive activity around freezes for both runs. It no longer tells you to record drive activity when the captures already have it.

## [1.1.2] - 2026-10-31

### Fixed

- **Game settings dialog no longer cuts off buttons.** On the "Import with my AI" tab the "Send this to my AI" button was clipped below the file preview. The tab is now tall enough to show everything.

## [1.1.1] - 2026-10-31

### Fixed

- **Pages no longer spill off the right edge.** After the window was resized, the Compare page (and potentially Analyze and Game library) could be laid out wider than the window, pushing the B column out of view. Page content now follows the window width, up to a comfortable maximum, and stays centred on very wide windows.

## [1.1.0] - 2026-10-31

A calmer Analyze page, and a clearer Compare result.

### Changed

- **Overview shows one step, not three.** The card is now "Start here" with the single most important step; the rest are behind "Show more steps".
- **Fewer numbers up top.** The 0.1% low card is gone from the Analyze page (it is still in Compare and in reports).
- **Findings are easier to scan.** Each finding is a headline, an explanation and a highlighted "Try this" box. The measurements behind it are folded into "Why we think this".
- **Shorter tab names:** "Your PC" and "Background".
- **Compare results are colour-coded.** The Result column has icons and colours: green Better, red Worse. Rows that are not scored say "For reference" instead of "Context".

## [1.0.0] - 2026-10-31

First stable release. Same features as 0.3.1; the repository is now public.

### Changed

- Version number is 1.0.0; the project is no longer marked pre-release.


## [0.3.1] - 2026-10-31

### Fixed

- The 1% low and 0.1% low cards no longer wrap and clip their caption ("fps, 100% of avg"); cards keep a minimum height.

## [0.3.0] - 2026-10-31

### Added

- **Ryzen and NVIDIA findings that work for any game and any model.** A hitch-rhythm check (`PERIODIC_HITCHES`, with the fTPM hint on Ryzen); two-chiplet X3D games running on the cores without the extra cache; CPUs that never reach their rated boost, or boost past it (PBO or an overclock); a missing AMD chipset or V-Cache driver; DDR5 above Ryzen's sweet spot; GPU clocks dropping at light load; a GPU overclock, lowered power limit or probable undervolt; frame generation with hardware GPU scheduling off; and games that ship DLSS on a GPU-limited RTX card.
- **Unstable tuning shows up.** Windows' own count of hardware errors (WHEA) and graphics driver resets over the last 30 days becomes a finding, which is the honest sign of an unstable Curve Optimizer, PBO, overclock, undervolt or memory profile.
- New read-only probes (listed in the privacy page): the NVIDIA driver's clock offsets and power limit, the Windows error log counts, AMD driver presence, and the DLL names in the game's folder.

### Changed

- **Tearing is no longer reported as a fault on the game's request alone.** Modern games send every frame with VSync off even with G-SYNC or driver VSync on, so a flat 100% never meant tearing. analyxell now checks whether frames land exactly on the display refresh (VSync applied by the driver), says "cannot judge" when there is no refresh-rate reading or answer in PC setup, and the chart bar is labelled "VSync off (game request)".
- **Heat thresholds follow the hardware.** Ryzen 7000 and 9000 may reach about 95 °C by design; NVIDIA cards are flagged at 88 °C, not 85, and thermal throttling needs 91 °C without the driver's flag; GDDR6X memory is flagged at 104 °C. A desktop card at its power limit under full load is GPU-bound, not a power fault.
- **Analyze page.** The verdict card carries **Ask AI**; "What you can do" comes before the chart; capture quality, a tracked change and a newer capture share one slim card; metric cards show good, warning or bad at a glance, with the 1% and 0.1% lows as a share of the average; stutters are on the chart by default; findings show how serious they are; the header keeps Open and Recent visible and moves the rest under a **...** menu.

## [0.2.0] - 2026-10-31

### Changed

- **Renamed to analyxell** (from the old working name). The app, installer, folders and repository all use the new name.
- **One simple way to set a game up.** The Game library has no "add to library", game file or executable steps any more. Every game shows a **Set up...** button that opens one screen with three optional steps (install folder, online lookup, settings), the same from the library and from the Analyze page. Games are matched to captures by name.
- Findings and AI answers name their measurements in plain words ("1% low, freeze count") instead of internal keys.
- Seven metric cards fit one row; dialogs no longer clip their content.

### Added

- **Look a game up (PCGamingWiki).** On the Analyze page (PC and setup tab) and in the Game library, "Look this game up online" asks PCGamingWiki, only when you press it and sending only the game's name, where the game keeps its settings file and which upscalers, frame generation, VSync and ray tracing options it offers. You read the result before saving. The settings file is then offered to the import with your AI, and the options reach the AI as facts that say what the game offers, not what is switched on.

- **Try it, then check it.** Each step under "What you can do" has "I'll try this and check if it helped". analyxell remembers the current run as the "before"; when you open a later capture of the same game it compares the two and says whether the change helped, made no clear difference, or made things worse.
- **Capture quality check.** A warning appears when a capture may not show the whole picture: too short, pinned at a frame cap, apparently recorded in a menu, windowed, or without sensor data. The AI is told too.
- **Less friction.** "Open my newest capture" and a quiet "A newer capture was recorded" banner while the page is open (when CapFrameX or a captures folder is set up), and "Try a sample" so a new user can see the app before recording anything.
- **Copy summary to share.** A short, anonymised summary on the clipboard to paste into a forum or chat. It has no file paths, computer name, motherboard or keys.

### Added

- Ask the AI your own question (a box above the note, and another under its open questions). It answers it directly, separately from the analysis.
- When you ask again, the AI gets a short digest of the last exchange (your question, its answer and main points), labelled as its own earlier output so it re-checks it against the facts instead of repeating it. It is shown in the preview; no transcript is sent.

## [0.1.1] - 2026-10-30

### Changed

- You can answer what the AI says it could not tell: each item has a box, and "Ask again with my answers" sends them with the next question (the AI is told to use them as given and not ask again).
- New Drive activity chart (one line per drive, named NVMe or SATA/other), and the disk finding says which kind of drive spiked and what is known about the game's drive.
- Fixed: with frame generation, generated frames were counted as frames above the display's refresh rate (49% on a 180 Hz panel).
- With frame generation, FPS and lows are now measured on the frames the screen displays, not on the generated frames presented under a millisecond after each real one (which read as 1,600 fps frames). A "Rendered" card shows what your PC really draws, and the finding warns when that base rate is low.
- Frame generation no longer reads as stutter: the capture is checked for generated frames (labelled frames, a steady long-short frame-time pattern, or a display changing twice as often as the game presents) and stutter is judged on groups of frames when they are present. Chart markers use the same judgement.
- Compare now lists the game settings that differ between the two runs (DLSS, frame generation, ray tracing, resolution...), warns when they change the frame rate itself, and tells the AI.
- Importing game settings with your AI shows the same progress (time, what it is doing, Cancel) and the same failure messages as Ask AI.
- The Game library finds installed games by asking Steam (every library folder, on any drive), Epic and GOG, and lists the ones you have not opened a capture of yet, with a button to add them. It reads only the launchers' small manifest files; it does not crawl your drives.
- New Game library page: every game analyxell knows (built in, seen in a capture, or added by you), what is known about its settings, and a way to add a game by choosing its file and entering its settings. Settings you enter for a game are used for captures recorded after you saved them.
- The game's own settings now matter: upscaling off while the GPU is the limit, frame generation on while motion or latency is off, low latency mode off while the GPU is the limit, and the game's VSync setting feed the findings, not only the AI.
- The chart legend shows a hand cursor so it is clear you can press a name to hide or show a line.

- Game settings now belong to one capture. A game's config file only holds its latest settings, so an older capture was being judged by newer settings (and so was the AI). A config saved after the capture was recorded is no longer used; you can say "these are the settings I played with" or enter them yourself, and they are kept for that capture only.
- Charts with several lines: press a name in the legend to hide or show that line (at least one stays visible); the scale follows what is shown.
- The AI can ask for what it is missing without asking you a question: it names checks from a short list analyxell offers (background programs, PC setup, game settings), they appear as buttons you press, analyxell runs them read-only, and the answer is marked out of date until you ask again. Anything the AI names that was not offered is dropped.
- The AI is told what a capture cannot show (driver VSync, actual tearing, why a spike happened) so it reads the findings the way the rules do.
- CPU bottlenecks are now spelled out: "The CPU is holding the GPU back", whether one thread is the limit, and stretches where the CPU limited an otherwise GPU-bound run.
- New diagnoses: performance that declines over a run (with the memory or heat growth that fits), hitches that fade (shader and loading), single-channel or slow memory, Resizable BAR, Game Mode, background recording, desktop-compositor presentation and Memory integrity. See docs/problems-and-causes.md.
- Your VSync setup now counts: VSync set in the GPU driver cannot be seen in a capture, so the tearing findings believe what you entered.
- Analyze page redesigned to show less at once: a one-line verdict, the metric cards, then tabs (Overview, Ask AI, Findings, PC and setup, Background programs). Findings that need attention come first and the rest are folded away; the overview shows the top three steps.
- Compare page: pick the metric to overlay (frame time, FPS, latency, GPU load, temperature, clock, memory, power); the two runs are now yellow (A) and blue (B), named in the legend, with bold one-second averages over faint per-frame lines.
- Asking an AI shows progress (elapsed time, what it is doing, hints when slow) with Cancel, and failures explain what went wrong and what to try (endpoint, key, model, timeout, unreadable answer) with Try again and a shortcut to Settings. Requests give up after 2 minutes.

- The chart can mark more than freezes: stutters, FPS drops (seconds well below the typical frame rate), dropped frames and where tearing was possible. Switch each on or off under the chart; freezes are on by default.
- Test AI connection shows how long it has been waiting, when it gives up, and has Cancel.
- The UI tests set your real settings aside and restore them, so running them never touches your AI setup.

### Fixed

- Chart legend swatches were not centred on their text.

- A blank button in the page header where the CapFrameX button should be.

### Added

- CapFrameX detection with Open CapFrameX / Get CapFrameX (official GitHub releases) and a way to point at the portable version.
- The AI endpoint can be given with or without /v1 (providers document both), and 404/405 errors now say what to check.
- Settings problems are shown under the field they are about instead of at the top of the page.
- PC setup is reachable from the page header and from the PC details.
- New logo.
- Findings now come with concrete suggestions and a ranked "What you can do" list (deterministic, no AI needed).
- More diagnoses: low FPS, GPU/CPU/VRAM heat, system memory pressure, tearing conditions and display sync, input latency, motion pacing, game on a hard disk, nearly full drive, small shader cache, Power saver plan, display below its maximum refresh.
- Chart views for frame time, FPS, GPU and CPU load, temperatures, clocks, memory, power and latency.
- PC specs: a one-line summary with full details on demand (graphics, processor, memory, display, board and Windows, game drive).
- Read-only system probes (Windows gaming settings, display mode, drive type and free space, shader cache size) and an optional setup form for what cannot be read (G-SYNC/FreeSync, driver options, monitor, install folder).
- Background check: which other programs use CPU, disk and GPU, and well-known interferers, on demand.
- Game settings for any game: type, paste, or import from a config file with your own AI, with every value checked against the file.
- AI is optional and the main feature of the page; export an analysis pack for your own AI; the AI answers once and never asks questions, and reports what it could not tell.
- Settings: model name (with List models), API key assurance and memory-only option, CapFrameX MCP tutorial with plain-language connection diagnosis.
- Analyze page: FPS statistics (average, P1, 1% and 0.1% low averages, matching CapFrameX), stutter and freeze detection, GPU/CPU/thermal/power bottleneck verdict, and a frame-time chart with a one-second average and freeze markers.
- Rule-based findings that cite the measurements behind them, including what the sensors did around each freeze (GPU load, drive activity, memory).
- Compare page: run-to-run comparison with scored and context-only measures and an overlay chart.
- Optional AI explanation through any OpenAI-compatible endpoint: exact-message preview, no tools, citation checking, and discarding of uncited claims.
- Game settings reader for Black Myth: Wukong (read-only).
- Optional read-only CapFrameX MCP integration for listing captures.
- Markdown report export through a save dialog.
- Drag and drop, command-line capture paths, and a recent-captures list.
- API key stored with Windows DPAPI; settings file holds no secrets.
- Installer (MSIX) and portable (zip) packaging scripts.
- Unit tests, a UI smoke test with a mock AI endpoint, secret scanning, and CI.
- Documentation: user guide, architecture, privacy, 11 ADRs, 8 BDRs, contributing, security policy, code of conduct.

[Unreleased]: https://github.com/axellageraldinc/analyxell/commits/main
