# CinnabarSharp — Performance and memory

Goal: stay fast and within memory on large photos (24 MP and more), many layers and long editing sessions, on every OS. Each phase can ship on its own; measure before and after (Phase P0).

**Targets** (24 MP photo, 4000 × 6000, on a mid-range laptop):
- Memory: 10 layers + 100 undo steps under 2 GB of RAM, and never unbounded.
- Brush and selection tools: 60 fps while dragging.
- Effect dialogs: preview updates within ~150 ms of a slider change; final apply of a simple effect (Gaussian Blur 10, Auto-Enhance) under 1 s.
- Undo/redo of any step: under 200 ms, even when the step was moved to disk.
- Open and save: no more than twice the time Magick.NET alone needs to decode/encode the file.

## Where memory and time go today

This table is the **original baseline**, before any of the phases below. Several rows are now partially or fully fixed — each says so; check the phase section it links to for the current, exact state rather than trusting the row alone once it's marked fixed.

| Area | What happens | Cost on a 24 MP photo |
|---|---|---|
| Layer pixels | Each layer is a Magick.NET image (`Layer.Surface`, Q8: 4 bytes per pixel, unmanaged). | ~96 MB per layer |
| History, pixel steps | ~~`PixelRegionHistoryItem` (brush, effects, Cartoon…) keeps **both** the before and the after pixels of the touched rectangle.~~ **Fixed (P1.1/P1.3):** stores only the XOR diff of the two, compressed. | was ~192 MB per full-layer effect; see P1's before/after numbers |
| History, layer steps | `SwapSurfaceHistoryItem` (fill, erase, crop, paste, merge, flatten) and `ResizeImageHistoryItem` (resize, rotate, canvas size) keep a full copy of every affected layer. **Unchanged** — P1.1 only narrowed this for pixel-only edits (fill/erase now use `PixelRegionHistoryItem` instead); layer/crop/resize steps still copy the whole layer. | ~96 MB per layer per step |
| History limit | ~~`ImageDocumentHistory` never drops old steps: memory grows with every edit until the document is closed.~~ **Fixed (P1.2/P1.4):** a byte budget + 100-step cap drop the oldest steps, and far-back steps spill to disk first. | was unbounded; now bounded (budget is per-document, not yet pooled across documents — see P1.2) |
| Full-image copies | `Surface.ToBgra()`, `GetFlattenedBgra()`, `Utility.FromBgra()` allocate a new image-sized array (Large Object Heap) each time: `PaintSession` base, every `DocumentActions` pixel edit, crop, MCP preview… **Unchanged** — P2 confirmed Magick.NET's own export API has no destination-buffer overload, so this needs the bigger "layers as our own BGRA buffers" refactor P2 scoped out. | ~96 MB each, GC pressure |
| Canvas | `CanvasView.RebuildBitmap()` flattens all layers and copies into a new `WriteableBitmap` on many changes (zoom, layer property, undo…), not only the changed region. **Partially fixed (P4):** undo/redo of a pixel-region step now redraws only that region; layer-property changes, effect apply and zoom still trigger a full rebuild (zoom's is a confirmed, not-yet-fixed bug, not just "not optimized"). | 2 × ~96 MB per rebuild, for the cases still unfixed |
| Layer thumbnails | `LayerViewModel.CreateThumbnail()` clones the full layer and resizes it, for every layer, on every history change (`RefreshThumbnails`). **Unchanged**, not attempted. | a full clone per layer per step |
| Effects | ~~`Effect.ForEachPixel` runs on one core~~; **parallelized (P3):** `EffectSession.Compute` runs it across `Parallel.For` strips (6.4×/6.3× measured on the two slowest effects) — analysis-based effects (Auto-Enhance, Auto-Level) still repeat their whole-image pass per strip, a known remaining inefficiency, not a correctness issue. Dialog previews (`PreviewDialogViewModel.RequestPreview`) still compute at full resolution on every slider change, unchanged. | was seconds per preview/apply on large photos; see P3's before/after numbers |
| Compositing | `BlendOps.Composite` blends byte by byte in scalar code. **Attempted and reverted (P3):** a `Vector<int>` version was ~1.9× *slower*, not faster — see P3 for why. Still scalar. | slow flatten with many layers |

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
   - [ ] Measure the actual compression *ratio* on real edits (brush stroke, adjustment, crop) — `CinnabarSharp.Benchmarks` now exists and tracks time/allocation (P0), but nothing yet reports compressed-vs-raw bytes for a real edit; `CompressedDiffTests` only pins the synthetic mostly-zero case.
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

- [ ] Rent large buffers from `ArrayPool<byte>.Shared` in the one place P0's numbers pointed at: `PixelRegionHistoryItem.Swap()` now decompresses the diff into a pooled buffer instead of `CompressedDiff.Get()` allocating a fresh one every undo/redo. **Not done for `ToBgra`/`GetFlattenedBgra`/`PixelRegion.Extract`/effect destinations** — see the Span-based-overloads item below for why: Magick.NET's pixel *export* has no destination-buffer overload, so pooling on the read side would only add a copy, not remove an allocation, for those specific call sites.
- [ ] Span-based overloads: **`WriteRegion(rect, ReadOnlySpan<byte>)`** added (`Utility.cs`) and is a true zero-copy pass-through to Magick.NET's `ImportPixels(ReadOnlySpan<byte>, …)`, which does accept a span. **`ReadRegion(rect, Span<byte>)` turned out not to be possible**: checked Magick.NET 14.17.1's `IPixelCollection<byte>` by reflection — every pixel-export method (`ToByteArray`, `GetArea`, …) returns a freshly allocated `byte[]`; there is no "copy into my buffer" overload for BGRA-mapped export, only `GetReadOnlyArea` (raw quantum data, not BGRA-remapped, would need manual channel handling to use safely). So `ReadRegion` still always allocates — this is the concrete, measured reason the "big refactor" item below exists, not a stylistic preference.
- [ ] `PaintSession`: read only the rectangle a tool can touch (brush bounds grown during the stroke) instead of the whole layer at pointer down.
- [ ] Cache the flattened image of the layers below and above the current layer while painting, so each brush dab composites one layer instead of all of them.
- [ ] Consider storing layers as our own BGRA buffers (`byte[]` or native memory) instead of `IMagickImage<byte>`, and use Magick.NET only for codecs and resampling: removes most `ToBgra`/`FromBgra` round trips. Big refactor: decide after P0 numbers. **Now that P0 numbers and the Span-overload investigation above both exist: this is the only way left to remove `ReadRegion`/`ToBgra`'s per-call allocation** (Magick.NET's own API has no zero-copy export). Still not started — real scope (every `Layer.Surface` consumer, `BlendOps`, every codec path) is too large for this pass; a separate, dedicated pass should decide it.
- [x] Set Magick.NET `ResourceLimits` explicitly: `ServiceExtensions.ConfigureMagickResourceLimits()` caps `ResourceLimits.Memory` to 25% of `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (same fraction/source as `ImageDocumentHistory`'s own budget) so Magick.NET spills to its disk cache instead of growing unbounded; global to the process, so idempotent by design. `Disk`/other limits left at Magick.NET's defaults.

**Before/after (P0's `CinnabarSharp.Benchmarks`, same machine, same job config as the P0 baseline):**

| Benchmark | Before (P0 baseline) | After (this pass) |
|---|---:|---:|
| `HistoryUndoRedoBenchmarks.PixelEditWholeLayer` (time) | 576.7 ms | 549.1 ms |
| `HistoryUndoRedoBenchmarks.PixelEditWholeLayer` (allocated) | 384.0 MB | **187.5 MB (-51%)** |
| `HistoryUndoRedoBenchmarks.PixelEditSmallRegion` (time) | 830 µs | 814 µs |
| `HistoryUndoRedoBenchmarks.PixelEditSmallRegion` (allocated) | 626 KB | **314 KB (-50%)** |

Allocation roughly halved as expected (one of the two same-size buffers per undo/redo is now pooled); time barely moved, because this change targets GC pressure, not the scalar XOR loop or Magick.NET's own (unmanaged, not counted above) work — that's P3's SIMD-compositing item, still the right next lever for the 576 ms/200 ms-target gap.

## P3 — CPU

- [x] Parallel effects: `EffectSession.Compute` splits the region into horizontal strips (64+ rows, up to `Environment.ProcessorCount` strips) run with `Parallel.For`; each strip renders into its own exactly-sized buffer via the existing, unmodified `Effect.Render`, then copies into the shared result — no change to `Effect`, `ForEachPixel`, or any of the ~30 effect implementations. `EffectParallelismTests` (Core.Tests, tall enough image to force multiple strips) pins bit-identical output against a single block for every effect in `EffectCatalog.All` **and** `EffectCatalog.Adjustments`; the existing pinned checksum test is untouched and still passes unchanged, confirming the parallel path is bit-identical to what it pinned.
- [ ] Effects that analyze the whole image (Auto-Enhance, Auto-Level, Levels/Curves histograms) compute their analysis once per session, not once per strip. **Still not done — and now measurably costing something**: `ColorAdjustment.Render` (the base class `AutoLevel`/`Curves`/etc. share) calls `Create(values, context.Source)` — which does the histogram/LUT work — on *every* `Render` call, so parallelizing by strip means every strip repeats it. This is why `AutoEnhanceEffect` only got ~4.5× faster on 10 cores below (its `Analyze()` samples up to 250k pixels, once per strip) while embarrassingly-parallel effects like `MedianEffect`/`CartoonEffect` got ~6.3–6.4×. The fix is a real optimization, not just cleanup: give `Effect`/`ColorAdjustment` a way to run whole-image analysis once and hand the per-strip `Render` calls its result.
- [ ] Dialog previews at reduced resolution: compute the preview on the visible part of the canvas at screen resolution (like the TV and comic page previews), and the full resolution only on OK. Effects whose result depends on scale (blur radius, outline width) scale their parameters for the preview.
- [ ] SIMD compositing: `BlendOps.Composite` with `System.Numerics.Vector<T>` / `Vector128` for Normal, Multiply, Screen, Additive…; keep the pinned checksum (results must stay bit-identical). **Attempted and reverted — a real, measured negative result, not a shortcut avoided.** Built a `Vector<int>` version (4 modes without a per-lane branch in `Blend`, confirmed correct: the "aT == 255 copies top exactly" and "aT == 0 leaves the pixel untouched" scalar special cases both fall out of the same general weighted-average formula at those two modes' extremes, verified by working through the integer arithmetic by hand and with a 2000-trial random-fuzz + alpha-boundary parity test against the scalar path). It matched the pinned checksum bit-for-bit after fixing two bugs (a rounding mismatch between the vector and scalar `Div255`, then a `DivideByZeroException` from SIMD lanes computing unconditionally even where the scalar loop would have `continue`d past a zero divisor). Bit-identical, but **`FlattenBenchmarks.FlattenFull` went from 1.50 s to 2.86 s — ~1.9× *slower*, not faster** (`FlattenRegion` similarly: 374 ms → 696 ms), reproduced twice. The BGRA data is byte-interleaved, so getting it into `Vector<int>` lanes needs a scalar gather (byte → int, 8 gathers + 3 scatters per batch of `Vector<int>.Count` pixels); that scalar data-shuffling overhead outweighs the vector math it enables, especially since `Vector<int>.Count` is only 4 on this machine (128-bit/32-bit lanes) — not enough parallelism to pay for the gather/scatter cost. A version worth shipping would need to operate on the raw interleaved bytes directly (`Vector<byte>`/`Vector128<byte>` with proper widen/narrow and channel-shuffle intrinsics) instead of gathering into `int` lanes — a harder, riskier rewrite than this pass attempted; reverted to the original scalar `BlendOps.cs` (verified identical to before by diff) rather than ship a regression.
- [x] Brush and shape coverage (`CoverageMask`): per-dab processing was **already true** (`Paint()` already clipped every shape to its own tight local rectangle; checked, not changed). The one thing that wasn't: `CoverageMask` itself used to allocate its backing store at the *full image size* up front (`new CoverageMask(document.ImageSize.Width, document.ImageSize.Height)` in `PaintbrushTool.OnPointerDown`, ~22.9 MB of 1-byte-per-pixel coverage data on a 24 MP canvas), no matter how small the stroke turned out to be. `CoverageMask` (`Models/Raster.cs`) now allocates nothing until the first shape is drawn and grows (reallocate + copy, like a growable list, with a 64px padding margin on whichever side(s) actually grow so a gradually-wandering stroke doesn't reallocate on almost every dab) only as far as strokes on it actually reach — its public API (constructor, indexer, `Width`/`Height`, every shape method) is unchanged, so this needed no changes anywhere else. `CoverageMaskGrowthTests` (Core.Tests) pins growth in all four directions matches a mask that was always full-size, plus that nothing is allocated before the first shape and the backing store stays far smaller than the image for a small stroke; the existing `CoverageMaskTests`/`BrushAndTextToolsTests`/paint checksum tests are untouched and still pass, confirming pixel output is unchanged. `BrushStrokeBenchmarks.Stroke`: 116.0 MB → 93.3 MB allocated (the ~23 MB drop matches removing that full-image allocation almost exactly); time unchanged (54.8 ms → 55.0 ms, this was never the slow part). The much larger remaining allocation in that benchmark (~92 MB) is `PaintSession`'s own full-layer `_base` copy at pointer-down — the separate, not-yet-done P2 item ("PaintSession: read only the rectangle a tool can touch instead of the whole layer at pointer down").
- [ ] Cancel stale work everywhere (previews already do): TV and comic page previews during drags, thumbnail generation.

**Before/after (P0's `CinnabarSharp.Benchmarks.EffectBenchmarks`, same 2 MP size/job config as the P0 baseline, Apple M5, 10 cores):**

| Case | Before | After | Speedup |
|---|---:|---:|---:|
| MedianEffect | 770.4 ms | 120.3 ms | **6.4×** |
| CartoonEffect | 468.5 ms | 74.0 ms | **6.3×** |
| AutoEnhanceEffect | 110.0 ms | 24.3 ms | 4.5× (histogram repeated per strip — see the unchecked item above) |
| BlackAndWhite | 7.7 ms | 5.2 ms | 1.5× (already fast; strip overhead eats into the win) |
| Sepia | 7.2 ms | 5.3 ms | 1.4× |

The two effects P0 flagged as outliers are now both comfortably under 200 ms at 2 MP; scaled to the 24 MP target size they'd still be the slowest in the catalog, but no longer several-seconds slow. `AutoEnhanceEffect` — the doc's own named "under 1 s" example — went from a real risk (110 ms at 2 MP, projected over 1 s at 24 MP per the P0 write-up) to comfortably inside budget even before its histogram gets deduplicated across strips.

## P4 — Rendering and UI

- [ ] Canvas: always update only the changed region (`CanvasView.UpdateRegion`) — for layer property changes and effects apply: **not attempted this pass** (would need the same whole-layer-rect treatment as `SwapSurfaceHistoryItem`/`ResizeImageHistoryItem` etc., which P2 already deferred; not a one-line change). **Zoom no longer re-flattens (done 2026-10-04):** a zoom step used to rebuild the bitmap twice (`ViewSizeChanged` and the `Invalidate()` of the `Scale` setter both bumped `RenderVersion`). `Scale` now raises only `ViewSizeChanged`; the view model turns it into `ViewVersion`, which re-measures and redraws `CanvasView` from the bitmap it has. Changes of the image size still go through `ImageDocument.Resize`, which invalidates the canvas. Tests: `HistoryTests.Zoom_fires_view_size_changed_only`, `ViewportTests.Zoom_redraws_without_recompositing`.
  - [x] **Undo/redo of pixel steps: done.** Found the actual bug by reading `CanvasView`/`MainViewModel` together: `Workspace.Invalidate()` (no rect, "whole image") and `Invalidate(rect)` already share one event (`CanvasEventItem`) that `MainViewModel` already routes correctly — an empty `Rect` bumps `RenderVersion` (full `CanvasView.RebuildBitmap()`), a non-empty one calls the cheap `UpdateRegion(rect)` — but `ImageDocumentHistory.Undo`/`Redo` called the *parameterless* `Invalidate()` unconditionally, so **every** undo/redo rebuilt the full bitmap regardless of what actually changed, even a one-pixel brush-dab undo. `IHistoryItem` gained `RectangleI? TouchedRect` (null = whole image/unknown, the same behavior every step had before this existed); `PixelRegionHistoryItem` reports its own `Rect`, `CompoundHistoryItem` unions its children's (or null if any child doesn't have one); everything else (layer add/remove/move/flip/properties, selection, resize/crop) still reports null, so those keep doing a full invalidate. `ImageDocumentHistory.Undo`/`Redo` now invalidate the item's own rect when it has one. Zero UI-layer changes needed — `MainViewModel`/`CanvasView` already handled a rect-carrying event correctly, they just never received a narrow one from undo/redo. New tests (`HistoryTests.Undo_of_a_pixel_edit_invalidates_only_its_own_rectangle` / `..._structural_step_still_invalidates_the_whole_image`) pin the two cases via the actual `CanvasEventItem.Rect`; `ImageDocumentEventTests`' pinned event-sequence tests are untouched and still pass. Magnitude: no direct benchmark of `CanvasView` itself (would need headless Avalonia timing, not attempted), but P0's own `FlattenBenchmarks` numbers are the right proxy for the two paths this now correctly routes between — `GetFlattenedBgra()` (what `RebuildBitmap`/`RenderVersion++` triggers) vs `GetFlattenedBgra(region)` (what `UpdateRegion` now actually gets used for on a pixel-edit undo/redo): 1.50 s vs 374 ms on a 24 MP, 10-layer document (P0 baseline numbers).
- [ ] Very large images at low zoom: draw from a downscaled pyramid (mipmaps) of the flattened image instead of scaling a 24 MP bitmap every frame.
- [ ] Layer thumbnails: produce them from a downscaled copy (read every Nth pixel, no full `Clone()`), only for layers that changed, in the background, and debounce during strokes.
- [ ] History panel and layer list: avoid rebuilding the whole collection on every change (`RefreshHistory`, `RefreshLayers`); update the changed items.

## P5 — Files and big images

- [x] Open: decode on a background thread with the busy indicator (done 2026-10-04). `ImageFormat` now separates `Decode(file)` (reads and decodes, touches no document, thread-safe) from `Import(file, decoded)` (creates the document and raises the events); `IFormatManager.OpenAsync` runs the first on a background thread and the second where it is awaited, so `DocumentEventsService` subscribers still run on the UI thread. `MainViewModel.OpenFileAsync` wraps it in `RunBusyAsync("Opening …")`. Only `MagickImageFormat` (every format but ORA) has a separate decode; ORA still opens synchronously. The document appears when the decode is done, not before (no progressive display). Tests: `FormatManagerTests.Open_async_*`, `FileWorkflowTests.Opening_a_file_shows_the_busy_status_…`.
- [x] Save: encode in the background with the busy indicator. `IFormatManager` gained `SaveAsync(document, file, format?, cancellation)` alongside the existing synchronous `Save` (kept, still used by tests and unaffected callers): it runs `format.Export(document, file)` — confirmed pure (reads the document, writes bytes, no `IWorkspaceService`/event side effects) — via `Task.Run`, then does the same clean-up (`document.File`, `FileType`, `History.SetClean()`) the sync version does. Because `await Task.Run(...)` resumes on the caller's captured `SynchronizationContext`, that clean-up runs back on the UI thread automatically, with no manual dispatch needed — same reasoning `Export` being pure made this safe applies here in reverse. `MainViewModel.SaveDocumentAsync` now calls it through the existing `RunBusyAsync` (same pattern already used for effects/TV export), which disables editing for the save's duration, closing the one real race (the document must not be mutated by further edits while a background thread is reading it to encode). `FormatManagerTests` gained round-trip/dirty-flag parity tests for `SaveAsync` plus one that pins the encode actually runs off the calling thread (a probe format overriding `Export`); `FileWorkflowTests`' existing headless UI save round-trips (`Save_new_image_asks_for_path_then_saves` etc.) are unchanged and still pass — real end-to-end confirmation this doesn't break in the actual Avalonia dispatcher, not just reasoning.
  - [x] ORA writes layers one at a time instead of building everything in memory first — **already true**, checked `OraFormat.Export` instead of changing it: its layer loop calls `WritePng(zip, src, layer.Surface)` directly inside the loop, streaming each layer straight into its own zip entry as it goes; nothing accumulates a list of encoded layer bytes first. (`layer.Surface` itself is always memory-resident regardless of export — that's the live document, not something `Export` chooses to hold onto.) The one real full-size temporary is `document.GetFlattenedImage()` for `mergedimage.png`/the thumbnail, which is unavoidable — ORA's format requires that merged entry — and is one extra flattened image, not "everything".
- [ ] Prepare Folder for TV and the MCP batch tools: process several photos in parallel (bounded by cores and memory budget).
- [x] Warn before opening an image larger than the memory budget allows — **done, without the downscale option**. `ImageFormat.PeekSize(file)` reads just the pixel dimensions from the file's header/metadata (`MagickImageInfo` for `MagickImageFormat`, `stack.xml`'s `w`/`h` for `OraFormat`, no layer decoding) — cheap even for a huge file; default `null` for formats that can't tell without a full `Import`. `IFormatManager.PeekSize` resolves the format and delegates. `ImageSize.IsRiskyToOpen(availableBytes?)` (`Extensions/ImageSizeExtension.cs`) is the actual budget check: estimates one BGRA copy plus headroom for decode/compose intermediates (16 bytes/pixel, a deliberately rough multiplier, not the "e.g. 100 MP" the checklist suggests — tied to the *machine's* available memory instead, same `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` source P1's budget and P2's `ResourceLimits` already use, so a 24 MP photo is fine on an 8 GB machine and a 900 MP one isn't). `MainViewModel.OpenFileAsync` peeks before opening and, if risky, asks via the existing `ConfirmAsync` (proceed-or-cancel, the same primitive already used elsewhere — no new dialog UI). **Not done: the "open it downscaled" option** — needs a new 3-way dialog primitive (`IDialogService` only has 2-way `ConfirmAsync` today) plus a downscale-on-import path; out of scope for this pass. `ImageSizeExtensionTests` pins `IsRiskyToOpen`'s decision with an explicit `availableBytes` (deterministic, not tied to the test machine's real memory); `FormatManagerTests` pins `PeekSize` for PNG/JPEG/TIFF/ORA and that an unrecognized file returns null. **Not covered by an automated end-to-end test**: `MainViewModel` actually showing the dialog for a genuinely huge file — constructing a test fixture large enough to trip the real threshold (hundreds of MP) would make the test itself slow/memory-heavy; the full suite (369/143/16) still passes unchanged, confirming no existing test's (small) images trip the new check.

## P6 — Startup time

Origin: [issue #1](https://github.com/pgourlain/CinnabarSharp/issues/1) (a user asks for a faster start, suggesting Native AOT). Before this phase nothing was enabled: no ReadyToRun, trimming or AOT, and `packaging/package.sh` published self-contained as is. Measure first; each step is only kept if the numbers show a clear gain.

**Measured** (Apple M5, macOS, Release self-contained `osx-arm64`, warm, median of 8 runs, from process start to the first frame drawn; `StartupTrace`, below):

| Build | First frame | Notes |
|---|---:|---|
| Plain (before) | **739 ms** | |
| ReadyToRun | **460 ms** (−38 %) | package +24 MB (143 → 167 MB published) |
| ReadyToRun + compiled bindings | 464 ms | no measurable gain (kept, see below) |
| ReadyToRun, `TieredPGO=0` | 494 ms | no gain (plain: 737 ms) |
| ReadyToRun, `TieredCompilation=0` | 602 ms | worse |

The first run after a reboot (cold disk cache) took 3.1 s plain and 1.7 s with ReadyToRun. Not measured on Windows and Linux (the CI can't show a window): only the macOS numbers above exist.

Where the ~455 ms of the ReadyToRun build go: runtime start to `Main` ≈ 25 ms; Avalonia platform init (`Main` → framework initialized) ≈ 140 ms; our services, view model and `MainWindow` (XAML + menu) ≈ 100 ms in total (`AppServices.Build` 10 ms, view model 20 ms, XAML 37 ms, menu 7 ms); native window creation and show ≈ 70 ms; first layout and render ≈ 100 ms. Little of it is ours, so there is nothing worth deferring.

- [x] Measure the start. `StartupTrace` (`Services/StartupTrace.cs`) writes the time since process start at each phase to stderr when `CINNABARSHARP_STARTUP_TRACE` is set (`=exit` also quits after the first frame, to script runs). Done on macOS only; to repeat on another OS run the packaged app with that variable.
- [x] ReadyToRun: `-p:PublishReadyToRun=true` in `packaging/package.sh`, so the four release packages get it. All four targets build from a macOS arm64 machine (osx-x64, linux-x64 and win-x64 cross-compile); the CI release workflow will be the first to build them on their own OS.
- [x] Lazy start: **not done, on purpose.** The measure above shows our own start-up work is about a fifth of the total and has no slow step to defer.
- [x] Compiled bindings: `AvaloniaUseCompiledBindingsByDefault` is on. It builds without changes (every view already had its `x:DataType`) and all UI tests pass, but start-up doesn't change. Kept because it checks bindings at build time and is the first requirement of a Native AOT build.
- [x] Native AOT study (branch `aot-experiment`, macOS arm64): **it works.** Results:
  - Dependencies: `PublishAot` with `TrimmerSingleWarn=false` reports **no warning in any package** (Avalonia, Magick.NET, `ModelContextProtocol`, DI, Hosting). All 44 warnings were in our code: reflection-based `System.Text.Json` (settings, recent files, Claude Desktop config, MCP resources), `new Binding(path)` for the checked menu item, `GetMethod` on effects, `Assembly.Location`. Fixed with source-generated contexts (`DesktopJson`, `McpJson`), a property-changed handler for the menu item, `[DynamicallyAccessedMembers]` on `Effect`, and `AppContext.BaseDirectory`: 0 warnings.
  - The MCP SDK is AOT-ready but needs our tool parameter and result types: `WithTools<ImageTools>(McpJson.ToolOptions)`. `AotJsonTests` (Mcp.Tests) fails when a tool uses a type missing from `McpJson`, so new tools can't silently break an AOT build.
  - Run: the whole MCP suite (38 tests) passes against the native binary (`CINNABARSHARP_TEST_EXE=<path to the native app> dotnet test CinnabarSharp.Mcp.Tests`), and the GUI opened a 9 MP HEIC (Magick.NET under AOT) and served an attached agent (list documents, Auto-Enhance) from its in-process MCP server.
  - Start: **~250 ms** to the first frame (ReadyToRun 460 ms, plain 739 ms); cold first run 1.3–1.7 s. Size: 80 MB published (native binary 36 MB + Skia, HarfBuzz, Avalonia and Magick native libraries) instead of 167 MB with ReadyToRun.
  - Not covered: the headless UI tests run JIT, so dialogs and views were only checked by hand-free smoke runs; Windows and Linux not tried. Local note: the Homebrew .NET SDK links `-lssl`/`-lbrotli*` from Homebrew (`LIBRARY_PATH=/opt/homebrew/opt/openssl@3/lib:/opt/homebrew/opt/brotli/lib`); the official SDK used by the CI doesn't.
  - macOS, the installed 0.7.0-rc3 `.dmg` (Native AOT, checked by hand): **248 ms** to the first frame, the same as the build made here. Phases: process start to `Main` 22 ms, Avalonia init 99 ms, our code 62 ms (services 4 ms, view model 1 ms, window and XAML 17 ms, menu 1 ms), window show 12 ms, first render 53 ms.
  - Linux (Omarchy, Hyprland, the 0.7.0-rc2 pacman package, checked by hand): opens a photo, applies an effect, saves and shows dialogs. First frame 567 ms on the first run, **432–495 ms** warm. Of the first run: Avalonia platform init (`Main` → framework initialized) 217 ms and window show + first render 206 ms (Avalonia uses X11, so XWayland under Hyprland); our own code 89 ms, as on macOS. ReadyToRun not measured on Linux, so the AOT gain there is unknown.
  - [x] Shipped: `packaging/package.sh` publishes with Native AOT, falling back to ReadyToRun (with a `::warning::` in the CI log) if the AOT build fails; `CINNABARSHARP_PUBLISH=r2r` forces ReadyToRun. The release workflow installs `clang` and `zlib1g-dev` on Linux. `IsAotCompatible` (Core, Mcp) and the trim/AOT analyzers (Desktop) report new problems at build time. Checked locally: osx-arm64 and osx-x64 (cross-compiled) packages build and start; Windows and Linux are first built by the release workflow.
- [ ] Reply to the issue with these numbers once the release with ReadyToRun is out.

## P7 — Work on reduced versions; hibernate inactive images

Origin: with 15 photos open you only work on one at full resolution, and the comic page with 9 photos is slow to react before Apply.

### P7.1 Comic page on proxies (first)

Today (`MainViewModel.ComicPage`, `RefreshComicPreviewAsync`):
- the start dialog flattens **every** open document into a full-resolution `ComicSource` (about 48 MB per 12 MP photo, 720 MB for 15), kept for as long as the page is edited, including photos not placed on the page;
- every change (drag, zoom, gutter, divider, layout, options) recomposes the preview from the full-resolution photos: each panel crops its photo and resizes it with Lanczos (`TvExport.Resize`) to a page up to 2560 px wide. Changes made during a refresh are merged, but a running refresh is never cancelled.

**Done** (macOS, Apple M5, `CinnabarSharp.Benchmarks`: `ComicPageBenchmarks` and `dotnet run -c Release -- --memory 15 12`):

| 3 × 3 page of a 16:9 4K, 9 photos of 12 MP, preview 1920 px wide | Before | After |
|---|---:|---:|
| One preview (every drag, zoom, divider move) | **775 ms** | **64 ms** (12× faster) |
| Allocated per preview | 186 MB | 27 MB |
| Managed memory held by the comic mode, 15 photos open | 702 MB (a full copy of every open image) | **16 MB** (9 proxies) |

- [x] Measure first (the numbers above; the memory part also gives P7.2's baseline, below).
- [x] Proxies sized for the panel (`ComicSource` in `ViewModels`, `ComicProxies` in `Services`, `ComicPage.NeededScale` in Core): made in the background with a box filter, 25 % more resolution than needed so small changes don't make a new one, a bigger one when the layout, a divider, the gutter or the zoom ask for more (up to the full photo), a smaller one only if the proxy is over 16 MB and more than twice what is needed. The panel keeps its framing (`ComicPageTool.SwapPhoto`); `ComicPanelContent.Source` says which picture a panel shows whatever copy it holds. At most 3 photos are read at once while proxies are made.
- [x] Bilinear for the preview, Lanczos for Apply (`ComicQuality.Preview` / `Full` in `ComicPage.Compose`; the default is `Full`, so MCP and the pinned results don't change).
- [x] No full-resolution copy while editing: open images are flattened only when a panel needs them (proxy), or for a thumbnail (in the background, nothing kept), or at Apply; a file added in the dialog is read once for its size and first proxy (1600 px). Apply reads each photo at full size, one panel at a time (`ComicPanelContent.LoadFull`), so a single full photo is in memory at once. A file moved meanwhile makes Apply say so and leave the page open.
- [x] Cancel a running preview when a newer change comes (the `CancellationToken` of `Compose`).
- [x] Tests: `NeededScale`; a preview from a proxy matches one from the full photo (same framing); Full reads `LoadFull`, Preview never does; `SwapPhoto` keeps the framing; cancelled compose; sources not read until needed; a sharper proxy after a zoom (and none for a small change); Apply with a missing file; background thumbnails. The existing Apply pixel checks pass unchanged.

### P7.2 Hibernate inactive images (after measuring)

Each open image keeps all its layers in memory (`Layer.Surface`, ~48 MB per 12 MP layer) whether you look at it or not. The history already has a budget and spills to disk (P1), the layers don't.

Idea: when the memory of all open images goes over a budget, write the layers of the least recently used inactive images to disk (compressed, reusing the P1.4 history storage), free them, keep the tab and thumbnails, and read them back when the image is used again. The active image and the last 2 used always stay in memory.

- [x] Measure first (`dotnet run -c Release --project CinnabarSharp.Benchmarks -- --memory 15 12`, macOS): 15 photos of 12 MP open take **779 MB** (about 52 MB each, 49 MB at start). One more full-size layer in each (a worst case; real edits mostly add diffs) brings it to 1467 MB. So hibernating the 14 inactive photos would free about **700 MB of 780**, and more once they are edited. Not measured on Linux and Windows. Decision left to the user: real, but a moderate gain on a typical machine, for the riskiest change of the phase.
- [ ] If yes: a Core service that owns "loaded or hibernated" per document, with **one** way in for anything that reads pixels (`ImageDocument.Layers`, flatten, save, MCP tools, comic page, Prepare for TV), which reloads transparently; tests in the style of `HistoryTests` (hibernate → every action → identical pixels, history, dirty flag).
- [ ] Budget and timing (decision 3, still open), and what the user sees while an image is read back (a short busy indicator over 100 ms?).

### Decisions (2026-10-04)

1. **Proxy size computed from the page and the panel**, since a double click can put a new photo in any panel: a photo's proxy has the pixels its panel needs at the preview's resolution, times its zoom, never more than the photo. When the layout, a divider, the gutter or the zoom asks for more than the proxy has, a bigger proxy is made in the background (from the open document, or by decoding the file again); it is not made smaller on every change, only when it is more than twice what is needed, so dragging doesn't keep re-making proxies. While a new proxy is being made, the preview uses the current one.
2. **Files added in the dialog or by double click are decoded at full size only at Apply.** When added they are decoded once to make the proxy, then the full image is dropped. Apply decodes them again; if one is gone, Apply says which and the page stays open.
3. **Hibernation budget**: open, to settle with the P7.2 measure.
4. **Order**: P7.1 now, with the P7.2 measure; P7.2 only if the measure shows a real problem.

## Validation

- [ ] P0 benchmarks before and after each phase, numbers recorded here, on macOS (Apple Silicon), Windows 11 and Linux.
- [ ] A scripted 1-hour editing session on a 24 MP photo (MCP server driving 500 edits and 200 undo/redo) stays under the memory target and never grows unbounded; history files are deleted at the end.
- [ ] Undo/redo of steps read back from disk is exact (`HistoryTests` extended to force every step through disk storage).
- [ ] Killing the app mid-session leaves no orphan history folders after the next start (or offers recovery, once implemented).
