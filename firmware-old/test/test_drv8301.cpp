// ============================================================================
//  DriveLab Firmware
//  test_drv8301.cpp — Testes de HOST do framing SPI puro do DRV8301
//  (drv8301.h): encode/decode do frame de 16 bits e os valores de Control
//  Register 1/2, conferidos byte-a-byte contra o drv8301.cpp/.hpp oficial do
//  ODrive (Firmware/Drivers/DRV8301/). Roda sem placa — drv8301.h não
//  depende de Arduino/SPI (ver comentário no topo do header).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_motor/drv8301.h"

#include <cstdio>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main()
{
    // --- drv8301Frame: bit[15]=R/W, bits[14:11]=addr, bits[10:0]=data.
    // Fonte: ODrive drv8301.hpp, build_ctrl_word() = ctrlMode | regName | (data & 0x07FF),
    // com CtrlMode_e{Read=1<<15, Write=0<<15} e RegName_e{Control1=2<<11, ...}.
    {
        // Escrita (read=false) no Control Register 1 (addr 0x02) com dado 0x0F.
        uint16_t frame = drv8301Frame(false, 0x02, 0x0F);
        CHECK(frame == ((0u << 15) | (0x02u << 11) | 0x0F));
        CHECK((frame & 0x8000) == 0); // R/W = 0 (write)
        CHECK(drv8301FrameAddr(frame) == 0x02);
        CHECK(drv8301FrameData(frame) == 0x0F);

        // Leitura (read=true) do Status Register 2 (addr 0x01), payload ignorado na leitura.
        uint16_t rframe = drv8301Frame(true, 0x01, 0);
        CHECK((rframe & 0x8000) != 0); // R/W = 1 (read)
        CHECK(drv8301FrameAddr(rframe) == 0x01);
        CHECK(drv8301FrameData(rframe) == 0);

        // Roundtrip com todos os endereços de registro conhecidos (0..3) e dado
        // cobrindo os 11 bits (0x7FF = data máxima representável).
        for (uint8_t addr = 0; addr <= 3; ++addr)
        {
            uint16_t f = drv8301Frame(false, addr, 0x07FF);
            CHECK(drv8301FrameAddr(f) == addr);
            CHECK(drv8301FrameData(f) == 0x07FF);
        }

        // Endereço fora da faixa de 4 bits deve ser mascarado (não vazar para o
        // bit de R/W nem para o campo de dado).
        uint16_t masked = drv8301Frame(false, 0xFF, 0);
        CHECK(drv8301FrameAddr(masked) == 0x0F);
        CHECK((masked & 0x8000) == 0);
    }

    // --- Endereços de registro (fonte: drv8301.hpp, enum RegName_e).
    {
        CHECK(kDrv8301RegStatus1 == 0);
        CHECK(kDrv8301RegStatus2 == 1);
        CHECK(kDrv8301RegControl1 == 2);
        CHECK(kDrv8301RegControl2 == 3);
    }

    // --- drv8301ControlReg1: valores default devem reproduzir EXATAMENTE o
    // que o ODrive escreve (drv8301.cpp, Drv8301::config()):
    //   (21 << 6) | (0b01 << 4) | (0b0 << 3) | (0b0 << 2) | (0b00 << 0) = 0x550
    {
        uint16_t cr1 = drv8301ControlReg1();
        CHECK(cr1 == 0x550);
        CHECK(((cr1 >> 6) & 0x1F) == 21);   // OC_ADJ_SET
        CHECK(((cr1 >> 4) & 0x03) == 0b01); // OCP_MODE: latched shutdown
        CHECK(((cr1 >> 3) & 0x01) == 0b0);  // PWM_MODE: 6x PWM
        CHECK(((cr1 >> 2) & 0x01) == 0b0);  // não limpa faults latched
        CHECK((cr1 & 0x03) == 0b00);        // GATE_CURRENT: 1.7A

        // Overrides individuais preservam os outros campos.
        uint16_t cr1b = drv8301ControlReg1(/*ocAdjSet=*/10);
        CHECK(((cr1b >> 6) & 0x1F) == 10);
        CHECK(((cr1b >> 4) & 0x03) == 0b01);

        uint16_t cr1c = drv8301ControlReg1(21, 0b01, /*sixPwmMode=*/false);
        CHECK(((cr1c >> 3) & 0x01) == 1); // 3x PWM mode quando sixPwmMode=false

        uint16_t cr1d = drv8301ControlReg1(21, 0b01, true, /*resetLatchedFaults=*/true);
        CHECK(((cr1d >> 2) & 0x01) == 1);
    }

    // --- drv8301ControlReg2: valor default para cada ganho deve reproduzir o
    // ODrive (drv8301.cpp): (0b0<<6) | (0b00<<4) | (gain_setting<<2) | (0b00<<0),
    // gain_setting = índice em {10,20,40,80} V/V = 0,1,2,3.
    {
        CHECK(drv8301ControlReg2(Drv8301Gain::G10) == 0x00);
        CHECK(drv8301ControlReg2(Drv8301Gain::G20) == 0x04);
        CHECK(drv8301ControlReg2(Drv8301Gain::G40) == 0x08);
        CHECK(drv8301ControlReg2(Drv8301Gain::G80) == 0x0C);

        // Confere os bits de ganho isoladamente (campo [3:2]).
        uint16_t cr2 = drv8301ControlReg2(Drv8301Gain::G40);
        CHECK(((cr2 >> 2) & 0x03) == static_cast<uint16_t>(Drv8301Gain::G40));
        CHECK(((cr2 >> 6) & 0x01) == 0);   // OC_TOFF: cycle-by-cycle
        CHECK(((cr2 >> 4) & 0x03) == 0);   // calibração desligada
        CHECK((cr2 & 0x03) == 0);          // nOCTW: reporta OT e OC

        // Override do modo OC_TOFF: cycleByCycleOcToff=false -> bit[6]=1.
        uint16_t cr2b = drv8301ControlReg2(Drv8301Gain::G10, /*cycleByCycleOcToff=*/false);
        CHECK(((cr2b >> 6) & 0x01) == 1);
    }

    std::printf("drv8301: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
