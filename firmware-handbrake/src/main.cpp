// firmware-handbrake/src/main.cpp — M0 scaffold (blink + serial, no HID)
#include <Arduino.h>
// -DUSE_TINYUSB (platformio.ini) troca o Serial padrão pelo Adafruit_USBD_CDC;
// sem incluir a lib aqui o linker não resolve `Serial`. Ainda não usamos HID
// (isso é Task 14+), mas a lib TinyUSB já entra pronta desde o M0.
#include <Adafruit_TinyUSB.h>

// Waveshare RP2040-Zero: LED onboard é um WS2812 (NeoPixel) em GP16, não um LED
// simples — LED_BUILTIN não é definido pela variant (ver pins_arduino.h). Para o
// scaffold M0 (só provar o toolchain), alternamos o pino digitalmente; isso não
// acende a cor corretamente (precisaria do protocolo WS2812), mas comprova o
// build/flash. O blink "de verdade" fica para quando o HID entrar (Task 14+).
#define LED_BUILTIN PIN_NEOPIXEL

void setup() {
  Serial.begin(115200);
  pinMode(LED_BUILTIN, OUTPUT);
}

void loop() {
  Serial.println("=== DriveLab Freio de mão — M0 (scaffold) ===");
  digitalWrite(LED_BUILTIN, HIGH); delay(250);
  digitalWrite(LED_BUILTIN, LOW);  delay(250);
}
