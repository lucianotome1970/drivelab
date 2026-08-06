#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

# Testes de host do firmware (logica pura, sem placa)
"$root/firmware-base/test/run.sh"

# Testes do app
cd "$root/app"
dotnet test -c Release
