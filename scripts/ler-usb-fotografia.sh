#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-usb-fotografia.sh — Retrato COMPLETO do periferico USB no momento em
#  que a base fica muda.
#
#  POR QUE EXISTE: quando a base trava, o PC deixa de falar com ela E derruba
#  os outros controles do mesmo hub. Um PC so para de mandar sincronismo para
#  um dispositivo quando DESABILITA a porta dele, e ele faz isso depois de
#  erros seguidos de transacao — ou seja, ha um endpoint nosso que parou de
#  responder. Qual, e em que estado, nao da para deduzir: tem de ser lido.
#
#  Este script tira o retrato dos registradores do periferico e TRADUZ. Nao
#  para o nucleo (parar a CPU com o motor armado o derruba).
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
set -u
RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
OCD="$HOME/.platformio/packages/tool-openocd"

R() {  # le uma lista de enderecos absolutos
  "$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
    -c "init" -c "echo D:$1" -c "shutdown" 2>&1 | grep -oE '^D:.*' | cut -c3-
}

B=0x50000000
LISTA="[read_memory $((B+0x000)) 32 1]:[read_memory $((B+0x008)) 32 1]:[read_memory $((B+0x014)) 32 1]:[read_memory $((B+0x018)) 32 1]:[read_memory $((B+0x800)) 32 1]:[read_memory $((B+0x804)) 32 1]:[read_memory $((B+0x808)) 32 1]:[read_memory $((B+0x818)) 32 1]:[read_memory $((B+0x81C)) 32 1]:[read_memory $((B+0x834)) 32 1]:[read_memory $((B+0x900)) 32 1]:[read_memory $((B+0x920)) 32 1]:[read_memory $((B+0x940)) 32 1]:[read_memory $((B+0x908)) 32 1]:[read_memory $((B+0x928)) 32 1]:[read_memory $((B+0x948)) 32 1]:[read_memory $((B+0x910)) 32 1]:[read_memory $((B+0x930)) 32 1]:[read_memory $((B+0x950)) 32 1]"

D=$(R "$LISTA")
[ -n "$D" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }
IFS=':' read -r GOTGCTL GAHBCFG GINTSTS GINTMSK DCFG DCTL DSTS DAINT DAINTMSK DIEPEMPMSK \
                 IC0 IC1 IC2 II0 II1 II2 IT0 IT1 IT2 <<< "$D"

h(){ printf "0x%08x" $(($1)); }
bit(){ echo $((( $(($1)) >> $2) & 1)); }
sn(){ [ "$1" = "1" ] && echo SIM || echo nao; }

echo "── LIGACAO FISICA ─────────────────────────────────────"
printf "VBUS (o PC esta alimentando) : %s\n" "$(sn $(bit $GOTGCTL 19))"
printf "pull-up ligado (nos nos anunciamos): %s\n" "$([ $(bit $DCTL 1) -eq 1 ] && echo "NAO — desconectados de proposito" || echo sim)"
printf "endereco recebido do PC     : %d\n" $(( ($(($DCFG)) >> 4) & 0x7F ))
printf "contador de frames          : %d\n" $(( ($(($DSTS)) >> 8) & 0x3FFF ))
printf "estado suspenso             : %s\n" "$(sn $(bit $DSTS 0))"
echo
echo "── INTERRUPCOES ───────────────────────────────────────"
printf "pendentes (GINTSTS) : %s\n" "$(h $GINTSTS)"
printf "habilitadas (GINTMSK): %s\n" "$(h $GINTMSK)"
printf "  reset de barramento pendente: %s | enumeracao concluida: %s\n" \
       "$(sn $(bit $GINTSTS 12))" "$(sn $(bit $GINTSTS 13))"
printf "  suspensao pendente: %s | dados a receber na FIFO: %s\n" \
       "$(sn $(bit $GINTSTS 11))" "$(sn $(bit $GINTSTS 4))"
printf "  ENTREGA GLOBAL de interrupcao ligada (GAHBCFG): %s\n" "$(sn $(bit $GAHBCFG 0))"
echo
echo "── ENDPOINTS DE ENTRADA (nos -> PC) ───────────────────"
nome(){ case $1 in 0) echo "EP0 controle (as perguntas do PC)";; 1) echo "EP1 canal do JOGO";; 2) echo "EP2 canal do APP";; esac; }
for i in 0 1 2; do
  case $i in 0) C=$IC0; I=$II0; T=$IT0;; 1) C=$IC1; I=$II1; T=$IT1;; 2) C=$IC2; I=$II2; T=$IT2;; esac
  printf "%s\n" "$(nome $i)"
  printf "   ligado: %s | NAK forcado: %s | desabilitado por erro: %s\n" \
         "$(sn $(bit $C 31))" "$(sn $(bit $C 27))" "$(sn $(bit $C 30))"
  printf "   bytes ainda por enviar: %d   (>0 parado = transferencia PRESA)\n" $(( $(($T)) & 0x7FFFF ))
  printf "   avisos pendentes deste endpoint: %s\n" "$(h $I)"
done
echo
printf "aviso de FIFO vazia armada para: %s\n" "$(h $DIEPEMPMSK)"
printf "endpoints com aviso pendente   : %s (habilitados: %s)\n" "$(h $DAINT)" "$(h $DAINTMSK)"
echo
echo "── O QUE ISSO QUER DIZER ──────────────────────────────"
if [ $(bit $GOTGCTL 19) -eq 0 ]; then
  echo "O PC NAO ESTA ALIMENTANDO a porta: caiu a ligacao fisica (cabo/hub), nao e software."
elif [ $(bit $DCTL 1) -eq 1 ]; then
  echo "NOS nos desconectamos do barramento (pull-up desligado) — foi o firmware que soltou."
elif [ $(( ($(($DSTS)) >> 8) & 0x3FFF )) -eq 0 ]; then
  echo "Sem sincronismo do PC: a porta foi DESABILITADA do outro lado."
  echo "Olhe acima qual endpoint ficou com bytes por enviar — e o que parou de responder."
else
  echo "O barramento esta vivo neste instante."
fi
