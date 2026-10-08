#!/usr/bin/env bash
# Builds a self-contained, unsigned CinnabarSharp package for one runtime.
# Native AOT by default (first frame in ~250 ms instead of ~460 ms with ReadyToRun, half the size: performance-tasks.md
# P6). If the AOT build fails, falls back to ReadyToRun with a warning; CINNABARSHARP_PUBLISH=r2r forces ReadyToRun.
# AOT needs the platform's native toolchain: Xcode command line tools, clang + zlib on Linux, MSVC on Windows.
# Usage: packaging/package.sh <rid> <version> [output-dir]
# CINNABARSHARP_TELEMETRY_URL=https://... builds in the usage statistics endpoint (docs/telemetry.md); empty: none.
#   rid: win-x64 | linux-x64 | osx-arm64 | osx-x64
set -euo pipefail

rid="$1"
version="$2"
out="${3:-artifacts}"
root="$(cd "$(dirname "$0")/.." && pwd)"
publish="$root/$out/publish-$rid"
name="CinnabarSharp-$version-$rid"

mkdir -p "$root/$out"
publish_with() {
  rm -rf "$publish"
  dotnet publish "$root/CinnabarSharp.Desktop/CinnabarSharp.Desktop.csproj" \
    -c Release -r "$rid" --self-contained true \
    -p:Version="$version" -p:DebugType=none \
    -p:TelemetryEndpoint="${CINNABARSHARP_TELEMETRY_URL:-}" \
    "$@" -o "$publish"
}
if [ "${CINNABARSHARP_PUBLISH:-aot}" = "aot" ] && publish_with -p:PublishAot=true; then
  # Native debug symbols are not shipped.
  rm -rf "$publish"/*.dSYM "$publish"/*.dbg "$publish"/*.pdb
  echo "Published with Native AOT"
else
  [ "${CINNABARSHARP_PUBLISH:-aot}" = "aot" ] && echo "::warning::Native AOT build failed for $rid; falling back to ReadyToRun"
  publish_with -p:PublishReadyToRun=true
  echo "Published with ReadyToRun"
fi

# Licenses of the bundled components: required by their licenses, opened by the About window (next to the executable).
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.txt" "$publish/"
rm -rf "$publish/third-party"
cp -R "$root/third-party" "$publish/third-party"

case "$rid" in
  osx-*)
    app="$root/$out/$name/CinnabarSharp.app"
    rm -rf "$root/$out/$name"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$publish/." "$app/Contents/MacOS/"
    cp "$root/packaging/CinnabarSharp.icns" "$app/Contents/Resources/"
    # Icon Composer icon (macOS 26: stays red in the dark icon style); older systems use the .icns.
    cp "$root/packaging/macos/Assets.car" "$app/Contents/Resources/"
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
