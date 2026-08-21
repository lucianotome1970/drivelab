#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

# Setting que o app transporta e o firmware ignora (ver o cabecalho do script)
python3 "$root/scripts/check-orphan-settings.py"
python3 "$root/scripts/check-config-declarada.py"

# Contrato dos settings gravados nas placas dos usuarios: id nunca reciclado,
# unidade/tipo/faixa de um id em uso nunca mudam (ver o cabecalho do script)
python3 "$root/scripts/check-schema-freeze.py"

# Testes de host do firmware (logica pura, sem placa)
"$root/firmware-base/test/run.sh"

# Testes do app
cd "$root/app"
dotnet test -c Release
