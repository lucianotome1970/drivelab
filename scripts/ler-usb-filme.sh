#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-usb-filme.sh — Os ultimos acontecimentos do USB antes do silencio.
#
#  POR QUE EXISTE: quando a base fica muda, os contadores dizem QUE parou, mas
#  nao o que aconteceu antes — e e o antes que explica por que o PC fecha a
#  porta e leva junto os outros controles. Cada acontecimento do USB e anotado
#  com a hora; aqui eles saem em ordem, com o intervalo entre um e outro.
#
#  O RITMO E A INFORMACAO: dez reinicios de barramento em cem milissegundos
#  contam uma historia bem diferente de um a cada dois segundos.
#
#  ⚠️ LE SEM PARAR O NUCLEO.
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

BB=$("$GDB" -q -batch "$ELF" -ex "print &g_bb_trace" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
[ -n "$BB" ] || { echo "nao achei g_bb_trace no ELF"; exit 1; }
OFF=$("$GDB" -q -batch "$ELF" -ex "print (int)&((BlackBoxTrace*)0)->usb_filme" 2>/dev/null | grep -oE '[0-9]+$' | head -1)
[ -n "$OFF" ] || OFF=56
F=$(printf "0x%x" $((0x${BB#0x} + OFF)))

D=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
      -c "init" -c "echo D:[read_memory $F 32 25]" -c "shutdown" 2>&1 | grep -oE '^D:.*' | cut -c3-)
[ -n "$D" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

read -r -a V <<< "$D"
POS=$(( $(printf "%d" "${V[24]}") % 24 ))

nome() {
  # 0xE0|i = pedido de TEXTO numero i; 0xC0|t = outra DESCRICAO; 0x80|p = pergunta comum
  if [ $(( $1 & 0xE0 )) -eq 224 ]; then
    case $(( $1 & 0x1F )) in
      1) echo "pediu o texto 1 (FABRICANTE)";;
      2) echo "pediu o texto 2 (PRODUTO)";;
      3) echo "pediu o texto 3 (NUMERO DE SERIE)";;
      5) echo "pediu o texto 5 (nome da interface)";;
      14) echo "pediu o texto 0xEE (extensao da Microsoft) — recusar e normal";;
      *) echo "pediu o texto $(( $1 & 0x1F ))";;
    esac
    return
  fi
  if [ $(( $1 & 0xC0 )) -eq 192 ]; then
    case $(( $1 & 0x0F )) in
      1) echo "pediu a DESCRICAO DO DISPOSITIVO";;
      2) echo "pediu a DESCRICAO DA CONFIGURACAO";;
      3) echo "pediu um TEXTO (nome/fabricante/serie)";;
      6) echo "pediu a descricao de dispositivo (outra velocidade)";;
      7) echo "pediu a configuracao (outra velocidade)";;
      *) echo "pediu a DESCRICAO tipo $(( $1 & 0x0F )) (RELATORIO HID, se 34)";;
    esac
    return
  fi
  if [ $(( $1 & 0x80 )) -eq 128 ]; then
    case $(( $1 & 0x0F )) in
      0) echo "perguntou o ESTADO";;
      1) echo "mandou LIMPAR uma trava";;
      3) echo "mandou TRAVAR algo";;
      5) echo "deu o ENDERECO";;
      8) echo "perguntou a CONFIGURACAO ATUAL";;
      9) echo "mandou APLICAR a configuracao";;
      10) echo "perguntou a interface";;
      11) echo "mandou trocar de interface";;
      *) echo "pergunta comum $(( $1 & 0x0F ))";;
    esac
    return
  fi
  case $1 in
    1) echo "o PC REINICIOU o barramento";;
    2) echo "o PC nos TIROU do barramento";;
    3) echo "dormiu (suspensao)";;
    4) echo "acordou";;
    5) echo "o PC PERGUNTOU algo";;
    7) echo "marco de tempo";;
    8) echo "*** RECUSAMOS A PERGUNTA (canal de controle travado) ***";;
    0) echo "(vazio)";;
    *) echo "codigo $1";;
  esac
}

echo "── OS ULTIMOS ACONTECIMENTOS, DO MAIS ANTIGO AO MAIS NOVO ──"
ant=0
for k in $(seq 0 23); do
  i=$(( (POS + k) % 24 ))
  w=$(printf "%d" "${V[$i]}")
  [ "$w" -eq 0 ] && continue
  ms=$(( (w >> 8) & 0xFFFFFF ))
  cod=$(( w & 0xFF ))
  if [ $ant -eq 0 ]; then gap="   —"; else gap=$(printf "%+5d" $((ms - ant))); fi
  printf "  %8d ms  %s ms   %s\n" "$ms" "$gap" "$(nome $cod)"
  ant=$ms
done
echo
echo "── COMO LER ────────────────────────────────────────────"
echo "  varios REINICIOS seguidos  -> o PC nao consegue nos enumerar e insiste"
echo "  PERGUNTA e o silencio      -> travamos respondendo aquela pergunta"
echo "  dormiu logo apos entregas  -> paramos de responder e o PC desistiu"
echo "  filme vazio                -> nada excepcional aconteceu: a queda nao passou pela pilha"
