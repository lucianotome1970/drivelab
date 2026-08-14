#!/usr/bin/env bash
# ============================================================================
#  DriveLab
#  ler-offset-eletrico.sh — Le o alinhamento eletrico do motor pelo SWD e diz
#  se ele REPETE entre boots.
#
#  POR QUE EXISTE: o "phase_offset" cru do ODrive nao serve para comparar dois
#  boots. Ele e a MEDIA da contagem do encoder durante a varredura de
#  calibracao (encoder.cpp:524), entao carrega junto a posicao em que o volante
#  estava. Dois boots podem travar em ciclos eletricos DIFERENTES e mesmo assim
#  estar perfeitamente alinhados — a fase e periodica, ciclo inteiro nao doi.
#
#  O que precisa repetir e o RESTO dentro de um ciclo eletrico:
#      counts_por_ciclo = CPR / pole_pairs
#      resto            = phase_offset mod counts_por_ciclo
#
#  Comparar o numero cru levou a um veredito errado em 14/08/2026 ("o indice nao
#  estabilizou") quando dois dos tres valores estavam a 16 graus eletricos um do
#  outro. Este script faz a conta certa para o erro nao se repetir.
#
#  USO: rodar depois de cada boot, com o motor ARMADO. Anota o resto no arquivo
#  ao lado para a comparacao entre boots ser automatica.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
set -u

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
ELF="$RAIZ/firmware-base/build/drivelab-base.elf"
HIST="${TMPDIR:-/tmp}/drivelab-offsets.txt"
OCD="$HOME/.platformio/packages/tool-openocd"
GDB=$(find "$HOME/.platformio/packages" -name "arm-none-eabi-gdb.exe" 2>/dev/null | head -1)

[ -f "$ELF" ] || { echo "ELF nao encontrado: $ELF (compile antes)"; exit 1; }

# Os enderecos mudam A CADA BUILD — derivar do ELF, nunca cravar.
addr() { "$GDB" -q -batch "$ELF" -ex "print $1" 2>/dev/null | grep -oE '0x[0-9a-f]+' | head -1; }

A_OFS=$(addr "&encoders[0].config_.phase_offset")
A_CPR=$(addr "&encoders[0].config_.cpr")
A_PP=$(addr  "&motors[0].config_.pole_pairs")
A_ARM=$(addr "&motors[0].is_armed_")
A_RDY=$(addr "&encoders[0].is_ready_")
A_IDX=$(addr "&encoders[0].index_found_")
# direction entra na conta porque a fase e multiplicada por ele (encoder.cpp:966).
# Se ele alternar entre boots, dois offsets "diferentes" podem ser o MESMO alinhamento
# visto do outro lado — e dois iguais podem ser opostos.
A_DIR=$(addr "&encoders[0].config_.direction")
# Estourou o teto de espera = o rotor nunca ficou parado, e a calibracao comecou com ele em
# movimento. Quando isto acende, o offset da vez nasceu suspeito e a culpa e mecanica.
A_TMO=$(addr "&encoders[0].calib_settle_timed_out_")

leitura=$("$OCD/bin/openocd.exe" -s "$OCD/openocd/scripts" \
    -f interface/stlink.cfg -f target/stm32f4x.cfg \
    -c "init" \
    -c "echo D:[read_memory $A_OFS 32 1]:[read_memory $A_CPR 32 1]:[read_memory $A_PP 32 1]:[read_memory $A_ARM 8 1]:[read_memory $A_RDY 8 1]:[read_memory $A_IDX 8 1]:[read_memory $A_DIR 32 1]:[read_memory $A_TMO 8 1]" \
    -c "shutdown" 2>&1 | grep -oE '^D:.*')

# "open failed" aqui quase sempre e o ST-Link DESLIGADO de proposito: o boot tem de acontecer sem
# ele energizado (com ele na USB a base nao calibra), entao o fluxo normal e bootar desconectado e
# so plugar para medir. Nao e defeito nem trava — e a ordem certa.
[ -n "$leitura" ] || { echo "SWD nao respondeu — o ST-Link esta na USB? (o boot e feito com ele fora; plugue agora para medir)"; exit 1; }

IFS=':' read -r _ ofs cpr pp armado pronto idx dir tmo <<< "$leitura"
d() { printf "%d" "$1"; }
ofs=$(d "$ofs"); cpr=$(d "$cpr"); pp=$(d "$pp")

