# Diagnostic rules

"Findings" are computed by plain code, locally, with no AI and no network. Each rule below looks at measured values, fires only when its condition holds, cites the facts it is based on, and carries concrete suggestions. The AI never decides whether a rule fires; it only explains and prioritises the results ([ADR 0004](adr/0004-deterministic-analysis-ai-explains.md), [ADR 0014](adr/0014-suggestions-and-single-answer-ai.md)). For the problems players describe, their causes and fixes, and what a capture can and cannot show, see [Gaming problems, causes and fixes](problems-and-causes.md).

Thresholds are conservative rules of thumb, not guarantees. Hardware limits vary by model.

## Frame delivery

| Rule | Fires when | Severity | Typical suggestions |
|---|---|---|---|
| `FREEZES` | any frame of 200 ms or more | Low (1-2), Medium (3-9), High (10+ or a freeze of 500 ms+) | Note what you were doing at those moments; replay the route to see if they repeat in the same place |
| `FREEZES_GPU_BUSY` | GPU load around the freezes averages 85% or more | Medium | Lower GPU-heavy settings one at a time (ray tracing first); try a stronger upscaling mode |
| `FREEZES_GPU_IDLE` | GPU load around the freezes averages under 50% | Medium | Check background programs; replay the area (shader compilation); move the game to an SSD |
| `DISK_ACTIVITY_AT_FREEZES` | a drive reached 70% activity near a freeze | Medium | SSD or NVMe, pause downloads and sync, keep space free |
| `DISK_RULED_OUT` | freezes exist and no drive passed the threshold | Info | none |
| `UNEVEN_FRAMETIMES` | 1% low average is under half the average | Medium | Fix freezes first; cap the frame rate slightly below what the PC sustains; enable variable refresh |
| `LOW_FPS` | average under 60 fps | Medium (High under 30; Info if you entered a cap under 60) | Upscaling, resolution scale, GPU-heavy settings (or CPU-heavy settings when CPU-bound) |
| `SMOOTH` | no freezes, few stutters, healthy lows | Info | none |

## Hardware limits and heat

| Rule | Fires when | Severity |
|---|---|---|
| `GPU_BOUND`, `BALANCED` | the bottleneck verdict (GPU busy time, GPU and CPU load) | Info |
| `CPU_BOUND` | busiest CPU thread 90%+ while GPU load is under 85%: "The CPU is holding the GPU back" | Medium under 60 fps, else Info |
| `CPU_SINGLE_THREAD` | CPU-bound, busiest thread 85%+, whole CPU 60% or less | Medium under 60 fps, else Low |
| `CPU_LIMITED_MOMENTS` | not CPU-bound overall, but 15%+ of samples had a maxed thread and a GPU under 85% | Low / Medium (30%+) |
| `THERMAL` | GPU thermal limit flag in 5% or more of samples, or GPU at 91 °C or more | High |
| `POWER_LIMIT` | GPU power limit flag in 25% or more of samples while the GPU is not flat out (a card at full load that also hits its power limit is simply GPU-bound) | Low on a desktop card, Medium on a laptop |
| `GPU_HOT` | GPU 88 °C or more on NVIDIA, 85 on others (92 / 90 is High), or hot spot 95 °C or more | Medium / High |
| `CPU_HOT` | CPU package 90 °C or more (95 is High). Ryzen 7000 and 9000 are built to boost until about 95 °C, so for them it needs 97 °C (99 is High) | Medium / High |
| `VRAM_HOT` | video memory junction 95 °C or more; 104 °C for GDDR6X cards (RTX 3070 Ti and up, 4070 and up), which are rated for it | Medium |
| `VRAM_PRESSURE` | peak video memory 85% or more of the card's total (95 is High) | Medium / High |
| `RAM_PRESSURE` | system memory peaked at 92% or more | Medium |
| `COMMIT_PRESSURE` | Windows memory commit averaged 90% or more | Low |

## Over the length of the run

| Rule | Fires when | Severity |
|---|---|---|
| `PERFORMANCE_DECLINES` | last-quarter frame rate is 15%+ below the first quarter (runs of 60 s or more); names video memory, system memory or GPU temperature growth when present | Medium |
| `HITCHES_FADE` | the game hitches and the first third of the run has 8+ stutters and freezes and 2.5x more than the last third | Low (a likely explanation, not proven) |

## Memory setup

