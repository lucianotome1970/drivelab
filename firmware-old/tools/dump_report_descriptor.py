#!/usr/bin/env python3
# ============================================================================
#  DriveLab Firmware — tools/dump_report_descriptor.py
#  Lê, no Mac (pyusb/libusb), o descriptor de config + o HID REPORT descriptor
#  de um dispositivo USB e checa se ele sai INTEIRO (declared == received) —
#  a checagem-chave do M0.5 v2 (o shim entregava truncado).
#  Autor: Luciano Tomé <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tomé — Licença MIT
# ============================================================================
#  Uso: python3 dump_report_descriptor.py [VID] [PID]
#       (defaults: 0x1209 0x0001 = DriveLab Base). Requer:
#       brew install libusb ; python3 -m pip install pyusb
#       DYLD_LIBRARY_PATH=/opt/homebrew/lib python3 dump_report_descriptor.py
import sys, os, importlib.util
import usb.core, usb.backend.libusb1

def get_backend():
    for p in ("/opt/homebrew/lib/libusb-1.0.dylib", "/usr/local/lib/libusb-1.0.dylib"):
        if os.path.exists(p):
            return usb.backend.libusb1.get_backend(find_library=lambda x: p)
    return usb.backend.libusb1.get_backend()

def main():
    vid = int(sys.argv[1], 0) if len(sys.argv) > 1 else 0x1209
    pid = int(sys.argv[2], 0) if len(sys.argv) > 2 else 0x0001
    dev = usb.core.find(backend=get_backend(), idVendor=vid, idProduct=pid)
    if dev is None:
        print(f"dispositivo {vid:#06x}:{pid:#06x} nao encontrado"); return 2

    head = bytes(dev.ctrl_transfer(0x80, 0x06, 0x0200, 0, 9))
    total = head[2] | (head[3] << 8)
    cfg = bytes(dev.ctrl_transfer(0x80, 0x06, 0x0200, 0, total))
    print(f"device {vid:#06x}:{pid:#06x}  config={total} bytes")

    rdlen = None
    i = 0
    while i + 1 < len(cfg):
        blen, btype = cfg[i], cfg[i + 1]
        if blen == 0:
            break
        if btype == 0x04:      # interface
            print(f"  INTERFACE class=0x{cfg[i+5]:02x} numEP={cfg[i+4]}")
        elif btype == 0x21 and i + 8 < len(cfg):  # HID
            rdlen = cfg[i + 7] | (cfg[i + 8] << 8)
            print(f"  HID descriptor: wReportDescriptorLength={rdlen}")
        elif btype == 0x05:    # endpoint
            print(f"  ENDPOINT addr=0x{cfg[i+2]:02x} attr=0x{cfg[i+3]:02x}")
        i += blen

    if rdlen is None:
        print("  (sem interface HID / sem HID descriptor)")
        return 1

    rd = bytes(dev.ctrl_transfer(0x81, 0x06, 0x2200, 0, rdlen))
    ok = len(rd) == rdlen
    print(f"\nREPORT DESCRIPTOR: declared={rdlen} received={len(rd)} -> "
          f"{'INTEIRO OK' if ok else 'TRUNCADO!'}")
    here = os.path.dirname(os.path.abspath(__file__))
    spec = importlib.util.spec_from_file_location(
        "v", os.path.join(here, "validate_ffb_descriptor.py"))
    v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v)
    print("validate:", v.validate(rd))
    return 0 if ok else 1

if __name__ == "__main__":
    sys.exit(main())
