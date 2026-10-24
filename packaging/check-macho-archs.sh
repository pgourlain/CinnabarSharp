#!/usr/bin/env bash
# Lists every Mach-O file of an .app with its architectures; fails if one lacks <arch> or contains another one.
# Usage: packaging/check-macho-archs.sh <App.app> <arm64|x86_64>
set -euo pipefail

app="$1"
arch="$2"
bad=0
count=0
while IFS= read -r -d '' f; do
  file -b "$f" | grep -q 'Mach-O' || continue
  archs="$(lipo -archs "$f")"
  count=$((count + 1))
  if [ "$archs" = "$arch" ]; then
    echo "ok    $archs  ${f#"$app"/}"
  else
    echo "FAIL  $archs  ${f#"$app"/}" >&2
    bad=1
  fi
done < <(find "$app" -type f -print0)

if [ "$count" -eq 0 ]; then echo "No Mach-O file found in $app" >&2; exit 1; fi
if [ "$bad" -ne 0 ]; then echo "Some Mach-O files are not $arch only" >&2; exit 1; fi
echo "$count Mach-O files, all $arch"
