# User guide

## Before you start

daignostic does not record gameplay. Use [CapFrameX](https://github.com/CXWorld/CapFrameX) to capture a session; it saves a `.json` file. For the best diagnosis, turn on CapFrameX's sensor logging (CPU, GPU, memory and drive sensors). Without sensors you still get frame-time results, but not the "why" (GPU busy, disk activity, temperatures).

Tip: record 5 to 10 minutes that include the moments that bother you.

## Opening a capture

- Drop a `.json` file on the window, or press **Open capture...**
- **Recent captures** lists captures found through CapFrameX (if it is running with MCP enabled) or in your captures folder (set in **Settings**).
- From a terminal: `Daignostic.exe "C:\path\run1.json" "C:\path\run2.json"` opens one or more captures.

## Reading the Analyze page

**The cards**

| Card | Meaning |
|---|---|
| Average | Mean frame rate. |
| 1% low / 0.1% low | The frame rate of your worst 1% / 0.1% of frames. These decide how smooth the game *feels*; a good average with bad lows means stutter. |
| Stutters | Frames that took more than twice as long as their neighbours. |
| Freezes | Frames that took 200 ms or more: the game visibly hung. |
| Limit | Whether the GPU, CPU, temperature or power limit holds performance back, and how confident that call is. |

**The chart** shows every frame's time in milliseconds (lower is smoother). The white line is a one-second average. Red markers are freezes. Spikes above the visible range are clipped so one huge freeze does not flatten the rest. Hover to read exact values.

**What stands out** lists findings. Each ends with "Based on: ..." naming the measurements behind it. Blue is information, yellow is worth a look, red is serious.

**Game settings** (when your game is supported) shows the graphics settings from the game's own config file. Values marked *(raw id)* are the game's internal numbers, not preset names. The file shows the settings at the game's *last save*, which may differ from what was active during the capture.

## Asking the AI

1. Configure an endpoint in **Settings** and use **Test AI connection**.
2. On the Analyze page, open **Preview exactly what will be sent**. That text is the entire message.
3. Optionally add a note ("I turned ray tracing off in this run").
4. Press **Ask AI**.

The answer lists findings with a confidence, suggested things to try, and the evidence each claim rests on. Claims that cite no evidence are discarded and counted. An "inconclusive" notice means the data did not support a firm conclusion; take that seriously.

The AI can only suggest in-game settings or simple checks. It never runs anything.

## Comparing two runs

Pick the earlier run as **A** and the later one as **B** (load captures with the buttons, or from the Analyze page). The table shows each measure, the change, and whether B is better. *Context* rows (GPU load, VRAM, temperature) are shown for information and are not scored. Change **one thing per run** so the result means something.

## Exporting a report

**Export report...** saves a Markdown summary (metrics, findings, game settings and any AI analysis). You choose where it goes. It contains no file paths or device names.

## Settings

| Setting | Purpose |
|---|---|
| Captures folder | Where **Recent captures** looks when CapFrameX is not available. |
| Find captures through CapFrameX (MCP) | Lets daignostic ask a running CapFrameX for its list of captures. Needs `McpEnabled` in CapFrameX's settings. Read-only. |
| AI endpoint / preset / API key | Where AI questions go. Optional. |

## Troubleshooting

| Problem | What to do |
|---|---|
| "That file is not a readable CapFrameX capture" | Open the `.json` that CapFrameX saved, not a summary or exported CSV. |
| No sensor-based findings | The capture has no sensor data. Enable sensor logging in CapFrameX and record again. |
| "Could not reach the AI endpoint" | Check the endpoint address and that the gateway is running. |
| "Check the API key in Settings" | The endpoint rejected the key (HTTP 401 or 403). |
| CapFrameX not found | Start CapFrameX, make sure MCP is enabled, then use **Test connection** in Settings. Or just open the file directly. |
| Every claim from the AI was discarded | The model ignored the citation rule. Try a stronger model or preset. |
