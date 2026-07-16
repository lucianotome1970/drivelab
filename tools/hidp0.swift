// Testador do canal vendor P0 (DriveLab Pedal) via IOKit no macOS.
// Faz: read(Smooth) -> write(Smooth=novo) -> read(Smooth). Se o valor mudar, a escrita app->firmware OK.
// Uso: swiftc hidp0.swift -o hidp0 && ./hidp0
import Foundation
import IOKit.hid

let VID = 0x1209, PID = 0x0002
let RID_SET_WRITE: UInt8 = 0x14, RID_SET_READREQ: UInt8 = 0x15, RID_SET_VALUE: UInt8 = 0x16
let F_SMOOTH: UInt8 = 4, IDX: UInt8 = 0, T_U8: UInt8 = 0

var lastSmooth: Int? = nil
var gotValue = false
var seenIDs: [UInt8: Int] = [:]
let inbufLen = 128
let inbuf = UnsafeMutablePointer<UInt8>.allocate(capacity: inbufLen)
var device: IOHIDDevice?

let reportCb: IOHIDReportCallback = { _, _, _, _, reportID, report, length in
    let rid = UInt8(reportID & 0xFF)
    seenIDs[rid, default: 0] += 1
    if rid == RID_SET_VALUE {
        let field = report[0], index = report[1], type = report[2], v0 = report[3]
        print("  <- 0x16 SettingValue: field=\(field) index=\(index) type=\(type) valor=\(v0)")
        lastSmooth = Int(v0); gotValue = true
    }
}

// Manager agendado no runloop; ao casar o device, registra o callback de input report.
let mgr = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
IOHIDManagerSetDeviceMatching(mgr, [kIOHIDVendorIDKey: VID, kIOHIDProductIDKey: PID] as CFDictionary)
let matchCb: IOHIDDeviceCallback = { _, _, _, dev in
    device = dev
    IOHIDDeviceRegisterInputReportCallback(dev, inbuf, inbufLen, reportCb, nil)
}
IOHIDManagerRegisterDeviceMatchingCallback(mgr, matchCb, nil)
IOHIDManagerScheduleWithRunLoop(mgr, CFRunLoopGetCurrent(), CFRunLoopMode.defaultMode.rawValue)
IOHIDManagerOpen(mgr, IOOptionBits(kIOHIDOptionsTypeNone))

func pump(_ secs: Double) { RunLoop.current.run(until: Date().addingTimeInterval(secs)) }
pump(0.6) // deixa o matching + registro acontecerem

guard let dev = device else { print("ERRO: DriveLab Pedal não casou (VID 0x1209/PID 0x0002)"); exit(1) }

func readSmooth() -> Int? {
    gotValue = false
    var p: [UInt8] = [F_SMOOTH, IDX]                       // read-request: [field][index]
    let r = IOHIDDeviceSetReport(dev, kIOHIDReportTypeOutput, CFIndex(RID_SET_READREQ), &p, p.count)
    if r != kIOReturnSuccess { print("  (SetReport 0x15 retornou 0x\(String(UInt32(bitPattern: r), radix:16)))") }
    pump(0.8)
    return gotValue ? lastSmooth : nil
}

func writeSmooth(_ v: UInt8) {
    var p: [UInt8] = [F_SMOOTH, IDX, T_U8, v]              // write: [field][index][type][valor]
    let r = IOHIDDeviceSetReport(dev, kIOHIDReportTypeOutput, CFIndex(RID_SET_WRITE), &p, p.count)
    print("  -> 0x14 SettingWrite Smooth=\(v) (SetReport=\(r == kIOReturnSuccess ? "ok" : "0x"+String(UInt32(bitPattern: r), radix:16)))")
}

print("=== Teste P0 write (DriveLab Pedal) ===")
print("[0] diagnóstico: escutando input reports por 2s…")
pump(2.0)
let diag = seenIDs.map { "0x\(String($0.key, radix:16))×\($0.value)" }.sorted().joined(separator: " ")
print("    reports recebidos: \(diag.isEmpty ? "NENHUM" : diag)")

print("[1] lê Smooth atual…")
let before = readSmooth()
print("    Smooth antes = \(before.map(String.init) ?? "SEM RESPOSTA")")

let novo: UInt8 = (before == 42) ? 77 : 42
print("[2] grava Smooth=\(novo)…")
writeSmooth(novo)
pump(0.3)

print("[3] lê Smooth de novo…")
let after = readSmooth()
print("    Smooth depois = \(after.map(String.init) ?? "SEM RESPOSTA")")

print("=== RESULTADO ===")
if before == nil && after == nil {
    print("❌ Sem resposta 0x16 — vendor read não respondeu.")
} else if after == Int(novo) {
    print("✅ ESCRITA OK — firmware recebeu 0x14 e o valor mudou p/ \(novo). App→firmware funciona.")
} else {
    print("⚠️ Leitura ok, escrita não pegou: antes=\(before.map(String.init) ?? "?") depois=\(after.map(String.init) ?? "?").")
}
