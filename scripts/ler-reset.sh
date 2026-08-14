#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-reset.sh — Por que a base reiniciou, e ONDE ela estava quando isso
#  aconteceu.
#
#  POR QUE EXISTE: o watchdog salva a base de um travamento e, ao salvá-la,
#  apaga a evidencia — diferente do hard fault, ele nao guarda PC nem LR. Ficava
#  "reiniciou de novo, ninguem sabe por que". Este script le a causa do reset E o
#  rastro do laco, fotografado no boot antes de o proprio laco sobrescreve-lo.
#
#  USO: rodar LOGO DEPOIS de um reinicio inesperado, com o ST-Link plugado. O
#  rastro nao sobrevive a queda de energia — se voce tirou da tomada, ele se foi.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
set -u
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
ELF="$RAIZ/firmware-base/build/drivelab-base.elf"
OCD="$HOME/.platformio/packages/tool-openocd"
GDB=$(find "$HOME/.platformio/packages" -name "arm-none-eabi-gdb.exe" 2>/dev/null | head -1)
[ -f "$ELF" ] || { echo "ELF nao encontrado — compile antes"; exit 1; }

# Enderecos mudam A CADA BUILD: derivar sempre, nunca cravar.
a(){ "$GDB" -q -batch "$ELF" -ex "print &$1" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1; }
R=$(a g_bb_reset_reason); B=$(a g_bb_boots)
S=$(a g_bb_trace_prev_step); L=$(a g_bb_trace_prev_last); T=$(a g_bb_trace_prev_tick)
F=$(a g_bb_fault)

out=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
  -c "init" \
  -c "echo D:[read_memory $R 32 1]:[read_memory $B 32 2]:[read_memory $S 32 1]:[read_memory $L 32 1]:[read_memory $T 32 1]" \
  -c "echo E:[read_memory $F 32 5]" \
  -c "shutdown" 2>&1)
linha=$(echo "$out" | grep -oE '^D:.*')
[ -n "$linha" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

IFS=':' read -r _ razao boots_magic boots step last tick <<< "$linha"
d(){ printf "%d" "$1"; }

case $(d "$razao") in
  1) causa="POWER-ON (a fonte foi ligada) — normal";;
  2) causa="BROWN-OUT: a tensao caiu. Fonte ou carga, NAO firmware";;
  3) causa="pino NRST (inclui o reset do ST-Link ao gravar)";;
  4) causa="SOFTWARE (nosso reboot, ou entrada em DFU)";;
  5) causa="WATCHDOG: o firmware parou de responder e a base se reiniciou";;
  6) causa="window watchdog";;
  7) causa="saida anormal de standby";;
  *) causa="desconhecida";;
esac

case $(d "$step") in
  0) onde="(nada marcado)";;
  1) onde="inicio do laco";;
  2) onde="telemetria / canal do app";;
  3) onde="GRAVANDO NA FLASH (Salvar no controlador)";;
  4) onde="dimensionando os limites de bus";;
  5) onde="guardas (sobrevelocidade / angulo / curso)";;
  6) onde="auto-arme / calibracao";;
  7) onde="calculo do torque";;
  8) onde="fim do laco";;
  *) onde="trecho $step";;
esac

echo "causa do ultimo reset : $causa"
echo "boots desde a tomada  : $(d "$boots")"
echo
if [ "$(d "$step")" -eq 0 ]; then
    echo "rastro do laco        : vazio (a energia caiu, ou este e o primeiro boot do ciclo)"
else
    echo "onde estava           : $onde"
    echo "trecho anterior       : $(d "$last")"
    echo "voltas do laco        : $(d "$tick")"
fi

falha=$(echo "$out" | grep -oE '^E:.*')
if [ -n "$falha" ] && ! echo "$falha" | grep -q "E:0x0 "; then
    echo; echo "HARD FAULT registrado: $falha"
    echo "  (traduza o pc com: arm-none-eabi-addr2line -f -e $ELF <pc>)"
fi
