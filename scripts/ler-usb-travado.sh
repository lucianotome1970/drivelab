#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-usb-travado.sh — Quando a base "congela" o jogo e o Windows: de que lado
#  parou, o nosso ou o do PC?
#
#  POR QUE EXISTE: o sintoma "a base trava tudo e so libera quando eu desligo"
#  tem duas causas opostas, e elas pedem consertos opostos:
#    · o PC parou de falar com a base  -> suspensao/porta desabilitada, e o
#      conserto e no Windows (suspensao seletiva, porta, hub);
#    · a base parou de responder       -> e nosso, e o conserto e no firmware.
#  Deduzir pelo sintoma nao separa os dois. Este script separa, em 3 segundos.
#
#  COMO SEPARA: o contador de frames do USB (DSTS) avanca a cada milissegundo
#  enquanto o host manda SOF. Ele e do HARDWARE — nao depende de o firmware
#  estar bem. Duas leituras espacadas dizem tudo:
#    frame ANDANDO + sem interrupcao  -> o host fala, nos nao ouvimos: NOSSO
#    frame PARADO                     -> o host calou: do lado do PC
#
#  ⚠️ LE SEM PARAR O NUCLEO. Parar a CPU com o motor armado o derruba.
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

# Enderecos do periferico USB sao FIXOS (registradores); os do firmware mudam a cada build.
BB=$("$GDB" -q -batch "$ELF" -ex "print &g_bb_trace" 2>/dev/null | grep -oE '0x[0-9a-f]{6,}' | head -1)
U=$(printf "0x%x" $((0x${BB#0x} + 28)))

ler() {
  "$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
    -c "init" \
    -c "echo D:[read_memory 0x50000808 32 1]:[read_memory 0x50000014 32 1]:[read_memory 0x50000804 32 1]:[read_memory 0xE000E108 32 1]:[read_memory $U 32 7]" \
    -c "shutdown" 2>&1 | grep -oE '^D:.*'
}

a=$(ler); sleep 2; b=$(ler)
[ -n "$a" ] && [ -n "$b" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

campos() { echo "$1" | cut -d: -f$2; }
d(){ printf "%d" "$1"; }

fa=$(( ( $(d $(campos "$a" 2)) >> 8) & 0x3FFF ));  fb=$(( ( $(d $(campos "$b" 2)) >> 8) & 0x3FFF ))
susp=$(( $(d $(campos "$b" 2)) & 1 ))
gint=$(campos "$b" 3)
dctl=$(( ( $(d $(campos "$b" 4)) >> 1) & 1 ))
nvic=$(( ( $(d $(campos "$b" 5)) >> 3) & 1 ))
read -r ta ia _ _ _ _ _ <<< "$(campos "$a" 6)"
read -r tb ib claim dwc2 txfe rel passo <<< "$(campos "$b" 6)"

# Nome do ponto do laco da pilha USB (BB_USBT_* em blackbox.h).
nome_passo() {
  case "$(d $1)" in
    0)  echo "fora do laco (normal)";;
    1)  echo "esperando evento na fila";;
    2)  echo "tratando reset de barramento";;
    3)  echo "RESPONDENDO A UMA PERGUNTA DO PC (setup)";;
    4)  echo "avisando a classe que a transferencia terminou";;
    5)  echo "suspensao / retomada";;
    10) echo "TOMANDO o endpoint (mutex)";;
    11) echo "DEVOLVENDO o endpoint (mutex)";;
    *)  echo "desconhecido ($1)";;
  esac
}

echo "── O PC ESTA FALANDO COM A BASE? ──────────────────────"
printf "contador de frames do USB : %d -> %d   (%s)\n" "$fa" "$fb" \
       "$([ "$fa" != "$fb" ] && echo ANDANDO || echo PARADO)"
printf "suspenso pelo host        : %s\n" "$([ $susp -eq 1 ] && echo SIM || echo nao)"
printf "auto-desconectado         : %s\n" "$([ $dctl -eq 1 ] && echo SIM || echo nao)"
printf "interrupcao habilitada    : %s\n" "$([ $nvic -eq 1 ] && echo sim || echo NAO)"
echo
echo "── A BASE ESTA OUVINDO? ───────────────────────────────"
printf "tarefa do TinyUSB : %d -> %d\n" "$(d $ta)" "$(d $tb)"
printf "interrupcoes USB  : %d -> %d\n" "$(d $ia)" "$(d $ib)"
printf "eventos pendentes : %s\n" "$gint"
echo
echo "── VEREDITO ───────────────────────────────────────────"
if [ "$fa" = "$fb" ]; then
  if [ $susp -eq 1 ]; then
    # ⚠️ NAO CONCLUA "e do PC" AQUI. Este script ja disse isso uma vez, e estava errado.
    # Suspensao e o que o host faz com um dispositivo que parou de responder: primeiro nos calamos,
    # depois ele desiste. A ordem dos acontecimentos nao aparece na foto — mas a tarefa da pilha
    # aparece. Se ela esta PARADA, a causa e nossa, e o campo "onde a tarefa esta" diz o ponto.
    if [ "$(d $ta)" = "$(d $tb)" ]; then
      echo "⚠️ SUSPENSO, E A NOSSA TAREFA DA PILHA ESTA PARADA."
      echo "A suspensao aqui e CONSEQUENCIA: paramos de responder e o host desistiu."
      echo "O ponto exato esta em \"onde a tarefa esta\", acima — e ai que ela ficou."
    else
      echo "Suspenso pelo host, e a nossa tarefa continua andando. Este caso sim aponta para o"
      echo "lado do PC (suspensao seletiva de USB, energia da porta/hub)."
    fi
  else
    echo "O HOST PAROU DE FALAR (sem frames e sem suspensao) — porta desabilitada pelo"
    echo "Windows, cabo ou hub. Tambem do lado do PC."
  fi
elif [ "$(d $ib)" = "$(d $ia)" ]; then
  echo "⚠️ O HOST FALA E NOS NAO OUVIMOS: os frames andam e nenhuma interrupcao chega."
  echo "Isso e NOSSO — o periferico esta gerando frames mas a interrupcao nao roda."
  echo "Olhe os eventos pendentes acima e a mascara de interrupcao."
else
  echo "Os dois lados estao vivos: o congelamento nao esta aqui neste momento."
fi
