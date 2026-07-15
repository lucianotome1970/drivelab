// ============================================================================
//  DriveLab Firmware
//  main.cpp (m05) — Rascunho M0.5: enumeração USB HID FFB (de-risco), sem motor.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================

// DriveLab Firmware — M0.5 (USB/FFB enumeration de-risk)
// Alvo: ODESC v4.2 (STM32F405). Framework: Arduino (STM32duino).
//
// OBJETIVO: provar a decisão B2 no F405 — enumerar como um dispositivo
// DirectInput de Force Feedback (volante), reusando:
//   * USBLibrarySTM32           (Levi--G)   — reimplementa a API USB do AVR no STM32
//   * ArduinoJoystickWithFFBLibrary (YukMingLaw) — o "cérebro" FFB PID (efeitos + descriptor)
//
// >>> STATUS: PRIMEIRO RASCUNHO, escrito SEM placa para testar. <<<
//     É esperado que precise de iteração na bancada. Os pontos de maior incerteza
//     estão marcados com "RISCO"/"VERIFICAR" abaixo. NÃO é garantido compilar/rodar
//     de primeira — a combinação shim + lib de FFB no F405 nunca foi publicada.
//
// SEGURANÇA: continua SEM motor / sem estágio de potência. Só enumeração + eixo.
//   Verificação no Windows: Painel de Controle -> Dispositivos de Jogo ->
//   Propriedades -> deve aparecer o eixo de direção se mexendo e a aba de Force Feedback.

#include <Arduino.h>
#include <USBLibrarySTM32.h>   // VERIFICAR: nome exato do header do shim (pode variar).
#include <Joystick.h>          // ArduinoJoystickWithFFBLibrary

// Volante de eixo único: só o eixo de direção (steering) habilitado.
Joystick_ Joystick(
    JOYSTICK_DEFAULT_REPORT_ID,
    JOYSTICK_TYPE_JOYSTICK,
    0,      // botões
    0,      // hat switches
    false,  // X
    false,  // Y
    false,  // Z
    false,  // Rx
    false,  // Ry
    false,  // Rz
    false,  // Rudder
    false,  // Throttle
    false,  // Accelerator
    false,  // Brake
    true    // Steering  <-- o eixo do volante
);

void setup()
{
    // Sobe o USB pelo shim (substitui a pilha USB do core).
    USB_Begin();
    uint32_t t0 = millis();
    while (!USB_Running() && (millis() - t0 < 3000))
        delay(5);

    // Inicia o joystick. 'false' = enviaremos o estado manualmente (sendState()).
    Joystick.begin(false);
    Joystick.setSteering(0);

    // TODO(bancada): setGains()/setEffectParams() p/ ajustar força dos efeitos.
    //   API: setGains(Gains[2]) e setEffectParams(...). Não é necessário só p/ enumerar,
    //   mas será p/ sentir força quando o motor entrar (M5). Ver Joystick.h da lib.
}

void loop()
{
    // 1) Servir o tráfego PID/FFB vindo do host.
    //    RISCO #1: na versão AVR isto roda DENTRO da ISR de USB. Aqui chamamos em loop()
    //    como primeira tentativa. Se os efeitos não registrarem no teste de FFB do Windows,
    //    provavelmente precisa ser plugado no callback/ISR de USB do shim.
    Joystick.getUSBPID();

    // 2) Varre o eixo de direção devagar, só pra VER o dispositivo se mexendo no Windows
    //    (prova enumeração + eixo). Faixa do steering: -32767..32767.
    int16_t angle = (int16_t)(32767.0f * sinf(millis() / 1000.0f));
    Joystick.setSteering(angle);
    Joystick.sendState();

    // 3) Lê a força FFB que a lib calculou (o que mandaríamos ao motor no M5).
    //    Sem motor no M0.5, só lemos. Faixa [-255,255], forces[0] = X/steering.
    int32_t forces[2] = {0, 0};
    Joystick.getForce(forces);
    // (M5: mapear forces[0] -> torque no SimpleFOC. Por ora, não usado.)

    delay(2); // ~500 Hz
}
