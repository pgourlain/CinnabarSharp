# CinnabarSharp — Roadmap

Goal: a Paint.NET-like image editor running natively on **macOS, Linux and Windows**.

## UI framework decision

| Option | macOS | Linux | Windows | Notes |
|---|---|---|---|---|
| .NET MAUI (current) | Mac Catalyst | **No** | WinUI | No official Linux desktop target. Disqualified. |
| **Avalonia UI 12** | Yes | Yes (X11) | Yes | Same Skia renderer on every OS, so the canvas looks identical everywhere. XAML/MVVM close to MAUI. Mature desktop controls (menus, docking, native dialogs). Used by Pixi editor and JetBrains Rider tooling. |
| Uno Platform (Skia desktop) | Yes | Yes | Yes | Viable, but WinUI API surface is heavier; desktop tooling less mature than Avalonia. |
| GTK4 via gir.core (Pinta 2.x path) | Weak | Yes | OK | Native on Linux only; macOS/Windows look and packaging are poor. |
| Eto.Forms | Yes | Yes (GTK) | Yes | Native widgets, but custom canvas rendering differs per backend. |

**Recommendation: Avalonia UI 12 + SkiaSharp, on .NET 10 (LTS).** (Plan originally said 11; 12.1 is current stable and is what the project uses.)

- Canvas: custom Avalonia `Control`. Phase 1 draws the flattened image as a `WriteableBitmap` (Avalonia renders it with Skia on every OS); switch to direct `SkiaSharp` drawing via `ICustomDrawOperation` if painting performance requires it (Phase 6). The rendering path is the same on all three OSes, so any difference you see between them is a real bug, not a backend quirk.
- Pixels: layers stored as BGRA buffers (the existing `ColorBgra` type fits) or `SKBitmap`. Keep Magick.NET for file formats and heavy effects, but don't use it for per-stroke painting, which would be too slow.
- `CinnabarSharp.Core` stays UI-free. The legacy MAUI `CinnabarSharpApp` was deleted in Phase 2.
- .NET 7 is out of support. Moving to .NET 10 is the first task.

## How to validate each phase

Every phase ends with a **Validation** checklist. Run it on all three OSes, and don't start the next phase until it passes on all three:

- [ ] macOS (Apple Silicon)
- [ ] Linux (Ubuntu 24.04 x64, X11 and Wayland/XWayland)
- [ ] Windows 11 x64

CI builds and runs all tests on the three OSes, including headless UI tests (`CinnabarSharp.Desktop.Tests`) that render the real window with Skia and upload screenshots per OS as GitHub Actions artifacts (`screenshots-windows-latest`, `screenshots-macos-latest`, `screenshots-ubuntu-latest`), so you can compare them side by side. The manual checks cover what headless can't: native menus, real keyboard/trackpad input, HiDPI, window management.

---

## Phase 0 — Foundation

- [x] Retarget `CinnabarSharp.Core` and `CinnabarSharp.Core.Tests` to `net10.0`; update NuGet packages (Magick.NET 14, xunit, Microsoft.Extensions 10).
- [x] Create a root `CinnabarSharp.slnx` with Core and Tests (Desktop added in Phase 1).
- [x] Remove platform assumptions from Core (no hard-coded paths or OS checks found).
- [x] Enforce the "Core is non-visual" constraint (see CLAUDE.md): `System.Drawing` removed (`Size` → `ImageSize`); `CoreArchitectureTests` fails if Core references a UI/rendering assembly.
- [x] CI matrix: `windows-latest`, `macos-latest`, `ubuntu-latest` running `dotnet build` and `dotnet test` (GitHub Actions, `.github/workflows/ci.yml`).
- [ ] Verify Magick.NET native libs load on all 3 (`Magick.NET-Q8-AnyCPU` ships osx-arm64, osx-x64, linux-x64, win-x64).

**Validation**
- [ ] `dotnet test` green on the 3 OSes (locally and in CI).
- [ ] `ImageDocumentEventTests` passes on Linux (sample file path resolution).

## Phase 1 — Avalonia shell

