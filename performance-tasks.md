# CinnabarSharp — Performance and memory

Goal: stay fast and within memory on large photos (24 MP and more), many layers and long editing sessions, on every OS. Each phase can ship on its own; measure before and after (Phase P0).

**Targets** (24 MP photo, 4000 × 6000, on a mid-range laptop):
- Memory: 10 layers + 100 undo steps under 2 GB of RAM, and never unbounded.
- Brush and selection tools: 60 fps while dragging.
- Effect dialogs: preview updates within ~150 ms of a slider change; final apply of a simple effect (Gaussian Blur 10, Auto-Enhance) under 1 s.
- Undo/redo of any step: under 200 ms, even when the step was moved to disk.
- Open and save: no more than twice the time Magick.NET alone needs to decode/encode the file.

## Where memory and time go today

| Area | What happens | Cost on a 24 MP photo |
|---|---|---|
| Layer pixels | Each layer is a Magick.NET image (`Layer.Surface`, Q8: 4 bytes per pixel, unmanaged). | ~96 MB per layer |
| History, pixel steps | `PixelRegionHistoryItem` (brush, effects, Cartoon…) keeps **both** the before and the after pixels of the touched rectangle. A whole-layer effect stores two full copies. | ~192 MB per full-layer effect |
| History, layer steps | `SwapSurfaceHistoryItem` (fill, erase, crop, paste, merge, flatten) and `ResizeImageHistoryItem` (resize, rotate, canvas size) keep a full copy of every affected layer. | ~96 MB per layer per step |
| History limit | `ImageDocumentHistory` never drops old steps: memory grows with every edit until the document is closed. | unbounded |
| Full-image copies | `Surface.ToBgra()`, `GetFlattenedBgra()`, `Utility.FromBgra()` allocate a new image-sized array (Large Object Heap) each time: `PaintSession` base, every `DocumentActions` pixel edit, crop, MCP preview… | ~96 MB each, GC pressure |
| Canvas | `CanvasView.RebuildBitmap()` flattens all layers and copies into a new `WriteableBitmap` on many changes (zoom, layer property, undo…), not only the changed region. | 2 × ~96 MB per rebuild |
| Layer thumbnails | `LayerViewModel.CreateThumbnail()` clones the full layer and resizes it, for every layer, on every history change (`RefreshThumbnails`). | a full clone per layer per step |
| Effects | `Effect.ForEachPixel` runs on one core; dialog previews (`PreviewDialogViewModel.RequestPreview`) compute at full resolution on every slider change. | seconds per preview on large photos |
| Compositing | `BlendOps.Composite` blends byte by byte in scalar code. | slow flatten with many layers |

## P0 — Measure first

- [ ] `CinnabarSharp.Benchmarks` project (BenchmarkDotNet, not run in CI): open/save 24 MP JPEG and PNG, flatten 10 layers, `GetFlattenedBgra(region)`, a 500-point brush stroke, each effect at defaults, undo/redo of each history item type, TV and comic page compose. Track time **and** allocated bytes (`[MemoryDiagnoser]`).
- [ ] Sample large images for benchmarks, not in git: a script downloads or generates them (12 MP, 24 MP, 50 MP; photo, screenshot, transparent PNG).
- [ ] A hidden "Performance" panel or `--diagnostics` flag: working set, managed heap, Magick.NET unmanaged memory (`ResourceLimits`), history size in memory / on disk, last effect/compose time. Also exposed to the MCP server as a resource for agents testing performance.
- [ ] Record the baseline numbers in this file before starting P1.

## P1 — History memory (the biggest win)

**Opinion on keeping history on disk: yes, it's a good idea and the right long-term design** — Paint.NET itself stores its undo history in temporary files — but as the *last* step. On its own it would move today's waste to disk and make undo slow. First store less, then compress, then spill what is still too big to disk.

1. **Store one copy, not two.**
   - [x] `PixelRegionHistoryItem`: keep only the pixels that are *not* in the layer right now (the before when done, the after when undone) and swap them on undo/redo. Halves every brush and effect step.
   - [x] `SwapSurfaceHistoryItem` / `ResizeImageHistoryItem`: same principle already, but for pixel-only edits inside a selection (fill, erase), record a `PixelRegionHistoryItem` on the selection bounds instead of a whole-layer copy. (`ReplaceLayerPixels`, e.g. Comic Page, is a genuine whole-image replace and still uses `SwapSurfaceHistoryItem`.)
   - [x] Keep `HistoryTests.Actions` green: every action still undoes and redoes exactly.
