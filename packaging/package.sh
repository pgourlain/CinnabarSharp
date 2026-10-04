#!/usr/bin/env bash
# Builds a self-contained, unsigned CinnabarSharp package for one runtime.
# ReadyToRun (precompiled code) cuts the start time by about a third (performance-tasks.md P6) for ~25 MB more.
# Usage: packaging/package.sh <rid> <version> [output-dir]
#   rid: win-x64 | linux-x64 | osx-arm64 | osx-x64
set -euo pipefail

rid="$1"
version="$2"
out="${3:-artifacts}"
root="$(cd "$(dirname "$0")/.." && pwd)"
publish="$root/$out/publish-$rid"
name="CinnabarSharp-$version-$rid"

rm -rf "$publish"
mkdir -p "$root/$out"
dotnet publish "$root/CinnabarSharp.Desktop/CinnabarSharp.Desktop.csproj" \
  -c Release -r "$rid" --self-contained true \
  -p:Version="$version" -p:DebugType=none \
  -p:PublishReadyToRun=true \
  -o "$publish"

case "$rid" in
  osx-*)
    app="$root/$out/$name/CinnabarSharp.app"
    rm -rf "$root/$out/$name"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$publish/." "$app/Contents/MacOS/"
    cp "$root/packaging/CinnabarSharp.icns" "$app/Contents/Resources/"
    # macOS bundle versions are numeric (x.y.z): drop a pre-release suffix such as "-rc".
    sed "s/@VERSION@/${version%%-*}/g" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
    # Apple Silicon refuses to run unsigned code; an ad-hoc signature is enough for local use.
    if command -v codesign >/dev/null; then
      codesign --force --deep --sign - "$app"
    fi
    (cd "$root/$out/$name" && ditto -c -k --keepParent CinnabarSharp.app "$root/$out/$name.zip")
    # Disk image: the app, a shortcut to Applications and the first-launch instructions (the app is not notarized).
    if command -v hdiutil >/dev/null; then
      dmg="$root/$out/$name-dmg"
      rm -rf "$dmg" "$root/$out/$name.dmg"
      mkdir -p "$dmg"
      ditto "$app" "$dmg/CinnabarSharp.app"
      ln -s /Applications "$dmg/Applications"
      cp "$root/packaging/macos/ReadMeFirst.txt" "$dmg/Read Me First.txt"
      hdiutil create -volname "CinnabarSharp" -srcfolder "$dmg" -ov -format UDZO "$root/$out/$name.dmg" >/dev/null
      rm -rf "$dmg"
    fi
    ;;
  linux-*)
    dir="$root/$out/$name"
    rm -rf "$dir"
    mkdir -p "$dir"
    cp -R "$publish/." "$dir/"
    cp "$root/packaging/linux/cinnabarsharp.desktop" "$dir/"
    cp "$root/packaging/icon-256.png" "$dir/cinnabarsharp.png"
    chmod +x "$dir/CinnabarSharp"
    tar -C "$root/$out" -czf "$root/$out/$name.tar.gz" "$name"
    ;;
  win-*)
    rm -f "$root/$out/$name.zip"
    if command -v zip >/dev/null; then
      (cd "$publish" && zip -qr "$root/$out/$name.zip" .)
    elif command -v 7z >/dev/null; then
      (cd "$publish" && 7z a -tzip -bso0 "$root/$out/$name.zip" .)
    else
      powershell -NoProfile -Command "Compress-Archive -Path '$publish/*' -DestinationPath '$root/$out/$name.zip'"
    fi
    ;;
  *)
    echo "Unsupported runtime: $rid" >&2
    exit 1
    ;;
esac

echo "Created $(ls "$root/$out/$name".*)"
