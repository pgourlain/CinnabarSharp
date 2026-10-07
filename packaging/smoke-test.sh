#!/usr/bin/env bash
# Runs the smoke test script (packaging/smoke-test.txt: every MCP tool, every effect, every file format) with the
# packaged app of one runtime, as the release workflow does after packaging. Fails if any command fails.
# Usage: packaging/smoke-test.sh <rid> <version> [output-dir]   (the same arguments as package.sh)
set -euo pipefail

rid="$1"
version="$2"
out="${3:-artifacts}"
root="$(cd "$(dirname "$0")/.." && pwd)"
name="CinnabarSharp-$version-$rid"

case "$rid" in
  osx-*) exe="$root/$out/$name/CinnabarSharp.app/Contents/MacOS/CinnabarSharp" ;;
  linux-*) exe="$root/$out/$name/CinnabarSharp" ;;
  win-*) exe="$root/$out/publish-$rid/CinnabarSharp.exe" ;;
  *) echo "Unsupported runtime: $rid" >&2; exit 1 ;;
esac
[ -f "$exe" ] || { echo "Packaged app not found: $exe" >&2; exit 1; }

# A native Windows program wants Windows paths, whatever the shell (Git Bash) uses.
native() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/samples" "$work/out"
cp "$root"/CinnabarSharp.Core.Tests/Data/SampleFiles/sample1.* "$work/samples/"
samples="$(native "$work/samples")"
output="$(native "$work/out")"

echo "Smoke test of $exe"
"$exe" --run "$(native "$root/packaging/smoke-test.txt")" \
  --var "samples=$samples" --var "out=$output" --allow "$samples" --allow "$output" --quiet

for file in result.png result.jpg result.ora flat.png flat.jpg drawing.svg drawing.png; do
  [ -s "$work/out/$file" ] || { echo "Missing output: $file" >&2; exit 1; }
done
echo "Smoke test passed ($rid)"
