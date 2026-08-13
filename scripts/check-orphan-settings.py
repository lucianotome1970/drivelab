#!/usr/bin/env python3
# ============================================================================
#  DriveLab
#  check-orphan-settings.py — Acusa setting que o app transporta e o firmware
#  IGNORA.
#
#  POR QUE EXISTE: o app e o firmware sao dois lados que so se encontram no fio.
#  Um campo pode nascer no app com a intencao certa e nunca ser lido do outro
#  lado — e nada acusa. Ele fica na tela, a pessoa ajusta, salva, o valor volta
#  correto ao reiniciar (porque E salvo), e nao acontece nada.
#
#  Foi assim com o CPR do encoder: cravado em 4000 no firmware enquanto a tela
#  deixava escolher. Quem tinha um encoder de 2500 PPR rodava com a base lendo
#  uma volta como duas e meia, sem nenhuma pista do porque.
#
#  Este script nao conserta os orfaos existentes — ele impede que a lista
#  CRESCA. Cada um que for implementado sai da lista abaixo, e ela so encolhe.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
import re
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent

# Orfaos CONHECIDOS em 2026-08-10. Esta lista e uma divida registrada, nao uma
# permissao: ao implementar um deles no firmware, REMOVA a linha daqui.
# Acrescentar linha aqui so se houver motivo escrito no commit.
ORFAOS_CONHECIDOS = {
    "PositionSmoothing",
    "PowerLimit",
    "BrakingLimit",
    "CoggingEnable",         # a compensacao de cogging nao existe no firmware
    "ThermalContinuousPct",  # envelope de potencia: depende de medir o motor, nao a placa
    "ThermalPeakSeconds",
    "MotorTempLimitC",       # exige NTC no enrolamento — o dos FETs ja esta ligado
    "SoftPowerEnable",       # groundwork opt-in, hardware nao fiado
    "PowerButtonEnable",
}


def ids_do_app():
    texto = (RAIZ / "app/DriveLab.Core/Settings/BaseSettingId.cs").read_text(encoding="utf-8")
    return {int(m.group(2)): m.group(1)
            for m in re.finditer(r"^\s*(\w+)\s*=\s*(\d+),", texto, re.M)}


def indices_usados_no_firmware():
    usados = set()
    for caminho in (RAIZ / "firmware-base/src").glob("*.cpp"):
        texto = caminho.read_text(encoding="utf-8", errors="replace")
        # O corpo de a0_load_defaults ATRIBUI os padroes — nao e consumo. Contar aquilo como uso
        # esconde orfaos: foi assim que CurrentP e CurrentI passaram batido, aparecendo so ali.
        texto = re.sub(r"static void a0_load_defaults\(void\)\s*\{.*?\n\}", "", texto, flags=re.S)
        # leitura direta dos arrays de setting
        for m in re.finditer(r"s_[if]val\[(\d+)\]", texto):
            usados.add(int(m.group(1)))
        # leitura pelo acessor: enum SET_XXX = N, depois a0_get_setting(SET_XXX).
        # O sufixo _f e o acessor dos campos T_FLOAT (o valor mora em s_fval, nao em s_ival) —
        # sem ele na expressao, um setting float LIDO pelo firmware seguiria contando como orfao.
        constantes = {m.group(1): int(m.group(2))
                      for m in re.finditer(r"(SET_[A-Z0-9_]+)\s*=\s*(\d+)", texto)}
        for nome, valor in constantes.items():
            if re.search(r"a0_get_setting(?:_f)?\(\s*" + nome, texto):
                usados.add(valor)
    return usados


