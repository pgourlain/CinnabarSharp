# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Goal

Build a cross-platform image editor with the scope and UX of [Paint.NET](https://www.getpaint.net/): layered documents with blend modes and opacity, selection tools, painting/shape/text tools, adjustments and effects, unlimited undo/redo history, and multiple open documents in tabs. When deciding how a feature should behave, use Paint.NET's behavior as the reference.

The app must run on **macOS, Linux and Windows**. The chosen stack is Avalonia UI 12 on .NET 10 (MAUI was dropped because it does not support Linux). The phased plan, with a validation checklist per OS for each phase, is in [tasks.md](tasks.md).

## Constraint: CinnabarSharp.Core is non-visual

`CinnabarSharp.Core` contains only non-visual logic: the document model, layers, pixel buffers, compositing, history, selection geometry, tool and effect algorithms, file codecs and events. It must build and pass its tests on macOS, Linux and Windows without any UI framework installed.

- **Forbidden in Core:** any reference to a UI or windowing stack (Avalonia, MAUI, WPF, WinForms, GTK/Gdk, Cairo, AppKit/UIKit) or to a rendering toolkit (SkiaSharp, `System.Drawing.Common`). Also forbidden: `System.Drawing` types (use Core's own `Rectangle`, `ImageSize`, `ColorBgra`, etc.), UI threading/dispatchers, dialogs, windows, controls, input events, clipboard, fonts rendering and platform APIs (`#if WINDOWS`/`MACOS`, P/Invoke).
- **Allowed:** BCL, `Microsoft.Extensions.*` abstractions (DI, logging), Magick.NET (codecs and image processing only).
- Anything visual or platform-specific lives in the UI project (`CinnabarSharp.Desktop`) behind an interface defined in Core, and is registered through DI.
- Core talks to the UI only through `IDocumentEventsService` events and interfaces; it never calls the UI directly.
- When porting Pinta code into Core, replace Cairo/GTK types with Core types instead of adding a dependency.

## Overview

The code is ported from [Pinta](https://github.com/PintaProject/Pinta), itself a Paint.NET clone written for GTK/Cairo. This port targets Avalonia and uses Magick.NET (`IMagickImage<byte>`) in place of Cairo/GdkPixbuf. Pinta's source is the first place to look for how to implement a Paint.NET feature. Many files still carry Pinta's license headers, its type names (`GdkPixbufFormat`, `ImageDocumentWorkspace`, `UserLayer`) and commented-out `PintaCore.*` code that is waiting to be ported.

## Projects

- `CinnabarSharp.Core/` — net10.0 class library, all document/layer/history logic. Non-visual (see constraint above).
- `CinnabarSharp.Core.Tests/` — xUnit tests for Core, including `CoreArchitectureTests` which enforces the non-visual constraint.
- `CinnabarSharp.Mcp/` — MCP server library (official C# SDK `ModelContextProtocol`), Core only, no UI. Hosted by the desktop executable: `CinnabarSharp --mcp` (headless, stdio) or `--mcp --attach` (stdio relayed to the running app's socket).
- `CinnabarSharp.Desktop/` — Avalonia 12 desktop app (MVVM with CommunityToolkit.Mvvm).
- `CinnabarSharp.Mcp.Tests/` — xunit v3; starts the real `CinnabarSharp.dll --mcp` (copied to its output through the Desktop project reference) and drives it with an MCP client over stdio.
- `CinnabarSharp.Desktop.Tests/` — headless UI tests (Avalonia.Headless + Skia, **xunit v3**, unlike Core.Tests which is xunit v2). They render the real `MainWindow`, assert on pixels of the captured frame, and save screenshots to `CinnabarSharp.Desktop.Tests/bin/<Config>/net10.0/screenshots/`.

Root solution: `CinnabarSharp.slnx`. Repository: https://github.com/pgourlain/CinnabarSharp (remote `origin`). CI: `.github/workflows/ci.yml` builds and tests on Windows, macOS and Linux and uploads the UI screenshots per OS as artifacts (`screenshots-<os>`).

## Commands

```bash
dotnet build CinnabarSharp.slnx
dotnet test CinnabarSharp.slnx

# Single test (xunit v2 project)
dotnet test CinnabarSharp.Core.Tests --filter "FullyQualifiedName~ImageDocumentEventTests.Test_importFile_fireevents"

# Single UI test (xunit v3 project: filter syntax is the same through dotnet test)
dotnet test CinnabarSharp.Desktop.Tests --filter "FullyQualifiedName~MainWindowTests.New_white_image_is_shown_on_canvas"

# Run the app
dotnet run --project CinnabarSharp.Desktop

# MCP server over stdio (build first; `dotnet run` would write build output to stdout)
dotnet CinnabarSharp.Desktop/bin/Debug/net10.0/CinnabarSharp.dll --mcp --allow ~/Pictures

# Self-contained package (win-x64 | linux-x64 | osx-arm64 | osx-x64) into artifacts/
packaging/package.sh osx-arm64 0.1.0
```

The desktop assembly/executable is named `CinnabarSharp` (not `CinnabarSharp.Desktop`). Releases: push a `v*` tag; `.github/workflows/release.yml` packages every OS and creates the GitHub release. Icons are generated from `packaging/icon.svg` (see Phase 10 in tasks.md).

To check UI changes visually without a display, run `CinnabarSharp.Desktop.Tests` and look at the screenshots it writes.

Magick.NET 14 uses `uint` for image width/height; Core's own types (`ImageSize`, `RectangleI`) use `int`. Cast at the Magick.NET boundary.

## Architecture

**DI entry point:** `ServiceExtensions.AddCinnabarSharpServices()` (`CinnabarSharp.Core/Extensions/ServiceExtensions.cs`) registers everything. Used by the UI app's startup and by `BaseTests.CinnabarSharpService()`. Singletons: `IDocumentEventsService`, `IDocumentsHistoryService`, `IWorkspaceService` (`WorkspaceManager`), `IFormatManager`, and one `IImageImporter` per file format. Transient: `ImageDocument`.

**Document model:** `WorkspaceManager` holds open `ImageDocument`s and the active index; it resolves new documents from the `IServiceProvider`. Each `ImageDocument` owns:
- `Layers` (`ImageDocumentLayers`) — user layers plus internal tool and selection layers. Layer pixels live in Magick.NET images (`Layer.Surface`), but **compositing is ours**: `GetFlattenedBgra()` runs `BlendOps.Composite` over straight-alpha BGRA buffers (`Utility.ToBgra/FromBgra`), applying opacity and blend mode. Don't use Magick's merge/composite for layer blending: results must be bit-identical across OSes, and `BlendOpsTests` pins a checksum. The canvas draws `GetFlattenedBgra()` directly.
- `Workspace` (`ImageDocumentWorkspace`) — view size/zoom/canvas state, and owns the `ImageDocumentHistory`.
- `Actions` (`DocumentActions`) — **the only way the UI edits a document.** Each action performs the change through `ImageDocumentLayers` (low-level, no history) and then pushes an `IHistoryItem` that can undo/redo it. Calling `ImageDocumentLayers` mutators directly from the UI makes the edit non-undoable and leaves `IsDirty` wrong.

**Selection and tools:** `ImageDocument.Selection` is a `SelectionMask` (image-sized, 0/255 per pixel) or null for "nothing selected" (pixel actions then use the whole layer). `SetSelection` raises `SelectionChanged` but doesn't record history; tools call `Actions.RecordSelectionChange(before, name)` once at pointer-up. Tools (`CinnabarSharp.Core.Tools`) implement `ITool` with `ToolPointer` input in image coordinates — no Avalonia types — so they are unit-tested in Core. `ToolModifiers.Command` means ⌘ on macOS and Ctrl elsewhere; the canvas maps both Meta and Control to it. Tools that preview (Move Selected Pixels) swap `Layer.Surface` during the drag and push one history item at the end. Rectangle/Ellipse Select derive from `BoxSelectionTool`: the last drawn box keeps 8 resize handles while `document.Selection` is still the mask it produced (reference equality), so any other selection change drops them; tools ask for hover cursors through `IOverlayTool.CursorAt`. Escape that the tool doesn't use deselects (`MainViewModel.ToolKeyDown`). The clipboard is `IClipboardService` (Core) implemented by `AvaloniaClipboardService`; tests use `FakeClipboardService`.

**Editable tools:** Line/Curve and Text stay editable after drawing (`IEditingTool`): their `PaintSession` keeps its base pixels and `Commit()` *updates* its own history step while `PaintSession.IsLive` (the session's step is still the current one). Any other history change ends editing automatically; `MainViewModel` also calls `Finish` when the tool or active document changes and `Refresh` when colors/options change. Keys go to tools through `IKeyboardTool` (tunnel handlers in `MainWindow`); single-letter shortcuts are disabled while `IsTyping`, and must not mark the key handled (on some platforms a handled KeyDown produces no text input). Tools describe handles/caret/frames as a `ToolOverlay`, drawn by `CanvasView`. Text rendering is `ITextRasterizer` (Core interface, `AvaloniaTextRasterizer` in Desktop, `BlockTextRasterizer` in Core tests).

**Speech bubbles:** `SpeechBubbleTool` is an editable text tool like Text (`ITextEditingTool`, which the view model's clipboard/Select All commands target). Geometry is `BubbleShape`, a signed distance (body ∪ tail); the outline is the shape filled with the primary color, then filled again inset by the outline width with the secondary color (`CoverageMask.FillDistance`). Text layout, word wrap, caret and rasterizing are `TextBlock`, shared with `TextTool`. By default a bubble first selects (or adds, one history step) the "Bubbles" layer at the top. Changing an option while a bubble is live restyles it; finish it (Escape) first to style the next one differently. MCP `add_speech_bubble` uses `SpeechBubbleTool.Place` and needs `McpContext.TextRasterizer`, which only attached mode has.

**Painting:** tools create a `PaintSession` (keeps the layer's original pixels, recomputes rectangles from them, writes into `Layer.Surface` **in place** with `Utility.WriteRegion`, clips to the selection, and pushes one `PixelRegionHistoryItem` with only the touched rectangle). Coverage shapes come from `CoverageMask` (pure C#, no Skia/Magick drawing, pinned by a checksum test). Paint operations call `Workspace.Invalidate(rect)` with an image-space rectangle; the view model forwards it as `RegionInvalidated` and `CanvasView.UpdateRegion` re-composites only that rectangle (`GetFlattenedBgra(region)`). `Invalidate()` without a rectangle means a full redraw. Colors, brush width and other options live in `ToolSettings` (Core); the view model wraps them for binding.

**Photo tools:** the Photo menu lists `EffectCatalog.PhotoTools` (Auto-Enhance, Adjust Photo, Photo Filter, Straighten); the Effects menu lists `EffectCatalog.Effects`; `EffectCatalog.All` covers both for the tests. Effects with `HasCustomDialog` (Curves, Levels, Photo Filter) get their own dialog view model deriving from `PreviewDialogViewModel`. `CinnabarSharp.Core.Photo.TvExport` composes 16:9 TV images (Magick.NET Lanczos resampling, so not checksum-pinned) and exports folders. `ComicPage` (same folder) lays out and composes comic pages; `TvExport.Resize` is the shared Lanczos resize. Photo › Comic Page is a canvas mode like Prepare for TV: `MainViewModel.Comic` (`ComicPageViewModel`, also the start dialog's view model) owns a `ComicPageTool` that gets the mouse instead of the selected tool (`ActiveTool`); the page is shown as a preview picture (`RefreshComicPreviewAsync` merges changes made while one is computing), tools and tabs are disabled, Enter/Escape apply/cancel, and Apply writes the full page with `DocumentActions.ReplaceLayerPixels`.

**Adjustments and effects:** both derive from `Effect` (`CinnabarSharp.Core.Effects`); `ColorAdjustment` is the per-pixel case. `Effect.Render(context, region, destination, values, ct)` must read only `EffectContext.Source` (the layer as it was) so it can run on a background thread, must write every pixel of the region, and must be deterministic (use `Sampling.Hash/Noise` for randomness, never `Random`). Add new effects to `EffectCatalog.All`: the menu, the determinism/cancellation tests and the checksum test pick them up (update the checksum on purpose). `EffectSession` handles selection clipping and history; the UI dialog is generated from `Parameters`.

**History:** items are created *after* the change (state "done"). The first item is always a `BaseHistoryItem` ("New Image"/"Open Image") pushed by `WorkspaceManager`. `IsDirty` is derived: `Pointer != cleanPointer`; `FormatManager.Save` calls `History.SetClean()`. Items own surfaces that are not in the document in their current state and dispose them in `OnDispose` (e.g. `AddLayerHistoryItem` disposes the layer if undone, `DeleteLayerHistoryItem` if done), so low-level layer methods must not dispose surfaces they replace or remove. When adding an action, add it to `HistoryTests.Actions`: that test checks undo restores order, properties, pixels and current layer exactly.
- `Selection` (`ImageDocumentSelection`).

**MCP server (`CinnabarSharp.Mcp`, user docs in `docs/mcp.md`):** `ImageTools` holds every tool; each tool body runs through `McpContext.Run`, which calls `IMcpDispatcher` (serialized in headless mode, `Dispatcher.UIThread` in attached mode, because the view model reacts to Core events on the UI thread) and turns Core exceptions into `McpException` (the SDK hides other exception messages from the agent). Edits must go through `DocumentActions`/`EffectSession` like the UI. Paths go through `FileAccessPolicy.Resolve` (allowed folders, symlinks resolved); overwriting a file needs `overwrite`, closing unsaved work needs `discardChanges`. Documents have session ids from `McpContext.IdOf`. `Program.Main` branches on `--mcp` **before** Avalonia starts: stdout is the protocol, so never write to it there (logs go to stderr). Attached mode: `AgentConnection` (Desktop) starts `AttachListener` on a Unix domain socket (`<LocalAppData>/CinnabarSharp/mcp.sock`) when File › Allow AI Agents is on (`AppSettings.AllowAgents`); `AttachProxy` relays stdio to it. New Core features an agent should use need a tool in `ImageTools` and a line in `docs/mcp.md`; new effects appear automatically (`EffectCatalogInfo` reads `EffectCatalog`).

**Automation and diagnostics:** `CinnabarSharp --run script.txt` (`CinnabarSharp.Mcp/Scripting`, user docs in `docs/automation.md`) is a script of MCP tool calls: `ScriptParser` (text, variables), `ScriptArguments` (value → JSON from the tool's input schema) and `ScriptRunner`, which starts the same server as `--mcp` inside the process over two in-memory pipes and calls it through an MCP client. So it has no command list of its own: a new tool is scriptable at once. `Program.Main` branches on `--run` before Avalonia starts. `packaging/smoke-test.txt` calls every tool and every effect; `packaging/smoke-test.sh` runs it on the packaged app of each OS in the release workflow, and `Mcp.Tests` fails when a tool or effect is missing from it — add new ones there. `AppLog` (Desktop) writes `log.txt` in the app-data folder (errors shown in dialogs, unobserved task exceptions, Avalonia warnings; also stderr with `CINNABARSHARP_LOG=1`); Help › Open Log Folder (application menu on macOS).

**Events replace Pinta's C# events:** Instead of `EventHandler`s (left commented out in several classes), state changes call `IDocumentEventsService.PushEvent(new DocumentEventItem(doc, DocumentEventEnum.X))` (or `LayerEventItem`/`CanvasEventItem`). Consumers subscribe to the `IObservable<EventItem<DocumentEventEnum>>` stream; `MainViewModel` subscribes and turns events into view-model updates (tabs, layers list, `RenderVersion` to redraw the canvas). `DocumentEventsService` is a hand-rolled observable (no Rx dependency in Core; tests use `System.Reactive.Linq`). Tests assert the exact ordered sequence of events, so adding/reordering `PushEvent` calls breaks `ImageDocumentEventTests`.

**Import/export:** formats derive from `ImageFormat` (`SupportsLayers`, `MatchesContent` for sniffing). Single-layer formats are `MagickImageFormat` (replaces Pinta's `GdkPixbufFormat`); `JpegFormat` subclasses it to flatten alpha onto white. `OraFormat` is the layered format (zip + stack.xml). All are registered in `AddCinnabarSharpServices`. The UI goes through `IFormatManager.Open/Save`, which picks the format (extension first, then content sniffing), reuses an already-open document, and updates `File`/`FileType`/`IsDirty` after saving. Exporters write the flattened image; layered formats (ORA) come in Phase 3. `WorkspaceManager.CloseDocument` disposes layer surfaces and raises `DocumentClosed`.

**UI (`CinnabarSharp.Desktop`):**
- `AppServices.Build()` is the composition root (Core services + `MainViewModel` singleton); `App` and the UI tests both use it.
- Long operations (effects, TV export) go through `MainViewModel.RunBusyAsync`: status-bar progress, wait cursor, editing areas disabled. The macOS native menu stays usable, so check the history didn't change before applying a result computed in the background (see `ApplyAsync`). This only covers operations that run with the effect/TV dialog already closed (or without one, e.g. Auto-Enhance). While an effect's dialog is open (`PreviewDialogViewModel`, backing Effect/Curves/Levels/PhotoFilter windows), `Computing` is the same idea at dialog scope: true while a live preview or the final `CommitAsync` is running, bound in each window to a small "Computing…" progress row and to disabling OK, so slow effects (Zoom Blur, Gaussian Blur…) give feedback while a slider is dragged, not just after OK.
- Prepare for TV is a mode, not a dialog: `MainViewModel.Tv` (a `PrepareForTvViewModel`) drives the options bar, and the Crop tool shows the 16:9 frame (`ForcedRatio`, `CanDrawNewFrame = false`, `Propose`) at the resolution's size in image pixels (`ProposeTvFrame`); it may be larger than the image (`CropTool.KeepOnImage`), and `TvExport` keeps only its part on the photo. Fit with borders/Stretch/Side by side have no frame to place, so instead `RefreshTvPreviewAsync` renders a real (downscaled) TV preview in the background via `TvExport.Compose`/`SideBySide`'s `size` parameter and shows it as `ToolOverlay.Picture`, cached as a bitmap in `CanvasView` keyed by reference equality. Enter/Escape apply/cancel; changing tool or document leaves it. The dialog is only used for Prepare Folder for TV.
- `MainViewModel` owns commands and UI state. It never pushes pixels itself: it calls Core (`IWorkspaceService`, `ImageDocument.Layers`, `Workspace.Scale`) and reacts to the Core events that follow.
- `Controls/CanvasView` draws `ImageDocument.GetFlattenedImage()` as a `WriteableBitmap` over a checkerboard, sized to `Workspace.ViewSize` (1 image pixel = 1 logical pixel at 100%). It is rebuilt whenever `RenderVersion` changes.
- Menus are defined once as `MenuSpec` records in `MainWindow.axaml.cs` and turned into a macOS `NativeMenu` or an in-window `Menu` on Windows/Linux. Shortcuts use `PlatformHotkeyConfiguration.CommandModifiers` (⌘ on macOS, Ctrl elsewhere); never hard-code `KeyModifiers.Control`. Features from later phases are bound to the disabled `NotYetImplemented` command.
- Dialogs go through `IDialogService` (file pickers, save-changes prompt, confirmations, errors); zoom that must keep a point in place goes through `IViewportService` (implemented by `MainWindow`, which owns the ScrollViewer). Tests replace dialogs with `FakeDialogService`; `TestHarness` builds the real window with a temp `RecentFilesStore` so tests never touch the user's app data.
- **macOS native menu:** build it once. Replacing the window's `NativeMenu` after it is shown crashes Avalonia.Native ("The menu being updated does not match"); mutate `NativeMenu.Items` instead (see `RefreshRecentMenu`). Headless tests cannot catch this; run the real app on macOS after touching menus.
- **Zoom anchoring:** `MainWindow.ZoomTo` calls `UpdateLayout()` before reading positions and after setting the offset; without it, consecutive zooms (fast wheel) read stale positions and drift.

**Test pixel checks:** headless Skia captures frames as RGBA, not BGRA; `TestHarness.PixelAt` handles both. Always test with colors that differ per channel (not white/grey), or channel swaps go unnoticed.

**Avalonia 12 notes:** `TopLevel.PlatformSettings` isn't accessible; use `Application.Current.PlatformSettings`. `Bitmap.Save(path)` is obsolete; pass `PngBitmapEncoderOptions.Default`. Magick.NET and Core both define `PointD`; alias `CinnabarSharp.Core.Models.PointD` in files that import `ImageMagick`.

## Conventions

- Nullable reference types and implicit usings enabled.
- `Models/Tanslations/` folder name is misspelled — keep as is (referenced in csproj). `Translations.GetString` is used for user-visible strings.
- Releases are Native AOT (`packaging/package.sh`, performance-tasks.md P6): no reflection-based `System.Text.Json` (add the type to `DesktopJson` or `McpJson`), no `new Binding(path)` from code, no `Assembly.Location`. A new MCP tool parameter or result type goes in `McpJson` (`AotJsonTests` fails otherwise). The build reports trim/AOT warnings; keep them at zero. MCP tests can run against a native build with `CINNABARSHARP_TEST_EXE=<path to the native app>`.
- Test sample images live in `CinnabarSharp.Core.Tests/Data/SampleFiles/` and must be marked `CopyToOutputDirectory` in the test csproj; access via `BaseTests` helpers.
