# 0008. A small custom chart control instead of a charting dependency

- Status: Accepted
- Date: 2026-10-30

## Context

The app needs one kind of chart: frame time over time, with a smoothed overlay, reference lines, freeze markers and a hover read-out, for captures of up to roughly 45,000 frames per ten minutes. Charting libraries for WinUI 3 pull in large dependencies (often SkiaSharp), lag behind new Windows App SDK releases, and are awkward under trimming and theming.

## Decision

Draw the chart ourselves with XAML shapes in `FrameTimeChart`. Reduce data to one min/max pair per pixel column, take colours from theme resources so light and dark mode work, and expose an automation name that summarises the chart for screen readers.

## Consequences

- No third-party runtime dependency; smaller package; follows the system theme.
- Smooth for long captures because only about one point per pixel is drawn.
- We maintain about 300 lines of drawing code. Features beyond this chart (zoom, pan, many chart types) would be expensive here; revisit with a new ADR if they become requirements.
