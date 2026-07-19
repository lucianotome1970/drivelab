#!/usr/bin/env bash
# Testes de HOST do cérebro FFB — compila e roda no PC, SEM placa (HIL simplificado com mocks).
# Uso: firmware-base/test/run.sh
set -euo pipefail
cd "$(dirname "$0")"
out="$(mktemp -d)/ffb_brain_test"
c++ -std=c++17 -I../lib/brain -Wall -Wextra -Werror -o "$out" test_ffb_brain.cpp
"$out"

out2="$(mktemp -d)/ffb_report_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out2" test_ffb_report.cpp ../src/m05/ffb_report.cpp
"$out2"
