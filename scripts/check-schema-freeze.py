#!/usr/bin/env python3
# ============================================================================
#  DriveLab
#  check-schema-freeze.py — Protege a configuracao gravada nas placas dos
#  usuarios contra mudancas incompativeis no schema de settings.
#
#  POR QUE EXISTE: a partir do momento em que uma placa que nao e nossa tem
#  configuracao salva, o id de um setting deixa de ser um detalhe de codigo e
#  passa a ser um CONTRATO. A gravacao por chave/valor (nvm_kv) ja resolveu a
#  parte facil: acrescentar ou remover campo nao desloca mais nada. Sobrou a
#  parte que nenhum formato de arquivo resolve sozinho —
#
#    · RECICLAR um id: o valor que o usuario salvou para o setting antigo passa
#      a ser lido como se fosse o novo. Nao ha erro, nao ha CRC quebrado: a base
#      arma e funciona ERRADO. Os ids 12 e 13 (CurrentP/CurrentI) ja foram
#      aposentados assim, com o comentario certo no enum — este script e o que
#      impede que a proxima vez dependa de alguem lembrar.
#
#    · Mudar UNIDADE, TIPO ou FAIXA de um id em uso: a chave e a mesma, o valor
#      e lido normalmente, e o numero muda de significado. Um teto de torque que
#      era "Nm x 10" virando "Nm" transforma os 150 gravados em 150 Nm. Num
#      direct drive isso nao e bug de configuracao, e risco fisico.
#
#  COMO USAR
#    python3 scripts/check-schema-freeze.py            # verifica (roda no test.sh)
#    python3 scripts/check-schema-freeze.py --freeze   # adota o schema atual
#
#  O --freeze e deliberado: acrescentar setting novo exige rodar e commitar o
#  congelamento junto, o que poe a mudanca de contrato no diff, onde da para ver.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
import json
import re
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
SCHEMA_CS = RAIZ / "app/DriveLab.Core/Settings/BaseSettingsSchema.cs"
IDS_CS = RAIZ / "app/DriveLab.Core/Settings/BaseSettingId.cs"
CONGELADO = RAIZ / "docs/settings-schema-congelado.json"

# Ids que NUNCA podem voltar a ser usados. Um id aposentado carrega valores nas
# placas de quem ja salvou; reaproveita-lo faz o firmware novo ler aquele valor
# como se fosse do setting novo.
APOSENTADOS = {
    12: "CurrentP — o ODrive deriva os ganhos do motor e da banda (ver CurrentBandwidthHz)",
    13: "CurrentI — idem",
}
# Ids que existem no fio mas nao sao configuracao do usuario.
INTERNOS = {
    47: "build_id — identificacao do firmware, so leitura",
}


def ler_ids():
    texto = IDS_CS.read_text(encoding="utf-8")
    return {int(m.group(2)): m.group(1)
            for m in re.finditer(r"^\s*(\w+)\s*=\s*(\d+),", texto, re.M)}


def ler_schema():
    """Extrai (id, chave, tipo, min, max, unidade, aba, default) de cada descritor."""
    texto = SCHEMA_CS.read_text(encoding="utf-8")
    nomes = {v: k for k, v in ler_ids().items()}
    padrao = re.compile(
        r'new\(BaseSettingId\.(\w+),\s*"([^"]*)",\s*"[^"]*",\s*'
        r'SettingType\.(\w+),\s*([-\d.]+),\s*([-\d.]+),\s*"([^"]*)",\s*'
        r'SettingTab\.(\w+),\s*([^,)]+)')
    out = {}
    for m in padrao.finditer(texto):
        nome = m.group(1)
        if nome not in nomes:
            print(f"ERRO: BaseSettingId.{nome} esta no schema mas nao tem id no enum")
            sys.exit(1)
        out[nomes[nome]] = {
            "nome": nome,
            "chave": m.group(2),
            "tipo": m.group(3),
            "min": float(m.group(4)),
            "max": float(m.group(5)),
            "unidade": m.group(6),
            "default": m.group(8).strip(),
        }
    return out


