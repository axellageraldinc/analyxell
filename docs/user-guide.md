# User guide

## Before you start

daignostic does not record gameplay. Use [CapFrameX](https://github.com/CXWorld/CapFrameX) to capture a session; it saves a `.json` file. For the best diagnosis, turn on CapFrameX's sensor logging (CPU, GPU, memory and drive sensors). Without sensors you still get frame-time results, but not the "why" (GPU busy, disk activity, temperatures).

Tip: record 5 to 10 minutes that include the moments that bother you.

## CapFrameX

daignostic checks whether CapFrameX is installed. The **CapFrameX** button in the Analyze header (and the card in Settings) says **Open CapFrameX** when it is found, and **Get CapFrameX** when it is not, which opens the official page: https://github.com/CXWorld/CapFrameX/releases. If you use the portable version, choose its `CapFrameX.exe` once. Opening CapFrameX is the only thing daignostic ever starts, and only when you press the button.

## Opening a capture

- Drop a `.json` file on the window, or press **Open capture...**
- **Recent captures** lists captures found through CapFrameX (if it is running with MCP enabled) or in your captures folder (set in **Settings**).
- From a terminal: `Daignostic.exe "C:\path\run1.json" "C:\path\run2.json"` opens one or more captures.

## The Analyze page, top to bottom

The page leads with a one-line **verdict** (healthy, worth fixing, serious problems) and the numbers behind it, then splits the rest into tabs so you only read what you need.

**The cards**

| Card | Meaning |
|---|---|
| Average | Mean frame rate. |
| 1% low / 0.1% low | The frame rate of your worst 1% / 0.1% of frames. These decide how smooth the game *feels*; a good average with bad lows means stutter. |
| Stutters | Frames that took more than twice as long as their neighbours. |
| Freezes | Frames that took 200 ms or more: the game visibly hung. |
| Limited by | Whether the GPU, CPU, heat or power limit holds performance back, and how confident that call is. |

**Above the cards** you may see: a **capture quality** warning (too short, frame-capped, idle, windowed or no sensors); a note about a change you are trying; and "A newer capture was recorded" with an **Open it** button. **Copy summary to share** puts a short, anonymised summary on the clipboard.

**Trying a change.** Under **What you can do**, press **I'll try this and check if it helped** on a step. Make the change, record the same route again, and open the new capture: daignostic compares it with the one you tried it on and says whether it helped. One change at a time makes the answer meaningful; differences of a few percent are normal run-to-run variation, and it says so.

**Overview tab**

- **Ask AI bar.** One line and a button. Pressing **Ask AI** jumps to the Ask AI tab so you can watch it work.
- **The chart.** Pick a view: frame time, FPS, GPU and CPU load, temperatures, clock speeds, memory, power or latency (only the views your capture has data for). Under the chart, switch markers on or off (freezes, stutters, FPS drops, dropped frames, tearing possible). Press a line's name in the legend to hide or show it. Hover to read exact values. For frame time and FPS the white line is a one-second average, and extreme spikes are clipped so one huge freeze does not flatten everything else.
- **What you can do.** The three most valuable things to try, most important first; **Show more steps** has the rest. Try one at a time, record the same route again, and use **Compare**.

**Ask AI tab.** The full AI panel; see below.

**Findings tab.** What needs attention comes first, each with the measurements behind it ("Based on: ...") and **What you can try**. Blue is information, yellow is worth a look, red is serious. Lower-priority notes are folded into **other notes** (opened for you when nothing else needs attention). The tab title shows how many things need a look. These come from fixed rules, not an AI; see [Diagnostic rules](rules.md).

**PC and setup tab.**

- **This PC.** Your GPU, CPU, memory and resolution, then the full picture: graphics (driver, video memory, clocks, power, temperatures, Resizable BAR), processor (cores, boost clock), memory, display (refresh range, variable refresh), motherboard and Windows settings, and the drive the game is installed on. The motherboard is shown here only; it is never sent to an AI.
- **Game settings** (when daignostic can read them) and **Help daignostic know more**: things a capture cannot show. All optional, all kept on this PC:
  - **Add game settings...** For games daignostic cannot read by itself: type or paste them (JSON, `key=value` or `key: value` lines), or let your own AI pull the graphics settings out of the game's config file. For the AI route, likely secrets and your user name are removed first, you see exactly what is sent, and the app keeps only settings whose names and values really appear in the file. Settings are shown as the game's own raw values, never guessed into friendly names. Settings belong to the capture they were entered or read for: if the game's config file was saved after a capture was recorded, daignostic does not assume it matches, and asks you.
  - **Edit PC setup...** (also the **PC setup...** button in the page header.) Tell daignostic what it cannot read: G-SYNC/FreeSync state, VSync, Reflex or low-latency mode, frame cap, driver shader cache size, GPU power mode, your monitor, and where the game is installed. The install folder lets it find out whether the game is on an SSD or a hard disk, NVMe or SATA, and how full the drive is. Answers are used as given and never verified.

**Background programs tab.** A capture cannot show which other programs were running. Press **Check background programs (10 s)** to sample your PC right now: which programs use CPU, disk and GPU, and which well-known troublemakers are open (overlays, recorders, browsers, launchers, cloud sync, security scans, vendor software, virtual machines). For the best result start the game, idle in a menu, switch back and run it. It only reads. The result reflects that moment, not necessarily the captured session.

**Header buttons.** **Open capture...**, **Recent captures**, **PC setup...**, **Open CapFrameX** (or **Get CapFrameX** if it is not installed) and **Export report...**.

## The game library

**Game library** lists every game daignostic knows: the ones it can read by itself (Black Myth: Wukong today), every game you have opened a capture of, and games you add. Each shows whether its settings are read automatically, entered by you, or unknown ("diagnosable from captures, no settings yet"). Any game can be diagnosed from a capture; settings only make the findings and the AI more accurate.

- **Found on this PC.** daignostic asks Steam (every library folder, on any drive), Epic Games and GOG what is installed, and lists those games too, marked "Not opened in a capture yet". It reads only the launchers' manifest files and never searches your drives. Games from elsewhere (other launchers, copied folders) are added by hand. Press **Look again** to re-check.
- **Add a game...** (or **Add to library...** on a found game) choose the game's `.exe` (daignostic only reads its name and folder), then type or paste its settings, or let your own AI pull them out of its config file.
- **Edit settings...** and **Set install folder...** change what is known; **Remove from library** forgets a game you added.
- Settings saved here apply to captures recorded after you saved them. For an older capture, enter its settings on the Analyze page (PC and setup tab), where they are kept for that capture only.

## Asking an AI (optional)

AI is optional. Without it, everything above still works.

**Inside daignostic.** In **Settings**, turn on "Ask an AI from inside daignostic", enter the endpoint address and a **model name** exactly as your provider spells it (use **List models** to see what your endpoint offers), add an API key if needed, and press **Test AI connection**. Then, on the Analyze page (the **Ask AI** tab):

1. Open **Preview exactly what will be sent or exported**. That text is the entire message.
2. Optionally add a note ("I turned ray tracing off in this run").
3. Press **Ask AI**. A progress card shows what is happening and how long it has taken, and has a **Cancel** button. Slow answers get a hint; failures say what went wrong (cannot reach the AI, key or model rejected, took too long, or an answer in the wrong format, which you can read) and what to try, with **Try again** and a shortcut to Settings.

The answer ranks the likely causes for your data, with confidence, things to try, and the evidence behind each claim. Claims that cite no evidence are discarded and counted. It also lists **what the AI could not tell from this data**. You can also type a question of your own ("Ask the AI something specific"); it is answered in a separate card. Each time you ask again, the AI receives a short digest of the previous exchange so it keeps the context. The AI answers once per request and never asks you questions back: this is not a chat, and it has no access to your PC.

**With your own AI.** Press **Export for my own AI...** to save one file containing instructions, the exact measured data, and how to check the answer. Give it to any chat or model you like.

## Comparing two runs

Pick the earlier run as **A** and the later one as **B** (load captures with the buttons, or from the Analyze page). The table shows each measure, the change, and whether B is better. The chart overlays both runs: A in yellow, B in blue, with bold one-second averages over faint per-frame lines. Use the picker to switch between frame time, FPS and (when both captures have them) GPU load, temperature, clock, video memory, power and latency. *Context* rows (GPU load, VRAM, temperature) are shown for information and are not scored. Change **one thing per run** so the result means something. The AI panel works here too.

## Exporting a report

**Export report...** saves a Markdown summary (metrics, findings with suggestions, game settings and any AI analysis). You choose where it goes. It contains no file paths or computer name.

## Settings

Problems are shown directly under the field they are about (for example a wrong endpoint address appears under the endpoint box), never in a banner at the top.

| Setting | Purpose |
|---|---|
| Captures folder | Where **Recent captures** looks when CapFrameX is not available. |
| Find captures through CapFrameX | Lets daignostic ask a running CapFrameX for its list of captures. The page has a step-by-step guide for turning MCP on in CapFrameX and **Test connection** says which of the usual problems you have. Read-only. |
| Ask an AI from inside daignostic | Off by default. When on: endpoint, model name, API key, and whether to remember the key on this PC (encrypted) or keep it in memory only. |
| Include other programs' names in AI messages | Off by default. When off, the AI gets only anonymous totals and category names. |

## Troubleshooting

| Problem | What to do |
|---|---|
| "That file is not a readable CapFrameX capture" | Open the `.json` that CapFrameX saved, not a summary or exported CSV. |
| No sensor-based findings | The capture has no sensor data. Enable sensor logging in CapFrameX and record again. |
| No game settings | daignostic only reads some games by itself. Use **Add game settings...**. |
| "Could not reach the AI endpoint" | Check the endpoint address and that the gateway is running. |
| "Check the endpoint address and the model name" | The server answered 404: usually a misspelled model name. Use **List models**. |
| "Check the API key in Settings" | The endpoint rejected the key (HTTP 401 or 403). |
| CapFrameX not found | Follow the guide in Settings, then **Test connection**. Or just open the file directly. |
| Every claim from the AI was discarded | The model ignored the citation rule. Try a stronger model. |
| Background check shows GPU as n/a | Windows did not provide per-program GPU counters; CPU and disk results are still valid. |