| Rule | Fires when | Severity |
|---|---|---|
| `RAM_SINGLE_MODULE` | the capture lists one memory stick, and the game is CPU-bound or hitching | Medium if CPU-bound, else Low |
| `RAM_SLOW_SPEED` | memory at 2667 MT/s or less (4800 when CPU-bound), and CPU-bound or hitching | Medium if CPU-bound, else Low |

## Display, sync and latency

| Rule | Fires when | Severity |
|---|---|---|
| `TEARING_POSSIBLE` | frames presented with tearing allowed and no sync (90%+), a refresh-rate reading that stays fixed, frames not on the refresh grid, and no variable refresh entered | Medium |
| `TEARING_ABOVE_RANGE` | variable refresh is active but 5% or more of frames exceed the display's maximum refresh | Medium |
| `TEARING_UNLIKELY` | sync is off but variable refresh is active and the game stays in range | Info |
| `TEARING_HANDLED_BY_DRIVER` | you entered VSync as on in the driver (or fast/adaptive). A driver override happens after the game, where a capture cannot see it, so your entry is believed and the tearing rules stay quiet | Info |
| `VSYNC_DETECTED` | the game asked for no VSync, but 90%+ of frames reached the screen exactly on a refresh. That pattern means the driver applied VSync, so tearing is unlikely | Info |
| `TEARING_UNKNOWN` | the game asked for no VSync, the capture has no refresh-rate reading, and you have not entered your display sync or VSync. Most modern games send every frame this way even with G-SYNC on, so it says what to check instead of guessing | Info |
| `VSYNC_ENTERED_BUT_NOT_SEEN` | you entered in-game VSync as on, but the capture shows none | Info |
| `COMPOSED_PRESENT` | the capture says the game was presented through the desktop compositor and it hitches | Low |
| `HIGH_LATENCY` | estimated input-to-display latency averages 60 ms or more, or its 95th percentile reaches 100 ms | Medium / Low |
| `UNEVEN_PACING` | 99th percentile animation error of 10 ms or more | Low |
| `DISPLAY_BELOW_MAX` | Windows runs the display slower than the maximum refresh you entered | Medium |

Tearing: a capture cannot show tearing itself, only the conditions for it, and the wording says so. "Variable refresh is active" is inferred from the display's refresh rate moving during the capture, or from your entered setup.

## Your setup and Windows

| Rule | Fires when | Severity |
|---|---|---|
| `GAME_ON_HDD` | the game's drive is a hard disk | Medium with freezes or many stutters, else Low |
| `DRIVE_NEARLY_FULL` | the game's drive has under 10% free | Low |
| `SHADER_CACHE_SMALL` | the GPU shader cache holds under 100 MB and there are freezes | Low |
| `POWER_PLAN_SAVER` | Windows is on the Power saver plan | Medium |
| `GAME_MODE_OFF` | Windows Game Mode is off and the game hitches | Low |
| `RESIZABLE_BAR_OFF` | Resizable BAR is off and the game is CPU-bound or hitching | Low |
| `BACKGROUND_CAPTURE_ON` | Windows background recording is on and the game hitches | Low |
| `MEMORY_INTEGRITY_ON` | Memory integrity is on and the game is CPU-bound or hitching. Information only; no advice to turn it off | Info |
| `UPSCALING_OFF_GPU_BOUND` | the game's settings show DLSS off, the GPU is the limit and the average is under 60 fps | Medium |
| `FRAME_GENERATION_DETECTED` | the capture shows frame generation: 15%+ of frames labelled as not from the game, frame times that alternate long-short (lag-1 correlation of -0.4 or lower), or a display that changes 1.6x as often as the game presents. Stutter is then judged on groups of frames so the alternation is not counted as stutter | Info |
| `UPSCALING_SHIFTS_LIMIT` | DLSS is on in the game's settings and the CPU is the limit: expected, not a fault | Info |
| `FRAME_GENERATION_ON` | frame generation is on and the game hitches, pacing is uneven (99th percentile animation error 8 ms+) or latency averages 50 ms+. A hypothesis to test | Low |
| `REFLEX_OFF` | low latency mode is off, the GPU is the limit and latency averages 40 ms+ | Low |
| `RAY_TRACING_WITH_HITCHES` | the game's settings show ray tracing on and the capture hitches | Medium (a hypothesis to test) |

## Ryzen, NVIDIA and unstable tuning

These read the CPU and GPU model names, so they work for any Ryzen and any GeForce, not for one PC. They do not depend on a game's settings file, which is what makes them useful for games analyxell has no reader for.

