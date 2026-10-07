# ADR 0001: Editing SVG files, alone or mixed with bitmaps

- **Status:** Accepted (option 2; implemented, see [tasks-svg.md](../../tasks-svg.md))
- **Date:** 2026-10-06
- **Related:** GitHub issue #2 ("Svg support ?")

## Context

CinnabarSharp is a Paint.NET-style raster editor. Every layer is a pixel buffer, painting and effects work on pixels, compositing is our own `BlendOps` (bit-identical on every OS) and history stores pixel rectangles.

Users also have `.svg` files they want to **edit**, not only look at. Four needs, from small to large:

1. **Open** an SVG as an image (rasterized).
2. **Vectorize** a bitmap into an SVG (export).
3. **Edit** an SVG and save it back as SVG, keeping it vector.
4. **Mix** vector objects and bitmaps in one document (SVG over a photo, a photo inside an SVG).

Constraints from the project:

- `CinnabarSharp.Core` stays non-visual: no Skia, no `System.Drawing`, no UI types. It must build and test on macOS, Linux and Windows.
- Rendering must be deterministic across OSes (checksum-pinned tests).
- Releases are Native AOT: no reflection-based JSON, no `Assembly.Location`, zero trim warnings.
- Every edit goes through one actions class and one history; the MCP server and the `--run` scripts must keep working on the same code.
- Scope: Paint.NET is the reference. An Inkscape clone is out of scope.

## What each need requires

### 1. Open SVG as image

- Register a read-only `MagickImageFormat` for `.svg`/`.svgz` (`MagickFormat.Svg`, `Msvg`). Magick.NET renders with its internal MSVG renderer.
- Limits: weak on filters, CSS, fonts, `<use>`/`<symbol>`; output may differ from browsers. Size comes from `width`/`height`/`viewBox` at 96 dpi, so small SVGs open small.
- Add a size/scale choice at import (rasterize at the right density instead of upscaling).
- **Cost:** about a day. No change to the document model.

### 2. Vectorize a bitmap

- Port Potrace to C# in Core (black and white, Bézier curves), then build color on top by quantizing to N colors and tracing each mask, stacked from the background up.
- `VectorPath` + `SvgWriter` in pure C#.
- Menu action with a preview dialog (mode, colors, threshold, speckle size, corner smoothness). It is an **export**, not a document edit, so no history item.
- MCP tool `vectorize_to_svg` (types in `McpJson`), docs, smoke test.
- **Cost:** 1-2 days for black and white, 2-3 more for color and dialog. Good for logos, line art, scans; poor for photos.

### 3. Edit an SVG and keep it vector

A new document kind. This is the core of the decision.

**Model (Core)**

- `SvgDocument`: tree of nodes (`Group`, `Path`, `Rect`, `Ellipse`, `Line`, `Polyline`, `Text`, `Image`), each with transform, fill, stroke, opacity, id; plus `<defs>` (gradients, clip paths, masks).
- Parser and writer that **preserve what we do not understand** (unknown elements, attributes, `<style>`, metadata). Editing a file must not destroy it. This is the biggest risk for fidelity: either keep the original XML and patch it, or model the full tree and round-trip it.
- Path data parsing (all commands, arcs to Béziers), transforms, units, `viewBox`.

**Rendering (Core)**

- A path rasterizer in pure C# for determinism: Bézier flattening, fill rules (nonzero, evenodd), strokes (width, caps, joins, miter limit, dashes), gradients, clipping, opacity and groups, antialiasing. `CoverageMask` is a starting point but only does simple shapes today.
- Text: glyph outlines are a platform concern. Core would need an `IGlyphOutlineProvider` interface (like `ITextRasterizer`), implemented in Desktop, so that text becomes paths for rendering. Until then, text renders only through the UI implementation, or is kept as `<text>` and shown as a placeholder.
- Filters (blur, drop shadow) are not planned for the first version.

**Editing (Core + UI)**

- `SvgActions` (the vector counterpart of `DocumentActions`) plus new `IHistoryItem`s: add, delete, move, transform, restyle, reorder, group/ungroup, edit nodes. The existing history engine (pointer, `IsDirty`, `SetClean`) is reusable.
- Tools implemented against an `IVectorTool` interface: select/move/resize/rotate objects, node editor, pen (Bézier), rectangle, ellipse, line, polygon/star, text. Handles and guides drawn through `ToolOverlay`.
- Operations: align/distribute, group, z-order, path boolean ops (union, difference, intersection), convert shape to path, stroke to path.
- Panels: object tree (replaces Layers for this kind), a style/properties panel (fill, stroke, gradient stops), optionally an XML view.

**Application**

- Mode is a property of the **document** (tab), not of the app. `WorkspaceManager` handles an `IDocument` (`ImageDocument` or `SvgDocument`).
- The view model, menus, tool bar, options bar and panels switch on the active document kind. Many commands today assume `ImageDocument`; each needs a `CanExecute` rule. Effects and adjustments are disabled for SVG (or apply after "Rasterize").
- Open: `.svg` opens as `SvgDocument`; "Open as image" keeps path 1. New: File › New offers Image or SVG.
- MCP: tools take a document id, so each tool must support or refuse each kind. New vector tools, `McpJson` types, `docs/mcp.md`, `packaging/smoke-test.txt`.
- Events: `ImageDocumentEventTests` pins event order; the SVG kind needs its own events.

**Cost:** MVP (basic shapes, paths, groups, solid colors and linear/radial gradients, transforms, undo, load/save with preservation of unknown content) is roughly 2-3 months of work. Complete Inkscape-level editing is not realistic.