2. **Memory budget with a step limit.**
   - [x] Each `IHistoryItem` reports `long Bytes`. `ImageDocumentHistory` keeps a budget (default 25 % of `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`, constructor-overridable) and a maximum step count (default 100, like Paint.NET's practical limit). *Simplification vs. the note above: the budget is per-document, not yet pooled across every open document — doing that safely needs a shared counter service, left for a follow-up.*
   - [x] When over budget, drop the oldest steps; the first step ("Open Image") becomes the oldest remaining one. Fires `DocumentEventEnum.HistoryTrimmed` once per document (UI status-bar message "Oldest steps were removed to save memory" still needs wiring in `CinnabarSharp.Desktop`). Moving old steps to disk first (P1.4) is not implemented yet — trimming drops them outright.
3. **Compress what stays in memory.**
   - [x] Store the *difference* (XOR of before and after) of pixel steps, then compress it with `System.IO.Compression` (Deflate fastest level, BCL, no new dependency): untouched pixels become zeros and compress to almost nothing. Compress on a background thread after the step is pushed; decompress on undo. (`CinnabarSharp.Core/Models/CompressedDiff.cs`, used by `PixelRegionHistoryItem`.) The XOR trick also means undo and redo are literally the same operation (XOR the layer's current pixels with the diff), so there's no separate "swap in the other copy" bookkeeping any more.
   - [ ] Measure the ratio on real edits (brush stroke, adjustment, crop) in P0 benchmarks before deciding the default — no P0 benchmark project yet, so this still needs real numbers; `CompressedDiffTests` only pins the synthetic mostly-zero case.
4. **Spill to disk.**
   - [x] `IHistoryStorage`/`IHistoryDocumentStorage`/`IHistoryBlob` in Core (`Services/IHistoryStorage.cs`, BCL file I/O only), implemented by `FileHistoryStorage` (`Services/FileHistoryStorage.cs`). Spillable history items implement `ISpillableHistoryItem`; only `PixelRegionHistoryItem` does so far (`CompressedDiff` holds an `IHistoryBlob` handle once spilled instead of a `byte[]`) — `AddLayerHistoryItem`/`SwapSurfaceHistoryItem`/`ResizeImageHistoryItem` hold real `Layer`/`IImageBuf` objects, not a portable buffer, so spilling them is a bigger follow-up.
   - [x] `ImageDocumentHistory.SpillFarSteps` moves steps 10+ away from `Pointer` (`DefaultSpillDistance`) to disk asynchronously (on every push/undo/redo), compressed already by P1.3. Undo/redo call `CompressedDiff.Get()`, which reads the blob back **synchronously** — a deliberate choice over "prefetch the next few steps": `IHistoryItem.Undo`/`Redo` stay synchronous (no ripple into `CinnabarSharp.Desktop`/`CinnabarSharp.Mcp`), and a single compressed rect diff reads well within the 200 ms undo/redo target, so prefetching wasn't needed. Once spilled, a step stays spilled even if it comes back within range (simpler; still fast enough).
   - [x] The folder (`LocalApplicationData/CinnabarSharp/history/<process>-<n>-<guid>/`, not literally `<session>/<document>/<step>.bin` as first sketched, but the same idea) gets user-only permissions on macOS/Linux (`UnixFileMode`; Windows relies on the default per-user ACL of `LocalApplicationData`, not hardened further) and is deleted when `ImageDocumentHistory.Clear()` runs (document closed/reverted). `FileHistoryStorage.CleanOrphans`/`CreateDefault` delete leftover folders from a run that didn't exit cleanly.
   - [x] Fall back to memory-only when the disk is full or not writable: every write/create in `FileHistoryStorage` catches `IOException`/`UnauthorizedAccessException` and reports failure (`null` blob) instead of throwing; `CompressedDiff.Spill` leaves the data in memory when that happens. Not covered by an automated test (hard to simulate a full disk portably); reasoned about instead.
   - [ ] Not wired into the app: nothing calls `AddSingleton<IHistoryStorage>(FileHistoryStorage.CreateDefault())`, so `CinnabarSharp.Desktop` never spills today — same "mechanism built, wiring deferred" state as the P1.2 budget. `ImageDocument`'s `IHistoryStorage?` constructor parameter and `HistoryTests.Fill_selection_pixel_step_can_be_spilled_and_read_back_from_disk` (DI end-to-end) prove the path works.
   - [ ] Settings: history on disk on/off, location, maximum disk size (currently unbounded except by the existing 100-step cap, which bounds worst-case disk usage to ~100 compressed diffs).
   - [ ] Bonus: unsaved work recovery (Phase 10) — not attempted.
   - [ ] Risks not exercised: slow disks (USB, network home folders), antivirus scanning temp files on Windows, a write that fails partway through (today it either fully succeeds or is caught and treated as a full failure — no partial-file cleanup needed since `FileMode.Create` truncates, but not stress-tested).

## P2 — Fewer full-image copies

- [ ] Rent large buffers from `ArrayPool<byte>.Shared` (or a pixel-buffer pool sized for the current document) in `ToBgra`, `GetFlattenedBgra`, `PixelRegion.Extract` and effect destinations; return them after use.
- [ ] Span-based overloads (`ReadRegion(rect, Span<byte>)`, `WriteRegion(rect, ReadOnlySpan<byte>)`) so callers reuse a buffer instead of allocating.
- [ ] `PaintSession`: read only the rectangle a tool can touch (brush bounds grown during the stroke) instead of the whole layer at pointer down.
- [ ] Cache the flattened image of the layers below and above the current layer while painting, so each brush dab composites one layer instead of all of them.
- [ ] Consider storing layers as our own BGRA buffers (`byte[]` or native memory) instead of `IMagickImage<byte>`, and use Magick.NET only for codecs and resampling: removes most `ToBgra`/`FromBgra` round trips. Big refactor: decide after P0 numbers.
- [ ] Set Magick.NET `ResourceLimits` (memory, disk) explicitly so large files use its disk cache instead of failing or swapping.

## P3 — CPU

- [ ] Parallel effects: `EffectSession.Compute` splits the region into horizontal strips run with `Parallel.For`. The effect contract already allows it (read only `Source`, write every pixel of the region, deterministic). Add a test that every effect in `EffectCatalog.All` gives bit-identical results in one block and in strips; keep the checksum test unchanged.
- [ ] Effects that analyze the whole image (Auto-Enhance, Auto-Level, Levels/Curves histograms) compute their analysis once per session, not once per strip.
- [ ] Dialog previews at reduced resolution: compute the preview on the visible part of the canvas at screen resolution (like the TV and comic page previews), and the full resolution only on OK. Effects whose result depends on scale (blur radius, outline width) scale their parameters for the preview.
- [ ] SIMD compositing: `BlendOps.Composite` with `System.Numerics.Vector<T>` / `Vector128` for Normal, Multiply, Screen, Additive…; keep the pinned checksum (results must stay bit-identical).
- [ ] Brush and shape coverage (`CoverageMask`): only process the dab's bounding box; check with P0 stroke benchmark.
- [ ] Cancel stale work everywhere (previews already do): TV and comic page previews during drags, thumbnail generation.

## P4 — Rendering and UI

- [ ] Canvas: always update only the changed region (`CanvasView.UpdateRegion`) — for layer property changes, undo/redo of pixel steps and effects apply — and rebuild the full bitmap only when the image size changes. Zoom must not re-flatten.
- [ ] Very large images at low zoom: draw from a downscaled pyramid (mipmaps) of the flattened image instead of scaling a 24 MP bitmap every frame.
- [ ] Layer thumbnails: produce them from a downscaled copy (read every Nth pixel, no full `Clone()`), only for layers that changed, in the background, and debounce during strokes.
- [ ] History panel and layer list: avoid rebuilding the whole collection on every change (`RefreshHistory`, `RefreshLayers`); update the changed items.

## P5 — Files and big images

- [ ] Open: decode on a background thread with the busy indicator (large HEIC/TIFF can take seconds); show the document as soon as the first layer is ready.
- [ ] Save: encode in the background with the busy indicator; ORA writes layers one at a time instead of building everything in memory first.
- [ ] Prepare Folder for TV and the MCP batch tools: process several photos in parallel (bounded by cores and memory budget).
- [ ] Warn before opening an image larger than the memory budget allows (e.g. 100 MP), with the option to open it downscaled.

## Validation

- [ ] P0 benchmarks before and after each phase, numbers recorded here, on macOS (Apple Silicon), Windows 11 and Linux.
- [ ] A scripted 1-hour editing session on a 24 MP photo (MCP server driving 500 edits and 200 undo/redo) stays under the memory target and never grows unbounded; history files are deleted at the end.
- [ ] Undo/redo of steps read back from disk is exact (`HistoryTests` extended to force every step through disk storage).
- [ ] Killing the app mid-session leaves no orphan history folders after the next start (or offers recovery, once implemented).
