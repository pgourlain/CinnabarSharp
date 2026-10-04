# Changelog

All notable changes to CinnabarSharp. The format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

### Changed
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
