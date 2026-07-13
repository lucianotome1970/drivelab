$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot ".." "app")
dotnet restore
dotnet build -c Release --no-restore
