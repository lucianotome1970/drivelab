#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../app"
dotnet restore
dotnet build -c Release --no-restore
