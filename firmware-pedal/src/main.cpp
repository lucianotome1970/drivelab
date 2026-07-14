// DriveLab Firmware — Pedaleira (RP2040) — M1: HID Joystick (3 eixos 12-bit)
// Aparece no Windows como "DriveLab Pedal" (Dispositivos de Jogo), com 3 eixos.
// Ainda SEM canal vendor P0 (M2) e SEM load cell/curva/deadzone (M3).
// Só lê os 3 ADCs e manda como eixos do joystick. Nome/VID/PID vêm do platformio.ini.
// Design: docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md
#include <Arduino.h>
#include <Adafruit_TinyUSB.h>

// --- Report descriptor: Joystick, 3 eixos (Rx/Ry/Rz) 12-bit (0..4095), Report ID 1 ---
// Mesma ordem que o app lê do P2000: Rx=embreagem, Ry=freio, Rz=acelerador.
static uint8_t const kHidReport[] = {
  0x05, 0x01,         // Usage Page (Generic Desktop)
  0x09, 0x04,         // Usage (Joystick)
  0xA1, 0x01,         // Collection (Application)
    0x85, 0x01,       //   Report ID (1)
    0x15, 0x00,       //   Logical Minimum (0)
    0x26, 0xFF, 0x0F, //   Logical Maximum (4095)
    0x75, 0x10,       //   Report Size (16 bits)
    0x95, 0x03,       //   Report Count (3)
    0x09, 0x33,       //   Usage (Rx)  -> embreagem
    0x09, 0x34,       //   Usage (Ry)  -> freio
    0x09, 0x35,       //   Usage (Rz)  -> acelerador
    0x81, 0x02,       //   Input (Data, Variable, Absolute)
  0xC0                // End Collection
};

typedef struct __attribute__((packed)) {
  uint16_t rx;  // embreagem  (A0 / GP26)
  uint16_t ry;  // freio      (A1 / GP27)
  uint16_t rz;  // acelerador (A2 / GP28)
} PedalReport;

static Adafruit_USBD_HID g_hid;

void setup() {
  Serial.begin(115200);
  analogReadResolution(12);  // ADC do RP2040: 0..4095 (12-bit)

  g_hid.setReportDescriptor(kHidReport, sizeof(kHidReport));
  g_hid.begin();

  // Aguarda o host montar o USB (até 3s).
  const unsigned long t0 = millis();
  while (!TinyUSBDevice.mounted() && (millis() - t0) < 3000) {
    delay(10);
  }
  Serial.println("=== DriveLab Pedaleira — M1 (HID Joystick 3 eixos 12-bit) ===");
}

void loop() {
  if (!g_hid.ready()) {
    delay(1);
    return;
  }

  PedalReport r;
  r.rx = analogRead(A0);  // 0..4095
  r.ry = analogRead(A1);
  r.rz = analogRead(A2);

  g_hid.sendReport(1, &r, sizeof(r));  // Report ID 1
  delay(2);                            // ~500 Hz
}
