# Changelog

All notable changes to CinnabarSharp. The format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Changed
- Node tool: the selected nodes are drawn bigger, filled in blue with a white edge, so they stand out from the others, and the node buttons (Corner, Smooth, Symmetric, Line, Curve, Join, Break, To path) are grayed until they can apply.
- The grid looks like draw.io's: thin light grey lines with a darker one every fifth line, on a plain white page (the soft checkerboard comes back when the grid is off).

## [0.9.2] - 2026-10-07

### Added
- Edit › Copy Style and Paste Style (Alt+Shift+C / Alt+Shift+V, with Ctrl/⌘) for drawings: take the look of an object (fill, stroke, widths, dashes, opacity; the font between texts) and give it to others, one step; a group passes it to everything inside. MCP: `svg_copy_style`.
- Shape tool (U) for drawings: over 120 ready-made shapes in eight categories (Basic, Arrows, Symbols, Flowchart, Dialog balloons, Nature, Weather, Objects) picked from a flyout in the options bar and drawn by dragging a box (Shift keeps the proportions, Alt draws from the center). They are ordinary paths, so they take fills, strokes, gradients and can be edited point by point.
- Node tool: pressing a point on the outline of any shape (rectangle, ellipse, circle, line, polygon) turns it into a path and edits that point, so every point of every object can be moved; the corner radius handles of a rectangle now sit just inside its corners.
- Double click on a text with the Select tool edits it again.
- Grid: View › Show Grid (Ctrl/⌘+') draws a grid over images and drawings, and View › Snap to Grid (Ctrl/⌘+;) makes the drawing tools place their points on it: shapes, pen, node, gradient and text tools on drawings, and the shape, line, gradient, crop and box-selection tools on images; the Select tool snaps the edges of what you move or resize. Grid size and the two toggles are in the options bar and remembered.
- File › Export As… (Ctrl/⌘+Alt+S): writes a flat picture (PNG, JPEG, WebP…) of an image, or a rendered picture of a drawing, without changing the document's file, name or unsaved state; it remembers the last format.

### Changed
- Behind a drawing the checkerboard is much softer and the grid lines are stronger.
- SVG drawings can be resized from Image › Resize (the page and its content scale together) and Image › Canvas Size (the page grows or shrinks around an anchor, the objects keep their size), both undoable; `resize_image` and `resize_canvas` work on drawings too.
- Save never flattens an image that has layers: when its file is a flat format (PNG, JPEG…), Save asks where to put a layered copy and suggests OpenRaster (.ora); after that, Save rewrites the .ora. Choosing a flat format in Save As still warns before flattening, and now points to Export As.

### Fixed
- The color dialog's tabs (spectrum, palette, components) showed no icons, so they could not be told apart.
- The welcome screen's thumbnail loader could stay blocked if a window closed while it was reading a file.

## [0.9.1] - 2026-10-07

### Added
- Update check: the app asks once for permission, then looks for a newer release on GitHub after it starts (in the background, at most once a day) and shows a banner with Download, Skip this version and Later; Help › Check for Updates (Check for Updates… in the application menu on macOS) does it on demand. One request, nothing about you, nothing installed.
- Status bar hints for the vector tools (the Select tool tells you to click the selection again for rotate handles); icons on the Objects panel buttons.

## [0.9.0] - 2026-10-07

The SVG editor.

### Added
- **SVG editor**: SVG files open as drawings with vector tools (select, node, pen, pencil, shapes, text, gradient, eyedropper), an Objects panel, a Properties panel, Object and Path menus (align, distribute, flip, rotate, union, difference, intersection, exclusion, division, stroke to path, combine, break apart, simplify, reverse), pictures inside drawings (import embedded or linked, clip, Edit Bitmap), and are saved back as SVG without disturbing the parts of the file you did not edit. Export to PNG, JPEG, WebP and other formats at any size. Big drawings (thousands of paths) are drawn on a background thread.
- MCP: `new_svg` and the `svg_` tools (tree, add shapes/paths/text/images, style, transform, align, path operations, clip, select); `save_image`, `export_image`, `render_preview`, `undo` and `redo` work on drawings. Scripts can call them too.
- SVG files (`.svg`, `.svgz`) can also be opened as images (File › Open as Image), and Layers › Import From File places an SVG, e.g. a logo, as a new layer: rendered sharp at its natural size, or smaller to fit the canvas.

## [0.8.1] - 2026-10-04

Accessibility, 200 % screens, faster opening and thumbnails, on top of the 0.8.0 redesign (0.8.0 itself was never released: this is the first release with the redesign).

### Added
- Thumbnails in the image tabs and in the recent files of the welcome screen.

### Changed
- Opening a photo no longer freezes the window: it is decoded in the background with an "Opening …" status (a 12 MP HEIC takes seconds).
- Small or high-scale screens (200 % on 1080p): the tool options scroll sideways, the side panels share the height, and long dialogs (Adjust Photo, Levels…) scroll with their buttons always visible.
- Keyboard: every control of the tool options bars can be reached with Tab, and the focused control has a visible accent ring.
- Secondary text is darker so it meets the 4.5:1 contrast ratio.

## [0.8.0] - 2026-10-04

The Cinnabar redesign: a new look, welcome screen and icon, plus faster zoom and a fix for pasting.

### Fixed
- Paste: moving the pasted image no longer leaves a transparent hole where it was pasted; the pixels it covered come back (Paint.NET's floating paste).

### Added
- Welcome screen when no image is open: Open / New / Paste buttons, recent files, a shortcut cheat sheet; "Show this screen when no image is open" turns it off.

### Changed
- Side panels use a 4-point grid: more air around Layers, History and Colors, smaller section titles.
- IBM Plex Sans and Mono (SIL Open Font License) ship with the app: the text looks the same on every OS.
- New app icon (a cinnabar square with a brush stroke).
- Dialogs show their title in the body and their buttons in a footer band.
- Prepare for TV: the resolution is a segmented control (Full HD, 4K, 8K); selected text in fields is readable; tooltips are dark.
- OK/Save buttons are filled with the accent color; the active tab has an accent underline and an unsaved image shows a dot instead of " *".
- The toolbar shows icons (New, Open, Save, Undo, Redo, zoom, Fit) instead of text.
- New "Cinnabar" colors (warm graphite neutrals, one cinnabar accent) in light and dark, from one theme file (`Themes/Cinnabar.axaml`); the canvas background is darker so images stand out.
- Zooming is faster on large images: a zoom step no longer recomposes all the layers (it did it twice per step).

## [0.7.0-rc4] - 2026-10-04

### Changed
- Comic page: the preview is much more responsive and uses far less memory. Panels work on reduced copies of the photos until Apply (a sharper copy is made in the background when a panel is zoomed or enlarged); a preview that took 775 ms with 9 photos of 12 MP takes 64 ms, and the comic mode holds 16 MB instead of 700 MB with 15 photos open. Apply still builds the page from the photos at full resolution.

## [0.7.0-rc3] - 2026-10-04

### Added
- Automation: `CinnabarSharp --run script.txt [--input files] [--var name=value]` runs a text script of the MCP tools (open, effects, resize, save…) without a window, on one or many files (`--keep-going` to continue after a failing file). See `docs/automation.md`.
- Release smoke test: the release workflow runs a script that calls every MCP tool and every effect and saves in every format on the packaged app of each OS, so a broken native build fails the release.
- Diagnostics: `log.txt` in the app-data folder records errors shown in dialogs, exceptions of background tasks and Avalonia warnings (`CINNABARSHARP_LOG=1` also prints them to stderr). Help › Open Log Folder (in the application menu on macOS) opens it.

## [0.7.0-rc2] - 2026-10-04

### Changed
- The release packages are compiled to native code (Native AOT): the first window appears in about 250 ms on macOS (460 ms with 0.7.0-rc's ReadyToRun, 740 ms before), and the packages are about half the size. A platform where the AOT build fails falls back to ReadyToRun.

## [0.7.0-rc] - 2026-10-04

### Changed
- Faster start: the release packages are now compiled ahead of time (ReadyToRun). On macOS the first window appears in about 460 ms instead of 740 ms; the packages are about 25 MB bigger.
- XAML bindings are compiled (checked at build time).

## [0.6.0] - 2026-10-04

### Added
- Comic page: two new layouts, "Large centre + surround" and "3 × 3 + overlapping centre" (the centre photo covers its neighbours by 10–25 %, set with the Overlap slider in the options bar).
- Comic page: drag the gutter between panels to resize them (minimum 5 % of the page); picking the layout again restores its proportions.
- Comic page: the dialog remembers the page format, gutter, border and background, and lists the most recently used layouts first.
- Comic page: the photo list of each panel shows a thumbnail of every photo.
- macOS: a `.dmg` (app, Applications shortcut, "Read Me First" with the Gatekeeper steps) is built next to the `.zip`.
- Arch Linux / Omarchy: the release workflow builds a `cinnabarsharp-bin` `.pkg.tar.zst` (install with `sudo pacman -U`); the PKGBUILD is in `packaging/arch/`.
- Release notes live in `packaging/release-notes.md` (one source for the GitHub release).

### Changed
- Comic page: the default page is 16:9 TV 4K and the default gutter is 20 (was A4 portrait and 40).
- Comic page: layout thumbnails in the dialog follow the proportions of the chosen page.
- Comic page: the "1 panel" layout is no longer offered in the dialog (still available through MCP).
- README and release notes: macOS 15+ no longer opens an unnotarized app with right-click › Open; they now describe System Settings › Privacy & Security › Open Anyway.

### Fixed
- The tabs of the last documents were hidden when more than about ten images were open: the tab strip now scrolls and keeps the active tab in view.
- Edit boxes and drop-downs in the tool options bars (very visible in Comic page) had different heights, and the number boxes lost their top and bottom borders; they now share one height.
- Auto-Enhance could darken a photo; it now only brightens (or leaves it as is).
- macOS: the application menu showed "About Avalonia" instead of "About CinnabarSharp".