- [x] New project `CinnabarSharp.Desktop` (Avalonia 12 MVVM, CommunityToolkit.Mvvm), DI wired with `AddCinnabarSharpServices()` (`AppServices.Build()`).
- [x] Main window layout like Paint.NET: menu bar, toolbar, image tabs, tools strip (left), canvas (center), docked panels (Layers with visibility + add, History placeholder, Colors with swap), status bar (tool, cursor position, image size, zoom).
- [x] Native menu on macOS (`NativeMenu`; app menu with About, Avalonia adds Quit) and in-window menu on Windows/Linux, both built from one menu definition in `MainWindow.axaml.cs`. Items for later phases are shown disabled. Preferences item comes with settings (Phase 10).
- [x] Keyboard shortcuts: platform modifier (`Cmd` on macOS, `Ctrl` elsewhere) from `PlatformHotkeyConfiguration.CommandModifiers`; Paint.NET shortcuts (Redo is ⌘⇧Z on macOS, Ctrl+Y elsewhere). `X` swaps colors.
- [x] New Image dialog (width, height, white/transparent background) → new Core API `IWorkspaceService.NewDocument(size, background)`.
- [x] Canvas control renders the flattened document over a transparency checkerboard; nearest-neighbour when zoomed in; zoom in/out/best fit/actual size with Paint.NET presets; large images open fitted to the window.
- [ ] Tool icons (currently 2-letter labels). Pick an icon set that renders identically on all OSes (SVG/path icons, not font glyphs).
- [ ] Zoom keeps the view centered / zooms around the mouse (port `ImageDocumentWorkspace.ZoomAndRecenterView`) — moved to Phase 2 with Ctrl+wheel.

**Validation**
- [ ] App launches; window resizes; HiDPI crisp (Retina, Windows 150%, Linux scale 2).
- [ ] File > New creates a document shown on canvas.
- [ ] Shortcuts use the right modifier per OS; macOS app menu present.
  - macOS (2026-09-27): app launches, native menu bar shows CinnabarSharp/File/Edit/View/Image/Layers/Adjustments/Effects, hint shows ⌘+N. Still to check by hand: shortcuts inside the opened menus.

## Phase 2 — Documents, open/save, view

