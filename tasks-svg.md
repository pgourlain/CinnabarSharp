# CinnabarSharp — SVG editor roadmap

Goal: open an `.svg`, edit it as **vector objects**, and save it back as SVG without losing what we did not touch. This is need 3 of [docs/adr/0001-svg-editing.md](docs/adr/0001-svg-editing.md), with option B of need 4 (bitmaps are `<image>` objects inside the SVG). Paint.NET has no vector editor, so the reference for behavior is a small subset of Inkscape: object selection, node editing, pen, shapes, fill/stroke, z-order, groups. Anything not listed here is out of scope.

## Read this first (agent instructions)

- Read [CLAUDE.md](CLAUDE.md) and the ADR before starting. Every rule there still applies: Core is non-visual, edits go through an actions class and history, Native AOT (no reflection JSON, zero trim/AOT warnings), deterministic rendering pinned by checksums, MCP and `--run` scripts keep working, `Translations.GetString` for user-visible strings, `PlatformHotkeyConfiguration.CommandModifiers` for shortcuts.
- Work **one task at a time, in order**. A phase's tasks depend on the previous phases. Do not start a phase before the previous one's automated checks pass.
- For each task: implement, add the tests listed, run `dotnet build CinnabarSharp.slnx` (zero warnings added) and `dotnet test CinnabarSharp.slnx`, then tick the box (`- [x]`) in this file and commit with a message describing the task. Do not push.
- If a task turns out to be wrong or impossible as written, do not improvise a large redesign: write what you found under the task as an indented `> Note:` line, leave it unticked, and move to the next independent task.
- Existing tests must keep passing. Some pin exact behavior on purpose (`ImageDocumentEventTests` event order, `BlendOpsTests` and effect checksums, `HistoryTests.Actions`, `CoreArchitectureTests`). Do not update a pinned checksum or expected event sequence unless the task says the change is intended.
- Raster documents must behave exactly as before. After Phase S1, every UI and MCP test that exists today must still pass unchanged.
- Two places for the code (see decision 8):
  - **`CinnabarSharp.Vector`** (new project, namespace `CinnabarSharp.Vector`): the pure SVG engine — geometry, path data, model, parser, writer, style cascade, rasterizer, stroker, path boolean operations. Tests in **`CinnabarSharp.Vector.Tests`**; sample SVGs in `CinnabarSharp.Vector.Tests/Data/svg/`, marked `CopyToOutputDirectory`.
  - **`CinnabarSharp.Core`** (namespaces `CinnabarSharp.Core.Vector` and `CinnabarSharp.Core.Vector.Tools`): everything that touches the application — `IDocument`, `SvgDocument`, `SvgFormat`, `SvgActions`, vector history items, `IVectorTool` and the tools, the Magick.NET image decoder. Tests in `CinnabarSharp.Core.Tests/Vector/`.
- Write the SVG parser/writer with `System.Xml.Linq` (AOT-safe). `CinnabarSharp.Vector` references the BCL only (no Core, no Magick.NET, no UI, no `Microsoft.Extensions.*`). No new NuGet dependency in Core. Svg.Skia / SkiaSharp are **not** allowed in Core or Vector; do not add them anywhere without a note asking the user.

## Design decisions already taken

These answer the ADR's open questions so the work can start. Do not revisit them.

1. **Document per kind.** A tab holds either an `ImageDocument` (raster, unchanged) or an `SvgDocument` (vector). No vector layers inside raster documents (ADR option A is rejected).
2. **Full model with round-trip of unknown content.** The parser builds a typed node tree; every element and attribute it does not understand is kept verbatim (`XElement` / `XAttribute` copies) on the node and written back in place. A file opened and saved without edits must be semantically identical (same elements, attributes, order; whitespace and attribute quoting may differ).
3. **Rendering in pure C# in `CinnabarSharp.Vector`** (`VectorRasterizer`), deterministic and checksum-pinned, used for the canvas, thumbnails, export to PNG/JPEG and "Rasterize". No Skia rendering of SVG in Desktop.
4. **Text:** `<text>` is modeled and edited as text. Rendering goes through a new interface `IGlyphOutlineProvider` (glyph outlines as paths), defined in `CinnabarSharp.Vector`, implemented in Desktop with Avalonia's `FormattedText.BuildGeometry` (or `GlyphRun` geometry), and in tests by a fake provider that returns boxes. In headless MCP mode (no provider), text renders as a placeholder box.
5. **Coordinates:** the document's user space is the root `viewBox` (or `width`/`height` if no `viewBox`). The canvas shows the viewBox at 96 dpi × zoom. Tools work in user-space units (`PointD`), converted from `ToolPointer` image coordinates by the document.
6. **History:** reuse the history engine (pointer, `IsDirty`, `SetClean`, `BaseHistoryItem` first). Vector history items store node-level before/after snapshots (cloned subtrees), not pixels.
7. **Filters** (`<filter>`, blur, shadow), **CSS beyond inline `style` and simple `<style>` selectors**, **SMIL animation**, **`<foreignObject>`** and **`<switch>`** are preserved on save but not rendered (placeholder or ignored) and not editable.
8. **Separate engine project.** `CinnabarSharp.Vector` holds the SVG engine and depends on nothing but the BCL; `CinnabarSharp.Core` references it. Vector has its **own simple primitive types** (`VPoint`, `VRect`, `VColor` or similar, `readonly record struct`s) instead of Core's `PointD`/`RectangleD`/`RectangleI`/`ColorBgra`, which it cannot see; conversions live in Core (`VectorInterop` extension methods). Rendering output is a straight-alpha BGRA `Span<byte>`, the same layout as Core uses, so no pixel conversion is needed. Things that need Magick.NET or fonts are interfaces in Vector, implemented outside it: `IImageDecoder` (for `<image>`, implemented in Core with Magick.NET) and `IGlyphOutlineProvider` (implemented in Desktop). Do not move Core's primitives into a shared project; that is a later refactoring if the duplication hurts.

