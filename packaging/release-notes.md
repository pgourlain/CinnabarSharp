Unsigned builds for Windows, macOS (Apple Silicon and Intel) and Linux.

- **Windows**: unzip and run `CinnabarSharp.exe` (SmartScreen may warn: More info → Run anyway).
- **macOS**: open the `.dmg` and drag `CinnabarSharp.app` to Applications (or unzip the `.zip`). The app is not notarized, so macOS blocks the first launch: open System Settings › Privacy & Security and click **Open Anyway**, or run `xattr -dr com.apple.quarantine /Applications/CinnabarSharp.app`. The right-click → Open trick no longer works on macOS 15 and later.
- **Linux**: extract the `.tar.gz` and run `./CinnabarSharp`; `cinnabarsharp.desktop` and `cinnabarsharp.png` can be installed for a menu entry.
- **Arch / Omarchy**: `sudo pacman -U CinnabarSharp-<version>-1-x86_64.pkg.tar.zst`.
