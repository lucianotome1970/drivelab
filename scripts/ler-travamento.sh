#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-travamento.sh — Onde o firmware estava quando travou.
#
#  POR QUE EXISTE: o cao-de-guarda reinicia a base quando o firmware trava, e a
#  caixa-preta guarda o ponto do laco. So que ela mora numa area que sobrevive
#  ao RESET e nao a DESLIGAR — e a reacao natural na bancada e desligar. Nas
#  tres primeiras vezes que fomos ler, a evidencia ja tinha sido apagada.
#  Agora o firmware copia o rastro para a memoria permanente no primeiro laco
#  depois de um travamento, e ele sobrevive a tirar da tomada.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
set -u
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
ELF="$RAIZ/firmware-base/build/drivelab-base.elf"
OCD="$HOME/.platformio/packages/tool-openocd"
GDB=$(find "$HOME/.platformio/packages" -name "arm-none-eabi-gdb.exe" 2>/dev/null | head -1)

passo() {
  case $1 in
    0) echo "nenhum (nao chegou a marcar)";;
    1) echo "TOPO DO LACO";;
    2) echo "enviando ao PC (joystick / canal do app)";;
    3) echo "GRAVANDO NA MEMORIA (congela a CPU de proposito)";;
    4) echo "dimensionando os limites de tensao";;
    5) echo "guardas: sobrevelocidade, angulo, curso excedido";;
    6) echo "armando o motor / calibrando";;
    7) echo "CALCULANDO A FORCA e escrevendo o torque";;
    8) echo "fim do laco, antes de dormir";;
    10) echo "enviando o relatorio do volante";;
    11) echo "DENTRO da pilha USB (e onde o mutex e tomado)";;
    16) echo "pedindo o endpoint (mutex)";;
    17) echo "copiando o pacote";;
    18) echo "entregando ao driver do USB";;
    19) echo "ESCREVENDO NA FILA do periferico";;
    20) echo "a escrita na fila retornou";;
    21) echo "a entrega ao driver retornou";;
    *) echo "passo $1";;
  esac
}

# le direto da memoria permanente: chaves 0xFF20..0xFF23
D=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
# ⚠️ SAO DUAS PAGINAS (setores 1 e 2): a ativa alterna quando uma enche. Ler so a primeira
# faz o rastro parecer inexistente quando ele esta na outra (visto em 21/08/2026).
      -c "init" -c "echo P:[read_memory 0x08004000 32 2048]" -c "echo P:[read_memory 0x08008000 32 2048]" -c "shutdown" 2>&1 | grep -oE '^P:.*' | cut -c3- | tr '
' ' ')
[ -n "$D" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

python - "$D" <<'PY'
import sys
vals = [int(x,16) for x in sys.argv[1].split()]
chaves = {0xFF20:"onde o laco estava", 0xFF21:"o passo ANTERIOR a esse",
          0xFF22:"voltas do laco ate ali", 0xFF23:"interrupcoes de USB ate ali"}
achado = {}
for i in range(0, len(vals)-1, 2):
    cab, val = vals[i], vals[i+1]
    if cab == 0xFFFFFFFF: break
    if (cab >> 16) != 0xC5C5: continue      # marca de registro bom
    ch = cab & 0xFFFF
    if ch in chaves: achado[ch] = val        # o ultimo vale
if not achado:
    print("Nenhum travamento gravado ainda.")
    print("(a base so grava isto no primeiro laco DEPOIS de reiniciar por travamento)")
else:
    print("── ONDE O FIRMWARE TRAVOU ──────────────────────────────")
    for ch in sorted(chaves):
        if ch in achado: print(f"  {chaves[ch]:<30}: {achado[ch]}")
PY