## Definition of done for the whole roadmap

- Open the sample files (see S0) and real files exported by Inkscape, Illustrator and Figma; edit them with the tools below; save; reopen in a browser: the edits are there and nothing else changed.
- Undo/redo of every vector action, `IsDirty` correct.
- MCP tools and `--run` scripts can create, edit and save SVG documents.
- Same checksums on macOS, Linux and Windows in CI.

---

## Phase S0 — Projects, samples and test harness

- [x] Create `CinnabarSharp.Vector` (net10.0 class library, nullable and implicit usings enabled, `IsAotCompatible` true like the other libraries) and `CinnabarSharp.Vector.Tests` (xunit v3, like the newer test projects). Add both to `CinnabarSharp.slnx`; `CinnabarSharp.Core` references `CinnabarSharp.Vector`. Check that CI (`.github/workflows/ci.yml`) runs the new tests on the 3 OSes (it should through the solution; fix it if not) and that `packaging/package.sh` still publishes with zero trim/AOT warnings.
- [x] `VectorArchitectureTests` in `CinnabarSharp.Vector.Tests`: fails if `CinnabarSharp.Vector` references any assembly other than the BCL (in particular `CinnabarSharp.Core`, `Magick.NET*`, `Avalonia*`, `SkiaSharp*`, `Microsoft.Extensions.*`, `System.Drawing*`). Same approach as `CoreArchitectureTests`.
- [x] Add sample SVGs to `CinnabarSharp.Vector.Tests/Data/svg/` (hand-written, small, license-free; copy the existing `CinnabarSharp.Core.Tests/Data/SampleFiles/logo.svg` there too, and leave the original where it is): `shapes.svg` (rect with rx, circle, ellipse, line, polyline, polygon), `paths.svg` (every path command M L H V C S Q T A Z, absolute and relative, implicit repeats), `transforms.svg` (nested groups with translate/scale/rotate/skewX/skewY/matrix), `styles.svg` (fill/stroke as attributes, inline `style`, a `<style>` with class and id selectors, `currentColor`, `none`, opacity, fill-opacity, stroke-opacity, fill-rule evenodd), `strokes.svg` (widths, linecap, linejoin, miterlimit, dasharray/dashoffset), `gradients.svg` (linear and radial, `gradientUnits` both kinds, `gradientTransform`, `spreadMethod`, `href` inheritance between gradients), `clip.svg` (clipPath, mask), `use.svg` (`<defs>`, `<symbol>`, `<use>` with x/y), `text.svg` (text with tspans, font-size, text-anchor), `image.svg` (an embedded base64 PNG and a linked relative PNG), `unknown.svg` (a `<metadata>` block with RDF, an `inkscape:` / `sodipodi:` namespace with attributes on several elements, a `<filter>`, a comment, a `<desc>`), `units.svg` (width/height in mm with a viewBox).
- [x] Add `SvgTestFiles` helpers in Vector.Tests (path of a sample, parse it once Phase S2 exists). Core.Tests and Desktop.Tests that need samples reference the same files (link them in their csproj with `<None Include="..\CinnabarSharp.Vector.Tests\Data\svg\*.svg" Link="Data\svg\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`) instead of copying them.
- [x] Add a pixel-comparison helper for vector rendering tests in Vector.Tests: render to BGRA, compute the same checksum style as the existing checksum tests, and on failure write the PNG to the test output folder so a human can look at it.

**Validation**
- [ ] Every sample opens in a web browser and looks as intended (manual, once).

## Phase S1 — Document abstraction (no behavior change)

