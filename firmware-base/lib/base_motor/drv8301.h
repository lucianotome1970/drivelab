// ============================================================================
//  DriveLab Firmware
//  drv8301.h — Framing SPI PURO do gate driver DRV8301 (ODrive v3.6 / MKS
//  ODRIVE-S V3.6-S6V): encode/decode do frame de 16 bits e os construtores
//  dos valores de Control Register 1/2. Sem dependência de Arduino/SPI —
//  compila e roda no host (ver test/test_drv8301.cpp, test/run.sh). A classe
//  de hardware (SPI de verdade, EN_GATE, nFAULT) fica em drv8301.cpp.
//
//  Todos os valores/layout de bits vieram do firmware oficial da ODrive
//  Robotics (github.com/odriverobotics/ODrive, branch master):
//    Firmware/Drivers/DRV8301/drv8301.hpp — build_ctrl_word(), CtrlMode_e,
//      RegName_e (endereços de registro).
//    Firmware/Drivers/DRV8301/drv8301.cpp — Drv8301::config() (valores
//      literais de Control Register 1/2, comentados linha a linha abaixo).
//  Nada aqui foi inventado — onde um campo é parametrizável (ex.: OC_ADJ_SET),
//  o default reproduz exatamente o valor do ODrive; ajustá-lo para o motor
//  real do DriveLab é trabalho de bring-up, não deste módulo.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

// ----------------------------------------------------------------------------
// Frame SPI de 16 bits do DRV8301.
//   bit[15]    = R/W (1 = read, 0 = write)
//   bits[14:11]= endereço do registro (4 bits)
//   bits[10:0] = dado (11 bits)
//
// Fonte: drv8301.hpp —
//   enum CtrlMode_e { DRV8301_CtrlMode_Read = 1 << 15, DRV8301_CtrlMode_Write = 0 << 15 };
//   enum RegName_e { kRegNameStatus1 = 0 << 11, kRegNameStatus2 = 1 << 11,
//                    kRegNameControl1 = 2 << 11, kRegNameControl2 = 3 << 11 };
//   static inline uint16_t build_ctrl_word(ctrlMode, regName, data) {
//       return ctrlMode | regName | (data & 0x07FF);
//   }
// ----------------------------------------------------------------------------

inline uint16_t drv8301Frame(bool read, uint8_t addr, uint16_t data)
{
    return (read ? (1u << 15) : 0u)
         | (static_cast<uint16_t>(addr & 0x0Fu) << 11)
         | (data & 0x07FFu);
}

inline uint8_t drv8301FrameAddr(uint16_t frame)
{
    return static_cast<uint8_t>((frame >> 11) & 0x0Fu);
}

inline uint16_t drv8301FrameData(uint16_t frame)
{
    return frame & 0x07FFu;
}

// Endereços de registro (fonte: drv8301.hpp, enum RegName_e — valores já sem
// o shift de 11 bits, que é aplicado por drv8301Frame()).
static constexpr uint8_t kDrv8301RegStatus1  = 0;
static constexpr uint8_t kDrv8301RegStatus2  = 1;
static constexpr uint8_t kDrv8301RegControl1 = 2;
static constexpr uint8_t kDrv8301RegControl2 = 3;

// ----------------------------------------------------------------------------
// Ganho do amplificador de shunt (Control Register 2, bits[3:2]).
// Fonte: drv8301.cpp, Drv8301::config():
//   float gain_choices[] = {10.0f, 20.0f, 40.0f, 80.0f};
//   ... gain_setting é o ÍNDICE (0..3) desse array, escrito direto nos bits
//   GAIN de Control Register 2 (ver drv8301ControlReg2 abaixo).
// ----------------------------------------------------------------------------
enum class Drv8301Gain : uint8_t { G10 = 0, G20 = 1, G40 = 2, G80 = 3 };

// ----------------------------------------------------------------------------
// Control Register 1 (endereço kDrv8301RegControl1).
// Fonte: drv8301.cpp, Drv8301::config():
//   new_config.control_register_1 =
//         (21 << 6)    // Overcurrent set to approximately 150A at 100degC.
//                      //   This may need tweaking. [OC_ADJ_SET, bits 10:6]
//       | (0b01 << 4)  // OCP_MODE: latch shut down          [bits 5:4]
//       | (0b0 << 3)   // 6x PWM mode                        [bit 3]
//       | (0b0 << 2)   // don't reset latched faults         [bit 2]
//       | (0b00 << 0); // gate-drive peak current: 1.7A      [bits 1:0]
//
// Os defaults abaixo reproduzem ESSE valor exato (drv8301ControlReg1() ==
// 0x0550, conferido em test_drv8301.cpp). ocAdjSet é o único campo que o
// ODrive já comenta como "may need tweaking" para outro motor/shunt — os
// demais campos (modo de proteção, modo PWM, corrente de gate) descrevem a
// topologia elétrica da placa (6x PWM / 500uOhm), não o motor, então mantemos
// o mesmo valor do ODrive por padrão.
// ----------------------------------------------------------------------------
inline uint16_t drv8301ControlReg1(uint8_t ocAdjSet = 21,
                                    uint8_t ocpMode = 0b01,
                                    bool sixPwmMode = true,
                                    bool resetLatchedFaults = false,
                                    uint8_t gateCurrent = 0b00)
{
    return (static_cast<uint16_t>(ocAdjSet & 0x1Fu) << 6)
         | (static_cast<uint16_t>(ocpMode & 0x03u) << 4)
         | (static_cast<uint16_t>(sixPwmMode ? 0u : 1u) << 3)
         | (static_cast<uint16_t>(resetLatchedFaults ? 1u : 0u) << 2)
         | (static_cast<uint16_t>(gateCurrent & 0x03u) << 0);
}

// ----------------------------------------------------------------------------
// Control Register 2 (endereço kDrv8301RegControl2).
// Fonte: drv8301.cpp, Drv8301::config():
//   new_config.control_register_2 =
//         (0b0 << 6)    // OC_TOFF: cycle by cycle              [bit 6]
//       | (0b00 << 4)   // calibration off (normal operation)   [bits 5:4]
//       | (gain_setting << 2) // select gain                    [bits 3:2]
//       | (0b00 << 0);  // report both over temp and over current
//                       //   on nOCTW pin                       [bits 1:0]
// ----------------------------------------------------------------------------
inline uint16_t drv8301ControlReg2(Drv8301Gain gain,
                                    bool cycleByCycleOcToff = true,
                                    uint8_t calibration = 0b00,
                                    uint8_t octwMode = 0b00)
{
    return (static_cast<uint16_t>(cycleByCycleOcToff ? 0u : 1u) << 6)
         | (static_cast<uint16_t>(calibration & 0x03u) << 4)
         | (static_cast<uint16_t>(static_cast<uint8_t>(gain) & 0x03u) << 2)
         | (static_cast<uint16_t>(octwMode & 0x03u) << 0);
}
