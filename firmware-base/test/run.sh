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

out5="$(mktemp -d)/a0_channel_test"
c++ -std=c++17 -I../lib/base_shared -Wall -Wextra -Werror -o "$out5" test_a0_channel.cpp
"$out5"

out6="$(mktemp -d)/drv8301_test"
c++ -std=c++17 -I../lib/base_motor -Wall -Wextra -Werror -o "$out6" test_drv8301.cpp
"$out6"

out7="$(mktemp -d)/velocity_estimator_test"
c++ -std=c++17 -I../lib/brain -Wall -Wextra -Werror -o "$out7" test_velocity_estimator.cpp
"$out7"

out8="$(mktemp -d)/apply_cfg_test"
c++ -std=c++17 -I../lib/brain -I../lib/base_shared -Wall -Wextra -Werror -o "$out8" test_apply_cfg.cpp ../lib/base_shared/base_cfg.cpp
"$out8"

out9="$(mktemp -d)/ffb_effects_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out9" test_ffb_effects.cpp ../lib/base_shared/ffb_effects.cpp
"$out9"

out10="$(mktemp -d)/effect_manager_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out10" test_effect_manager.cpp ../lib/base_shared/ffb_effects.cpp
"$out10"

out11="$(mktemp -d)/ffb_engine_safety_test"
c++ -std=c++17 -I../lib/brain -I../lib/base_shared -Wall -Wextra -Werror -o "$out11" test_ffb_engine_safety.cpp ../lib/base_shared/ffb_effects.cpp
"$out11"

out12="$(mktemp -d)/pid_state_test"
c++ -std=c++17 -Wall -Wextra -Werror -o "$out12" test_pid_state.cpp
"$out12"

out13="$(mktemp -d)/clip_meter_test"
c++ -std=c++17 -I../lib/brain -Wall -Wextra -Werror -o "$out13" test_clip_meter.cpp
"$out13"

out14="$(mktemp -d)/telemetry_force_test"
c++ -std=c++17 -I../lib/brain -I../lib/base_shared -Wall -Wextra -Werror -o "$out14" test_telemetry_force.cpp ../lib/base_shared/ffb_effects.cpp
"$out14"
