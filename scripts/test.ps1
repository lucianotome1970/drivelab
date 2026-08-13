$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot ".." "app")
dotnet test -c Release
