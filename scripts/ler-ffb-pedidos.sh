#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-ffb-pedidos.sh — O que o JOGO pede, na ordem, e onde ele para.
#
#  POR QUE EXISTE: o jogo monta o efeito por etapas (cria, descreve, manda a
#  forca, manda tocar) e dai em diante so atualiza a forca a cada quadro.
#  Medimos que ele cria dois efeitos, manda uma forca e para — e "para" nao
#  diz onde. Este rastro mostra a sequencia exata, degrau por degrau.
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

P=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_pedidos" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
I=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_inicio" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
N=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_inicio_n" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
Q=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_pedidos_pos" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
[ -n "$P" ] || { echo "nao achei o rastro no ELF"; exit 1; }

D=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
      -c "init" -c "echo D:[read_memory $P 8 24]:[read_memory $Q 32 1]" -c "shutdown" 2>&1 | grep -oE '^D:.*' | cut -c3-)
[ -n "$D" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }
IFS=':' read -r LISTA POS <<< "$D"
read -r -a V <<< "$LISTA"
p=$(( $(printf "%d" "$POS") % 24 ))

nome() {
  case $1 in
    1)  echo "DESCREVEU o efeito (tipo, duracao, eixo)";;
    2)  echo "descreveu o envelope";;
    3)  echo "descreveu a condicao (mola/amortecedor)";;
    4)  echo "descreveu o efeito periodico (vibracao)";;
    5)  echo "MANDOU A FORCA (constante) <- e este que se repete a cada quadro";;
    6)  echo "descreveu a rampa";;
    10) echo "MANDOU TOCAR / PARAR o efeito";;
    11) echo "liberou o efeito";;
    12) echo "controle do dispositivo (ligar/desligar forca)";;
    13) echo "ajustou o ganho geral";;
    17) echo "CRIOU um efeito novo";;
    18) echo "perguntou se o efeito entrou (e quanto cabe)";;
    19) echo "perguntou o tamanho do banco de efeitos";;
    *)  echo "relatorio 0x$(printf '%02X' $1)";;
  esac
}

# --- o COMECO da sessao (nao circular) ---
DI=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg       -c "init" -c "echo I:[read_memory $I 8 32]:[read_memory $N 32 1]" -c "shutdown" 2>&1 | grep -oE '^I:.*' | cut -c3-)
IFS=':' read -r INI QTD <<< "$DI"
read -r -a W <<< "$INI"
echo "── O COMECO DA SESSAO (os $(printf "%d" "${QTD:-0}") primeiros pedidos) ──"
for k in $(seq 0 $(( $(printf "%d" "${QTD:-0}") - 1 )) ); do
  b=$(printf "%d" "${W[$k]}")
  if [ $(( b & 0x80 )) -ne 0 ]; then printf "   PERGUNTOU : %s
" "$(nome $(( b & 0x7F )))"
  else printf "   escreveu  : %s
" "$(nome $b)"; fi
done
echo
echo "── O QUE O JOGO PEDIU, DO MAIS ANTIGO AO MAIS NOVO ──"
vazio=1
for k in $(seq 0 23); do
  i=$(( (p + k) % 24 ))
  b=$(printf "%d" "${V[$i]}")
  [ "$b" -eq 0 ] && continue
  vazio=0
  if [ $(( b & 0x80 )) -ne 0 ]; then
    printf "   PERGUNTOU : %s\n" "$(nome $(( b & 0x7F )))"
  else
    printf "   escreveu  : %s\n" "$(nome $b)"
  fi
done
[ $vazio -eq 1 ] && echo "   (nada — o jogo nao falou com a base)"
echo
echo "── COMO LER ────────────────────────────────────────────"
echo "  a sequencia sadia termina com MANDOU A FORCA repetindo sem parar"
echo "  se ela para logo depois de TOCAR, o jogo desistiu na primeira atualizacao"
echo "  se termina numa PERGUNTA, foi a nossa resposta aquela pergunta que o fez parar"
