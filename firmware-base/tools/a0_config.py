#!/usr/bin/env python3
# ============================================================================
#  DriveLab Firmware — tools/a0_config.py
#  Prova o canal A0 (config) do M0.5: manda um SETREAD (0x15) de um field e
#  lê a resposta SETVALUE (0x16); manda um SETWRITE (0x14) e confirma via
#  novo SETREAD que o valor mudou. Reports crus via hidapi (mesma técnica de
#  ffb_send_constant_force.py) na MESMA interface HID (g_hid, VID/PID
#  0x1209/0x0001) — não existe mais uma interface A0 separada (ver
#  a0_hid_descriptor.h / achado de bancada do Task 2 redo).
#  Autor: Luciano Tomé <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tomé — Licença MIT
# ============================================================================
#  Uso:
#    python3 a0_config.py                       # lê BID_TOTAL_STRENGTH (default)
#    python3 a0_config.py <fieldId>              # lê um field específico
#    python3 a0_config.py <fieldId> <novoValor>  # lê, escreve, lê de novo (confirma)
#
#  Requer: brew install libhidapi
import sys, time, struct, ctypes

VID, PID = 0x1209, 0x0001

# Report IDs do canal A0 (de include/a0_hid_descriptor.h)
A0_RID_SETVALUE = 0x16  # Input  — resposta de leitura
A0_RID_CMD      = 0x22  # Output — comando
A0_RID_DIRECT   = 0x10  # Output — escrita direta
A0_RID_SETWRITE = 0x14  # Output — grava setting
A0_RID_SETREAD  = 0x15  # Output — pede leitura de setting

REPORT_SIZE = 63  # payload por report (ReportConstants.ReportSize), sem contar o Report ID

# Tipos (BaseSettingId.cs / SettingType.cs — ver base_cfg.h)
BT_UINT8, BT_INT8, BT_UINT16, BT_INT16, BT_FLOAT = 0, 1, 2, 3, 4
TYPE_NAME = {BT_UINT8: "uint8", BT_INT8: "int8", BT_UINT16: "uint16", BT_INT16: "int16", BT_FLOAT: "float"}
TYPE_STRUCT = {BT_UINT8: "<B", BT_INT8: "<b", BT_UINT16: "<H", BT_INT16: "<h", BT_FLOAT: "<f"}
TYPE_SIZE = {BT_UINT8: 1, BT_INT8: 1, BT_UINT16: 2, BT_INT16: 2, BT_FLOAT: 4}

# Mapa mínimo de fields (id -> (nome, tipo)) — espelha base_cfg.h/BaseSettingId.cs.
# Só o suficiente p/ bancada; BID_TOTAL_STRENGTH é o default por ser fácil de
# testar (uint8, 0..100 tipicamente).
BID_MOTION_RANGE       = 0
BID_TOTAL_STRENGTH     = 3
BID_SPRING_STRENGTH    = 4
BID_DAMPER_STRENGTH    = 5
BID_POLE_PAIRS         = 11
BID_CURRENT_P          = 12

FIELD_MAP = {
    BID_MOTION_RANGE:    ("motionRange", BT_UINT16),
    BID_TOTAL_STRENGTH:  ("totalStrength", BT_UINT8),
    BID_SPRING_STRENGTH: ("springStrength", BT_UINT8),
    BID_DAMPER_STRENGTH: ("damperStrength", BT_UINT8),
    BID_POLE_PAIRS:      ("polePairs", BT_UINT8),
    BID_CURRENT_P:       ("currentP", BT_FLOAT),
}


def load_hidapi():
    for p in ("/opt/homebrew/lib/libhidapi.dylib", "/usr/local/lib/libhidapi.dylib"):
        try:
            return ctypes.CDLL(p)
        except OSError:
            continue
    raise SystemExit("libhidapi.dylib não encontrada (brew install libhidapi)")


def encode_value(type_id, value):
    fmt = TYPE_STRUCT[type_id]
    return struct.pack(fmt, value)


def decode_value(type_id, buf):
    fmt = TYPE_STRUCT[type_id]
    size = TYPE_SIZE[type_id]
    return struct.unpack(fmt, bytes(buf[:size]))[0]


