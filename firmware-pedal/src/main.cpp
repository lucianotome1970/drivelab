// DriveLab Firmware — Pedaleira (RP2040) — M0 bring-up
// Prova toolchain + gravação + serial USB. Inofensivo: só pisca o LED e imprime.
// SEM HID, SEM sensores. HID Joystick chega no M1; canal vendor P0 no M2.
// Design: docs/superpowers/specs/2026-07-14-pedal-firmware-rp2040-design.md
#include <Arduino.h>

static uint32_t tick = 0;

void setup() {
  pinMode(LED_BUILTIN, OUTPUT);  // GP25 no Pi Pico
  Serial.begin(115200);

  // Espera opcional o host abrir a porta serial (até 2s), pra não perder as 1as linhas.
  const unsigned long t0 = millis();
  while (!Serial && (millis() - t0) < 2000) {
    delay(10);
  }
  Serial.println();
  Serial.println("=== DriveLab Pedaleira — M0 bring-up ===");
  Serial.println("RP2040 vivo. Proximo marco: M1 (HID Joystick).");
}

void loop() {
  digitalWrite(LED_BUILTIN, HIGH);
  delay(250);
  digitalWrite(LED_BUILTIN, LOW);
  delay(250);

  Serial.print("DriveLab pedal M0 vivo, tick = ");
  Serial.println(tick++);
}
