# Spot The Diff

A Windows desktop tool that compares two screenshots and gives you back an image with **every difference boxed and numbered** - so you never have to eyeball two screens side by side again.

Typical use: compare the same settings page in two repositories, two environments (Dev vs Prod), before/after a release, or two versions of a UI.

Built with **.NET 8 and Windows Forms**. No external dependencies (the engine uses `System.Drawing`).

## Features

- **Paste straight from the Snipping Tool** - snip with `Win+Shift+S`, press `Ctrl+V`, done. The comparison runs immediately.
- **One image or two**
  - *One image mode (default):* paste a single snip containing both screens side by side. It is split down the middle (left = before, right = after).
  - *Two image mode:* paste or open two separate images. The first `Ctrl+V` fills *Before*, the second fills *After*; a third starts a new comparison.
- **Other inputs:** *Open...* button, drag & drop a file onto a panel, or right-click a panel (Paste here / Open... / Clear).
- **Clear marking:** red boxes (or circles) with numbers, drawn on both the before and after sides, numbered top-to-bottom, left-to-right.
- **Live tuning:** sliders and options re-run the comparison automatically.
- **Share the result:** copy it to the clipboard (paste into Teams, email, tickets) or save as PNG/JPG.
- **Zoom & pan:** mouse wheel zooms, drag pans, double-click fits. The Before and After panels move together.
- **Handles different sizes:** if the images differ in size, *After* is scaled to match *Before* (the status bar tells you).

## Getting started

### Requirements
- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer (to build), or Visual Studio 2022/2026 with the *.NET desktop development* workload

### Run it
```powershell
git clone https://github.com/dawood-myob/SpotTheDiff.git
cd SpotTheDiff
dotnet run --project src\SpotTheDiff
```
Or open `SpotTheDiff.sln` in Visual Studio, set **SpotTheDiff** as the startup project and press `F5`.

## How to use

1. Start the app.
2. Press `Win+Shift+S` and snip the area that contains both screens side by side.
3. Switch to Spot The Diff and press `Ctrl+V`.
4. The **Differences** panel shows the annotated image. Use **Copy result** (`Ctrl+C`) or **Save result...** (`Ctrl+S`).

To compare two separate images instead, untick *One image with both screens side by side*, then paste/open the *Before* image and the *After* image.

### Controls

| Control | What it does |
|---|---|
| **Paste (Ctrl+V)** | Pastes an image from the clipboard (Snipping Tool, Print Screen, a copied image or a copied image file). |
| **Open...** | Loads an image file (png, jpg, bmp, gif, tif). |
| **Swap** | Swaps Before and After. |
| **Clear** | Resets everything. |
| **Tolerance** (0-100) | How big a colour change must be to count as a difference. **Higher = ignores subtle changes** (anti-aliasing, compression noise). Lower it if real differences are missed. Default 25. |
| **Merge nearby (px)** | Changes closer together than this are grouped into one box (e.g. all the letters in a changed word). Default 12. |
| **Ignore smaller than (px)** | Regions with fewer changed pixels than this are treated as noise. Default 12. |
| **Mark with** | Rectangle (default) or Circle. |
| **Output** | *Side by side* shows annotated before and after; *After image only* shows just the after. |
| **Tint changed pixels** | Additionally shades the exact changed pixels in translucent red. |

### Keyboard shortcuts

| Keys | Action |
|---|---|
| `Ctrl+V` | Paste image |
| `Ctrl+C` | Copy result image |
| `Ctrl+S` | Save result image |
| Mouse wheel / drag / double-click | Zoom / pan / fit |

### Tips
- Too many tiny boxes? Raise *Tolerance* or *Ignore smaller than*.
- A changed word shows as several boxes? Raise *Merge nearby*.
- Missing a subtle change? Lower *Tolerance* (and make sure the snips are the same size and zoom level).
- For accurate results, capture both screens at the same browser zoom and window size.

## How it works

1. **Pixel compare** - each pixel's largest red/green/blue difference is checked against the tolerance, producing a "changed" mask.
2. **Merge** - the mask is dilated by *Merge nearby* pixels so close changes join up (linear-time separable dilation).
3. **Group** - connected regions are found with an iterative flood fill; each region's bounding box is taken from the real changed pixels, and tiny regions are dropped.
4. **Render** - boxes and numbers are drawn (with a white halo for visibility on any background) onto the before/after images.

## Project layout

```
SpotTheDiff.sln
src/
  SpotTheDiff.Core/     Diff engine: DiffEngine, DiffOptions, DiffResult (no UI)
  SpotTheDiff/          WinForms UI: MainForm, ImageViewer (zoom/pan), ImageUtil (clipboard/files)
tests/
  SpotTheDiff.Core.Tests/   xUnit tests for the engine
```

## Build & test

```powershell
dotnet build SpotTheDiff.sln -c Release
dotnet test  SpotTheDiff.sln
```

## Limitations / ideas

- Differences are numbered but not described (no text labels).
- The one-image mode splits exactly in half.
- It compares pixels, not meaning, so layout shifts can produce larger boxes.
- Ideas: auto-detect the split line, editable captions per difference, command-line mode.
