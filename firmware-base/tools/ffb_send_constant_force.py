#!/usr/bin/env python3
# ============================================================================
#  DriveLab Firmware — tools/ffb_send_constant_force.py
#  Prova o "cano FFB" do M0.5 v2 SEM Windows/DirectInput: envia um Constant
#  Force como HID OUTPUT report cru (via hidapi/IOHIDManager, que coexiste com
#  o macOS mesmo com a interface HID reivindicada) e lê o log no CDC do
#  dispositivo. Se o firmware imprimir "FFB const ... mag=...", o cano funciona.
#  Autor: Luciano Tomé <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
# ============================================================================
#  Uso: python3 ffb_send_constant_force.py [magnitude] [/dev/cu.usbmodemXXXX]
#  Requer: brew install libhidapi ; pip3 install --user pyserial
import sys, time, glob, ctypes, threading

VID, PID = 0x1209, 0x0001
# Report IDs (de include/ffb_hid_descriptor.h)
RID_SET_EFFECT          = 0x01
RID_SET_CONSTANT_FORCE  = 0x05
RID_EFFECT_OPERATION    = 0x0A
RID_CREATE_NEW_EFFECT   = 0x11

def load_hidapi():
    for p in ("/opt/homebrew/lib/libhidapi.dylib", "/usr/local/lib/libhidapi.dylib"):
        try:
            return ctypes.CDLL(p)
        except OSError:
            continue
    raise SystemExit("libhidapi.dylib não encontrada (brew install libhidapi)")

def find_cdc():
    ports = [p for p in glob.glob("/dev/cu.usbmodem*")]
    return ports[0] if ports else None

def main():
    mag = int(sys.argv[1]) if len(sys.argv) > 1 else 1000
    cdc = sys.argv[2] if len(sys.argv) > 2 else find_cdc()

    # --- leitor do CDC em background (se houver pyserial + porta) ---
    logs = []
    stop = threading.Event()
    def reader():
        try:
            import serial
        except ImportError:
            print("(pyserial ausente — não vou ler o CDC; instale: pip3 install --user pyserial)")
            return
        if not cdc:
            print("(porta CDC não encontrada — pulei a leitura)"); return
        try:
            s = serial.Serial(cdc, 115200, timeout=0.3)
        except Exception as e:
            print("(erro abrindo CDC:", e, ")"); return
        while not stop.is_set():
            ln = s.readline()
            if ln:
                txt = ln.decode(errors="replace").rstrip()
                logs.append(txt); print("  CDC>", txt)
        s.close()
    t = threading.Thread(target=reader, daemon=True); t.start()
    time.sleep(0.4)

    # --- abre o HID e manda os reports ---
    hid = load_hidapi()
    hid.hid_open.restype = ctypes.c_void_p
    hid.hid_open.argtypes = [ctypes.c_ushort, ctypes.c_ushort, ctypes.c_wchar_p]
    hid.hid_write.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]
    hid.hid_init()
    dev = hid.hid_open(VID, PID, None)
    if not dev:
        stop.set(); raise SystemExit(f"hid_open falhou p/ {VID:#06x}:{PID:#06x} (placa conectada? é o firmware Task 6?)")

    def send(name, data):
        n = hid.hid_write(dev, bytes(data), len(data))
        print(f"-> {name}: {['%02x'%b for b in data]}  (hid_write={n})")
        time.sleep(0.2)

    block = 1
    lo, himag = mag & 0xFF, (mag >> 8) & 0xFF
    # sequência mínima que o DirectInput faria; o log-chave é o Set Constant Force
    send("Create New Effect", [RID_CREATE_NEW_EFFECT, 0x01])          # tipo constant
    send("Set Effect",        [RID_SET_EFFECT, block, 0x01, 0,0, 0,0])
    send("Set Constant Force",[RID_SET_CONSTANT_FORCE, block, lo, himag])
    send("Effect Operation",  [RID_EFFECT_OPERATION, block, 0x01, 0x01])  # start

    time.sleep(1.0)
    stop.set(); time.sleep(0.4)
    hit = any("const" in l.lower() or "mag" in l.lower() for l in logs)
    print("\n=== RESULTADO:", "CANO OK — firmware logou o Constant Force ✅" if hit
          else "não vi o log do constant force (ver notas)", "===")

if __name__ == "__main__":
    main()