# Leitura so vale com o motor ARMADO e o indice achado. Desarmado, o que esta na config e o offset
# CARREGADO DA NVM (ou o da sessao anterior) — nao o desta calibracao. Gravar isso no historico
# envenena a comparacao entre boots, que foi o que aconteceu na primeira versao deste script.
valida=1
if [ "$(d "$armado")" -eq 0 ]; then
    echo "MOTOR DESARMADO — o offset lido e o carregado da NVM, nao o desta sessao. NAO conta na serie."
    valida=0
fi
if [ "$(d "$idx")" -eq 0 ]; then
    echo "INDICE NAO ENCONTRADO — gire o volante uma volta inteira (cruza o Z) antes de ativar."
    valida=0
fi

# awk porque o resto e fracionario: 4000/15 = 266,67 counts por ciclo eletrico.
read -r ciclo resto graus <<< "$(awk -v o="$ofs" -v c="$cpr" -v p="$pp" 'BEGIN{
    ciclo = c / p;
    r = o % ciclo;            # awk faz modulo em ponto flutuante
    if (r < 0) r += ciclo;
    printf "%.1f %.1f %.0f", ciclo, r, r / ciclo * 360;
}')"

dir=$(printf "%d" "$dir")
[ "$dir" -gt 2147483647 ] 2>/dev/null && dir=$((dir - 4294967296))   # -1 chega como 0xffffffff

if [ "$(d "$tmo")" -ne 0 ]; then
    echo "AVISO: o rotor nao assentou dentro do teto de 3 s — offset desta vez e SUSPEITO."
    echo "       Causa costuma ser mecanica: volante encostando, atrito, ou mao no aro."
fi

echo "phase_offset=$ofs  direction=$dir  (CPR=$cpr  pares=$pp  ciclo=$ciclo counts)"
echo "ALINHAMENTO: resto=$resto counts = ${graus} graus eletricos   <<< e ESTE que tem de repetir"

if [ "$valida" -eq 0 ]; then
    echo
    echo "(leitura NAO gravada na serie — ative o motor e rode de novo)"
    exit 0
fi

if [ -f "$HIST" ]; then
    echo
    echo "boots anteriores (resto em graus):"
    cat "$HIST"
    # Distancia CIRCULAR: 350 e 10 graus estao a 20 graus, nao a 340.
    # O veredito NAO pode sair so da pior divergencia. Uma serie que converge para o mesmo valor com
    # um desvio isolado no meio ("296, 296, 191, 296") e um resultado BOM — tem um valor verdadeiro
    # repetivel e um boot ruim —, mas pela pior divergencia ela aparece igualzinha a uma serie
    # totalmente aleatoria. Foi assim que quase descartei uma correcao que estava funcionando.
    # Entao contamos tambem quantos boots caem em torno do valor MAIS FREQUENTE.
    awk -v atual="$graus" -v dir="$dir" '{
        g[NR] = $1;
        d = (atual - $1); d = d % 360; if (d < 0) d += 360; if (d > 180) d = 360 - d;
        if (d > pior) pior = d;
        if (NF > 1 && $2 != dir) trocou = 1;
    } END {
        g[NR+1] = atual;   # o boot de agora entra na conta
        total = NR + 1;

        # Maior grupo de boots dentro de 20 graus uns dos outros (tolerancia de medicao).
        melhor = 0; centro = atual;
        for (i = 1; i <= total; i++) {
            n = 0;
            for (j = 1; j <= total; j++) {
                d = (g[i] - g[j]); d = d % 360; if (d < 0) d += 360; if (d > 180) d = 360 - d;
                if (d <= 20) n++;
            }
            if (n > melhor) { melhor = n; centro = g[i]; }
        }

        if (trocou) print "\n*** direction MUDOU entre boots — a fase inverte de sinal (encoder.cpp:966).";
        printf "\nboots nesta serie: %d | concordam em torno de %.0f graus: %d | pior divergencia: %.0f graus\n",
               total, centro, melhor, pior;

        if (melhor == total)        printf "ESTAVEL — todos os %d boots deram o mesmo alinhamento.\n", total;
        else if (melhor * 2 > total) printf "CONVERGINDO — %d de %d caem em %.0f graus; os outros sao desvios isolados.\n", melhor, total, centro;
        else                         print "INSTAVEL — sem valor recorrente; o lock-in nao esta assentando.";
    }' "$HIST"
fi
echo "$graus $dir" >> "$HIST"
echo
echo "(historico em $HIST — apague para comecar uma serie nova)"
