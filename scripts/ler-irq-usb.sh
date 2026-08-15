#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-irq-usb.sh — QUAL interrupção do USB está inundando a CPU.
#
#  POR QUE EXISTE: em operação normal a ISR do USB entra ~1.100 vezes por
#  segundo. Nos reinícios espontâneos, o rastro mostrou ~23.000 por segundo —
#  vinte vezes mais. A ISR come a CPU, o laço de FFB não roda, para de alimentar
#  o watchdog, e a placa reinicia. Faltava o NOME do evento; este script o diz.
#
#  COMO USAR: rode ANTES (baseline) e DEPOIS de um reinício, sem desligar a base
#  da tomada — os contadores vivem em .noinit e sobrevivem ao reset, mas não à
#  queda de energia. A DIFERENÇA entre as duas leituras é o surto.
#
#  Autor: Luciano Tomé <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tomé — Licença MIT
# ============================================================================
set -u
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
ELF="$RAIZ/firmware-base/build/drivelab-base.elf"
OCD="$HOME/.platformio/packages/tool-openocd"
NM="$HOME/.platformio/packages/toolchain-gccarmnoneeabi/bin/arm-none-eabi-nm.exe"

[ -f "$ELF" ] || { echo "compile o firmware antes (nao achei $ELF)"; exit 1; }

# ⚠️ Endereço derivado do ELF ATUAL, nunca cravado: cada build move os símbolos, e um endereço
# de outro binário lê memória qualquer e devolve número com cara de medição.
BASE=$("$NM" "$ELF" | grep -E " [Bb] g_bb_irq_bits$" | cut -d' ' -f1)
[ -n "$BASE" ] || { echo "g_bb_irq_bits nao existe neste firmware — grave o build novo"; exit 1; }

out=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
  -c "init" -c "echo M:[read_memory 0x$BASE 32 33]" -c "shutdown" 2>&1)

linha=$(echo "$out" | grep -oE '^M:.*')
[ -n "$linha" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

read -r -a v <<< "${linha#M:}"
magic=$((${v[0]}))
if [ "$magic" -ne $((0x1B175A1F)) ]; then   # deixar o shell converter: transcrever o magic em decimal a mao ja custou uma leitura falsa
    echo "contadores ainda nao inicializados (a base bootou agora?)"
    exit 0
fi

# Nomes por bit do GINTSTS (DWC2). Só os que existem em modo device.
nome() { case $1 in
  1) echo "MMIS — acesso indevido a registrador";;
  2) echo "OTGINT — evento OTG";;
  3) echo "SOF — start-of-frame (o host marca o compasso, 1 kHz)";;
  4) echo "RXFLVL — ha pacote recebido no FIFO";;
  5) echo "NPTXFE — FIFO de transmissao vazia";;
  6) echo "GINAKEFF — NAK global de entrada em vigor";;
  7) echo "BOUTNAKEFF — NAK global de saida em vigor";;
 10) echo "ESUSP — suspensao iminente";;
 11) echo "USBSUSP — barramento suspenso";;
 12) echo "USBRST — RESET do barramento (o host re-enumerou)";;
 13) echo "ENUMDNE — enumeracao concluida";;
 14) echo "ISOODRP — pacote isocrono descartado";;
 15) echo "EOPF — fim do periodo de frame";;
 18) echo "IEPINT — interrupcao de endpoint de ENTRADA";;
 19) echo "OEPINT — interrupcao de endpoint de SAIDA";;
 20) echo "IISOIXFR — transferencia isocrona IN incompleta";;
 21) echo "IPXFR — transferencia periodica OUT incompleta";;
 28) echo "CIDSCHG — mudanca de ID do conector";;
 29) echo "DISCINT — desconexao";;
 30) echo "SRQINT — pedido de sessao";;
 31) echo "WKUINT — wakeup";;
  *) echo "bit $1";;
esac; }

echo "INTERRUPCOES DO USB POR CAUSA (acumulado desde que a base foi LIGADA na tomada)"
echo
total=0
# ⚠️ read_memory devolve "0x...": converter SEMPRE antes de comparar, senão o shell
# reclama de "integer expected" e a linha inteira some do relatório.
for i in $(seq 1 32); do total=$((total + $((${v[$i]})) )); done
for i in $(seq 1 32); do
    n=$((${v[$i]}))
    [ "$n" -eq 0 ] && continue
    b=$((i - 1))
    pct=0
    [ "$total" -gt 0 ] && pct=$((n * 100 / total))
    printf "  %-58s %10d  (%2d%%)\n" "$(nome $b)" "$n" "$pct"
done
echo
printf "  %-58s %10d\n" "TOTAL" "$total"
echo
echo "COMO LER: rode isto ANTES e DEPOIS de um reinicio (sem tirar da tomada) — a causa da"
echo "tempestade e a linha que DISPARAR entre as duas leituras, nao a maior em termos absolutos."
echo "SOF sempre domina em operacao normal: sao os 1.000 quadros por segundo do host."