class A0Device:
    def __init__(self):
        self.hid = load_hidapi()
        self.hid.hid_open.restype = ctypes.c_void_p
        self.hid.hid_open.argtypes = [ctypes.c_ushort, ctypes.c_ushort, ctypes.c_wchar_p]
        self.hid.hid_write.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]
        self.hid.hid_read_timeout.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_int]
        self.hid.hid_read_timeout.restype = ctypes.c_int
        self.hid.hid_init()
        self.dev = self.hid.hid_open(VID, PID, None)
        if not self.dev:
            raise SystemExit(f"hid_open falhou p/ {VID:#06x}:{PID:#06x} (placa conectada? firmware Task 3+?)")

    def _write_report(self, report_id, payload):
        # Report OUTPUT: byte 0 = Report ID; payload é completado com zeros
        # até REPORT_SIZE (mesma convenção do P0/firmware-pedal).
        data = bytes([report_id]) + bytes(payload) + bytes(REPORT_SIZE - len(payload))
        n = self.hid.hid_write(self.dev, data, len(data))
        return n

    def send_read_request(self, field_id, index=0):
        # SETREAD (0x15): payload = [fieldId, index]
        return self._write_report(A0_RID_SETREAD, [field_id & 0xFF, index & 0xFF])

    def send_write(self, field_id, type_id, value, index=0):
        # SETWRITE (0x14): payload = [fieldId, index, type, value LE...]
        payload = [field_id & 0xFF, index & 0xFF, type_id & 0xFF] + list(encode_value(type_id, value))
        return self._write_report(A0_RID_SETWRITE, payload)

    def send_save(self):
        # CMD (0x22): payload = [cmd=2 SaveSettings, arg=0]
        return self._write_report(A0_RID_CMD, [2, 0])

    def read_value_reply(self, timeout_ms=500):
        # Input report SETVALUE (0x16): lê via hid_read_timeout até achar o
        # Report ID certo (a interface também manda RID_JOYSTICK a cada
        # ~10ms — precisamos descartar esses e pegar o 0x16).
        buf = (ctypes.c_char * 64)()
        deadline = time.time() + (timeout_ms / 1000.0)
        while time.time() < deadline:
            n = self.hid.hid_read_timeout(self.dev, buf, 64, 200)
            if n <= 0:
                continue
            raw = bytes(buf[:n])
            if raw[0] == A0_RID_SETVALUE:
                return raw
        return None


def describe_field(field_id):
    return FIELD_MAP.get(field_id, (f"field{field_id}", None))


def main():
    field_id = int(sys.argv[1]) if len(sys.argv) > 1 else BID_TOTAL_STRENGTH
    new_value = None
    if len(sys.argv) > 2:
        raw = sys.argv[2]
        new_value = float(raw) if "." in raw else int(raw)

    name, type_hint = describe_field(field_id)
    print(f"=== A0 config — field {field_id} ({name}) ===")

    dev = A0Device()

    def read_and_print(label):
        dev.send_read_request(field_id)
        reply = dev.read_value_reply()
        if reply is None:
            print(f"{label}: SEM RESPOSTA (timeout esperando 0x16)")
            return None
        got_field = reply[1]
        idx = reply[2]
        type_id = reply[3]
        value = decode_value(type_id, reply[4:4 + TYPE_SIZE.get(type_id, 1)])
        type_name = TYPE_NAME.get(type_id, f"?{type_id}")
        print(f"{label}: field={got_field} idx={idx} type={type_name} value={value}")
        return value

    before = read_and_print("leitura inicial")

    if new_value is not None:
        type_id = type_hint if type_hint is not None else (BT_FLOAT if isinstance(new_value, float) else BT_UINT8)
        print(f"-> escrevendo {new_value} (type={TYPE_NAME.get(type_id)}) ...")
        dev.send_write(field_id, type_id, new_value)
        time.sleep(0.1)
        after = read_and_print("leitura pós-write")
        ok = after is not None and (before is None or after != before or after == new_value)
        print("\n=== RESULTADO:", "OK — valor mudou/confere ✅" if ok else "valor não confere ⚠️", "===")
    else:
        print("\n(informe um 2º argumento para testar write, ex.: a0_config.py 3 55)")


if __name__ == "__main__":
    main()
