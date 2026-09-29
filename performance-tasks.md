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

- [x] `CinnabarSharp.Benchmarks` project (BenchmarkDotNet, added to `CinnabarSharp.slnx` so CI *builds* it — catches compile errors — but CI's `dotnet test` never executes it, matching "not run in CI"). Covers: open/save 24 MP JPEG and PNG, flatten 10 layers (full + a region), a 500-point brush stroke, every effect/adjustment/photo-tool at defaults, undo/redo of one instance of every concrete `IHistoryItem` type. `[MemoryDiagnoser]` on every class. Run with `dotnet run --project CinnabarSharp.Benchmarks -c Release -- --filter '*ClassName*'` (Release is required; BenchmarkDotNet refuses/warns on Debug). **Not done: TV and comic page compose** — `TvExport.Compose`/`ComicPage.Compose` need a fully-populated `TvOptions`/`ComicLayout`/panel-contents setup that didn't fit this pass; left for a follow-up.
- [x] Sample images: generated in-memory (`BenchmarkHelpers.CreateDocument` fills layers with seeded `Random` noise — incompressible, realistic compositing cost — no download script, nothing checked into git). Sizes are named constants in `BenchmarkSizes` (Small ~2 MP, Medium 12 MP, Large 24 MP = 4000×6000, the doc's own target); no 50 MP size yet.
- [ ] Hidden "Performance" panel / `--diagnostics` flag / MCP resource — not attempted this pass; the benchmark project substitutes for now as the "measure" mechanism, but there's no in-app live diagnostics yet.
- [x] Baseline numbers recorded below (Apple M5, macOS, arm64, `dotnet run -c Release`, default `[SimpleJob]` overridden per class to `warmupCount:1` + a small `iterationCount` to keep a full run's wall time short — see each class; **not** BenchmarkDotNet's default job, so don't compare these numbers to a run using the default job without re-baselining both).

### Baseline numbers (2026-09-29, before P2/P3/P4/P5)

24 MP = 4000×6000. "Small" (effects sweep) = 1600×1200 (~2 MP) — see `BenchmarkSizes`.

| Benchmark | Scenario | Mean | Allocated |
|---|---|---:|---:|
| `FlattenBenchmarks.FlattenFull` | 24 MP, 10 layers, `GetFlattenedBgra()` | 1.50 s | 1.01 GB |
| `FlattenBenchmarks.FlattenRegion` | same, but a quarter-image region | 373.7 ms | 251.8 MB |
| `BrushStrokeBenchmarks.Stroke` | 24 MP layer, 500-point stroke, brush width 20 | 54.8 ms | 116.0 MB |
| `OpenSaveBenchmarks.OpenPng` | 24 MP PNG, real decode (not the "already open" cache) | 465.8 ms | ~11 KB |
| `OpenSaveBenchmarks.OpenJpeg` | 24 MP JPEG | 324.4 ms | ~21 KB |
| `OpenSaveBenchmarks.SavePng` | 24 MP PNG | 2.48 s | 187.5 MB |
| `OpenSaveBenchmarks.SaveJpeg` | 24 MP JPEG | 773.9 ms | 187.5 MB |
| `HistoryUndoRedoBenchmarks.AddLayer` | Undo+Redo, `AddLayerHistoryItem` | 60 ns | 544 B |
| `HistoryUndoRedoBenchmarks.DeleteLayer` | `DeleteLayerHistoryItem` | 60 ns | 544 B |
| `HistoryUndoRedoBenchmarks.MoveLayer` | `MoveLayerHistoryItem` | 55 ns | 512 B |
| `HistoryUndoRedoBenchmarks.LayerVisibility` | `UpdateLayerPropertiesHistoryItem` | 39 ns | 368 B |
| `HistoryUndoRedoBenchmarks.Selection` | `SelectionHistoryItem` (Select All, 24 MP mask) | 30 ns | 304 B |
| `HistoryUndoRedoBenchmarks.Crop` | `ResizeImageHistoryItem` (crop to quarter, 24 MP) | 50 ns | 464 B |
| `HistoryUndoRedoBenchmarks.MergeDown` | `CompoundHistoryItem` (merge layer down, 24 MP) | 65 ns | 544 B |
| `HistoryUndoRedoBenchmarks.PixelEditSmallRegion` | `PixelRegionHistoryItem`, 200×200 fill, 24 MP layer | 830 µs | 626 KB |
| `HistoryUndoRedoBenchmarks.FlipLayer` | `FlipLayerHistoryItem`, 24 MP | **145.3 ms** | 240 B |
| `HistoryUndoRedoBenchmarks.PixelEditWholeLayer` | `PixelRegionHistoryItem`, whole 24 MP layer (Erase Selection, nothing selected) | **576.7 ms** | **384 MB** |

`EffectBenchmarks.Apply` — every effect/adjustment/photo-tool at defaults, **2 MP** (not 24 MP — see the class's own doc comment):

| Case | Mean | Allocated | | Case | Mean | Allocated |
|---|---:|---:|---|---|---:|---:|
| Sepia | 7.2 ms | 14.65 MB | | HueSaturation | 59.9 ms | 14.65 MB |
| Posterize | 7.6 ms | 14.65 MB | | EmbossEffect | 67.0 ms | 14.65 MB |
| BlackAndWhite | 7.7 ms | 14.65 MB | | SharpenEffect | 63.0 ms | 82.52 MB |
| InvertColors | 7.8 ms | 14.65 MB | | PhotoAdjustEffect | 72.3 ms | 14.65 MB |
| BrightnessContrast | 7.9 ms | 14.65 MB | | GlowEffect | 81.0 ms | 86.43 MB |
| Curves | 7.9 ms | 14.65 MB | | MandelbrotEffect | 80.6 ms | 14.65 MB |
| Levels | 7.9 ms | 14.65 MB | | ReliefEffect | 84.4 ms | 14.65 MB |
| FrostedGlassEffect | 9.5 ms | 14.65 MB | | ZoomBlurEffect | 83.8 ms | 14.65 MB |
| AutoLevel | 10.8 ms | 14.65 MB | | EdgeDetectEffect | 106.3 ms | 14.65 MB |
| PixelateEffect | 15.6 ms | 17.44 MB | | PhotoFilterEffect | 109.3 ms | 14.65 MB |
| BulgeEffect | 17.3 ms | 14.65 MB | | **AutoEnhanceEffect** | **110.0 ms** | 14.65 MB |
| StraightenEffect | 17.4 ms | 14.65 MB | | RadialBlurEffect | 233.1 ms | 14.65 MB |
| TwistEffect | 23.3 ms | 14.65 MB | | **CartoonEffect** | **468.5 ms** | 75.44 MB |
| VignetteEffect | 30.0 ms | 14.65 MB | | **MedianEffect** | **770.4 ms** | 14.66 MB |
| AddNoiseEffect | 27.1 ms | 14.65 MB | | | | |
| GaussianBlurEffect | 33.1 ms | 75.20 MB | | | | |
| CloudsEffect | 45.3 ms | 14.65 MB | | | | |
| MotionBlurEffect | 45.6 ms | 14.65 MB | | | | |

**What these numbers already say, before touching P2/P3/P4/P5:**
- The layer-management/selection/crop/merge undo/redo items (`AddLayer`, `DeleteLayer`, `MoveLayer`, `LayerVisibility`, `Selection`, `Crop`, `MergeDown`) are all nanoseconds: their `OnUndo`/`OnRedo` just swap an already-computed reference (list entry, `Layer.Surface` pointer) instead of recomputing or copying pixels. Comfortably inside the 200 ms target, no action needed.
- `PixelEditSmallRegion` (830 µs) shows P1's XOR-diff + compression approach works exactly as intended for the common case (a bounded brush/fill region): three orders of magnitude under the 200 ms target.
- **`PixelEditWholeLayer` (576.7 ms) blows the 200 ms undo/redo target by ~3×**, and allocates 384 MB per undo/redo. This is `PixelRegionHistoryItem.Swap()`'s scalar `for` loop XORing a full 24 MP (96 MB) buffer, plus `CompressedDiff.Get()` decompressing it, on every single undo/redo of a whole-layer pixel edit (Erase Selection / Fill Selection with nothing selected — a common action, not an edge case). This is the clearest concrete lead for **P3's SIMD compositing item** (`System.Numerics.Vector`/`Vector128` XOR instead of a byte-at-a-time loop) and for **P2's `ArrayPool`** item (384 MB allocated for what should be one ~96 MB buffer reused, not several fresh ones per call).
- **`FlipLayer` (145.3 ms)** is a real outlier against the other layer-history items: unlike `Crop`/`MergeDown`/etc., `FlipLayerHistoryItem.OnUndo`/`OnRedo` both call `layer.FlipHorizontal()`/`FlipVertical()` (`Surface.Flop()`/`Surface.Flip()`), which re-runs an actual Magick.NET flip over the full unmanaged 24 MP image on *every* undo/redo, instead of swapping a precomputed result like the other whole-layer items do. Still under 200 ms today, but 3+ orders of magnitude slower than its neighbors and the first thing to regress past the target on a slower machine or a larger image; worth converting to a precompute-once-and-swap item in a future pass.
- `SavePng` (2.48 s) is markedly slower than `SaveJpeg` (773.9 ms) for the same 24 MP image, as expected for PNG's compression — no action implied, just a baseline to compare Phase-5 background-encode work against.
- **`AutoEnhanceEffect`, the doc's own named example ("final apply of a simple effect… under 1 s"), is already 110 ms at 2 MP.** Scaled to the 24 MP target size (~12×, effects being roughly linear in pixel count on one core today), that's over 1 s before P3's parallelism work — the target is at real risk without it, not a theoretical concern.
- **`MedianEffect` (770 ms) and `CartoonEffect` (468 ms) are the standout outliers, already at 2 MP** — both far more expensive per pixel than every other effect (most simple per-pixel adjustments are 7–10 ms; even the other blurs are 30–85 ms). At 24 MP they would almost certainly take several seconds. These two are the clearest priority for **P3's `Parallel.For` strips** item, ahead of the rest of `EffectCatalog`.
- Effects allocate a near-uniform ~14.65 MB regardless of what they compute (the fixed cost of `PaintSession`/`EffectSession` extracting the base region + writing the result at 2 MP) — `GaussianBlurEffect` (75.2 MB), `SharpenEffect` (82.5 MB), `GlowEffect` (86.4 MB) and `CartoonEffect` (75.4 MB) allocate 5–6× that baseline, worth another look once P2's `ArrayPool`/span work lands, since they're the ones with extra internal buffers (kernels, intermediate passes).

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
   - [x] Wired into `CinnabarSharp.Desktop`: `AppServices.Build()` registers `IHistoryStorage` as `FileHistoryStorage.CreateDefault()` (real app), `TestHarness` overrides it with a throwaway temp folder deleted with the rest of its `TempDir` (UI tests never touch real app data, same pattern as `RecentFilesStore`). **Not** wired into the headless `--mcp` path (`McpHost.RunAsync` builds its own `AddCinnabarSharpServices()` without registering it) — attached-mode agents (`--mcp --attach`) get it for free since they share the running app's documents, but a standalone headless `CinnabarSharp --mcp` session still keeps everything in memory. `ImageDocument`'s `IHistoryStorage?` constructor parameter and `HistoryTests.Fill_selection_pixel_step_can_be_spilled_and_read_back_from_disk` (DI end-to-end) prove the path works regardless of caller.
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