### 4. Mix vector and bitmap

Three ways, with different trade-offs.

| Option | Description | Fidelity of the saved file | Impact |
|---|---|---|---|
| **A. Vector layer in `ImageDocument`** | A layer kind holding an object tree plus a cached raster. Pixel tools and effects disabled on it until "Rasterize layer". | Saved as ORA/PNG with the vector layer rasterized, or as SVG with bitmaps embedded. | Layer model, compositing, history, ORA format, tools: medium-high |
| **B. Bitmap objects in `SvgDocument`** | The SVG kind holds `<image>` elements (embedded base64 or linked). Bitmaps are objects: move, scale, crop, clip. Pixel editing happens by opening the image in a separate pixel tab and sending it back. | Exact SVG. | Smallest model change for SVG, but no pixel editing inside the SVG |
| **C. Non-editable SVG layer in `ImageDocument`** | Import SVG as a layer that keeps its source and re-rasterizes losslessly at any size (resize, zoom). No object editing. | Rasterized on save. | Low (about a week) |

A is the closest to "SVG over a photo" while staying a Paint.NET-like editor, but it makes one document speak two languages and the saved SVG can never be fully faithful. B keeps the SVG honest and is a natural extension of need 3. C covers the most common case ("put a logo SVG on a photo") cheaply.

## Options

1. **Needs 1, 2 and option C of need 4.** Re-rasterizable SVG layers. No vector editing.
2. **Needs 1, 2 and 3 with option B** (document-per-kind). A real but bounded SVG editor, alongside the pixel editor.
3. **Needs 1, 2, 3 and option A.** Vector layers inside raster documents as well.

## Decision

**Option 2:** needs 1, 2 and 3 with option B (document-per-kind), plus bitmaps inside SVG documents (need 4, option B): an SVG file opens as an SVG document with vector tools, object and path operations, and is saved back as SVG; a bitmap can be placed in the drawing as an `<image>` and edited in a raster tab (Edit Bitmap). Vector layers inside raster documents (option A) are not built.

The implementation plan, phase by phase and with a validation checklist per OS, is [tasks-svg.md](../../tasks-svg.md). What was built: `CinnabarSharp.Vector` (BCL only: parser/writer that round-trips the file, style cascade, path geometry, a deterministic pure-C# rasterizer, path booleans), the `IDocument` abstraction with `SvgDocument` in Core, `SvgActions` as the only edit path, vector tools, the Objects and Properties panels, and the `svg_` MCP tools.

Vectorizing a bitmap (need 2) and the re-rasterizable SVG layer for raster documents (option C) are still open; Trace Bitmap waits for a vectorizer.

Original recommendation, kept for the record:

1. Step 1: open SVG (need 1).
2. Step 2: vectorize to SVG (need 2). Validates the pure-C# path and SVG writer that need 3 reuses.
3. Step 3: re-rasterizable SVG layer (option C). Covers most "SVG plus photo" cases at low cost.
4. Step 4, only if SVG editing is still wanted after using steps 1-3: document-per-kind with option B, as a dedicated phase in [tasks.md](../../tasks.md) with a per-OS validation checklist and an explicit MVP list.

## Consequences

- Steps 1-3 leave the document model, history and compositing untouched, and keep the Paint.NET identity.
- Step 4 changes the product's identity ("Paint.NET for every OS" becomes "raster and vector editor"). It needs an explicit product decision, an `IDocument` abstraction and a lot of view-model work.
- The pure-C# path rasterizer and SVG parser/writer are shared by steps 2, 3 and 4. Building them early reduces risk for the larger option.
- Native AOT and the three-OS CI must be checked at each step (MSVG inclusion in the native build, XML handling without reflection).

## Licenses of the candidate dependencies

Checked on the NuGet API (`licenseExpression`) on 2026-10-06:

| Package | Version | License | Note |
|---|---|---|---|
| `Avalonia.Skia` | 12.1.3 | MIT | Already used by the app |
| `SkiaSharp` | 4.155.0-preview.2 | MIT | .NET binding |
| `SkiaSharp.NativeAssets.{macOS,Linux,Win32}` | 4.155.0-preview.2 | MIT | Package license; the bundled native Skia is BSD-3-Clause upstream (not shown in NuGet metadata) |
| `Svg.Skia` | 5.2.3 | MIT | SVG renderer on Skia |
| `ShimSkiaSharp` | 5.2.3 | MIT | Dependency of Svg.Skia |
| `Svg.Custom` | 5.2.3 | **MS-PL** | Dependency of Svg.Skia; permissive but not MIT: keep its notice |

All are permissive and compatible with commercial redistribution; the third-party notices shipped with the app must include them (MS-PL for `Svg.Custom`, plus the notices of the native libraries bundled with Skia). Skia itself is not allowed in `CinnabarSharp.Core` for architecture reasons, not license reasons.

## Open questions

- Which SVGs matter most (icons and logos, illustrations, files exported from Inkscape/Illustrator/Figma)? That sets the minimum feature set and the fidelity needed.
- Preserve the original XML and patch it, or model everything and rewrite? Patching is safer for unknown content; a full model is simpler to edit.
- Is Svg.Skia acceptable in Desktop (behind an interface) for display only, while Core keeps its own deterministic renderer for export and tests?
- Embedded fonts and text-to-path: who owns glyph outlines?
- Do we want path boolean operations in the MVP? They are the heaviest single algorithm.
