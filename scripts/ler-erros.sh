#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-erros.sh — Por que o motor desarmou. A causa real, e nao a deduzida do
#  sintoma.
#
#  POR QUE EXISTE: "o volante desarmou" tem meia duzia de causas que se parecem
#  na bancada — sobretensao do barramento, subtensao, uma das nossas guardas,
#  erro do proprio ODrive. Deduzir pelo sintoma ja custou horas a este projeto
#  varias vezes. O firmware SABE qual foi; este script pergunta a ele.
#
#  ⚠️ LE SEM PARAR O NUCLEO (read_memory, nunca halt). Parar a CPU com o motor
#  armado o derruba, e o proprio diagnostico inventaria o sintoma que investiga.
#
#  ⚠️ O ESTADO DE AGORA MENTE sobre o desarme: o auto-arme religa o motor e o
#  clear_errors zera os campos em segundos. Por isso o que vale e a FOTOGRAFIA
#  (g_fail_dbg), tirada antes do clear e preservada ate o proximo boot.
#
#  USO: rodar depois do desarme, com o ST-Link plugado e a base ligada.
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

AXIS=$(a g_axis_dbg)          # [0]armed [1]state [2]axis_err [3]motor_err [4]enc_err [5]pos [6]vel
GATE=$(a g_arm_gate)
GUARD=$(a g_guard_trip);      GIQ=$(a g_guard_iq_ma)
OVER=$(a g_overspeed_trip);   OVEL=$(a g_overspeed_vel_mts); ON=$(a g_overspeed_trips)
TRAV=$(a g_overtravel_trip);  TPOS=$(a g_overtravel_pos_mrad); TN=$(a g_overtravel_trips)
TD=$(a g_overtravel_disparos)
FAIL=$(a g_fail_dbg)          # a fotografia — ver o layout em ffb_task.cpp
# ⚠️ `&odrv` da o inicio do objeto, nao o campo: sem o `.error_` sai um ponteiro de
# vtable (0x08...) que parece um codigo de erro gigante e nao e nada.
ODRV=$(a odrv.error_)

out=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
  -c "init" \
  -c "echo A:[read_memory $AXIS 32 7]" \
  -c "echo B:[read_memory $GATE 32 1]:[read_memory $GUARD 32 1]:[read_memory $GIQ 32 1]:[read_memory $OVER 32 1]:[read_memory $OVEL 32 1]:[read_memory $ON 32 1]" \
  -c "echo C:[read_memory $TRAV 32 1]:[read_memory $TPOS 32 1]:[read_memory $TN 32 1]:[read_memory $TD 32 1]" \
  -c "echo F:[read_memory $FAIL 32 10]" \
  -c "echo O:[read_memory $ODRV 32 1]" \
  -c "shutdown" 2>&1)

