// DriveLab Firmware — M0 (bring-up)
// Alvo: ODESC v4.2 (STM32F405). Framework: Arduino (STM32duino).
//
// Objetivo do M0: provar que o toolchain compila, que a gravação funciona
// na SUA ODESC, e que a comunicação USB serial sobe. É o "hello world".
//
// SEGURANÇA: este firmware NÃO faz nada com o motor nem com o estágio de
// potência (DRV8301). Nenhum GPIO é acionado. Pode rodar só com o USB/ST-Link,
// SEM alimentar o motor. É deliberadamente inofensivo.

#include <Arduino.h>

void setup()
{
  // Serial pela USB (Virtual COM). Ver platformio.ini (USBD_USE_CDC).
  Serial.begin(115200);
  // Pequena espera para o host enumerar a porta antes das primeiras mensagens.
  delay(1500);
  Serial.println();
  Serial.println("=== DriveLab Firmware — M0 bring-up ===");
  Serial.println("Se voce esta lendo isto, o toolchain + gravacao + USB serial funcionam.");
  Serial.println("Nada foi feito com o motor. Proximo marco: M0.5 (USB/FFB).");
}

void loop()
{
  static uint32_t n = 0;
  Serial.print("DriveLab M0 vivo, tick = ");
  Serial.println(n++);
  delay(1000);
}
