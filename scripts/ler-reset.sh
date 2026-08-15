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
R=$(a g_bb_reset_reason)
CSR=$(a g_bb_reset_csr)
# +4: o primeiro campo do BlackBoxBoots e o magic; o contador de boots vem logo depois.
B=$(printf '0x%x' $(( $(a g_bb_boots) + 4 )))
S=$(a g_bb_trace_prev_step); L=$(a g_bb_trace_prev_last); T=$(a g_bb_trace_prev_tick)
V=$(a g_bb_trace_prev_vbus_mv); I=$(a g_bb_trace_prev_iq_ma); P=$(a g_bb_trace_prev_pos_mrad)
UT=$(a g_bb_trace_prev_usb_task); UI=$(a g_bb_trace_prev_usb_irq)
F=$(a g_bb_fault)

out=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" -f interface/stlink.cfg -f target/stm32f4x.cfg \
  -c "init" \
  -c "echo D:[read_memory $R 32 1]:[read_memory $B 32 1]:[read_memory $S 32 1]:[read_memory $L 32 1]:[read_memory $T 32 1]" \
  -c "echo S:[read_memory $CSR 32 1]" \
  -c "echo C:[read_memory $V 32 1]:[read_memory $I 32 1]:[read_memory $P 32 1]" \
  -c "echo U:[read_memory $UT 32 1]:[read_memory $UI 32 1]" \
  -c "echo E:[read_memory $F 32 7]" \
  -c "shutdown" 2>&1)
linha=$(echo "$out" | grep -oE '^D:.*')
[ -n "$linha" ] || { echo "SWD nao respondeu — o ST-Link esta na USB?"; exit 1; }

# Cada read_memory devolve UM valor de proposito: pedindo dois, eles vem separados por ESPACO
# dentro do mesmo campo, e o split por ':' os deixa grudados — foi assim que os numeros sairam
# trocados na primeira versao deste script.
IFS=':' read -r _ razao boots step last tick <<< "$linha"
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

# ⚠️ TODAS as flags, e nao so a "vencedora" da ordem de prioridade acima.
#
# A decodificacao escolhe UMA causa, e isso escondeu o diagnostico real em 15/08/2026: o CSR estava
# em 0x1E000003 — software, power-on, pino E BROWN-OUT acesos ao mesmo tempo — e como 'software' vem
# antes de 'brown-out' na ordem, o script reportou "SOFTWARE" varias vezes seguidas. Passamos horas
# procurando quem mandava a base reiniciar, quando o que a placa dizia era que a TENSAO CAIU.
#
# As flags vem combinadas por natureza (um power-on normal acende POR e PIN juntos), entao escolher
# uma e util para ler rapido — mas esconder as outras nao. Agora aparecem todas, e brown-out ganha
# destaque proprio porque e a unica que aponta para fora do firmware.
csr=$(echo "$out" | grep -oE '^S:0x[0-9a-f]+' | cut -d: -f2)

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
  9) onde="TELEMETRIA: a0_service (canal do app)";;
 10) onde="TELEMETRIA: hid_send_joystick (antes do TinyUSB)";;
 11) onde="TELEMETRIA: DENTRO do tud_hid_report — mutex do endpoint do TinyUSB";;
 12) onde="a0_service: tud_hid_ready()";;
 13) onde="a0_service: enviando a RESPOSTA DE LEITURA (0x16) — dentro do TinyUSB";;
 14) onde="a0_service: montando a telemetria (nosso codigo, sem USB)";;
 15) onde="a0_service: enviando a TELEMETRIA (0x21) — dentro do TinyUSB";;
  *) onde="trecho $step";;
esac

echo "causa do ultimo reset : $causa"
if [ -n "$csr" ]; then
    flags=$(awk -v v=$(printf "%d" "$csr") 'BEGIN{
        n=split("25 BROWN-OUT(a tensao caiu),26 pino NRST,27 POWER-ON,28 SOFTWARE,29 WATCHDOG,30 window-watchdog,31 low-power", a, ",")
        out=""
        for (i=1;i<=n;i++){ split(a[i],b," "); bit=b[1]+0
            nome=substr(a[i], length(b[1])+2)
            if (int(v / 2^bit) % 2) out = out (out==""?"":", ") nome }
        print out }')
    echo "flags acesas no CSR   : $flags"
    case "$flags" in *BROWN-OUT*)
        echo "  ⚠️ BROWN-OUT presente: a tensao caiu abaixo do limiar. Isso e ALIMENTACAO —"
        echo "     fonte, cabo ou contato — e nao firmware. Confira antes de caçar bug de codigo.";;
    esac