- [x] Importers/exporters: PNG, JPEG (quality 85, transparency flattened onto white), BMP, GIF, TIFF, WebP via `MagickImageFormat`, registered in `AddCinnabarSharpServices`. `IFormatManager` picks the format by extension, or by file content when the extension is missing/wrong. Photos are auto-rotated from EXIF on open.
- [x] Native open/save dialogs (`StorageProvider`) with an "All images" filter plus one per format; Save As appends the extension if the user omits it.
- [x] Multiple open documents as tabs with a close button; `*` marks unsaved changes in tab and title; File › Close (⌘/Ctrl+W); closing a modified image or quitting asks Save / Don't Save / Cancel. Saving a multi-layer image warns that the file will be flattened (layers stay in CinnabarSharp).
- [x] HEIC/HEIF photos open (read-only: Magick.NET has no HEVC encoder, so saving asks for another format, PNG by default). Images with an embedded color profile (Display P3, CMYK…) are converted to sRGB on open.
- [ ] AVIF (Magick.NET can read and write it) — optional.
- [ ] Tab thumbnails (Paint.NET image strip).
- [x] Zoom around the mouse with ⌘/Ctrl + wheel, trackpad pinch, menu/toolbar zoom keeps the view centre. Pan with scrollbars, Space + drag, middle mouse drag, or the Pan tool.
- [x] File › Open Recent (10 entries, stored in the user's local app-data folder) with Clear Recent; drag-and-drop image files onto the window; image paths passed on the command line are opened at startup.
- [x] Deleted the legacy `MyPaintApp/` (MAUI) folder.
- [ ] Dirty tracking is done in the UI for now (layer add/visibility). Move it to Core with the history in Phase 4.

**Validation**
- [ ] Open `sample1.png`, a JPEG, a file with uppercase extension and a file with non-ASCII path. (Automated in `FormatManagerTests` on all 3 OSes via CI.)
- [ ] Save As each format; reopen; pixels match. (Automated round-trip for all 6 formats.)
- [ ] Zoom 1%–3200% smooth; trackpad pinch on macOS/Windows precision touchpad.
- [ ] Drag & drop from Finder / Explorer / Nautilus.
- [ ] Native open/save dialogs look and behave right on each OS (GTK/portal dialog on Linux).
  - macOS (2026-09-27): opening a file from the command line works; the native menu updates Open Recent without crashing (a crash here was found and fixed: the macOS menu can only be mutated, not replaced).

## Phase 3 — Layers

- [x] Layers panel: thumbnails, visibility, blend mode/opacity summary, buttons for Add / Delete / Duplicate / Move Up / Move Down / Merge Down / Properties; double-click opens Properties. Layers menu adds Import From File and Flip Layer Horizontal/Vertical; Image › Flatten.
- [x] Layer Properties dialog: name, visible, blend mode, opacity (slider + number), with live preview; Cancel restores everything, including the "modified" flag.
- [x] Compositing in Core (`BlendOps`, pure C#, straight-alpha BGRA): Paint.NET's 14 blend modes plus Hard Light and Soft Light. Unit-tested per mode, and a checksum test over all modes/opacities that must match on every OS.
- [x] OpenRaster (`.ora`) save/load (`OraFormat`): names, visibility, opacity, blend modes (`svg:*` ops, `pdn:*` for Paint.NET-only modes), merged image and thumbnail. Saving to a layered format skips the flatten warning.
- [x] Fixed ported Pinta bugs: move-up off-by-one, delete selecting the wrong layer, duplicate/merge-down not implemented, flatten ignoring opacity/blend.
- [ ] Import From File: when the imported image is larger than the canvas, offer to expand the canvas (Paint.NET behavior); today it is cropped. Needs Canvas Size (Phase 7).
- [ ] Non-separable blend modes (Hue, Saturation, Color, Luminosity) — not in Paint.NET, optional.
- [ ] Icons for the layer buttons (text labels for now).

**Validation**
- [ ] Each blend mode renders identically across the 3 OSes (automated: `BlendOpsTests.Compositing_is_bit_identical_across_platforms` runs in CI on all 3).
- [ ] ORA saved on one OS opens identically on the others; also opens in GIMP/Krita/MyPaint (the open-source app) with layers.
- [ ] Layer Properties live preview feels responsive on a large (4000×3000) image.

## Phase 4 — History (undo/redo)

- [x] Every user edit goes through `DocumentActions`, which records a history step (`IHistoryItem`): add/delete/duplicate/move/merge/flip layer, flatten, import from file, visibility, layer properties. History panel lists the steps, dims undone ones, and clicking a step jumps to it. Each document has its own history.
- [x] Undo/Redo in the Edit menu and toolbar (⌘Z / ⌘⇧Z on macOS, Ctrl+Z / Ctrl+Y on Windows/Linux).
- [x] "Modified" state now comes from Core: the document is dirty when the history position differs from the last save (undoing back to the saved state clears the `*`). The UI no longer sets `IsDirty`.
- [x] Pixel data owned by the history is released when steps are discarded or the document closes.
- [ ] Memory bound: store only the changed region per step. Today pixel steps (merge down, flatten) keep whole layer copies; painting tools (Phase 6) need region-based steps.
- [ ] Limit on history size / memory (Paint.NET has none by default; decide with Phase 6).

**Validation**
- [ ] 200 undo/redo cycles on a 4000×3000 image stay responsive; memory stable.

## Phase 5 — Selection and clipboard

- [x] Selection model in Core: `SelectionMask` (one byte per pixel) with rectangle, ellipse, polygon and magic-wand shapes; combine modes Replace / Union / Exclude / Xor / Intersect; invert, offset, crop, outline.
- [x] Tool framework: tools live in Core (`CinnabarSharp.Core.Tools`) and get UI-independent input (`ToolPointer`: image position, button, modifiers). The canvas forwards pointer events to the selected tool.
- [x] Tools: Rectangle Select, Ellipse Select, Lasso Select, Magic Wand (tolerance, contiguous/global, Shift = global). Modifiers: ⌘/Ctrl union, Alt exclude, ⌘/Ctrl+Alt intersect, right button exclude; click without drag deselects. Tool options bar (selection mode, tolerance, global).
- [x] Animated marching ants; status bar shows the selection size. Edit › Select All / Deselect All / Invert Selection.
- [x] Move Selected Pixels (area left behind becomes transparent) and Move Selection (outline only), by dragging.
- [ ] Move handles (scale/rotate the selection or selected pixels, as in Paint.NET).
- [ ] Paste keeps the pixels under the pasted image while it is being moved (Paint.NET's floating paste); today Paste composites onto the layer, so moving it afterwards leaves a transparent hole. Paste Into New Layer doesn't have this issue.
- [x] Image › Crop to Selection (non-rectangular selections make outside pixels transparent); Edit › Erase Selection (Delete) and Fill Selection with the primary color (Backspace).
- [x] System clipboard (`IClipboardService` in Core, Avalonia implementation in Desktop): Cut, Copy, Copy Merged, Paste (then switches to Move Selected Pixels), Paste Into New Layer, Paste Into New Image; Paste with no image open creates one. All undoable.
- [ ] Replace the `object clipboard` parameters in `TextEngine.PerformCopy/Cut/Paste` with `IClipboardService` (with the Text tool, Phase 6).
- [ ] Antialiased selection edges (the mask supports 0–255 but shapes produce hard edges).

**Validation**
- [ ] Copy from CinnabarSharp → paste into Preview/Paint/GIMP and back, on each OS (transparency preserved where the other app supports it).
- [ ] Magic wand result identical across OSes (pure C#, same code path as the blend-mode checksum test).
- [ ] Marching ants animate smoothly on a large (4000×3000) magic-wand selection.

## Phase 6 — Painting tools

- [x] Palette: primary/secondary swatches open a color dialog (Avalonia ColorView: spectrum, RGB/HSV, hex, alpha, palette); Swap (X), Reset to black/white (D). Colors live in Core `ToolSettings` so tools and the Color Picker share them.
- [x] Rasterizer in Core (`CoverageMask`, pure C#): antialiased/aliased discs, thick lines with round caps, rectangles and ellipses (fill/outline), 1-px Bresenham lines. Checksum test keeps strokes identical on every OS.
- [x] `PaintSession`: previews recomputed from the original pixels, written into the layer in place, clipped to the selection; one history step per operation storing only the touched rectangle (closes Phase 4's "store only the changed region" for painting).
- [x] Tools: Paintbrush (width, antialiasing), Eraser, Pencil, Paint Bucket (tolerance, contiguous/global), Color Picker (layer or merged image), Line, Shapes (rectangle/ellipse; outline, fill, or outline + fill with the secondary color), Gradient (linear, radial, diamond, conical). Left button = primary color, right button = secondary.
- [x] Tool options bar per tool; Paint.NET letter shortcuts (B, E, P, F, G, K, O, S cycles select tools, M, H); crosshair cursor for painting tools.
- [x] Canvas redraws only the rectangle a stroke touched (a full redraw of a 4000×3000 two-layer image takes ~50 ms; painting itself is <1 ms per move).
- [ ] Rounded Rectangle; Bézier curves for Line/Curve (Paint.NET's editable handles).
- [ ] Clone Stamp, Recolor.
- [ ] Text tool (font, size, bold/italic, alignment; re-editable until committed); wire `TextEngine` clipboard to `IClipboardService`.
- [ ] Brush-size cursor outline; gradient transparency mode; brush hardness.
- [ ] Pressure support for pen tablets where Avalonia exposes it (`PointerPoint.Properties.Pressure`).
- [ ] Letter shortcuts should not fire while typing in the brush-width box.

**Validation**
- [ ] Brush strokes smooth at 60 fps on a 4000×3000 canvas (check by hand on each OS).
- [ ] Text renders with system fonts on each OS (fallback when font missing).
- [ ] Wacom/tablet pressure on at least Windows and macOS.

## Phase 7 — Image menu

- [x] Resize Image dialog: by percentage or absolute size, keep aspect ratio, resampling (Best Quality/Lanczos, Bicubic, Bilinear, Nearest Neighbor via Magick.NET).
- [x] Canvas Size dialog: by percentage or size, 3×3 anchor; new area of the bottom layer uses the secondary color, other layers stay transparent (Paint.NET behavior).
- [x] Rotate 90° clockwise / counter-clockwise / 180°, Flip Horizontal / Vertical for the whole image (pure C#); Crop to Selection (Phase 5). Layer flips were done in Phase 3. All undoable in one step, zoom level kept.
- [ ] Arbitrary layer rotation / zoom (Paint.NET's Layers › Rotate / Zoom).
- [ ] Transform the selection with rotate/flip instead of deselecting.

**Validation**
- [ ] Rotate/flip/canvas size identical across OSes (pure C#; covered by `ImageTransformsTests` in CI). Resize uses Magick.NET resampling: compare exported files across OSes.
- [x] Undo restores exactly (every image operation is in `HistoryTests.Actions`).

## Phase 8 — Adjustments

- [x] Adjustments in Core (`CinnabarSharp.Core.Adjustments`, pure C#, pinned by a cross-OS checksum test): Auto-Level, Black and White, Brightness / Contrast, Hue / Saturation, Invert Colors, Levels, Posterize, Sepia. Alpha is always preserved.
- [x] `AdjustmentSession` reuses `PaintSession`: applies inside the selection only, one history step with just the affected rectangle.
- [x] Dialog generated from the adjustment's parameters (slider + number, Reset); live preview computed on a background thread, stale previews cancelled; Cancel restores the layer and leaves no history.
- [x] Adjustments menu with Paint.NET shortcuts (⌘/Ctrl+Shift+L, G, U, I, P, E; ⌘/Ctrl+L for Levels). Parameterless adjustments apply immediately.
- [ ] Curves (needs a curve-editor control); Levels with histogram and per-channel mode.

**Validation**
- [ ] Preview stays responsive while dragging a slider on a 4000×3000 image (check by hand on each OS).
- [x] Results identical across OSes (checksum test in CI).

## Phase 9 — Effects

- [x] Effect framework in Core (`CinnabarSharp.Core.Effects`): `Effect` renders a rectangle from the layer's original pixels with numeric parameters; adjustments are now per-pixel effects. `EffectSession` (selection-aware, one history step with only the affected rectangle) replaces `AdjustmentSession`. Pure C#; random effects use a seeded hash so results are reproducible.
- [x] One dialog for adjustments and effects, generated from the parameters; background live preview; final result computed in the background after OK; Cancel leaves no trace.
- [x] Effects: Blurs (Gaussian, Motion, Radial, Zoom), Photo (Glow, Sharpen, Vignette), Noise (Add Noise, Median), Distort (Bulge, Frosted Glass, Pixelate, Twist), Stylize (Edge Detect, Emboss, Relief), Render (Clouds with primary/secondary colors, Mandelbrot). Blurs average premultiplied colors (no dark halos at transparent edges).
- [x] Effects menu with category submenus; Repeat Last Effect (⌘/Ctrl+F).
- [ ] More Paint.NET effects: Unfocus, Surface Blur, Reduce Noise, Tile Reflection, Polar Inversion, Dents, Ink/Pencil Sketch, Oil Painting, Outline, Red Eye Removal, Soft Portrait, Julia Fractal.
- [ ] Progress bar for long effects; non-numeric parameters (checkboxes, choices, angle picker, center point picker for Bulge/Twist/Zoom).

**Validation**
- [x] Each effect cancellable (tested); runs on a background thread.
- [x] Results identical across OSes: CI checksum over all effects passes on Windows, macOS and Linux (effects use `Math.Sin/Cos/Exp`; if a future .NET/OS math library ever differs in the last bit, switch that test to a per-pixel tolerance).
- [ ] Multi-core rendering (effects are single-threaded today).

## Phase 10 — Polish and packaging

- [x] App icon (`packaging/icon.svg` → `Assets/icon.ico`, `icon.png`, `packaging/CinnabarSharp.icns`); executable named `CinnabarSharp`, version 0.1.0.
- [x] `packaging/package.sh <rid> <version>`: self-contained builds — Windows zip, macOS `.app` (ad-hoc signed, image file types declared) zipped, Linux tarball with `.desktop` file and icon.
- [x] Release workflow (`.github/workflows/release.yml`): pushing a `v*` tag tests, packages win-x64, linux-x64, osx-arm64, osx-x64 and publishes a GitHub release (or a draft via "Run workflow").
- [x] Settings persistence (`SettingsStore`, JSON in the per-OS app-data folder): window size/position/maximized, selected tool, tool options, palette colors. Recent files are persisted separately.
- [ ] JPEG quality dialog on save (quality is fixed at 85 today).
- [x] Open files passed by the OS: command-line arguments (Windows/Linux) and macOS file activation events ("Open With", double-click, drop on Dock icon).
- [ ] Localization (`Translations`), check the dark theme (the app follows the OS theme; canvas/panel colors need a pass).
- [ ] Crash log + "unsaved work recovery".
- [x] Tool icons (vector line icons; selection tools dashed; unimplemented tools dimmed). Zoom tool (left click in, right click out, around the click).
- [ ] Icons for toolbar and layer-panel buttons (text labels today).
- [ ] Packaging, signed:
  - [ ] Windows: MSIX or Inno Setup installer, file associations, code signing.
  - [ ] macOS: universal (arm64+x64) `.app`, Developer ID signing + notarization, `.dmg`.
  - [ ] Linux: AppImage and/or Flatpak, MIME associations.

**Validation**
- [ ] Download the release packages on fresh machines of each OS; the app starts, opens and saves images.
- [ ] Clean install on fresh VM of each OS; double-click a `.png` opens CinnabarSharp.
- [ ] Uninstall removes app; settings survive upgrade.
