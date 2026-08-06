/* Define to prevent recursive inclusion -------------------------------------*/
#ifndef __LOW_LEVEL_H
#define __LOW_LEVEL_H

#ifdef __cplusplus
extern "C" {
#endif

/* Includes ------------------------------------------------------------------*/
#include <cmsis_os.h>
#include <stdbool.h>
#include <adc.h>

/* Exported types ------------------------------------------------------------*/
/* Exported constants --------------------------------------------------------*/
// 16 e um LIMITE DE HARDWARE, nao uma escolha: o sequenciador regular do STM32F4 tem 16 slots
// (SQR1.L e um campo de 4 bits). Nao existe rank 17 — pedir um corrompe o campo de comprimento e
// a sequencia inteira passa a converter 1 canal so. Custou uma gravacao descobrir isso.
#define ADC_CHANNEL_COUNT 16

// Mod local do DriveLab: o SENSOR DE TEMPERATURA INTERNO do STM32 (canal 16) entra NO LUGAR do
// canal 7 na sequencia, em vez de virar um 17o rank. Vira o campo "Temperatura do MCU" do app — a
// unica temperatura real desta placa, ja que o clone MKS nao tras os termistores de FET/motor.
//
// Por que o slot 7 e o certo pra sacrificar: canal 7 = PA7 = M1_AL, uma SAIDA DE PWM do eixo 1
// (alternate function do timer). Amostra-la sempre foi leitura inutil e NINGUEM le este indice — os
// termistores de FET usam os canais 15 (M0_TEMP/PC5) e 4 (M1_TEMP/PA4), o AUX_TEMP e o 5 (PA5,
// reservado pro NTC do motor), o vbus e o 6 (PA6) e os GPIOs de usuario sao 3/4/5. Trocar o canal
// nao mexe no pino: o mux do ADC passa a ler o sensor interno, e o PA7 segue sendo PWM.
#define ADC_CHANNEL_MCU_TEMP 7
extern const float adc_full_scale;
extern const float adc_ref_voltage;
/* Exported variables --------------------------------------------------------*/
extern float vbus_voltage;
extern float ibus_;
extern bool brake_resistor_armed;
extern bool brake_resistor_saturated;
extern float brake_resistor_current;
extern uint16_t adc_measurements_[ADC_CHANNEL_COUNT];
extern osThreadId analog_thread;
extern const uint32_t stack_size_analog_thread;
/* Exported macro ------------------------------------------------------------*/
/* Exported functions --------------------------------------------------------*/

void safety_critical_arm_brake_resistor();
void safety_critical_disarm_brake_resistor();
void safety_critical_apply_brake_resistor_timings(uint32_t low_off, uint32_t high_on);

// called from STM platform code
extern "C" {
void vbus_sense_adc_cb(uint32_t adc_value);
void pwm_in_cb(TIM_HandleTypeDef *htim);
}

// Initalisation
void start_adc_pwm();
void start_pwm(TIM_HandleTypeDef* htim);
void sync_timers(TIM_HandleTypeDef* htim_a, TIM_HandleTypeDef* htim_b,
                 uint16_t TIM_CLOCKSOURCE_ITRx, uint16_t count_offset,
                 TIM_HandleTypeDef* htim_refbase = nullptr);
void start_general_purpose_adc();
void pwm_in_init();
void start_analog_thread();

// ADC getters
uint16_t channel_from_gpio(Stm32Gpio gpio);
float get_adc_voltage(Stm32Gpio gpio);
float get_adc_relative_voltage(Stm32Gpio gpio);
float get_adc_relative_voltage_ch(uint16_t channel);

void update_brake_current();

#ifdef __cplusplus
}
#endif

#endif //__LOW_LEVEL_H
