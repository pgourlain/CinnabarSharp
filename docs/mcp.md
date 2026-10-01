# Driving CinnabarSharp from AI agents (MCP)

CinnabarSharp includes a [Model Context Protocol](https://modelcontextprotocol.io) server. With it, Claude Code, Claude Desktop or any other MCP client can open, edit and save images using CinnabarSharp's tools. For example, you can ask the agent to *"make this photo 4K for my TV"* or *"apply sepia to every photo in this folder"*.

The server is part of the app. There is nothing else to install: the same `CinnabarSharp` executable starts the server when you pass `--mcp`.

## Two modes

| Mode | Command | What happens |
|---|---|---|
| **Headless** | `CinnabarSharp --mcp` | No window. The agent opens, edits and saves files. Suited to batch work. |
| **Attached** | `CinnabarSharp --mcp --attach` | The agent works on the images open in the running app, and you watch the changes live. Each change is one step in the History panel, so you can undo it with ⌘Z / Ctrl+Z. |

Attached mode only works while the app is running with **File › Allow AI Agents (MCP)** turned on. This setting is off by default, and the app remembers it between sessions. Only one CinnabarSharp window can accept agents at a time.

When you turn the option on, the app shows a **Connect an AI Agent** window. It has the exact `claude mcp add` command and the Claude Desktop configuration for this installation, each with a Copy button, and the list of allowed folders. The status bar then shows whether an agent is connected; click it, or use File › Connect an AI Agent…, to see the window again.

## Setup

Where the executable is:

| OS | Path |
|---|---|
| macOS | `/Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp` |
| Windows | `C:\path\to\CinnabarSharp\CinnabarSharp.exe` |
| Linux | `/path/to/CinnabarSharp/CinnabarSharp` |
| From source | `dotnet /path/to/CinnabarSharp.Desktop/bin/Debug/net10.0/CinnabarSharp.dll` (run `dotnet build` first; don't use `dotnet run`, because its build output would corrupt the protocol on stdout) |

### Claude Code

```bash
# Headless: the agent can use files under ~/Pictures
claude mcp add cinnabarsharp -- /Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp --mcp --allow ~/Pictures

# Attached: drive the running app
claude mcp add cinnabarsharp-live -- /Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp --mcp --attach
```

If you give no `--allow` option, the headless server allows the folder Claude Code runs in (the project folder).

### Claude Desktop

Add the server to `claude_desktop_config.json` (Settings › Developer › Edit Config):

```json
{
  "mcpServers": {
    "cinnabarsharp": {
      "command": "/Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp",
      "args": ["--mcp", "--allow", "/Users/me/Pictures"]
    }
  }
}
```

## Safety

- **Allowed folders.** The agent can only read and write files inside the allowed folders. Symbolic links are resolved before the check, so a link can't lead outside these folders.
  - Headless mode: the folders given with `--allow <folder>` (repeatable) and in the `CINNABARSHARP_MCP_ALLOW` environment variable (separated like `PATH`). Without either, the current directory.
  - Attached mode: your Pictures, Documents, Desktop and Downloads folders.
- **Destructive operations need an explicit parameter.** Saving over an existing file (even the image's own file) needs `overwrite: true`. Closing an image with unsaved changes needs `discardChanges: true`. Batch export into a non-empty output folder needs `overwrite: true`.
- **Everything is undoable.** Each edit goes through the same document actions as the menus. It is one history step, and the image is marked as modified.
- In attached mode, the app listens on a Unix domain socket in your app-data folder (`mcp.sock`). On macOS and Linux, only your user account can read or write it.

## Tools

| Area | Tools |
|---|---|
| Files | `open_image`, `new_image`, `save_image` (format, JPEG quality), `export_image` (a copy that doesn't change the image's own file), `close_image` |
| Inspect | `list_documents`, `get_image_info` (size, layers, selection, optional histogram), `render_preview` (downscaled PNG the agent can look at), `get_history` |
| History | `undo`, `redo` |
| Layers | `add_layer`, `import_layer`, `delete_layer`, `duplicate_layer`, `merge_layer_down`, `flatten`, `move_layer`, `select_layer`, `set_layer_properties` (name, visibility, opacity, blend mode), `flip_layer` |
| Selection | `select_rectangle`, `select_ellipse`, `magic_wand`, `select_all`, `deselect`, `invert_selection`, `fill_selection`, `erase_selection` |
| Adjustments and effects | `list_effects`, `apply_effect` (any adjustment, effect or Photo tool, with named parameters), `suggest_effect_values` (the dialog's Auto values) |
| Image | `resize_image`, `resize_canvas`, `crop` (rectangle, aspect ratio such as `16:9`, or the selection), `rotate_image`, `flip_image` |
| TV | `prepare_for_tv` (2K/4K/8K; crop to fill, fit with plain or blurred borders, or stretch), `prepare_folder_for_tv` |
| Annotations | `add_speech_bubble` (comic bubble with text pointing at a spot: square, rounded, oval or thought; colors, font size, fixed width, numbered badge; on a "Bubbles" layer by default). Attached mode only: it needs the app's fonts. |
| Comics | `compose_comic_page` (open images into a comic page: layout, A4/square/16:9, gutter, borders, white or black page; each image framed on its selection or its center, or stretched whole to its panel with `stretch`). The Cartoon effect is available through `apply_effect`. |

Resources: `cinnabar://documents`, `cinnabar://effects` (every effect with its parameter ranges, defaults and choices), and `cinnabar://documents/{id}/history`.

Images are identified by the `id` that `open_image`, `new_image` and `list_documents` return. When you give no id, the tool uses the active image. Layers are numbered from 0, the bottom layer.

## Examples

> Make a comic page with the four photos I have open, in cartoon style.

For each image, the agent calls `apply_effect {"effect": "Cartoon", "document": "<id>"}`, then `compose_comic_page {"layout": "2x2 grid"}` → `render_preview` → `save_image {"path": "comic.png"}`.

> Open ~/Pictures/beach.jpg, enhance it, crop it to 16:9 and save it as a 4K JPEG for my TV.

The agent calls `open_image` → `apply_effect {"effect": "Auto-Enhance"}` → `crop {"ratio": "16:9"}` → `prepare_for_tv {"resolution": "4K"}` → `render_preview` to check the result → `save_image {"path": "beach_4K.jpg", "jpegQuality": 90}`.

> Apply sepia to every photo in ~/Pictures/Trip and save the copies in a "sepia" subfolder.

For each file, the agent calls `open_image` → `apply_effect {"effect": "Sepia"}` → `export_image {"path": "sepia/<name>.jpg"}` → `close_image {"discardChanges": true}`.

> Make every photo in ~/Pictures/Frame ready for The Frame, with blurred borders for portraits.

The agent calls `prepare_folder_for_tv {"folder": "~/Pictures/Frame", "fit": "FitWithBorders", "background": "Blurred"}` once.
