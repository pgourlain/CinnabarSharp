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

- [ ] Palette widget: primary/secondary color, swap, color picker dialog (HSV/RGB/hex, alpha).
- [ ] Tools: Pencil, Paintbrush (width, antialiasing), Eraser, Paint Bucket (tolerance, fill modes), Color Picker, Line/Curve, Rectangle, Ellipse, Rounded Rectangle, Gradient (linear, radial, diamond, conical), Clone Stamp, Recolor, Text (font, size, bold/italic, alignment; re-editable until committed).
- [ ] Tool options toolbar per tool.
- [ ] Pressure support for pen tablets where Avalonia exposes it (`PointerPoint.Properties.Pressure`).

**Validation**
- [ ] Brush strokes smooth at 60 fps on a 4000×3000 canvas.
- [ ] Text renders with system fonts on each OS (fallback when font missing).
- [ ] Wacom/tablet pressure on at least Windows and macOS.

## Phase 7 — Image menu

- [ ] Resize Image (by %, by size, keep aspect, resampling), Resize Canvas (anchor — `Anchor` model), Crop.
- [ ] Rotate 90°/180°, Flip horizontal/vertical (image and layer), arbitrary layer rotation.

**Validation**
- [ ] Resize/rotate outputs identical across OSes; undo restores exactly.

## Phase 8 — Adjustments

- [ ] Auto-Level, Black and White, Brightness/Contrast, Curves, Hue/Saturation, Invert Colors, Levels, Posterize, Sepia.
- [ ] Dialog with live preview on canvas; apply to selection only when a selection exists.

**Validation**
- [ ] Preview updates without UI freeze (work on background thread).
- [ ] Results byte-identical across OSes.

## Phase 9 — Effects

- [ ] Effect framework: `IEffect` with parameters → auto-generated dialog, live preview, progress + cancel.
- [ ] Blurs (Gaussian, Motion, Radial, Zoom, Unfocus), Sharpen, Noise (Add, Reduce, Median), Distort (Bulge, Twist, Pixelate, Tile Reflection, Frosted Glass), Artistic (Ink Sketch, Oil Painting, Pencil Sketch), Stylize (Emboss, Edge Detect, Outline, Relief), Render (Clouds, Julia, Mandelbrot), Photo (Glow, Red Eye, Soft Portrait, Vignette).
- [ ] Repeat last effect (`Ctrl+F`).

**Validation**
- [ ] Each effect cancellable; multi-core used; results identical across OSes.

## Phase 10 — Polish and packaging

- [ ] Settings persistence (window layout, last tool, JPEG quality, recent files) in the per-OS app-data folder.
- [ ] Localization (`Translations`), light/dark theme following the OS.
- [ ] Crash log + "unsaved work recovery".
- [ ] Packaging:
  - [ ] Windows: MSIX or Inno Setup installer, file associations.
  - [ ] macOS: `.app` bundle, universal (arm64+x64), code signing + notarization, `.dmg`.
  - [ ] Linux: AppImage and Flatpak, `.desktop` file, MIME associations.

**Validation**
- [ ] Clean install on fresh VM of each OS; double-click a `.png` opens CinnabarSharp.
- [ ] Uninstall removes app; settings survive upgrade.
