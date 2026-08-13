#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
proj="$root/app/DriveLab.Studio/DriveLab.Studio.csproj"
out="$root/dist/win-x64"
dotnet publish "$proj" -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$out"
echo "Executavel gerado em: $out/DriveLab.Studio.exe"
