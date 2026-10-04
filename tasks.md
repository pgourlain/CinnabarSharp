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
- [x] Verify Magick.NET native libs load on all 3 (`Magick.NET-Q8-AnyCPU` ships osx-arm64, osx-x64, linux-x64, win-x64).

**Validation**
- [x] `dotnet test` green on the 3 OSes (locally and in CI).
- [x] `ImageDocumentEventTests` passes on Linux (sample file path resolution).

## Phase 1 — Avalonia shell

- [x] New project `CinnabarSharp.Desktop` (Avalonia 12 MVVM, CommunityToolkit.Mvvm), DI wired with `AddCinnabarSharpServices()` (`AppServices.Build()`).
- [x] Main window layout like Paint.NET: menu bar, toolbar, image tabs, tools strip (left), canvas (center), docked panels (Layers with visibility + add, History placeholder, Colors with swap), status bar (tool, cursor position, image size, zoom).
- [x] Native menu on macOS (`NativeMenu`; app menu with About, Avalonia adds Quit) and in-window menu on Windows/Linux, both built from one menu definition in `MainWindow.axaml.cs`. Items for later phases are shown disabled. Preferences item comes with settings (Phase 10).
- [x] Keyboard shortcuts: platform modifier (`Cmd` on macOS, `Ctrl` elsewhere) from `PlatformHotkeyConfiguration.CommandModifiers`; Paint.NET shortcuts (Redo is ⌘⇧Z on macOS, Ctrl+Y elsewhere). `X` swaps colors.
- [x] New Image dialog (width, height, white/transparent background) → new Core API `IWorkspaceService.NewDocument(size, background)`.
- [x] Canvas control renders the flattened document over a transparency checkerboard; nearest-neighbour when zoomed in; zoom in/out/best fit/actual size with Paint.NET presets; large images open fitted to the window.
- [x] Tool icons: vector path icons, identical on all OSes (done in Phase 10, `ToolIcons`).
- [x] Zoom keeps the view centered / zooms around the mouse (done in Phase 2 with Ctrl+wheel).

**Validation**
- [ ] App launches; window resizes; HiDPI crisp (Retina, Windows 150%, Linux scale 2).
- [x] File > New creates a document shown on canvas (headless test `New_white_image_is_shown_on_canvas` on all 3 OSes in CI).
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
- [x] Dirty tracking moved to Core with the history (Phase 4).

