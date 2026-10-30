# Logo brief

> **Status: done.** The final files are `assets/logo.svg` (the icon), `assets/logo-mark.svg` and `assets/logo-mark-light.svg`. A wordmark was also drafted but not adopted: its letters were hand-drawn paths with a malformed "s" and a "c" cut off by the canvas. If you want a wordmark, ask for text converted to outlines from a real font. The rest of this page is the brief that was used.

Give the prompt below to an image or logo generation AI. Ask for several concepts, pick one, then ask for the deliverables listed after the prompt.

## Prompt

> Design an app logo (icon) for **analyxell**, a Windows desktop app that diagnoses PC game performance problems, such as stutter and low frame rates, and explains them with the help of AI. The name is "analyze" plus "xell" (the author's name), in the family of aixell and xellter.
>
> **Concept:** a frame-time graph, the kind a game-performance tool draws, as a calm wavy line that runs left to right, with one sharp spike (a stutter). At the top of the spike sits a four-point sparkle, the common symbol for AI, as if the AI is spotting the problem. Optionally a tiny second sparkle nearby. The idea in one sentence: *"an AI that notices the one frame that went wrong."*
>
> **Style:** modern, flat, clean vector. Friendly but technical, in the family of Windows 11 / Fluent app icons. Smooth rounded shapes, thick round-capped line, soft depth only through a gentle gradient and one subtle glow behind the sparkle. No 3D, no photo-realism, no clutter.
>
> **Shape and layout:** a rounded-square app tile (corner radius about 22% of the width) containing the mark, centred, with about 10% padding on every side. Square 1:1 canvas.
>
> **Colours:** tile background deep navy, for example a diagonal gradient from #15294A to #0A1424. The wave in teal to sky blue (#2DD4BF to #38BDF8). The spike in warm amber (#FBBF24). The sparkle in pale cream (#FFF7E0) with a soft amber glow. A faint dotted baseline in white at about 12% opacity. Use six colours or fewer overall.
>
> **Must work small:** it has to stay recognisable at 16x16 and 32x32 pixels. The spike and the sparkle must stay readable when the thin details disappear, so keep the main shapes bold and the line thick.
>
> **Do not include:** any text or letters inside the mark, any existing brand or logo (NVIDIA, AMD, Xbox, Windows, CapFrameX, game logos), robots or faces, circuit patterns, brains, magnifying glasses or gears, drop shadows that bleed outside the tile.
>
> Give me 3 different concepts first. After I choose one, produce the final files listed below.

## Files I need back (after you pick a concept)

Put them in the repository's `assets/` folder.

| File | What it is | Requirements |
|---|---|---|
| `logo.svg` | **The main logo. Most important.** The full icon: tile plus mark. | Vector SVG, square, `viewBox="0 0 256 256"`. Shapes and paths only, with text and strokes converted to outlines where it matters. No embedded bitmap, no external fonts, no blur or filter effects that some renderers ignore. Linear and radial gradients are fine. Transparent outside the rounded tile. |
| `logo.png` | A render of the same icon. | 1024 x 1024 px, PNG with a transparent background (real alpha, not white). Used if the SVG cannot be used. |
| `logo-mark.svg` | **Only the mark** (wave, spike, sparkle), with no tile. | Same viewBox. Flat shapes in one colour is ideal. Used for tiny sizes and single-colour places. |
| `logo-mark-light.svg` (optional) | The mark in dark navy, for light backgrounds. | Same as above. |
| `logo-wordmark.svg` (optional) | A horizontal lockup: icon on the left, the word `analyxell` in lowercase on the right. For the README header. | Text converted to outlines (paths), so it needs no font. Dark and light variants if possible. |

If the AI can only produce raster images, a **1024 x 1024 (or larger) transparent PNG** is enough: I will generate the icon sizes and Windows tile assets from it. An SVG is still preferred because it stays sharp at every size.

## What happens after you hand them over

1. Save the files into `assets/`.
2. I run `scripts/build-icons.ps1` (or `-FromPng` for a PNG-only logo) to build `AppIcon.ico`, then `winapp manifest update-assets` to regenerate every Windows tile size, and then rebuild the app, installer and portable build.
3. I check it at 16, 24, 32, 48 and 256 px, in light and dark mode, before committing.

## Design checks

- Squint test: is it still "wave, spike, sparkle" when blurred?
- 16 px test: does the spike survive?
- Light and dark taskbar: is the tile edge visible on both?
- Does it avoid looking like an existing app or brand?