def congelar(atual):
    dados = {
        "_comentario": "Contrato dos settings gravados nas placas. NAO editar a mao — "
                       "use scripts/check-schema-freeze.py --freeze e commite junto.",
        "schema_version": 1,
        "aposentados": {str(k): v for k, v in APOSENTADOS.items()},
        "internos": {str(k): v for k, v in INTERNOS.items()},
        "settings": {str(k): v for k, v in sorted(atual.items())},
    }
    CONGELADO.write_text(json.dumps(dados, indent=2, ensure_ascii=False) + "\n",
                         encoding="utf-8")
    print(f"congelado: {len(atual)} settings em {CONGELADO.relative_to(RAIZ)}")


def main():
    atual = ler_schema()

    if "--freeze" in sys.argv:
        congelar(atual)
        return 0

    if not CONGELADO.exists():
        print("sem snapshot congelado — rode com --freeze e commite o resultado")
        return 1

    dados = json.loads(CONGELADO.read_text(encoding="utf-8"))
    antes = {int(k): v for k, v in dados["settings"].items()}

    graves, avisos, novos = [], [], []

    for sid, a in sorted(antes.items()):
        if sid not in atual:
            if sid in APOSENTADOS:
                continue
            graves.append(f"id {sid} ({a['nome']}) sumiu do schema sem entrar em APOSENTADOS — "
                          f"placas com esse valor salvo ficam com um id orfao que alguem "
                          f"pode reaproveitar sem saber")
            continue
        d = atual[sid]
        rot = f"id {sid} ({a['nome']})"
        if d["chave"] != a["chave"]:
            graves.append(f"{rot}: chave do fio mudou '{a['chave']}' -> '{d['chave']}'")
        if d["tipo"] != a["tipo"]:
            graves.append(f"{rot}: tipo mudou {a['tipo']} -> {d['tipo']} — o valor gravado "
                          f"passa a ser interpretado de outro jeito")
        if d["unidade"] != a["unidade"]:
            graves.append(f"{rot}: unidade mudou '{a['unidade']}' -> '{d['unidade']}' — "
                          f"o numero gravado muda de significado. Isto exige ID NOVO")
        if d["min"] > a["min"] or d["max"] < a["max"]:
            avisos.append(f"{rot}: faixa estreitou {a['min']}..{a['max']} -> "
                          f"{d['min']}..{d['max']} — valor ja salvo pode cair fora; "
                          f"exige clamp na leitura")
        if d["default"] != a["default"]:
            avisos.append(f"{rot}: default mudou {a['default']} -> {d['default']} "
                          f"(nao afeta quem ja salvou; afeta placa nova)")

    for sid, d in sorted(atual.items()):
        if sid in APOSENTADOS:
            graves.append(f"id {sid} foi APOSENTADO e voltou a ser usado por {d['nome']} — "
                          f"e o pior erro possivel: o valor antigo do usuario vira "
                          f"o valor do setting novo, sem nenhum aviso")
        elif sid not in antes:
            novos.append(f"id {sid} ({d['nome']}) e novo")

    for m in novos:
        print(f"  novo   {m}")
    for m in avisos:
        print(f"  AVISO  {m}")
    for m in graves:
        print(f"  GRAVE  {m}")

    if graves:
        print(f"\nschema: {len(graves)} incompatibilidade(s) GRAVE(S). "
              f"Se a mudanca e intencional, aposente o id e crie um novo.")
        return 1
    if avisos:
        print(f"\nschema: {len(avisos)} aviso(s). Confirme que a leitura faz clamp, "
              f"rode --freeze e commite o snapshot junto.")
        return 1
    if novos:
        print(f"\nschema: {len(novos)} setting(s) novo(s). "
              f"Rode --freeze e commite o snapshot junto.")
        return 1

    print(f"schema: {len(atual)} settings, contrato intacto")
    return 0


if __name__ == "__main__":
    sys.exit(main())