# ---------------------------------------------------------------------------
# O aviso do "?" precisa contar a verdade
# ---------------------------------------------------------------------------
# O texto de ajuda de cada campo traz "[ATENCAO] ... ainda nao tem efeito" quando o firmware ignora
# o setting. Esse aviso ENVELHECE: assim que alguem implementa a leitura, o campo passa a funcionar
# e a tela continua dizendo que nao faz nada — pior que nao avisar, porque o usuario deixa de mexer
# num controle que ja funciona. Aconteceu em 2026-08-10 com cinco campos de uma vez (CPR, pares de
# polos, corrente de calibracao, modelo e tecnologia do encoder).
#
# Regra: quem tem aviso tem que estar na lista de orfaos, e vice-versa.
AVISO = "[ATENCAO]"
RESX_PT = RAIZ / "app/DriveLab.Studio/Localization/StringsPt.resx"

# Setting que o FIRMWARE ignora mas que NAO e inutil: quem consome e o proprio app. Avisar "nao tem
# efeito" nestes seria mentira ao contrario — o usuario deixaria de preencher um campo que funciona.
# Cada entrada precisa dizer QUEM consome, senao vira desculpa para nao implementar.
#
# Vazia desde 2026-08-11: o Kt era o unico morador e saiu ao deixar de ser orfao — o firmware passou
# a aplicar o valor medido em motor.config.torque_constant, no lugar do 0,55 de catalogo que estava
# cravado. O app continua usando o mesmo campo para o torque estimado do monitor; os dois usos
# convivem, e e por isso que "usado pelo app" nunca foi argumento para nao implementar no firmware.
USADOS_PELO_APP = set()   # set() e nao {}, que em Python da um DICT vazio e quebra a subtracao


def avisados_no_resx():
    """Settings cujo texto de ajuda diz que nao tem efeito."""
    if not RESX_PT.exists():
        return None
    texto = RESX_PT.read_text(encoding="utf-8")
    achados = set()
    for linha in texto.splitlines():
        m = re.search(r'name="SettingHelp_([A-Za-z0-9_]+)"', linha)
        if m and AVISO in linha:
            achados.add(m.group(1))
    return achados


def conferir_avisos(orfaos):
    avisados = avisados_no_resx()
    if avisados is None:
        return 0
    inertes = orfaos - USADOS_PELO_APP     # ignorados pelo firmware E sem consumidor no app
    mentindo = sorted(avisados - inertes)  # diz que nao funciona, mas funciona
    calados = sorted(inertes - avisados)   # nao funciona e a tela nao avisa
    if mentindo:
        print("\nO '?' AVISA QUE NAO FUNCIONA, MAS O FIRMWARE JA LE:")
        for nome in mentindo:
            print(f"  - {nome}  -> tire o {AVISO} de SettingHelp_{nome} nos dois resx")
    if calados:
        print("\nO FIRMWARE IGNORA E O '?' NAO AVISA:")
        for nome in calados:
            print(f"  - {nome}  -> acrescente o aviso em SettingHelp_{nome} nos dois resx")
    return 1 if (mentindo or calados) else 0


def main():
    nomes = ids_do_app()
    usados = indices_usados_no_firmware()

    orfaos = {nome for idx, nome in nomes.items() if idx not in usados}
    novos = sorted(orfaos - ORFAOS_CONHECIDOS)
    resolvidos = sorted(ORFAOS_CONHECIDOS - orfaos)

    print(f"settings no app: {len(nomes)} | lidos pelo firmware: {len(orfaos ^ set(nomes.values()))} "
          f"| orfaos: {len(orfaos)}")

    if resolvidos:
        print("\nJa implementados — REMOVA da lista ORFAOS_CONHECIDOS deste script:")
        for nome in resolvidos:
            print(f"  - {nome}")

    falha_aviso = conferir_avisos(orfaos)

    if novos:
        print("\nSETTING NOVO QUE O FIRMWARE IGNORA:")
        for nome in novos:
            print(f"  - {nome}")
        print("\nO campo aparece na tela, e salvo na placa e NAO FAZ NADA. Ou implemente a leitura")
        print("no firmware, ou acrescente a ORFAOS_CONHECIDOS com o motivo no commit.")
        return 1

    if falha_aviso:
        return 1

    print("nenhum orfao novo; avisos do '?' batem com a realidade")
    return 0


if __name__ == "__main__":
    sys.exit(main())