The workspace, tabs, events and history are typed on `ImageDocument` today. Introduce a common abstraction so a second kind can be added, without changing raster behavior.

- [x] Define `IDocument` in Core (`CinnabarSharp.Core.Models`): `Guid`/identity, `DisplayName`, `File`, `FileType`, `HasFile`, `IsDirty`, `ImageSize` (pixel size at 100 %), `Workspace` (`ImageDocumentWorkspace` or a shared base for zoom/view size/history), `History` (`IImageDocumentHistory`), `Dispose`/close behavior. `ImageDocument` implements it. Keep names close to the existing members so call sites only change their type.
- [x] Generalize `ImageDocumentHistory` to depend on `IDocument` instead of `ImageDocument` (it only needs identity, events and dirty state). Keep the class name for now.
- [x] Generalize events: `EventItem<T>.Document` becomes `IDocument`. `LayerEventItem` and `CanvasEventItem` keep requiring `ImageDocument` (they are raster-only). Fix every consumer: subscribers that need raster data pattern-match `is ImageDocument`.
  > Note: `LayerEventItem` keeps requiring `ImageDocument`, but `CanvasEventItem` now takes `IDocument`: the workspace raises canvas/view-size events for every document kind (an SVG tab needs them to redraw and zoom).
- [x] `IWorkspaceService` / `WorkspaceManager`: `OpenDocuments` becomes `List<IDocument>`, `ActiveDocument` becomes `IDocument`. Add `ActiveImageDocument` (`ImageDocument?`, null when the active tab is not raster) to keep the many raster call sites short. `CloseDocument`, `SetActiveDocument` take `IDocument`.
- [x] `IFormatManager.Open`/`OpenAsync` return `IDocument`; `Save`/`SaveAsync` take `IDocument`. `ImageFormat` gets a `DocumentKind` (or a separate `IDocumentFormat` for vector formats) so Save As only lists formats valid for the document's kind. Raster formats keep working as they are.
- [x] Desktop: `DocumentViewModel` wraps `IDocument`; `MainViewModel` uses `ActiveImageDocument` wherever it needs layers/pixels. Every raster-only command gets a `CanExecute` that is false when the active document is not an `ImageDocument` (menus, tool bar, Layers panel, effects, adjustments, Image menu, Photo menu, TV, comic page, clipboard pixel operations). Thumbnails (tabs, recent files) go through a method on `IDocument` that returns a small BGRA preview.
- [x] MCP: `McpContext.IdOf` and document lookup work on `IDocument`. Every existing tool that needs raster calls a helper `RequireImage(id)` that throws a clear `McpException` ("document N is an SVG document; this tool works on images") for other kinds. `list_documents` reports a `kind` field (`image` / `svg`); add it to the result type in `McpJson`.
- [x] Tests: all existing tests pass unchanged (except type changes in test code). Add a Core test with a fake second `IDocument` kind in the workspace: switching tabs, closing, dirty state, events carry the right document.

**Validation**
- [ ] Full test suite green on the 3 OSes in CI; zero new build warnings; `dotnet publish` with AOT reports no new trim warnings (`packaging/package.sh` on the current OS).
- [ ] Manual smoke test of the raster app: open, paint, layers, effects, undo, save, tabs, welcome screen thumbnails.

## Phase S2 — SVG model, parser and writer (`CinnabarSharp.Vector`, then Core)

All tasks of this phase go in `CinnabarSharp.Vector` with tests in `CinnabarSharp.Vector.Tests`, except the last two (`SvgDocument`, Core interop), which go in Core.