**Validation**
- [x] Open `sample1.png`, a JPEG, a file with uppercase extension and a file with non-ASCII path. (Automated in `FormatManagerTests` on all 3 OSes via CI.)
- [x] Save As each format; reopen; pixels match. (Automated round-trip for all 6 formats.)
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
- [x] Each blend mode renders identically across the 3 OSes (automated: `BlendOpsTests.Compositing_is_bit_identical_across_platforms` runs in CI on all 3).
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
- [x] Paste keeps the pixels under the pasted image while it is being moved (Paint.NET's floating paste): `FloatingPaste` keeps the layer's pixels from before the paste, so Move Selected Pixels puts them back; it follows later moves and undo/redo and ends with any other history step. Pasting into a partly off-canvas area clips the part outside, as before.
- [x] Image › Crop to Selection (non-rectangular selections make outside pixels transparent); Edit › Erase Selection (Delete) and Fill Selection with the primary color (Backspace).
- [x] System clipboard (`IClipboardService` in Core, Avalonia implementation in Desktop): Cut, Copy, Copy Merged, Paste (then switches to Move Selected Pixels), Paste Into New Layer, Paste Into New Image; Paste with no image open creates one. All undoable.
- [x] Replace the `object clipboard` parameters in `TextEngine.PerformCopy/Cut/Paste` with `IClipboardService` (done with the Text tool, Phase 6).
- [ ] Antialiased selection edges (the mask supports 0–255 but shapes produce hard edges).

**Validation**
- [ ] Copy from CinnabarSharp → paste into Preview/Paint/GIMP and back, on each OS (transparency preserved where the other app supports it).
- [x] Magic wand result identical across OSes (pure C#, exact-pixel `SelectionTests` pass on all 3 OSes in CI).
- [ ] Marching ants animate smoothly on a large (4000×3000) magic-wand selection.

## Phase 6 — Painting tools

- [x] Palette: primary/secondary swatches open a color dialog (Avalonia ColorView: spectrum, RGB/HSV, hex, alpha, palette); Swap (X), Reset to black/white (D). Colors live in Core `ToolSettings` so tools and the Color Picker share them.
- [x] Rasterizer in Core (`CoverageMask`, pure C#): antialiased/aliased discs, thick lines with round caps, rectangles and ellipses (fill/outline), 1-px Bresenham lines. Checksum test keeps strokes identical on every OS.
- [x] `PaintSession`: previews recomputed from the original pixels, written into the layer in place, clipped to the selection; one history step per operation storing only the touched rectangle (closes Phase 4's "store only the changed region" for painting).
- [x] Tools: Paintbrush (width, antialiasing), Eraser, Pencil, Paint Bucket (tolerance, contiguous/global), Color Picker (layer or merged image), Line, Shapes (rectangle/ellipse; outline, fill, or outline + fill with the secondary color), Gradient (linear, radial, diamond, conical). Left button = primary color, right button = secondary.
- [x] Tool options bar per tool; Paint.NET letter shortcuts (B, E, P, F, G, K, O, S cycles select tools, M, H); crosshair cursor for painting tools.
- [x] Canvas redraws only the rectangle a stroke touched (a full redraw of a 4000×3000 two-layer image takes ~50 ms; painting itself is <1 ms per move).
- [x] Rounded Rectangle (corner radius option); Line/Curve stays editable after drawing: drag its end points, or its two control points to bend it into a cubic Bézier; Enter, a click away or another tool finishes it. Still one history step.
- [x] Clone Stamp (⌘/Ctrl-click sets the source, offset kept between strokes), Recolor (replaces colors close to the secondary color with the primary, tolerance option).
- [x] Text tool: system fonts, size, bold/italic/underline, left/center/right alignment, antialiasing; caret, arrows/Home/End (Shift to select), click to place the caret, Select All/Cut/Copy/Paste through `IClipboardService`. Re-editable (text, font, color) until Escape, a click outside, another tool or any other edit; recorded as one history step. Font rendering is behind `ITextRasterizer` (Core) implemented with Avalonia in Desktop.
- [x] Brush-size outline under the pointer; gradient transparency mode; brush hardness (Paintbrush, Eraser, Clone Stamp, Recolor).
- [x] Pen pressure scales the brush width (`PointerPoint.Properties.Pressure`, pens only; mice always paint at full width).
- [x] Letter shortcuts don't fire while typing in a text box or with the Text tool.
- [x] Move the text being edited by dragging the handle at its bottom-right corner.

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
- [x] Curves: luminosity or RGB transfer map (edit red/green/blue together or separately), curve editor over the histogram (click to add a point, drag, right-click to remove), monotone cubic spline. Levels dialog with input and output histograms, per-channel mode and Auto. Both preview live and are pinned by a cross-OS checksum test.

**Validation**
- [ ] Preview stays responsive while dragging a slider on a 4000×3000 image (check by hand on each OS).
- [x] Results identical across OSes (checksum test in CI).

## Phase 9 — Effects

- [x] Effect framework in Core (`CinnabarSharp.Core.Effects`): `Effect` renders a rectangle from the layer's original pixels with numeric parameters; adjustments are now per-pixel effects. `EffectSession` (selection-aware, one history step with only the affected rectangle) replaces `AdjustmentSession`. Pure C#; random effects use a seeded hash so results are reproducible.
- [x] One dialog for adjustments and effects, generated from the parameters; background live preview; final result computed in the background after OK; Cancel leaves no trace.
- [x] Effects: Blurs (Gaussian, Motion, Radial, Zoom), Photo (Glow, Sharpen, Vignette), Noise (Add Noise, Median), Distort (Bulge, Frosted Glass, Pixelate, Twist), Stylize (Edge Detect, Emboss, Relief), Render (Clouds with primary/secondary colors, Mandelbrot). Blurs average premultiplied colors (no dark halos at transparent edges).
- [x] Effects menu with category submenus; Repeat Last Effect (⌘/Ctrl+F).
- [ ] More Paint.NET effects: Unfocus, Surface Blur, Reduce Noise, Tile Reflection, Polar Inversion, Dents, Ink/Pencil Sketch, Oil Painting, Outline, Red Eye Removal, Soft Portrait, Julia Fractal.
- [ ] Progress bar for long effects; non-numeric parameters (checkboxes, angle picker, center point picker for Bulge/Twist/Zoom). List parameters (choices) are done (Phase 11).

**Validation**
- [x] Each effect cancellable (tested); runs on a background thread.
- [x] Results identical across OSes: CI checksum over all effects passes on Windows, macOS and Linux (effects use `Math.Sin/Cos/Exp`; if a future .NET/OS math library ever differs in the last bit, switch that test to a per-pixel tolerance).
- [ ] Multi-core rendering (effects are single-threaded today).

## Phase 10 — Polish and packaging

- [x] App icon (`packaging/icon.svg` → `Assets/icon.ico`, `icon.png`, `packaging/CinnabarSharp.icns`); executable named `CinnabarSharp`, version 0.1.0.
- [x] `packaging/package.sh <rid> <version>`: self-contained builds — Windows zip, macOS `.app` (ad-hoc signed, image file types declared) zipped, Linux tarball with `.desktop` file and icon.
- [x] Release workflow (`.github/workflows/release.yml`): pushing a `v*` tag tests, packages win-x64, linux-x64, osx-arm64, osx-x64 and publishes a GitHub release (or a draft via "Run workflow").
- [x] Settings persistence (`SettingsStore`, JSON in the per-OS app-data folder): window size/position/maximized, selected tool, tool options, palette colors. Recent files are persisted separately.
- [x] JPEG quality dialog on save (done in Phase 11).
- [x] Open files passed by the OS: command-line arguments (Windows/Linux) and macOS file activation events ("Open With", double-click, drop on Dock icon).
- [ ] Localization (`Translations`), check the dark theme (the app follows the OS theme; canvas/panel colors need a pass).
- [ ] Crash log + "unsaved work recovery".
- [x] Tool icons (vector line icons; selection tools dashed; unimplemented tools dimmed). Zoom tool (left click in, right click out, around the click).
- [ ] Icons for toolbar and layer-panel buttons (text labels today).
- [ ] Review the About dialog: app icon, version and build date, short description, MIT license text, credits (Pinta, Magick.NET, Avalonia), links to the GitHub repository and issue tracker, "Copy system info" button for bug reports.
- [ ] Packaging, signed:
  - [ ] Windows: MSIX or Inno Setup installer, file associations, code signing.
  - [ ] macOS: universal (arm64+x64) `.app`, Developer ID signing + notarization, `.dmg`.
  - [ ] Linux: AppImage and/or Flatpak, MIME associations.

**Validation**
- [ ] Download the release packages on fresh machines of each OS; the app starts, opens and saves images.
- [ ] Clean install on fresh VM of each OS; double-click a `.png` opens CinnabarSharp.
- [ ] Uninstall removes app; settings survive upgrade.

## Phase 11 — Photo tools

**Photo enhancement (like the iPhone Photos app)**
- [x] Auto-Enhance (Photo menu, ⌥⌘E / Ctrl+Alt+E): analyzes the photo (median exposure, clipped highlights/shadows, tonal range, gray-world white balance, saturation) and applies balanced corrections; undoable as one step.
- [x] Adjust Photo dialog with the iPhone's 15 sliders (Exposure, Brilliance, Highlights, Shadows, Contrast, Brightness, Black Point, Saturation, Vibrance, Warmth, Tint, Sharpness, Definition, Noise Reduction, Vignette), live preview, reset per slider and for all, and an Auto button (Auto-Enhance's values as a starting point).
- [x] Filters (Vivid, Vivid Warm/Cool, Dramatic, Dramatic Warm/Cool, Mono, Silvertone, Noir) with an intensity slider and thumbnails of the current photo in each style.
- [x] Before/after comparison: "Hold to compare" in every effect/adjustment dialog shows the original while pressed.
- [x] Straighten: rotate by −45…45° with automatic zoom so no empty corners appear.
- [x] Straighten › Auto: detects the tilt of the dominant horizontal/vertical lines (horizon, buildings) and sets the angle.
- [x] All in Core as effects (`PhotoEffects.cs`, pure C#, cross-OS checksum test); list parameters (`EffectParameter.Choices`) and suggested values (`Effect.SuggestValues`) added to the effect framework.

**Crop and resize for 16:9 TVs (e.g. Samsung TV / The Frame art mode)**
- [x] Crop tool (C) with a locked aspect ratio (16:9, 9:16, 4:3, 3:2, 1:1, free): drag a frame, drag inside to move it, drag a corner to resize; rule-of-thirds lines, outside shaded; Enter or the Crop button crops (one history step), Escape removes the frame.
- [x] Photo › Prepare for TV: Full HD/2K, 4K UHD or 8K UHD, chosen in the options bar (no dialog); Crop to fill shows a frame of the TV's size (one image pixel per TV pixel: 1920 × 1080, 3840 × 2160 or 7680 × 4320), centered on the crop frame, the selection or the photo, reset to that size when the resolution changes; a frame larger than the photo extends beyond it and the view zooms out to show it; move or resize it, Enter applies, Escape cancels; Fit with borders and Stretch show a real preview at the screen's size (the photo scaled to fit without distortion, or stretched, with how much noted) in a frame centered on the photo (larger than the photo when the resolution is), Side by side previews both photos combined; Fit with borders (black, white or blurred background) or Stretch; Lanczos resampling; warning when the photo is enlarged. The result opens as a new image named `photo_4K`, suggested as JPEG.
- [x] JPEG quality asked on every JPEG save (remembered, default 90); JPEGs carry an sRGB profile. Photo › Prepare Folder for TV exports every photo of a folder to a "TV 4K" subfolder as `name_4K.jpg`.
- [x] Portrait photos: blurred-photo background, or two open photos side by side.

**Validation**
- [ ] Auto-Enhance results reviewed on a set of real photos (under/over-exposed, indoor, night, portrait).
- [ ] Exported 4K/8K JPEGs display correctly on a Samsung TV (USB and SmartThings upload).

## Phase 12 — UI and UX review

- [x] UI review and design system proposal ("Cinnabar": warm graphite neutrals, one cinnabar accent, IBM Plex Sans/Mono, 4-pt spacing, light and dark tokens, components, icon set, app icon refinement) — design canvas: https://claude.ai/artifact/8WQk6mytP9LxjX9iuZk7aR
- [ ] Mockups: main window light and dark, welcome screen, Adjust Photo and Prepare for TV are on the canvas; still to draw: New, Resize, Canvas Size, Layer Properties. **Waiting for review/iteration before implementing.**
- [ ] Implement the design system as Avalonia styles/resources (one theme file), replacing ad-hoc colors and sizes; icons for every toolbar/panel button.
  - [x] Colors: `Themes/Cinnabar.axaml` (Light/Dark `ThemeDictionaries`: chrome, panel, surface, border, workspace, text, accent, warning) and the matching Fluent palettes in `App.axaml`; main window workspace, warnings and borders use the tokens. `ViewportTests.Dark_theme_…` checks the dark variant.
  - [x] Typography: IBM Plex Sans (Regular, Italic, Bold) and Mono (Regular, Bold) embedded in `Assets/Fonts` with their OFL license, set on every window at 13 px (`Themes/Controls.axaml`); Plex Mono for numeric fields, zoom and shortcut keys. The static Medium/SemiBold files are separate font families, so SemiBold text renders in Bold: embed them as such if it is too heavy.
  - [x] Spacing and radii: the main window panels use the 4-pt grid (panels pad 12, titles 11 px with 8 below, buttons 4 apart, list rows 8/4, radius 6 on the layer and history lists); the welcome screen uses 16/24/32. Toolbars and the options bar kept their compact 4/8 paddings.
  - [x] Components, first part: the dialog's default button (OK, Save…) is the primary one (accent fill, hover/pressed/disabled), buttons have radius 4, the active tab has an accent underline and an unsaved dot replaces the " *".
  - [x] Components, second part: segmented control (`ListBox.segmented`, used for the TV resolution in the dialog and the options bar), tooltips (dark, radius 6), readable text selection. The slider already fills with the accent.
  - [ ] Components, rest: tab thumbnails, shortcut chip in tooltips (needs the shortcut of every command), ghost button.
  - [x] Icons: the toolbar uses `Controls/StrokeIcon` (24 px grid, 1.6 px stroke, drawn in the inherited foreground so Undo/Redo dim when disabled); tools and layer buttons already had them.
  - [x] New app icon from the design canvas (`packaging/icon.svg`, a diagonal-stroke variant for 16–32 px in `icon-small.svg`); `packaging/make-icons.sh` regenerates `icon.ico`, `icon.png`, `icon-256.png` and `CinnabarSharp.icns`.
  - [x] Dialogs: the title in the body and the buttons in a footer band (`TextBlock.dialogTitle`, `Border.dialogFooter`) for the twelve dialogs (New, Resize, Canvas Size, Layer Properties, Paste Beside, JPEG quality, Prepare for TV, Comic Page, Effect, Curves, Levels, Photo Filter). Color picker, About, agent connection and message boxes keep the plain layout.
- [x] ~~Dock-able / collapsible panels, compact and full toolbar modes, remember panel layout.~~ Not retained: Paint.NET has none (its panels float and hide), the three panels fit in 232 px, and no need was seen.
- [x] Accessibility, keyboard and contrast: every control of the options bars can be reached with Tab (they used to be `Focusable=False`), a 2 px accent focus ring (`FocusAdorner` in `Themes/Controls.axaml`), the secondary text color replaces the grey that failed 4.5:1, and `AccessibilityTests` check the contrast of every text/surface pair of both themes, that nothing is unreachable, and the Tab order of the toolbar. Screen-reader names were done before.
- [ ] Accessibility, scaling: a usable UI at 150–200 % (see below).
- [x] First-run experience: welcome screen with the buttons Open / New / Paste, recent files (name, folder, extension badge: no thumbnails yet), the shortcut cheat sheet and a setting to turn it off (`MainViewModel.ShowWelcomeScreen`, saved in `AppSettings.ShowWelcome`); a short hint when it is off.
- [ ] Usability pass with a few real users; list friction points and fix the top ones.

**Validation**
- [ ] Screenshots of every screen in light and dark themes on the 3 OSes reviewed against the mockups.

## Phase 13 — MCP server (drive CinnabarSharp from Claude and other agents)

- [x] `CinnabarSharp.Mcp` library exposing a Model Context Protocol server (official C# SDK `ModelContextProtocol` 2.2), built on `CinnabarSharp.Core` only (no UI). No separate executable: the app runs it with `CinnabarSharp --mcp`, so every package already contains it.
- [x] Two modes: **headless** (`--mcp`, stdio, no window) and **attached** (`--mcp --attach` relays stdio to the running app over a Unix domain socket; File › Allow AI Agents (MCP), off by default and remembered; edits run on the UI thread and show live, one history step each).
- [x] Tools: open/new/save/export (formats, JPEG quality)/close, list documents, image info with histogram, PNG preview, history, undo/redo; layers (add, import, delete, duplicate, merge down, flatten, move, select, properties, flip); selection (rectangle, ellipse, magic wand, all, none, invert, fill, erase); any adjustment/effect/photo tool by name with named parameters and list choices, suggested (Auto) values; resize, canvas size, crop (rectangle, ratio, selection), rotate, flip; prepare for TV and batch folder export. `EffectCatalog.Adjustments` added to Core.
- [x] Resources: `cinnabar://documents`, `cinnabar://effects` (parameter ranges, defaults, choices, encoded values for Curves/Levels), `cinnabar://documents/{id}/history`.
- [x] Safety: allowed folders (`--allow`, `CINNABARSHARP_MCP_ALLOW`, default the current directory; attached: Pictures, Documents, Desktop, Downloads), symbolic links resolved before the check; `overwrite` / `discardChanges` required for destructive operations; edits go through `DocumentActions` / `EffectSession`; socket readable by the user only.
- [x] Docs: [docs/mcp.md](docs/mcp.md) with `claude mcp add` and Claude Desktop setup, tools, safety, examples.
- [x] Tests: `CinnabarSharp.Mcp.Tests` start `CinnabarSharp --mcp` and drive it over stdio (end-to-end photo → TV JPEG, allowed folders and symlinks, overwrite/discard, effects parameters, undo/redo, layers, preview, batch), the `--attach` proxy against an in-process listener; a headless UI test drives the real window through the socket and checks pixels and undo.

**Validation**
- [ ] Claude Code edits a photo end-to-end through the MCP server (open → enhance → crop 16:9 → export 4K) on each OS.
- [ ] Windows: the packaged `CinnabarSharp.exe` (GUI subsystem) serves stdio when launched by Claude Code/Desktop; if not, add a small console launcher.
- [ ] Attached mode with the real app on each OS: toggle File › Allow AI Agents (native menu check mark on macOS), agent edits appear live, undo works.

## Phase 14 — Creative tools

- [x] Cartoon effect (Effects › Artistic): edge-preserving smoothing (bilateral filter), flat colors (hue in 15° steps, 4 saturation steps, N brightness tones), black outlines (Sobel on the smoothed brightness, threshold, width, strength); pure C#, deterministic, pinned by the cross-OS checksum.
- [x] Comic page (Photo › Comic Page…): a dialog picks the photos (open images, or files added), their order, the page (A4 portrait/landscape, square, 16:9 4K) and one of 10 layouts; then the page is edited on the canvas: click a panel to select it, drag to move the photo in it, zoom slider, change or empty a panel's photo, change the layout, gutter, border width and white/black page live; Enter applies (one history step, flattened), Escape closes the page. Core: `ComicPage` (layouts, geometry, compose), `ComicPageTool`.
- [x] MCP: `compose_comic_page` (framing from each image's selection or center); Cartoon through `apply_effect`.
- [x] Speech Bubble tool (U): press on the subject (tail tip), drag to where the bubble goes, type; the bubble grows with its text. Square, Rounded, Oval and Thought (cloud with a trail of circles); outline and text in the primary color, inside in the secondary color; optional numbered badge (1, 2, 3…, next number editable). Editable until the next edit like the Text tool: tail tip, 8 resize handles (the width then stays and the text wraps), drag the border to move it (the tip stays), click elsewhere to start the next bubble. Drawn on a "Bubbles" layer at the top by default ("Own layer" option). Core: `SpeechBubbleTool`, `BubbleShape` (signed distances, pinned by a checksum), `TextBlock` (text layout and word wrap shared with the Text tool).
- [x] MCP: `add_speech_bubble` (attached mode only: headless has no font rendering).

**Validation**
- [ ] Cartoon on real photos (portraits, landscapes, low light): outlines and flat colors look good at the default settings.
- [ ] Comic page with real photos on each OS: dragging a panel's photo feels responsive on a full A4 page; printed A4 at 300 dpi looks right.
- [ ] Speech bubbles on a real photo on each OS: place 5 numbered bubbles quickly, restyle one live, undo/redo, hide the Bubbles layer, save as ORA and PNG.

## Criteria for 1.0

0.8.0 is the last beta of the design work. 1.0.0 means ready for people who are not the developer; it waits for:
- [ ] The validation checklists of the phases passed on a real macOS, Linux (X11 and Wayland) and Windows 11 machine.
- [ ] Packages that install without a security warning: Developer ID signing and notarization on macOS, code signing on Windows, an installer with file associations on Windows, and an AppImage or Flatpak on Linux (Phase 10).
- [ ] Unsaved work recovery after a crash (Phase 10) and a history size setting.
- [ ] Accessibility: keyboard navigation everywhere, contrast ratios, a usable UI at 150–200 % (Phase 12).
- [ ] A usability pass with a few real users, with the top friction points fixed (Phase 12).
- [ ] Opening a large file no longer blocks the window (performance-tasks.md, P5).
