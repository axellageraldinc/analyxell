# Gaming problems, their causes and fixes

This is the knowledge behind daignostic's findings: what players usually complain about, what usually causes it, what usually fixes it, and **whether daignostic can see it**. It is the plan the rules in [rules.md](rules.md) follow. Where a cause can only be guessed from a capture, the finding says it is a likely explanation, not a proven one.

Thresholds are rules of thumb. Several of the sources below are vendor blogs and forum threads, so no number here is a guarantee; the advice that several independent sources agree on is the advice used.

Sources: [PC stutter guides](https://techfuelhq.com/articles/pc-stuttering-in-games-2026/), [ASUS ROG on 1% lows](https://rog.asus.com/articles/guides/what-are-1-lows-how-to-monitor-and-fix-stutters-in-your-pc-games/), [telling CPU from GPU limits](https://www.digitalcitizen.life/how-to-tell-whether-your-cpu-or-gpu-is-limiting-gaming-performance/), [Intel GPA trace analysis](https://www.intel.com/content/www/us/en/docs/gpa/get-started-guide/2023-3/trace-analysis.html), [common gaming PC issues](https://www.cyberpowerpc.com/blog/common-gaming-pc-issues-and-how-to-fix-them/).

## 1. The problems players actually describe

| What the player says | What it is in the data |
|---|---|
| "Low FPS" | Average frame rate below what they want |
| "It stutters even though FPS is fine" | Good average, bad 1% / 0.1% lows, many frame-time spikes |
| "It freezes for a moment" | Frames of 200 ms or more |
| "It gets worse the longer I play" | Frame rate or lows fall over the run |
| "It hitches when I enter a new area" | Spikes clustered at the start or at loading points |
| "It feels laggy / delayed" | High input-to-display latency |
| "The picture tears" | Frames shown mid-refresh (sync off, outside the display's range) |
| "Motion is not smooth at a high FPS" | Uneven pacing, frames dropped or shown at uneven intervals |
| "It was fine, then a patch / driver broke it" | Needs two runs: use Compare |
| "It crashes" or "online lag" | Not visible in a frame-time capture (see section 7) |

## 2. Causes, how daignostic recognises them, and the fixes

### Hardware limits

| Cause | Evidence in a capture | daignostic finding | Fixes it suggests |
|---|---|---|---|
| **GPU is the limit** | GPU busy 90%+ of frame time, GPU load ~95%+ | `GPU_BOUND` | Lower resolution or GPU-heavy settings, upscaling, a frame cap just under what the GPU can do |
| **CPU is the limit** | One thread near 90%+ while the GPU is below 85%; frame rate barely moves with resolution | `CPU_BOUND` ("The CPU is holding the GPU back") | Lower CPU-heavy settings, memory at its rated speed, close background programs; test by lowering resolution |
| **One thread is the limit** | Busiest thread 85%+ while the whole CPU averages 60% or less | `CPU_SINGLE_THREAD` | Faster single-core speed or more cache helps; more cores will not |
| **CPU limits only some moments** | 15%+ of samples have a maxed thread and a GPU with room | `CPU_LIMITED_MOMENTS` | CPU-heavy settings for busy areas |
| **GPU too hot** | Thermal-limit flag, 85-90 °C+ | `THERMAL`, `GPU_HOT` | Dust, airflow, fan curve, frame cap |
| **GPU power-limited** | Power-limit flag in 25%+ of samples | `POWER_LIMIT` | Restore the power limit, cap the frame rate |
| **CPU or video memory too hot** | 90-95 °C+ | `CPU_HOT`, `VRAM_HOT` | Cooling |
| **Not enough video memory** | Video memory at 85-95%+ of the card | `VRAM_PRESSURE` | Lower texture quality / resolution scale |
| **Not enough system memory** | RAM above 92%, commit above 90% | `RAM_PRESSURE`, `COMMIT_PRESSURE` | Close programs, more RAM, bigger page file |
| **Memory in single-channel mode** | The capture lists one memory stick | `RAM_SINGLE_MODULE` | Add a matching second stick |
| **Memory at default speed (XMP/EXPO off)** | Memory at 2667 MT/s or less (4800 when CPU-bound) | `RAM_SLOW_SPEED` | Enable XMP / EXPO / DOCP in the BIOS (only if the kit is rated higher) |
| **Game on a hard disk** | Drive type read from Windows | `GAME_ON_HDD` | Move to an SSD |
| **Drive nearly full** | Under 10% free | `DRIVE_NEARLY_FULL` | Free space |
| **Resizable BAR off** | Capture lists it as disabled | `RESIZABLE_BAR_OFF` | Enable in BIOS if supported |

### Things that change over time

| Cause | Evidence | Finding | Fixes |
|---|---|---|---|
| **Memory fills up, leaks, or the GPU warms up** | Last-quarter frame rate 15%+ below the first quarter, with video or system memory or temperature rising | `PERFORMANCE_DECLINES` | Lower textures, restart the game now and then, cooling |
| **Shader compilation / loading new areas** | 2.5x more hitches in the first third than in the last third | `HITCHES_FADE` (a likely explanation, not proven) | Play before judging, record again, keep the driver shader cache on, SSD, precompile option |
| **Freezes** | Frames of 200 ms+ | `FREEZES`, with `FREEZES_GPU_BUSY`, `FREEZES_GPU_IDLE`, `DISK_ACTIVITY_AT_FREEZES` | Depends on what the sensors show around each freeze |

### Resource contention

| Cause | Evidence | Finding | Fixes |
|---|---|---|---|
| **Other programs using CPU, disk or GPU** | Background check (10 s sample of the running PC) | `BACKGROUND_CPU`, `BACKGROUND_DISK`, `BACKGROUND_GPU` | Close or pause them |
| **Known troublemakers** (overlays, recorders, browsers, launchers, cloud sync, security scans, vendor software, VMs) | Process names, matched locally | `BACKGROUND_KNOWN_APPS` | Category-specific advice |
| **Windows background recording** | Registry flag | `BACKGROUND_CAPTURE_ON` | Turn it off if unused |

### Display, sync and latency

| Cause | Finding |
|---|---|
| Sync off, no variable refresh | `TEARING_POSSIBLE` |
| Frame rate above the display's refresh range | `TEARING_ABOVE_RANGE` |
| Sync off but variable refresh keeps up | `TEARING_UNLIKELY` |
| VSync forced in the GPU driver (invisible in a capture, so your entered setup is believed) | `TEARING_HANDLED_BY_DRIVER` |
| You said VSync is on but the capture shows none | `VSYNC_ENTERED_BUT_NOT_SEEN` |
| Display running below its maximum refresh rate | `DISPLAY_BELOW_MAX` |
| Game drawn through the desktop compositor (windowed) | `COMPOSED_PRESENT` |
| High input latency | `HIGH_LATENCY` |
| Uneven motion pacing | `UNEVEN_PACING` |

### Windows and driver options (read only, never changed)

| Option | Finding |
|---|---|
| Power saver plan | `POWER_PLAN_SAVER` |
| Game Mode off | `GAME_MODE_OFF` |
| Small or empty shader cache | `SHADER_CACHE_SMALL` |
| Memory integrity (VBS) on | `MEMORY_INTEGRITY_ON`, information only: it protects the PC, and many people keep it on |
| Hardware-accelerated GPU scheduling | Shown in PC details only. Sources disagree on whether it helps, so no finding tells you to change it |

## 3. What a capture cannot tell

Honest limits, so the findings do not overreach:

- **Why** a spike happened. The sensors around it narrow it down; the finding says "likely".
- **Network lag**, server tick rate, packet loss: not in a frame-time capture.
- **Crashes** and **driver timeouts**: nothing is recorded when the game dies.
- **Actual tearing**: only whether the conditions allow it.
- **Whether the GPU driver is the problem**: the driver version is shown but its quality is not judged. A clean reinstall is a common first step in guides, but no rule fires on it.
- **Game bugs** that no setting fixes (some engines stutter on any hardware). Compare two runs to see whether a change helped.

## 4. How the advice is chosen

1. Prefer fixes that are free, reversible and inside the game or a Windows setting.
2. Fixes that need money or opening the PC (more RAM, an SSD, a new CPU) come after, and say so.
3. Fixes that lower security (turning off Memory integrity) are mentioned only as information, never as a recommendation.
4. One change at a time, then record the same route again and use **Compare**.
