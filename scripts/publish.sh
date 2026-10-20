#!/usr/bin/env bash
# Builds a self-contained, single-file Windows release from macOS or Linux.
# Usage: scripts/publish.sh [runtime]   (default runtime: win-x64)
set -euo pipefail

runtime="${1:-win-x64}"
root="$(cd "$(dirname "$0")/.." && pwd)"
output="$root/dist/MinecraftServerManager-$runtime"

rm -rf "$output" "$output.zip"
dotnet publish "$root/src/MinecraftServerManager/MinecraftServerManager.csproj" -c Release -r "$runtime" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "$output"

(cd "$output" && zip -qr "$output.zip" .)
echo "Published to $output"
echo "Archive:     $output.zip"
