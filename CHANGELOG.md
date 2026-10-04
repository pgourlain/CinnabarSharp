# Changelog

All notable changes to CinnabarSharp. The format follows [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]

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