fi
echo "boots desde a tomada  : $(d "$boots")"
echo
if [ "$(d "$step")" -eq 0 ]; then
    echo "rastro do laco        : vazio (a energia caiu, ou este e o primeiro boot do ciclo)"
else
    echo "onde estava           : $onde"
    echo "trecho anterior       : $(d "$last")"
    echo "voltas do laco        : $(d "$tick")"

    # Complemento de dois: corrente e posicao sao assinados e o read_memory devolve cru.
    s32(){ v=$(printf "%d" "$1"); [ "$v" -gt 2147483647 ] && v=$((v - 4294967296)); echo "$v"; }
    cond=$(echo "$out" | grep -oE '^C:.*')
    if [ -n "$cond" ]; then
        IFS=':' read -r _ vbus iq pos <<< "$cond"
        echo
        echo "AS CONDICOES NO INSTANTE (o que separa causa de coincidencia):"
        awk -v v="$(s32 "$vbus")" -v i="$(s32 "$iq")" -v p="$(s32 "$pos")" 'BEGIN{
            printf "  barramento : %.2f V\n", v/1000
            printf "  corrente   : %.1f A\n", i/1000
            printf "  posicao    : %.0f graus\n", p*57.29578/1000
            a = (i<0) ? -i : i
            if (a > 8000) print "  -> corrente ALTA: o transiente eletrico entra como suspeito"
            else          print "  -> corrente baixa: nao foi pico de consumo. Olhe o USB"
        }'
    fi

    # A PILHA USB no instante do travamento. Estes contadores vivem na .noinit justamente para
    # sobreviver ao reset — variavel comum zera no boot e a medicao se perde, que foi o que
    # aconteceu na primeira versao deles.
    usb=$(echo "$out" | grep -oE '^U:.*')
    if [ -n "$usb" ]; then
        IFS=':' read -r _ utask uirq <<< "$usb"
        echo
        echo "A PILHA USB quando o laco parou:"
        printf "  tarefa do TinyUSB : %d voltas\n"   "$(d "$utask")"
        printf "  interrupcao do USB: %d entradas\n" "$(d "$uirq")"
        awk -v t="$(d "$utask")" -v i="$(d "$uirq")" 'BEGIN{
            if (t==0 && i==0)      print "  -> os DOIS zerados: o USB nunca subiu neste boot"
            else if (i>0 && t==0)  print "  -> interrupcao viva, TAREFA parada: travou na tarefa (mutex/espera)"
            else if (i==0 && t>0)  print "  -> tarefa viva, INTERRUPCAO parada: o host parou de falar"
            else                   print "  -> os dois vivos: a pilha nao travou; o problema e outro"
        }'
    fi
fi

falha=$(echo "$out" | grep -oE '^E:.*')
if [ -n "$falha" ] && ! echo "$falha" | grep -q "E:0x0 "; then
    read -r _ magic kind pc lr cfsr task cnt <<< "${falha//:/ }"
    echo
    case $(d "$kind") in
      1) echo "TRAVOU POR HARD FAULT"
         printf "  pc=0x%x  lr=0x%x  cfsr=0x%x\n" "$pc" "$lr" "$cfsr"
         echo "  (traduza o pc com: arm-none-eabi-addr2line -f -e $ELF $pc)";;
      2) # Nome da tarefa: 4 chars empacotados little-endian (ver o hook em main.cpp).
         t=$(d "$task")
         nome=$(printf "$(printf '\\x%02x\\x%02x\\x%02x\\x%02x' \
                  $((t & 0xff)) $(((t>>8) & 0xff)) $(((t>>16) & 0xff)) $(((t>>24) & 0xff)))" \
               | tr -d '\0')
         echo "TRAVOU POR ESTOURO DE PILHA — a tarefa passou do fim da propria pilha"
         echo "  tarefa: '$nome'"
         echo "  Aumente a pilha DELA; nao adianta mexer nas outras.";;
      3) echo "TRAVOU POR HEAP ESGOTADO (malloc do FreeRTOS falhou)";;
      *) echo "TRAVAMENTO registrado, tipo desconhecido: $falha";;
    esac
    printf "  ocorrencias desde o power-on: %d\n" "$(d "$cnt")"
fi
