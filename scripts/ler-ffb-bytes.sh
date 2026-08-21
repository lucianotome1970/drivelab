#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-ffb-bytes.sh — Os pacotes INTEIROS que o jogo mandou no comeco da sessao.
#
#  POR QUE EXISTE: saber QUE o jogo descreveu um efeito nao explica por que ele
#  para de mandar forca. O que decide isso esta DENTRO do pacote: o tipo do
#  efeito, a duracao, o eixo, o modo de tocar. Dois jogos podem fazer a mesma
#  sequencia de pedidos e divergir inteiramente no conteudo.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
set -u
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
ELF="$RAIZ/firmware-base/build/drivelab-base.elf"
OCD="$HOME/.platformio/packages/tool-openocd"
GDB=$(find "$HOME/.platformio/packages" -name "arm-none-eabi-gdb.exe" 2>/dev/null | head -1)
B=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_bytes" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
R=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_respostas" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
RN=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_respostas_n" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
N=$("$GDB" -q -batch "$ELF" -ex "print &g_ffb_bytes_n" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
[ -n "$B" ] || { echo "nao achei o rastro no ELF"; exit 1; }
D=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
      -c "init" -c "echo D:[read_memory $B 8 120]:[read_memory $N 32 1]" -c "shutdown" 2>&1 | grep -oE '^D:.*' | cut -c3-)
[ -n "$D" ] || { echo "SWD nao respondeu"; exit 1; }
IFS=':' read -r LISTA QTD <<< "$D"
read -r -a V <<< "$LISTA"
q=$(printf "%d" "${QTD:-0}")

nome() { case $1 in
  1) echo "DESCREVEU o efeito";; 3) echo "descreveu a condicao";; 5) echo "MANDOU A FORCA";;
  10) echo "TOCAR/PARAR";; 12) echo "controle do dispositivo";; 13) echo "ganho geral";;
  *) echo "relatorio 0x$(printf '%02X' $1)";; esac; }

echo "── PACOTES DO COMECO DA SESSAO ($q gravados) ──"
for k in $(seq 0 $((q-1))); do
  o=$((k*10))
  id=$(printf "%d" "${V[$((o+2))]}"); tam=$(printf "%d" "${V[$((o+1))]}")
  printf "  %-24s (%d bytes) :" "$(nome $id)" "$tam"
  for i in $(seq 2 9); do printf " %02X" $(printf "%d" "${V[$((o+i))]}"); done
  case $id in
    1)  d=$(( $(printf "%d" "${V[$((o+4))]}") | ($(printf "%d" "${V[$((o+5))]}") << 8) ))
        printf "\n      -> bloco %d, tipo %d, duracao %s" $(printf "%d" "${V[$((o+3))]}") $(printf "%d" "${V[$((o+4))]}") \
          "$([ $d -eq 0 ] && echo 'INFINITA' || echo "${d} ms")";;
    5)  f=$(( $(printf "%d" "${V[$((o+4))]}") | ($(printf "%d" "${V[$((o+5))]}") << 8) ))
        [ $f -gt 32767 ] && f=$((f - 65536))
        printf "\n      -> bloco %d, forca %d" $(printf "%d" "${V[$((o+3))]}") $f;;
    10) printf "\n      -> bloco %d, comando %d (1=tocar 2=parar 3=parar tudo), repeticoes %d" \
          $(printf "%d" "${V[$((o+3))]}") $(printf "%d" "${V[$((o+4))]}") $(printf "%d" "${V[$((o+5))]}");;
  esac
  echo
done

# --- e o que NOS respondemos ---
DR=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg       -c "init" -c "echo R:[read_memory $R 8 48]:[read_memory $RN 32 1]" -c "shutdown" 2>&1 | grep -oE '^R:.*' | cut -c3-)
IFS=':' read -r LR QR <<< "$DR"
read -r -a Y <<< "$LR"
qr=$(printf "%d" "${QR:-0}")
echo
echo "── O QUE NOS RESPONDEMOS ($qr respostas) ──"
for k in $(seq 0 $((qr-1))); do
  o=$((k*6)); id=$(printf "%d" "${Y[$o]}")
  b0=$(printf "%d" "${Y[$((o+2))]}"); b1=$(printf "%d" "${Y[$((o+3))]}")
  b2=$(printf "%d" "${Y[$((o+4))]}"); b3=$(printf "%d" "${Y[$((o+5))]}")
  if [ $id -eq 18 ]; then
    st="$([ $b1 -eq 1 ] && echo 'ENTROU (sucesso)' || { [ $b1 -eq 2 ] && echo 'BANCO CHEIO' || echo 'ERRO'; })"
    printf "  'o efeito entrou?' -> bloco %d, %s, ainda cabem %d
" "$b0" "$st" "$(( b2 | (b3 << 8) ))"
  elif [ $id -eq 19 ]; then
    printf "  'quanto cabe?'     -> banco de %d, ate %d ao mesmo tempo, modo %d
" "$(( b0 | (b1 << 8) ))" "$b2" "$b3"
  else
    printf "  relatorio 0x%02X      -> %02X %02X %02X %02X
" "$id" "$b0" "$b1" "$b2" "$b3"
  fi
done
