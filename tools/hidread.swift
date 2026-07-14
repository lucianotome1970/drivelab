// Leitor HID nativo (IOKit / IOHIDManager) para pedais Simagic no macOS.
// Uso: swiftc hidread.swift -o hidread && ./hidread [VID_dec] [PID_dec] [segundos]
// Padrão: Simagic P2000 (VID 1155 / PID 1316), 20s.
import Foundation
import IOKit.hid

let a = CommandLine.arguments
let vid = a.count > 1 ? Int(a[1]) ?? 1155 : 1155
let pid = a.count > 2 ? Int(a[2]) ?? 1316 : 1316
let secs = a.count > 3 ? Double(a[3]) ?? 20 : 20

var cur: [UInt32: Int] = [:]
var mn: [UInt32: Int] = [:]
var mx: [UInt32: Int] = [:]
var lastPrint: [UInt32: Int] = [:]
let start = Date()

func axis(_ u: UInt32) -> String {
    switch u {
    case 0x30: return "X"; case 0x31: return "Y"; case 0x32: return "Z"
    case 0x33: return "Rx"; case 0x34: return "Ry"; case 0x35: return "Rz"
    case 0x36: return "Slider"; case 0x37: return "Dial"
    default: return "0x" + String(u, radix: 16)
    }
}

let mgr = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
IOHIDManagerSetDeviceMatching(mgr, [kIOHIDVendorIDKey: vid, kIOHIDProductIDKey: pid] as CFDictionary)

let cb: IOHIDValueCallback = { _, _, _, value in
    let elem = IOHIDValueGetElement(value)
    guard IOHIDElementGetUsagePage(elem) == 1 else { return }
    let usage = IOHIDElementGetUsage(elem)
    guard usage >= 0x30 && usage <= 0x39 else { return }
    let v = IOHIDValueGetIntegerValue(value)
    cur[usage] = v
    mn[usage] = Swift.min(mn[usage] ?? v, v)
    mx[usage] = Swift.max(mx[usage] ?? v, v)
    if abs((lastPrint[usage] ?? -99999) - v) >= 40 {
        lastPrint[usage] = v
        let t = Int(Date().timeIntervalSince(start) * 1000)
        let line = cur.keys.sorted().map { "\(axis($0))=\(cur[$0]!)" }.joined(separator: "  ")
        print("[\(t)ms] \(line)")
    }
}
IOHIDManagerRegisterInputValueCallback(mgr, cb, nil)
IOHIDManagerScheduleWithRunLoop(mgr, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)

let r = IOHIDManagerOpen(mgr, IOOptionBits(kIOHIDOptionsTypeNone))
if r != kIOReturnSuccess {
    print("Falha ao abrir IOHIDManager: 0x\(String(UInt32(bitPattern: r), radix: 16)).")
    print("Se for permissão, conceda 'Monitoramento de entrada' ao Terminal em Ajustes > Privacidade.")
    exit(1)
}
print("Lendo VID=\(vid) PID=\(pid) por \(Int(secs))s — pise nos pedais...")
CFRunLoopRunInMode(.defaultMode, secs, false)

print("=== RESUMO (min..max por eixo) ===")
for u in mn.keys.sorted() { print("\(axis(u)): \(mn[u]!)..\(mx[u]!)") }
