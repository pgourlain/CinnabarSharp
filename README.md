# CinnabarSharp

A cross-platform image editor inspired by [Paint.NET](https://www.getpaint.net/), written in C# with .NET 10 and [Avalonia](https://avaloniaui.net/). It runs on Windows, macOS and Linux with the same rendering on all three.

> **Status: early development.** Documents, layers and file formats work; painting tools, undo/redo, adjustments and effects are next. See the [roadmap](tasks.md).

![CinnabarSharp main window](docs/screenshot.png)

## Features

- **Multiple images in tabs**, with unsaved-changes prompts on close and quit.
- **Layers**: add, delete, duplicate, reorder, merge down, flatten, import from file, flip; per-layer visibility, opacity and blend mode (Paint.NET's 14 modes plus Hard Light and Soft Light), with live preview in Layer Properties.
- **File formats**: PNG, JPEG, BMP, GIF, TIFF, WebP, and OpenRaster (`.ora`) to keep layers. Files are recognised by content when the extension is missing or wrong.
- **Zoom and pan** like Paint.NET: zoom presets, best fit, Ctrl/⌘ + wheel and trackpad pinch around the mouse, pan with Space + drag, middle mouse or the Pan tool.
- **Native feel on each OS**: macOS menu bar and ⌘ shortcuts, in-window menu and Ctrl shortcuts on Windows and Linux, native file dialogs, drag and drop, recent files.

## Build and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project CinnabarSharp.Desktop            # start the app
dotnet run --project CinnabarSharp.Desktop -- a.png   # open files at startup
dotnet test CinnabarSharp.slnx                        # run all tests
```

## Project layout

| Project | Contents |
|---|---|
| `CinnabarSharp.Core` | Document model, layers, compositing, file formats. No UI code; builds and tests without any UI framework. |
| `CinnabarSharp.Desktop` | Avalonia desktop app (MVVM). |
| `CinnabarSharp.Core.Tests` | Unit tests, including a checksum test that keeps blend modes bit-identical on every OS. |
| `CinnabarSharp.Desktop.Tests` | Headless UI tests that drive the real window and save screenshots. |

CI builds and tests on Windows, macOS and Linux, and publishes the UI screenshots of each OS as artifacts so they can be compared.

## Credits and license

MIT — see [LICENSE](LICENSE).

Parts of the code are ported from [Pinta](https://github.com/PintaProject/Pinta) (MIT), whose original copyright headers are kept in those files. Image decoding and encoding use [Magick.NET](https://github.com/dlemstra/Magick.NET).
