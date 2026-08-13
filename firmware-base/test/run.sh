#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  run.sh — Testes de host do firmware-base: logica pura, sem STM32.
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
set -euo pipefail
cd "$(dirname "$0")"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

for src in test_*.c; do
  echo "== $src"
  cc -std=c11 -Wall -Wextra -Werror -O1 -o "$out/${src%.c}" "$src" -lm
  "$out/${src%.c}"
done

echo "firmware-base: testes de host OK"
