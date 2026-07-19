#!/usr/bin/env python3
# ============================================================================
#  DriveLab Firmware — tools/check_fw_signature.py
#  Confere que um .bin compilado carrega a assinatura embutida definida em
#  firmware-base/src/m05/fw_signature.h (magic "DRVLABFW" + byte "kind"),
#  e que o kind bate com o esperado. Usado tanto localmente (checagem
#  pós-build) quanto como a base do check que o app fará antes de gravar um
#  firmware num dispositivo (validação "esse arquivo é do device certo").
#  Autor: Luciano Tomé <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tomé — Licença MIT
# ============================================================================
#
# Layout da assinatura (12 bytes, ver fw_signature.h):
#   offset 0..7  : magic, 8 bytes ASCII "DRVLABFW" (sem NUL)
#   offset 8     : kind (1=Base, 2=Pedal, 3=Handbrake, 4=Wheel)
#   offset 9..11 : ver[3] (major, minor, patch) — não checado aqui
#
# Uso:
#   python3 check_fw_signature.py <binfile> <expected_kind>
#       -> exit 0 se achou a assinatura E kind == expected_kind
#       -> exit 1 se não achou a assinatura no arquivo
#       -> exit 2 se achou mas o kind não bate
#   python3 check_fw_signature.py --self-test
#       -> roda a lógica de busca contra buffers sintéticos (sem precisar de
#          um .bin de verdade) e sai 0 se tudo passar.

import sys

MAGIC = b"DRVLABFW"


def find_signature(data: bytes):
    """Procura MAGIC em data; retorna (offset, kind) ou None se não achar.

    Se MAGIC aparecer mas não sobrar nem 1 byte depois dele (pra ler o
    kind), trata como não encontrado (assinatura truncada/incompleta).
    """
    offset = data.find(MAGIC)
    if offset == -1:
        return None
    kind_offset = offset + len(MAGIC)
    if kind_offset >= len(data):
        return None
    kind = data[kind_offset]
    return (offset, kind)


def check(binfile: str, expected_kind: int) -> int:
    try:
        with open(binfile, "rb") as f:
            data = f.read()
    except OSError as exc:
        print(f"ERRO: não consegui abrir {binfile}: {exc}")
        return 1

    result = find_signature(data)
    if result is None:
        print(f"FALHA: assinatura {MAGIC!r} não encontrada em {binfile}")
        return 1

    offset, kind = result
    if kind != expected_kind:
        print(
            f"FALHA: assinatura encontrada em offset {offset}, mas kind={kind} "
            f"!= esperado {expected_kind}"
        )
        return 2

    print(f"OK: assinatura encontrada em offset {offset}, kind={kind}")
    return 0


def self_test() -> int:
    # Buffer COM a assinatura (kind=1), com algum lixo em volta pra simular
    # um .bin de verdade.
    with_sig = b"\x00" * 16 + MAGIC + bytes([1, 0, 2, 0]) + b"\xff" * 32
    result = find_signature(with_sig)
    assert result is not None, "self-test: esperava achar a assinatura"
    offset, kind = result
    assert offset == 16, f"self-test: offset esperado 16, veio {offset}"
    assert kind == 1, f"self-test: kind esperado 1, veio {kind}"

    # Buffer SEM a assinatura.
    without_sig = b"\x00" * 64 + b"\xff" * 32
    result = find_signature(without_sig)
    assert result is None, "self-test: não esperava achar assinatura"

    # Buffer com o magic truncado bem no fim (sem byte de kind sobrando).
    truncated = b"\x00" * 8 + MAGIC
    result = find_signature(truncated)
    assert result is None, "self-test: magic truncado (sem kind) deve ser 'não encontrado'"

    # check() fim-a-fim: kind bate.
    import tempfile
    import os

    with tempfile.NamedTemporaryFile(delete=False) as tmp:
        tmp.write(with_sig)
        tmp_path = tmp.name
    try:
        assert check(tmp_path, 1) == 0, "self-test: check() deveria sair 0 com kind correto"
        assert check(tmp_path, 2) == 2, "self-test: check() deveria sair 2 com kind incorreto"
    finally:
        os.unlink(tmp_path)

    with tempfile.NamedTemporaryFile(delete=False) as tmp:
        tmp.write(without_sig)
        tmp_path = tmp.name
    try:
        assert check(tmp_path, 1) == 1, "self-test: check() deveria sair 1 sem assinatura"
    finally:
        os.unlink(tmp_path)

    print("OK: self-test passou (todas as asserções)")
    return 0


def main(argv):
    if len(argv) == 2 and argv[1] == "--self-test":
        return self_test()

    if len(argv) != 3:
        print(f"Uso: {argv[0]} <binfile> <expected_kind>")
        print(f"  ou: {argv[0]} --self-test")
        return 2

    binfile = argv[1]
    try:
        expected_kind = int(argv[2])
    except ValueError:
        print(f"ERRO: expected_kind deve ser um inteiro, veio {argv[2]!r}")
        return 2

    return check(binfile, expected_kind)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
