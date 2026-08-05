#!/usr/bin/env python3
# ============================================================================
#  DriveLab
#  enter_dfu.py — Teste de bancada ISOLADO do salto pro DFU: manda o comando
#  A0 EnterDfu (report 0x22, cmd=4) direto pra base via hidapi (sem o app) e
#  fica observando `dfu-util -l` por ~12s pra ver se a placa re-enumera como
#  bootloader STM32 (0483:df11). Serve pra separar "o firmware salta pro DFU?"
#  do "o app dispara o salto?". Uso: python3 tools/enter_dfu.py
#  Autor: Luciano Tomé <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tomé — Licença MIT
# ============================================================================

import ctypes
import subprocess
import time

VID, PID = 0x1209, 0x0001
A0_RID_CMD = 0x22
CMD_ENTER_DFU = 4
REPORT_SIZE = 63
DFU_MATCH = "0483:df11"
DFU_UTIL = "/opt/homebrew/bin/dfu-util"
WATCH_SECONDS = 12


def load_hidapi():
    for p in ("/opt/homebrew/lib/libhidapi.dylib", "/usr/local/lib/libhidapi.dylib"):
        try:
            return ctypes.CDLL(p)
        except OSError:
            continue
    raise SystemExit("libhidapi.dylib não encontrada (brew install libhidapi)")


def dfu_present():
    try:
        out = subprocess.run([DFU_UTIL, "-l"], capture_output=True, text=True, timeout=5)
        return DFU_MATCH in (out.stdout + out.stderr)
    except Exception as e:
        print(f"  (dfu-util falhou: {e})")
        return False


def main():
    hid = load_hidapi()
    hid.hid_open.restype = ctypes.c_void_p
    hid.hid_open.argtypes = [ctypes.c_ushort, ctypes.c_ushort, ctypes.c_wchar_p]
    hid.hid_write.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]
    hid.hid_init()

    dev = hid.hid_open(VID, PID, None)
    if not dev:
        raise SystemExit(f"hid_open falhou p/ {VID:#06x}:{PID:#06x} — a base está enumerada como HID? "
                         "(religue a placa com o firmware normal rodando)")

    data = bytes([A0_RID_CMD, CMD_ENTER_DFU, 0]) + bytes(REPORT_SIZE - 2)
    n = hid.hid_write(dev, data, len(data))
    print(f"EnterDfu enviado (report 0x22 cmd=4), hid_write retornou {n} byte(s).")
    if n < 0:
        print("  !! hid_write < 0 — a placa não recebeu o comando.")

    print(f"Observando `dfu-util -l` por {WATCH_SECONDS}s à espera de {DFU_MATCH}...")
    start = time.time()
    poll = 0
    while time.time() - start < WATCH_SECONDS:
        poll += 1
        if dfu_present():
            print(f"  ✓ poll {poll} (t+{time.time()-start:.1f}s): DFU {DFU_MATCH} PRESENTE — o firmware saltou!")
            print("\nRESULTADO: salto do firmware OK. dfu-util pode gravar agora.")
            return
        print(f"  poll {poll} (t+{time.time()-start:.1f}s): sem DFU ainda...")
        time.sleep(0.6)

    print(f"\nRESULTADO: TIMEOUT — {DFU_MATCH} nunca apareceu. O salto do firmware NÃO disparou "
          "(ou a placa voltou pro firmware normal). => é o salto por software (fix via RTC backup register).")


if __name__ == "__main__":
    main()
