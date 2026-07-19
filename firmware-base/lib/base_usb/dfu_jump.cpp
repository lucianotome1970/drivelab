// ============================================================================
//  DriveLab Firmware
//  dfu_jump.cpp — Implementação do salto para o bootloader de sistema da ST
//  (EnterDfu). Extraído do monolito src/m05/main.cpp (M5 Stage 0, Task 3) —
//  comportamento IDÊNTICO ao original (magic, ordem do jump, comentários de
//  bancada mantidos como registro). Só compilada nos envs do PlatformIO
//  (m05, futuro m5), nunca no host.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "dfu_jump.h"

#include <Arduino.h>
#include <Adafruit_TinyUSB.h>

// ----------------------------------------------------------------------
// EnterDfu, tentativa 2 (Task 2, redo pós-bancada) — "magic em RAM +
// reset de sistema + checagem no início do boot".
//
// A tentativa 1 (salto "ao vivo" pro bootloader de sistema de dentro do
// firmware rodando -- ver comentário antigo junto de jumpToBootloader()
// mais abaixo, mantido só como registro histórico) FALHOU na bancada: o
// host via o dispositivo desconectar (detach), mas o bootloader NUNCA
// re-enumerava (`dfu-util -l` não achava 0x0483:0xdf11). Causa raiz: o
// OTG_FS já tinha sido inicializado/usado pelo TinyUSB nesta mesma
// "vida" do chip -- HAL_RCC_DeInit()/HAL_DeInit() não bastam pra deixar
// o periférico USB num estado que o bootloader de sistema (que espera
// entrar como se fosse um power-on/reset limpo) reconhece. O bootloader
// da ST não foi desenhado pra ser saltado "ao vivo" de dentro de outro
// firmware com USB já ativo.
//
// A fix: em vez de saltar direto, a gente pede um NVIC_SystemReset() de
// verdade (reset de sistema completo -- reinicializa todos os
// periféricos, incluindo o OTG_FS, exatamente como um power-on) e só
// DEPOIS, já num boot novo e limpo, decide (bem no início do próximo
// setup(), ANTES de qualquer init de USB/clock) se deve pular pro
// bootloader. Isso precisa de um jeito de "lembrar" essa decisão
// atravessando o reset -- é pra isso que serve g_dfuMagic abaixo.
//
// Mecanismo de persistência escolhido: RAM não-inicializada (seção
// ".noinit"), NÃO registrador de backup do RTC. Motivo:
//   - Um NVIC_SystemReset() (reset de sistema via NVIC, o que a gente
//     usa aqui) NÃO limpa a SRAM -- só reinicializa os periféricos e o
//     core (ver PM0210 Rev 11, "1.3 System reset": um reset de sistema
//     "resets all registers... except FCLK/HCLK are configured... and
//     SRAM/registers of the... are unaffected" -- e mesmo sem citar a
//     RM ipsis litteris, é fato conhecido/documentado do Cortex-M/STM32
//     que RAM sobrevive a um reset que não seja power-on). O ÚNICO
//     "zeramento" que essa RAM sofreria é o loop do startup assembly
//     (Reset_Handler, startup_stm32f405xx.s) que copia .data da flash e
//     zera [_sbss, _ebss) -- e esse loop só toca os símbolos _sdata/
//     _edata/_sbss/_ebss, que vêm do LAYOUT do linker script
//     (ldscript.ld da variant F405RGT_F415RGT) -- uma seção com nome
//     PRÓPRIO (".noinit", não ".bss"/".bss*") cai fora desses ranges.
//   - Verificado: o ldscript.ld desta variant (STM32F4xx/
//     F405RGT_F415RGT/ldscript.ld) NÃO define uma seção ".noinit"
//     explícita, mas TAMBÉM não tem catch-all pra seções desconhecidas
//     -- o GNU ld trata ".noinit" como "orphan section" e a insere
//     sozinha numa região RAM alocável ("xrw") do MEMORY do script.
//     Confirmado via objdump -h no firmware.elf: o ld escolheu a região
//     CCMRAM (0x10000000, 64KB, região "xrw" separada da RAM principal
//     em 0x20000000 -- ainda SRAM on-chip normal do ponto de vista do
//     core Cortex-M4, só que num barramento dedicado, sem DMA -- não
//     importa aqui pois só o CPU lê/escreve g_dfuMagic), não a RAM
//     principal onde .data/.bss vivem -- ou seja, g_dfuMagic fica ainda
//     mais claramente FORA do range [_sbss, _ebss) que o Reset_Handler
//     zera (nem precisa checar endereço-a-endereço: é um bus/region
//     inteiramente diferente). Isso é o padrão comum/aceito em projetos
//     STM32duino/PlatformIO pra esse exato truque (magic de bootloader
//     sobrevivendo a reset) sem precisar editar o linker script -- e um
//     NVIC_SystemReset() não zera SRAM (nem a principal, nem a CCM), só
//     reinicializa periféricos/core.
//   - RTC->BKP0R funcionaria também (é o método "padrão-ouro" mesmo
//     através de um POWER-ON reset, que .noinit NÃO sobrevive), mas
//     exigiria ligar o clock de PWR + habilitar acesso ao domínio de
//     backup (__HAL_RCC_PWR_CLK_ENABLE + HAL_PWR_EnableBkUpAccess) e,
//     em alguns setups, o próprio RTC/LSE -- complexidade desnecessária
//     aqui: a gente só precisa sobreviver a um NVIC_SystemReset() (não
//     a um power-cycle), que é exatamente o caso em que .noinit já
//     basta. Fica registrado como fallback se .noinit se mostrar
//     não-confiável na bancada (não foi o caso -- ver relatório).
// ----------------------------------------------------------------------
static const uint32_t kDfuMagic = 0xB007DF00;
__attribute__((section(".noinit"))) static volatile uint32_t g_dfuMagic;

