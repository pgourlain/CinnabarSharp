#!/usr/bin/env bash
# Regenerates every app icon from icon.svg (large) and icon-small.svg (16-32 px).
# Needs rsvg-convert, python3, and iconutil (macOS only, for the .icns; skipped elsewhere).
set -euo pipefail
cd "$(dirname "$0")"
tmp=$(mktemp -d)
render() { # size, output
  local svg=icon.svg; [ "$1" -le 32 ] && svg=icon-small.svg
  rsvg-convert -w "$1" -h "$1" "$svg" -o "$2"
}
for s in 16 24 32 48 64 128 256 512 1024; do render $s "$tmp/$s.png"; done
cp "$tmp/512.png" ../CinnabarSharp.Desktop/Assets/icon.png
cp "$tmp/256.png" icon-256.png
python3 - "$tmp" ../CinnabarSharp.Desktop/Assets/icon.ico <<'PY'
import struct, sys
tmp, out = sys.argv[1:3]
sizes = [16, 24, 32, 48, 64, 256]
images = [open(f"{tmp}/{s}.png", "rb").read() for s in sizes]
data = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, png in zip(sizes, images):
    data += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(png), offset)
    offset += len(png)
open(out, "wb").write(data + b"".join(images))
PY
if command -v iconutil >/dev/null; then
  set_dir="$tmp/CinnabarSharp.iconset"; mkdir "$set_dir"
  for s in 16 32 128 256 512; do
    cp "$tmp/$s.png" "$set_dir/icon_${s}x${s}.png"
    cp "$tmp/$((s * 2)).png" "$set_dir/icon_${s}x${s}@2x.png"
  done
  iconutil -c icns "$set_dir" -o CinnabarSharp.icns
fi
rm -rf "$tmp"
