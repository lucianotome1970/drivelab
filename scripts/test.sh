#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../app"
dotnet test -c Release