// Salto mínimo pro bootloader de sistema da ST -- chamado SÓ no início de
// setup() (ver dfuCheckAtBootOrJump(), ANTES de qualquer init de USB/clock),
// ou seja, a partir de um NVIC_SystemReset() limpo, com o OTG_FS ainda no
// estado de reset (nunca foi tocado nesta "vida" do chip). Não precisa de
// flush/detach da USB (o OTG_FS nunca subiu neste boot), mas AINDA precisa de
// HAL_DeInit/HAL_RCC_DeInit: o core do STM32duino já reconfigurou o clock
// (HSE/PLL 168 MHz) antes do setup(), e o bootloader da ROM espera o clock no
// reset default (HSI) -- ver comentário abaixo.
static void jumpToBootloaderEarly()
{
    // O reset de sistema reinicia o NOSSO firmware, e o core do STM32duino já
    // reconfigura o clock (HSE/PLL 168 MHz) antes do setup() -- então neste
    // ponto o clock NÃO está no reset default. O bootloader por ROM (SW1/BOOT0)
    // roda no HSI. Precisamos voltar o clock pro estado de reset (HSI, HSE/PLL
    // off) e resetar os periféricos, senão a USB do bootloader não sobe (foi o
    // que falhou na bancada). HAL_RCC_DeInit usa SysTick p/ timeout -> chamar
    // ANTES de desligar SysTick/IRQ.
    HAL_DeInit();
    HAL_RCC_DeInit();

    __disable_irq();

    SysTick->CTRL = 0;
    SysTick->LOAD = 0;
    SysTick->VAL = 0;

    // Endereço do bootloader de sistema do STM32F405 (AN2606, boot via
    // system memory / BOOT0 alto): boot[0] = valor inicial do MSP,
    // boot[1] = endereço do reset handler.
    volatile uint32_t *boot = (volatile uint32_t *)0x1FFF0000;
    SCB->VTOR = 0x1FFF0000;
    __set_MSP(boot[0]);

    void (*blReset)(void) = (void (*)(void))boot[1];
    blReset();

    // Nunca deveria chegar aqui -- blReset() não retorna.
    while (1)
    {
    }
}

void dfuCheckAtBootOrJump()
{
    // Checagem EnterDfu (Task 2, redo) — TEM que ser a PRIMEIRÍSSIMA coisa
    // de setup(), antes de QUALQUER init de USB (TinyUSBDevice.begin()/
    // UsbBase::begin() logo em seguida) ou até de clock. g_dfuMagic (RAM
    // ".noinit", ver comentário completo junto da declaração dela lá em
    // cima) só sobrevive ao NVIC_SystemReset() disparado por
    // dfuRequestJump() (chamado do loop()) -- ela NÃO é limpa pelo startup
    // assembly (Reset_Handler só zera .bss). Se achar o magic aqui, é
    // porque acabamos de reiniciar de propósito pra entrar no bootloader:
    // consome o magic (senão um reset comum de novo cairia de novo no
    // bootloader, num loop) e salta -- com o OTG_FS ainda intocado nesta
    // "vida" do chip (nenhum TinyUSBDevice.begin() rodou ainda), o
    // bootloader de sistema entra limpo e re-enumera direito. Nada antes
    // disto no core (pré-setup(), ver Reset_Handler + SystemInit) liga o
    // OTG_FS -- SystemInit só configura clocks (RCC/PLL) e flash
    // wait-states, não periféricos USB.
    if (g_dfuMagic == kDfuMagic)
    {
        g_dfuMagic = 0;
        jumpToBootloaderEarly();
        // jumpToBootloaderEarly() não retorna.
    }
}

void dfuRequestJump()
{
    // Dá tempo do host/CDC drenar o log antes da gente sumir -- não é
    // estritamente necessário pro reset em si, mas evita truncar a última
    // linha de log ("A0 EnterDfu" etc.) na janela do monitor serial.
    SerialTinyUSB.flush();
    delay(10);

    g_dfuMagic = kDfuMagic;
    NVIC_SystemReset();

    // Nunca deveria chegar aqui -- NVIC_SystemReset() não retorna.
    while (1)
    {
    }
}
