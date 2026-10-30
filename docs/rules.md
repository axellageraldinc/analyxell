# Diagnostic rules

"What stands out" is computed by plain code, locally, with no AI and no network. Each rule below looks at measured values, fires only when its condition holds, cites the facts it is based on, and carries concrete suggestions. The AI never decides whether a rule fires; it only explains and prioritises the results ([ADR 0004](adr/0004-deterministic-analysis-ai-explains.md), [ADR 0014](adr/0014-suggestions-and-single-answer-ai.md)).

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
| `GPU_BOUND`, `CPU_BOUND`, `BALANCED` | the bottleneck verdict (GPU busy time, GPU and CPU load) | Info |
| `THERMAL` | GPU thermal limit flag in 5% or more of samples, or GPU at 87 °C or more | High |
| `POWER_LIMIT` | GPU power limit flag in 25% or more of samples | Medium |
| `GPU_HOT` | GPU 85 °C or more (90 is High), or hot spot 95 °C or more | Medium / High |
| `CPU_HOT` | CPU package 90 °C or more (95 is High) | Medium / High |
| `VRAM_HOT` | video memory junction 95 °C or more | Medium |
| `VRAM_PRESSURE` | peak video memory 85% or more of the card's total (95 is High) | Medium / High |
| `RAM_PRESSURE` | system memory peaked at 92% or more | Medium |
| `COMMIT_PRESSURE` | Windows memory commit averaged 90% or more | Low |

## Display, sync and latency

| Rule | Fires when | Severity |
|---|---|---|
| `TEARING_POSSIBLE` | frames presented with tearing allowed and no sync (90%+), and no sign of variable refresh | Medium |
| `TEARING_ABOVE_RANGE` | variable refresh is active but 5% or more of frames exceed the display's maximum refresh | Medium |
| `TEARING_UNLIKELY` | sync is off but variable refresh is active and the game stays in range | Info |
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
| `RAY_TRACING_WITH_HITCHES` | the game's settings show ray tracing on and the capture hitches | Medium (a hypothesis to test) |

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