- [x] Primitive types in `CinnabarSharp.Vector` (decision 8): point, vector, rectangle (double), integer rectangle, color (straight-alpha BGRA bytes). Small `readonly record struct`s with the operations the engine needs, nothing more.
- [x] Geometry types: `Matrix2D` (affine, multiply, invert, transform point/vector, decompose for display), `PathFigure`/`PathSegment` (MoveTo, LineTo, CubicTo, QuadTo, ArcTo, Close) and `VectorPath` (list of figures, bounds, transform). Unit tests for matrix math and bounds.
- [x] Path data parser (`PathDataParser`): every command, absolute/relative, implicit repeats, number formats (`1e-3`, `.5.5`, `-1-2`, flags without separators in arcs), errors stop at the first bad token like browsers do (keep what was parsed). Writer (`PathDataWriter`) produces compact absolute data with invariant culture and at most 3 decimals unless more are needed to round-trip. Tests with `paths.svg` and a table of edge cases.
- [x] Arc to cubic Bézier conversion (SVG implementation notes F.6, out-of-range radii scaled up, zero radius = line). Tests against known values.
- [x] Transform attribute parser/writer (`translate`, `scale`, `rotate(a cx cy)`, `skewX`, `skewY`, `matrix`, lists). Tests.
- [x] Color/paint parser: named colors (full SVG list), `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`/`rgba()` with numbers and percentages, `hsl()`/`hsla()`, `none`, `currentColor`, `url(#id)` with fallback. Lengths and units (px, pt, pc, mm, cm, in, em with 16px default, %, unitless). Tests.
- [x] Node model: `SvgNode` base (id, transform, presentation attributes as a typed `SvgStyle` — fill, fill-opacity, fill-rule, stroke, stroke-width, stroke-opacity, stroke-linecap, stroke-linejoin, stroke-miterlimit, stroke-dasharray, stroke-dashoffset, opacity, display, visibility — plus `UnknownAttributes` and `UnknownChildren` kept in order). Subclasses: `SvgRoot` (width, height, viewBox, preserveAspectRatio), `SvgGroup`, `SvgPath`, `SvgRect`, `SvgCircle`, `SvgEllipse`, `SvgLine`, `SvgPolyline`, `SvgPolygon`, `SvgText` (with `SvgTextSpan` children), `SvgImage`, `SvgUse`, `SvgDefs`, `SvgLinearGradient`, `SvgRadialGradient`, `SvgStop`, `SvgClipPath`, `SvgMask`, `SvgSymbol`, and `SvgRawElement` for everything else (kept verbatim). Each node knows how to produce its `VectorPath` in its own coordinates.
- [x] Style cascade: presentation attributes < `<style>` rules (type, `.class`, `#id`, simple descendant and comma lists; anything more complex is preserved but ignored and reported in the parse result's `Warnings` list, since Vector has no logging) < inline `style` < inheritance. Keep *where* each property came from, so the writer writes an edited property back to the same place (attribute vs `style`) and does not duplicate it.
- [x] `SvgParser`: from a stream (`.svg` and gzip `.svgz`), resolves `xlink:href` and `href`, `id` index, namespaces preserved (`inkscape:`, `sodipodi:`, `sketch:`, `figma` data attributes...), XML comments and processing instructions preserved, DTD and external entities **disabled** (XXE safety: `DtdProcessing.Prohibit` or `Ignore`, no `XmlResolver`). Rejects files over a size limit with a clear error.
- [x] `SvgWriter`: writes the tree back. Untouched nodes are written from their original XML (store the source `XElement` and a dirty flag per node, write the original when not dirty). Edited nodes are written from the model, keeping unknown attributes and children. Invariant culture for numbers.
- [x] Round-trip tests (in Vector.Tests): every sample file → parse → write → parse → compare trees (element names, attributes, order, text). `unknown.svg` keeps its metadata, namespaced attributes, filter, comment and desc byte-for-byte equal after normalizing whitespace. A parse of a malicious file with `<!DOCTYPE` and an external entity does not read the entity.
- [x] **Core:** `VectorInterop` in `CinnabarSharp.Core.Vector`: conversions between Vector's primitives and Core's (`PointD`, `RectangleD`, `RectangleI`, `ColorBgra`), with tests.
- [x] **Core:** `SvgDocument` class implementing `IDocument`, wrapping the Vector model (root node):  `ImageSize` from the viewBox at 96 dpi, `ImageDocumentWorkspace`/history, `Selection` (a list of selected nodes, not a pixel mask), events through `IDocumentEventsService` with new `DocumentEventEnum` values (`VectorTreeChanged`, `VectorSelectionChanged`, `VectorNodeChanged`). Registered as transient in `AddCinnabarSharpServices`. Tests in `CinnabarSharp.Core.Tests/Vector/`: created through DI, appears in the workspace, events, dirty state.

**Validation**
- [ ] Round-trip of 10 real-world SVGs (Inkscape, Illustrator, Figma, icon sets like Material/Lucide, a Wikimedia map): the written file opens in a browser and looks identical. Put the ones whose license allows it into the samples.

## Phase S3 — Vector rasterizer (`CinnabarSharp.Vector`)

All tasks go in `CinnabarSharp.Vector` with tests in `CinnabarSharp.Vector.Tests`, except the Magick.NET image decoder, which goes in Core.

- [x] Bézier flattening (cubic and quadratic) with a tolerance in device pixels; deterministic (no `Math.FusedMultiplyAdd` differences across OSes: write the math explicitly, use `double`).
- [x] Scanline polygon filler with analytic or 4×4/16× supersampled antialiasing (pick one and document it), nonzero and evenodd fill rules, output a coverage buffer clipped to a region. New `PathCoverage` class (Core's `CoverageMask` is not visible from Vector; take ideas from it, do not reference it).
- [x] Stroker: converts a path + stroke style into a fill path. Widths (including hairline < 1px), caps (butt, round, square), joins (miter with limit, round, bevel), dashes with offset, closed vs open figures, degenerate segments (zero length, round cap draws a dot).
- [x] Paints: solid color with opacity; linear and radial gradients (objectBoundingBox and userSpaceOnUse, gradientTransform, spread pad/reflect/repeat, focal point fx/fy, stop offsets/colors/opacity, href inheritance). Interpolate in sRGB, straight alpha, then premultiply for compositing; output straight-alpha BGRA like the rest of Core.
- [x] Compositing of the tree: groups with opacity render to an offscreen buffer, `display:none` / `visibility:hidden`, `clipPath` (as a coverage mask, clipPathUnits both kinds), `mask` (luminance), `<use>`/`<symbol>` instancing with viewBox, `preserveAspectRatio` on the root and on `<image>`/`<symbol>`.
- [x] `<image>`: Vector defines `IImageDecoder` (bytes → straight-alpha BGRA + size, or null) and resolves the data itself: embedded base64, and linked files relative to the SVG file, only inside the SVG's folder or below, never URLs (the caller passes the base folder, or none to refuse all links). Drawn with transform and bilinear sampling. Missing image, refused link or no decoder → placeholder (grey box with a cross). Tests use a fake decoder.
- [x] **Core:** `MagickImageDecoder` implements `IImageDecoder` with Magick.NET (PNG, JPEG, WebP, GIF first frame); tests with a real embedded PNG.
- [x] Text through `IGlyphOutlineProvider` (Vector interface: `VectorPath Outline(string text, TextStyle style, out double advance)`; font family, size, weight, style, `text-anchor`, `x`/`y`/`dx`/`dy` per span). Fake provider in tests; Desktop implementation in Phase S5. No provider → placeholder rectangle with the text's estimated box.
- [x] `VectorRasterizer.Render(root node, integer region, double scale, Span<byte> bgra, RenderOptions)` (options: image decoder, glyph provider, base folder, cancellation) renders any rectangle of the image at any zoom (for the canvas: only the visible region at the current zoom). Unknown and unsupported elements (filters...) render nothing, without exception.
- [x] Checksum tests: render each sample at 1× and 2.5× and pin checksums (new test class `VectorRasterizerTests`, same pattern as `BlendOpsTests`). Visual check by writing PNGs to the test output folder. A cancellation and a "region equals crop of full render" test.
- [x] Performance budget test (not a checksum): rendering `paths.svg` at 1920×1080 stays under a time limit generous enough for CI (e.g. 500 ms), to catch accidental quadratic algorithms.

**Validation**
- [ ] Rendering of the samples and the 10 real files compared side by side with a browser screenshot: shapes, strokes, gradients, clips and text placement match closely (a few pixels of antialiasing difference are fine).
- [ ] Same checksums in CI on the 3 OSes.

## Phase S4 — Open, save, export

- [x] `SvgFormat` (vector, `DocumentKind.Svg`): `.svg` and `.svgz` open as an `SvgDocument`; save writes SVG (gzip for `.svgz`); `MatchesContent` sniffs `<svg` after an optional XML declaration/comments. It replaces the read-only `SvgFormat` raster registration added before this roadmap for File › Open. Layers › Import From File on a raster document keeps rasterizing the SVG as a layer (`Utility.OpenVector`, unchanged).
- [x] "Open as Image…" (File menu) opens an SVG rasterized as an `ImageDocument`, with the existing path, so users can still paint on a rasterized SVG.
- [x] Export of an `SvgDocument` to PNG/JPEG/WebP/BMP/TIFF/ORA (single layer): rasterized with `VectorRasterizer` at a chosen scale (dialog: scale or width/height with ratio lock, background transparent/white). Save As lists SVG first, then the raster formats as exports (the document stays an SVG document, keeps its SVG file, and `IsDirty` is unchanged after an export).
- [x] File › New offers **Image** or **SVG drawing** (width, height, units px/mm/in; creates root with viewBox and an empty layer group `<g id="layer1">` like Inkscape).
- [x] Closing an SVG document with changes prompts like raster documents; recent files and welcome screen thumbnails work for SVGs (render via `VectorRasterizer`).
- [x] Image › Rasterize (on an SVG document): opens a **new** raster `ImageDocument` from the render at the chosen scale; the SVG document stays open.
- [x] Tests: open/save round-trip through `IFormatManager` (extension and content sniffing), `.svgz`, dirty state after save and after export, Save As format list per kind, recent files entry. UI test: open `shapes.svg`, the canvas shows the shapes (pixel checks with per-channel distinct colors), the tab title, Save writes the file.

**Validation**
- [ ] Open, save, reopen the real-world SVGs on each OS; the files still open in a browser; `.svgz` works.

## Phase S5 — Canvas and panels for SVG documents (Desktop)

- [ ] `CanvasView` draws an `SvgDocument` by asking `VectorRasterizer` for the visible region at the current zoom (re-render on zoom so edges stay sharp, not a scaled bitmap). Region invalidation from `VectorNodeChanged` (node bounds before ∪ after) to avoid full redraws while dragging.
- [ ] Desktop `AvaloniaGlyphOutlineProvider` implements `IGlyphOutlineProvider` (registered in `AppServices`), so text renders in the app and in attached MCP mode.
- [ ] Objects panel (replaces Layers for SVG tabs): tree of nodes (groups expandable; label = `inkscape:label` or `id` or element name), visibility toggle (`display`), lock toggle (`sodipodi:insensitive`), selection synced both ways with the canvas, drag to reorder and to move into/out of groups (through actions, so undoable), rename (id/label).
- [ ] Properties panel for the selection: fill and stroke (none / flat color / linear / radial gradient, opacity), stroke width, caps, joins, dashes, object opacity; geometry fields X, Y, W, H (of the bounding box, in document units) and rotation. Multiple selection edits all. Each change is one history step (slider drags coalesce into one step, like effect dialogs).
- [ ] Gradient editor in the properties panel: stops list, add/remove/move stop, color and opacity per stop; on-canvas gradient handles shown by the Gradient tool (Phase S7).
- [ ] Primary/secondary colors of the palette set fill/stroke of the selection (click = fill, shift-click = stroke, like Inkscape; the palette's existing behavior is unchanged on raster documents).
- [ ] Menus and tool bar switch on the document kind: raster-only items disabled (done in S1), vector items enabled only on SVG documents. Tool bar shows the vector tools on SVG tabs and the raster tools on image tabs; the selected tool of each kind is remembered.
- [ ] Optional (last task of the phase): read-only XML view of the selected node (for debugging and power users).
- [ ] UI tests: the panels show the tree of `shapes.svg`; selecting in the panel draws selection handles; changing fill color in the properties panel changes the canvas pixels and undo restores them. Screenshots for each OS in CI.

**Validation**
- [ ] On each OS: zoom from 10 % to 3200 % on a detailed SVG stays sharp and responsive; HiDPI; panels usable at 200 % scale; keyboard reach (Phase 12 rules).

## Phase S6 — Vector actions and history (Core)

- [ ] `SvgActions` (the vector counterpart of `DocumentActions`, owned by `SvgDocument.Actions`), the only way the UI and MCP edit an SVG document. Each action changes the model and pushes one `IHistoryItem`.
- [ ] History items: `VectorNodeChangeItem` (before/after clones of one or more nodes, by stable internal id — not the XML `id`, which can be missing or duplicated), `VectorInsertItem`, `VectorDeleteItem`, `VectorReorderItem` (parent + index before/after), `VectorSelectionItem` for selection changes that should be undoable (only at tool pointer-up, like raster selection). Items mark the touched nodes dirty for the writer.
- [ ] Actions: add node, delete selection, duplicate (Ctrl+D, new ids), move by delta, set transform, set geometry (x/y/w/h), set style property (one or many nodes), rename id/label, reorder (raise, lower, to top, to bottom), group (Ctrl+G), ungroup (Ctrl+Shift+G, pushes the group's transform and style into children), move into group, toggle visibility, toggle lock, convert object to path, edit path data (whole path replaced, for the node tool).
- [ ] Transforms keep the element's natural form when possible: moving a `rect` changes `x`/`y` if it has no rotation, else edits `transform`; scaling a `circle` without rotation changes `r` only when uniform. Document the rule in the class comment.
- [ ] ids: new nodes get unique ids (`path123` style); duplicate ids in an opened file are kept as they are but references (`url(#...)`, `href`) resolve to the first one, like browsers.
- [ ] Clipboard: copy/cut/paste of SVG nodes as `image/svg+xml` text through `IClipboardService` (add a text/SVG method if needed), plus a PNG render for other apps. Pasting SVG text from another app (Inkscape, Figma, browsers) inserts its nodes; pasting a bitmap inserts an `<image>` with base64 data.
- [ ] Tests: add every new action to a `VectorHistoryTests.Actions` list mirroring `HistoryTests.Actions`: do, undo (tree, styles, ids, selection and dirty state restored exactly), redo, and the written XML after undo equals the written XML before the action. Clipboard round-trip with `FakeClipboardService`.

**Validation**
- [ ] On each OS: long editing session (50+ actions), undo all, redo all, save: the file is correct; `IsDirty` returns to clean after undoing to the save point.

## Phase S7 — Vector tools (Core + Desktop)

Tools implement a new `IVectorTool` interface in Core (same shape as `ITool` but taking `SvgDocument` and user-space `PointD`, plus `IOverlayTool`-style `GetOverlay`/`CursorAt` and the keyboard interfaces). Overlays reuse `ToolOverlay` (add fields if needed, e.g. Bézier curves for paths, rotated frames). Every tool records one history step per gesture.

- [ ] **Select tool (S / F1):** click selects the topmost object (respecting lock and visibility; Alt+click selects below; Ctrl+click enters groups), Shift+click toggles, rubber-band selection, drag moves, arrow keys move by 1px (Shift = 10px), 8 resize handles (Shift keeps ratio, Alt from center), click again on a selection to show rotate/skew handles with movable rotation center, Delete removes, Escape deselects. Snapping to other objects' bounding boxes and to the page (toggle in options bar).
- [ ] **Node tool (N / F2):** shows the path's nodes and Bézier handles; click/rubber-band selects nodes; drag nodes and handles; double-click on a segment adds a node; Delete removes nodes (keeps shape as close as possible); node types (corner, smooth, symmetric) from options bar buttons; convert segment to line/curve; join/break nodes. On a shape that is not a path (rect, ellipse...) the tool shows that shape's own handles (rect corner radius, ellipse radii) and offers "Convert to path".
- [ ] **Pen tool (B):** click adds corner nodes, drag creates smooth nodes with handles, click on the first node closes, Enter/double-click finishes open, Escape cancels, Backspace removes the last node; Shift snaps angles to 15°. Editable until finished (like the raster Line/Curve tool, `IEditingTool` pattern).
- [ ] **Pencil tool (P):** freehand path simplified into Béziers (Ramer–Douglas–Peucker then curve fitting, Schneider's algorithm), smoothing option.
- [ ] **Rectangle (R), Ellipse (E), Line (L), Polygon/Star (*):** drag to create (Shift = square/circle/15°, Alt = from center); options bar: corner radius (rect), number of corners, star ratio, rounding (polygon/star). Created with the current fill (secondary color) and stroke (primary color, brush width) settings, like Paint.NET's shape tools; option "fill/stroke/both".
- [ ] **Text tool (T):** click creates `<text>`, type with caret, selection and clipboard (reuse `TextBlock`/`TextEngine` logic where possible); font family, size, bold, italic, alignment (`text-anchor`) in the options bar; click on existing text edits it. Implements `ITextEditingTool` so Edit menu commands go to the text.
- [ ] **Gradient tool (G):** on the selection, drag creates a linear (or radial, option) gradient for fill (or stroke, option); handles for start/end/radius/focus and stops on the canvas; editing stops syncs with the properties panel.
- [ ] **Eyedropper (K):** click an object to take its fill and stroke; Shift picks the rendered pixel color.
- [ ] **Zoom and Pan** work as on raster documents (shared, not reimplemented).
- [ ] Desktop wiring: tool view models and icons for the vector tools, options bar per tool, shortcuts (single letters disabled while typing), cursors from `CursorAt`.
- [ ] Tests (Core, per tool): pointer sequences produce the expected nodes/attributes and exactly one history step; undo restores; the overlay contains the expected handles. Headless UI tests for select/move/resize and for drawing a rectangle and a pen path, with pixel checks and screenshots.

**Validation**
- [ ] On each OS with a real mouse/trackpad: draw a simple logo from scratch (shapes, pen paths, text, gradient), edit nodes, group, save, open in a browser. Modifier keys behave the same on each OS (⌘ on macOS).

## Phase S8 — Object operations (Core + Desktop)

- [ ] **Object menu** (only on SVG tabs): Group, Ungroup, Raise, Lower, Raise to Top, Lower to Bottom, Flip Horizontal/Vertical, Rotate 90° CW/CCW, Align and Distribute (left, centers, right, top, middles, bottom; relative to first/last selected, biggest, page, selection; distribute centers and gaps) through a small dialog or panel.
- [ ] **Path menu:** Object to Path, Stroke to Path (uses the stroker from S3), Union, Difference, Intersection, Exclusion, Division, Combine, Break Apart, Simplify, Reverse.
- [ ] Path boolean operations in `CinnabarSharp.Vector` (`PathBoolean`, tests in Vector.Tests): robust polygon clipping on flattened curves (Vatti or Martinez–Rueda; port of a permissively licensed algorithm only — check the license, add it to `THIRD-PARTY-NOTICES.txt`), then curve fitting back to Béziers within a tolerance. Deterministic; tests with known shapes (two overlapping squares, circle minus square, self-intersecting star, holes), checksum of the rendered result.
- [ ] Every operation is an `SvgActions` method with history, added to `VectorHistoryTests.Actions`.

**Validation**
- [ ] On each OS: boolean ops on overlapping shapes, text converted to path, then union; result renders correctly in a browser.

## Phase S9 — Bitmaps in SVG documents (ADR need 4, option B)

- [ ] File › Import (on an SVG tab) places a raster image as `<image>`: embedded base64 PNG/JPEG by default, or linked (option, relative path, file must be in the SVG's folder or below). Size at 96 dpi, fitted into the page if larger.
- [ ] `<image>` objects can be moved, scaled, rotated, clipped (Object › Clip › Set Clip with the shape above it; Release Clip), and have opacity, like other objects.
- [ ] "Edit Bitmap" on an `<image>`: opens its pixels in a new raster `ImageDocument` tab; when that tab is saved (or with an explicit "Update in SVG" command), the SVG's `<image>` is updated (one history step in the SVG document). Closing without saving leaves the SVG unchanged.
- [ ] Paste a bitmap from the clipboard into an SVG document inserts an embedded `<image>`.
- [ ] Image › Trace Bitmap on a selected `<image>`: uses the vectorizer from ADR need 2 (Potrace port) if it exists in Core; otherwise leave this task unticked with a note.
- [ ] Tests: import, embed/link round-trip, Edit Bitmap round-trip updates the base64 and is undoable, linked path restricted to the folder.

**Validation**
- [ ] On each OS: a photo with an SVG logo and text on top, built as an SVG document, saved, opened in a browser.

## Phase S10 — MCP and scripting

- [ ] Tools in `ImageTools` (or a new `VectorTools` class registered the same way), all through `McpContext.Run` and `SvgActions`, types in `McpJson`: `new_svg` (width, height, units), `svg_tree` (nodes with internal id, element, XML id, label, bounds, style summary), `svg_get_node` (attributes and style), `svg_add_shape` (rect, ellipse, line, polygon, star with style), `svg_add_path` (path data + style), `svg_add_text` (attached mode only, like `add_speech_bubble`, unless the placeholder is acceptable), `svg_add_image` (file in allowed folders), `svg_set_style`, `svg_set_attributes` (whitelisted geometry attributes only), `svg_transform` (move, scale, rotate around a point), `svg_delete`, `svg_duplicate`, `svg_group`, `svg_ungroup`, `svg_reorder`, `svg_align`, `svg_path_operation` (union, difference, intersection, exclusion, stroke_to_path, object_to_path), `svg_select`. Existing tools: `open`, `save`, `export`, `close`, `list_documents`, `preview` (rendered PNG), `history`, `undo`, `redo` work on SVG documents.
- [ ] Paths go through `FileAccessPolicy` (including linked images inside an SVG being opened: unresolvable or disallowed links render as placeholders, never read).
- [ ] `docs/mcp.md`: SVG section with the tools and an example (draw a badge, export PNG). `docs/automation.md`: an SVG script example.
- [ ] `packaging/smoke-test.txt`: call every new tool (the `Mcp.Tests` check fails otherwise).
- [ ] Tests in `CinnabarSharp.Mcp.Tests`: end-to-end new SVG → shapes → boolean union → save SVG → export PNG, checked by reopening the SVG and by pixels of the PNG; refusing raster tools on SVG documents and vector tools on image documents with clear messages; `AotJsonTests` green.

**Validation**
- [ ] Claude Code draws a simple logo through MCP on each OS and the SVG opens in a browser; attached mode shows edits live and undo works.

## Phase S11 — Polish and release

- [ ] Translations of every new user-visible string (`Translations.GetString`), in all existing languages.
- [ ] Accessibility of the new panels and tools (keyboard reach, focus ring, contrast) per the Phase 12 checklist; usable at 200 % scale.
- [ ] Performance: an SVG with 10 000 paths (e.g. a detailed map) opens in under 2 s and pans/zooms without freezing (render on a background thread with cancellation like `RefreshTvPreviewAsync`, show the last frame meanwhile).
- [ ] Unsaved-work recovery and the history size setting (if they exist by then for raster documents) also cover SVG documents.
- [ ] File associations for `.svg` in the packages (optional, do not make the app the default SVG handler).
- [ ] Docs: user guide section for SVG editing (README feature list, CHANGELOG under Unreleased, tasks.md gets a "Phase 15 — SVG editor" entry pointing to this file). Update `CLAUDE.md`: add `CinnabarSharp.Vector` and `CinnabarSharp.Vector.Tests` to Projects (BCL only, enforced by `VectorArchitectureTests`), and add a short **SVG documents** paragraph to Architecture (Vector engine vs Core document split, parser/writer round-trip rule, `VectorRasterizer` determinism, `SvgActions` as the only edit path, `IVectorTool`).
- [ ] Update `docs/adr/0001-svg-editing.md`: status **Accepted**, Decision section records option 3 (needs 1, 2, 3 with option B) and links to this file.

**Validation**
- [ ] Full checklist on a real macOS, Linux (X11 and Wayland) and Windows 11 machine: open the real-world sample set, edit with each tool, boolean ops, bitmaps, save, reopen in a browser, MCP session, packaged AOT build.
