#!/usr/bin/env bash
# Testes de HOST do cérebro FFB — compila e roda no PC, SEM placa (HIL simplificado com mocks).
# Uso: firmware-base/test/run.sh
set -euo pipefail
cd "$(dirname "$0")"
out="$(mktemp -d)/ffb_brain_test"
c++ -std=c++17 -I../lib/brain -Wall -Wextra -Werror -o "$out" test_ffb_brain.cpp
"$out"

out2="$(mktemp -d)/ffb_report_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out2" test_ffb_report.cpp ../lib/base_shared/ffb_report.cpp
"$out2"

out3="$(mktemp -d)/base_cfg_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out3" test_base_cfg.cpp ../lib/base_shared/base_cfg.cpp
"$out3"

out4="$(mktemp -d)/sensors_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out4" test_sensors.cpp
"$out4"