| Rule | Fires when | Severity |
|---|---|---|
| `PERIODIC_HITCHES` | six or more freezes or stutters whose gaps are within 15% of each other (3 s to 15 min apart). A steady rhythm points at something on a timer, not at the game. Adds the fTPM hint on Ryzen when 20 s or more apart | Medium |
| `VCACHE_WRONG_CHIPLET` | a two-chiplet X3D (7900X3D, 7950X3D, 9900X3D, 9950X3D) with hitches or a CPU limit, where the cores without the extra cache are 30%+ busier than the cache cores. A likely explanation from core load | Medium |
| `CHIPLET_SPLIT` | a two-chiplet Ryzen 9 with both halves 20%+ busy and within 1.7x of each other, with hitches or a CPU limit. A possible cause to test | Low |
| `CPU_BELOW_BOOST` | the game is CPU-limited yet the fastest core peaked under 93% of the model's rated boost | Medium |
| `CPU_ABOVE_RATED` | the fastest core reached 150 MHz or more above the rated boost: PBO or an overclock. Information; a retest hint appears only if the game hitches | Info |
| `AMD_CHIPSET_MISSING` | no "AMD Chipset" program is installed on a Ryzen PC | Low / Medium (hitches or CPU-limited) |
| `RAM_ABOVE_SWEET_SPOT` | Ryzen 7000 or 9000 desktop with memory at DDR5-7200 or faster, where the memory controller drops to half speed | Low |
| `GPU_CLOCK_DROPS` | NVIDIA, hitches, GPU only 35-85% loaded, and the slowest tenth of core clocks under 60% of the usual best | Low |
| `GPU_OVERCLOCKED` | the driver reports a core offset of +50 MHz or more, a memory offset of +200 MHz or more, or the capture's peak core clock is 45 MHz past the card's stock maximum | Info (Low with hitches) |
| `GPU_CLOCKS_LOWERED` | the driver reports a core offset of -50 MHz or lower | Low |
| `GPU_POWER_LIMIT_LOWERED` | the power limit is under 97% of the card's default | Medium when GPU-limited, else Low |
| `UNDERVOLT_LIKELY` | at 95%+ GPU load the card held its clock (slowest tenth within 5% of the best) while drawing under 80% of its power limit and rarely hitting it. An inference: voltage curves cannot be read | Info |
| `HAGS_OFF_WITH_FRAMEGEN` | frame generation is on, the card supports DLSS frame generation (RTX 40 or newer) and hardware GPU scheduling is off | Low |
| `UPSCALER_AVAILABLE` | the game's folder holds DLSS files, the card is an RTX, the GPU is the limit under 60 fps and the game's settings are unknown. Adds frame generation (and multi frame generation on RTX 50) when the card supports it and 40+ fps are rendered | Low |
| `HARDWARE_ERRORS_LOGGED` | Windows logged WHEA hardware errors in the last 30 days (3 or more is Medium). The honest sign of an unstable Curve Optimizer, PBO, overclock or EXPO profile | Low / Medium |
| `DISPLAY_DRIVER_RESETS` | Windows logged event 4101 (display driver stopped responding) in the last 30 days | Low / Medium |

Not readable, and so not claimed: a custom GPU voltage curve, the CPU's Curve Optimizer or PBO settings, and whether DLSS or Ray Reconstruction is switched on (only whether the game ships them, from its files, or what you or a settings reader say).

## Programs competing with the game

These come from the **background check** you run yourself. They reflect that moment, not necessarily the captured session, and are shown only on your screen: the names never go into AI messages unless you turn that on in Settings.

| Rule | Fires when | Severity |
|---|---|---|
| `BACKGROUND_CPU` | other programs use 8% or more of the CPU (15% is Medium) | Low / Medium |
| `BACKGROUND_DISK` | other programs move 5 MB/s or more (20 is Medium) | Low / Medium |
| `BACKGROUND_GPU` | other programs use 3% or more of the GPU (8% is Medium) | Low / Medium |
| `BACKGROUND_KNOWN_APPS` | well-known troublemakers are running (overlays, recorders, browsers, launchers, cloud sync, security scans, vendor software, VMs...) | Low or Info |
| `BACKGROUND_QUIET` | nothing stood out | Info |

## Next steps

"What you can do" lists the most valuable suggestions, most severe findings first, without repeating advice, at most six. It is deterministic, so it works without AI.

## Adding or changing a rule

1. Add the rule in `Rules.cs`, citing fact keys that exist (a test fails if a finding cites a missing fact).
2. Give every finding above Info at least one suggestion that is an in-game setting or a simple, reversible check (a test enforces this).
3. Add tests for the rule firing and for staying quiet.
4. Update this page.
