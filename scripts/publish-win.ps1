$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot ".."
$proj = Join-Path $root "app/DriveLab.Studio/DriveLab.Studio.csproj"
$out  = Join-Path $root "dist/win-x64"
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out
Write-Host "Executavel gerado em: $out/DriveLab.Studio.exe"