la=$(echo "$out" | grep -oE '^A:.*'); lb=$(echo "$out" | grep -oE '^B:.*')
lc=$(echo "$out" | grep -oE '^C:.*'); lf=$(echo "$out" | grep -oE '^F:.*')
lo=$(echo "$out" | grep -oE '^O:.*')
[ -n "$la" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

read -r _ armado estado axis_err motor_err enc_err pos vel <<< "${la//:/ }"
read -r _ tentativas f_ctrl f_axis f_motor f_enc f_pmec f_pele f_vbus f_odrv desarmes <<< "${lf//:/ }"
IFS=':' read -r _ gate guard giq over ovel <<< "$lb"
IFS=':' read -r _ trav tpos tn td <<< "$lc"
IFS=':' read -r _ odrv_err <<< "$lo"

# Complemento de dois: posicao/velocidade sao assinados e o read_memory devolve cru.
s32(){ v=$(printf "%d" "$1"); [ "$v" -gt 2147483647 ] && v=$((v - 4294967296)); echo "$v"; }
# Sem `bc` no Git Bash — awk faz a conta e ja formata.
div(){ awk -v v="$1" -v d="$2" -v f="$3" 'BEGIN{printf f, v/d}'; }

ESTADOS=([1]=IDLE [3]=CALIBRACAO_COMPLETA [4]=CAL_MOTOR [6]=CAL_ENCODER [7]=CAL_INDICE [8]=MALHA_FECHADA)
nome_estado="${ESTADOS[$(printf %d "$estado")]:-desconhecido}"

echo "── AGORA ───────────────────────────────────────────────"
printf "armado=%d  estado=%d (%s)  gate=%d\n" "$armado" "$estado" "$nome_estado" "$gate"
printf "erros: axis=0x%x motor=0x%x encoder=0x%x odrv=0x%x\n" \
       "$axis_err" "$motor_err" "$enc_err" "$odrv_err"
printf "posicao=%s graus   velocidade=%s volta/s\n" \
       "$(div "$(s32 "$pos")" 17.4533 '%.1f')" "$(div "$(s32 "$vel")" 1000 '%.2f')"

echo "── A FOTOGRAFIA DA FALHA ───────────────────────────────"
if [ "$(printf %d "$desarmes")" = "0" ]; then
  printf "nenhum desarme COM ERRO neste boot (%d tentativa(s) de arme rotineira(s))\n" \
         "$(printf %d "$tentativas")"
else
  printf "%d desarme(s) com erro neste boot. No ULTIMO:\n" "$(printf %d "$desarmes")"
  printf "  axis=0x%x motor=0x%x encoder=0x%x controlador=0x%x odrv=0x%x\n" \
         "$f_axis" "$f_motor" "$f_enc" "$f_ctrl" "$f_odrv"
  printf "  vbus=%s V   potencia mecanica=%s W   eletrica=%s W\n" \
         "$(div "$(s32 "$f_vbus")" 1000 '%.2f')" \
         "$(div "$(s32 "$f_pmec")" 1000 '%.1f')" \
         "$(div "$(s32 "$f_pele")" 1000 '%.1f')"
  # Potencia mecanica NEGATIVA = o motor esta sendo GIRADO, devolvendo energia ao bus.
  awk -v p="$(s32 "$f_pmec")" 'BEGIN{ if (p < -500) print "  → o motor estava DEVOLVENDO energia: regeneracao" }'
fi

echo "── QUEM DISPAROU ───────────────────────────────────────"
culpado=""
[ "$(printf %d "$trav")"  != "0" ] && { culpado=1; \
  printf "→ guarda de CURSO: o volante foi a %s graus\n" "$(div "$(s32 "$tpos")" 17.4533 '%.1f')"; }
[ "$(printf %d "$over")"  != "0" ] && { culpado=1; \
  printf "→ guarda de SOBREVELOCIDADE: %s volta/s no disparo\n" "$(div "$(s32 "$ovel")" 1000 '%.2f')"; }
[ "$(printf %d "$guard")" != "0" ] && { culpado=1; \
  printf "→ guarda de ANGULO ELETRICO: %s A no disparo\n" "$(div "$(s32 "$giq")" 1000 '%.1f')"; }

# O erro do ODrive e campo de bits: mais de um pode estar aceso. Vale tanto o de agora
# quanto o fotografado — o de agora ja pode ter sido limpo.
for e in "$odrv_err" "$f_odrv"; do
  o=$(printf %d "$e"); [ "$o" = "0" ] && continue
  [ $((o & 0x02)) -ne 0 ] && { culpado=1; echo "→ ODrive: DC_BUS_OVER_VOLTAGE — regeneracao empurrou o bus acima do trip"; }
  [ $((o & 0x04)) -ne 0 ] && { culpado=1; echo "→ ODrive: DC_BUS_UNDER_VOLTAGE — o bus afundou sob corrente"; }
  [ $((o & 0x20)) -ne 0 ] && { culpado=1; echo "→ ODrive: DC_BUS_OVER_REGEN_CURRENT — o freio nao deu conta do que voltou"; }
  [ $((o & 0x40)) -ne 0 ] && { culpado=1; echo "→ ODrive: DC_BUS_OVER_CURRENT"; }
  [ $((o & 0x08)) -ne 0 ] && { culpado=1; echo "→ ODrive: BRAKE_RESISTOR_DISARMED"; }
done

[ -z "$culpado" ] && echo "(nada acusou — se desarmou mesmo assim, a causa nao passou por aqui)"
printf "── a guarda de curso disparou %dx neste boot\n" "$(printf %d "$tn")"
