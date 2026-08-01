# Brake chopper — EMI / reset sob corrente alta: pesquisa e mitigações

> Referência interna. Motivada pela bancada de 2026-07-31: o chopper do brake resistor **dumpa
> limpo (sem shoot-through, dead-time correto)**, mas a placa **reseta / larga o USB** em duty maior,
> dirigido pelo **pico de corrente** (12V/6,7A caiu no 6%; 19V/11,1A caiu no 4% → maior V = maior pico
> = cai mais cedo). Firmware confirmado limpo. Ver [[drivelab-brake-chopper]].

## OpenFFBoard bateu no MESMO problema
- O **changelog do OpenFFBoard** registra que passaram a **impedir a ativação do brake resistor quando
  a placa está só no USB**, porque isso causava um **loop de fault/reset**. É o análogo mais próximo do
  nosso sintoma ("brake liga → placa reseta"), e a correção deles foi **no firmware (gatear a ativação
  conforme o estado de energia)**. → fonte: https://github.com/Ultrawipf/OpenFFBoard/wiki/Changelog
- Circuito de sense do OpenFFBoard: divisor comparando `vint` (pós-diodo) vs `vext` (pré-diodo); dispara
  ~5V acima, piso absoluto de 6,5V; ativação por software + backup por hardware no TMC4671. → wiki
  Hardware: https://github.com/Ultrawipf/OpenFFBoard/wiki/Hardware
- (A comutação do brake deles mora no TMC4671, não num timer do STM32 — então não é comparação direta de
  freq/dead-time com o nosso, que é MCU-timer como o ODrive.)

## ODrive (= o que a gente portou) não suaviza a comutação
- `Firmware/MotorControl/low_level.cpp`: brake no **TIM2 CH3/CH4**, **mesma frequência do FOC (~24kHz)**,
  **dead-time por software** (`safety_critical_apply_brake_resistor_timings` → recusa com
  `ERROR_BRAKE_DEADTIME_VIOLATION`). Duty = `brake_current * brake_resistance / vbus`, clamp [0, 0.95],
  **proporcional direto, SEM slew/soft-start** na borda. Há um `enable_dc_bus_overvoltage_ramp` opcional,
  mas é clamp de sobretensão, não limitador de di/dt.
- **Conclusão:** o ODrive não faz nada de especial contra a EMI do chaveamento do brake → **nosso port é
  fiel** e a EMI é **inerente ao chopper resistivo**, não bug nosso.
- Fórum ODrive "cuts out when braking": causa era **resistor subdimensionado** (sobretensão), mecanismo
  diferente do nosso (não achei thread com o nosso sintoma exato de reset por pico de corrente).

## Mecanismo provável do nosso reset
1. **Brown-out (BOR)**: o pulso do chopper afunda o bus/VDD por um instante e o **limiar de BOR** do
   STM32 reseta (dip curto, inofensivo, mas o BOR dispara). BOR é **configurável** (option bytes, níveis
   0–4) → dá pra **relaxar como diagnóstico/mitigação**.
2. **Ground-bounce**: o retorno do resistor (di/dt alto) cria diferença de potencial instantânea que
   corrompe VDD/VSS do MCU ou o **D+/D− do USB** (diferencial, referenciado ao terra) → derruba a USB.

## Mitigações — prioridade pra chopper RESISTIVO
1. **Cabos do resistor curtos e TRANÇADOS** — conserto estrutural nº1. Sem indutância no resistor, o di/dt
   é ditado pela **indutância do laço dos cabos**; encurtar/trançar reduz muito. *(usuário faz)*
2. **Cap de decoupling (film ~0,1–0,22µF) direto nos terminais de bus no FET do chopper**, além dos
   eletrolíticos. *(hardware)*
3. **Retorno do resistor confinado à seção de potência**, longe do terra do USB/MCU/analógico. *(layout)*
4. **Checar/relaxar o BOR** do STM32 (option bytes). Diagnóstico: se relaxar e o reset adiar/sumir,
   confirma brown-out/dip. *(firmware/build — a tratar)*
5. **Ferrite/choke no cabo USB** + **afastar o USB dos cabos de potência**. *(barato)*
6. **Snubber RC** no FET — só após medir o ringing no osciloscópio.
7. **Gating estilo OpenFFBoard**: não deixar o brake (automático, futuro) disparar em condição que reseta.
   *(firmware — a tratar)*

## Estratégico (impacto no projeto)
- **Consenso de design (ODrive + OpenFFBoard): brake resistivo é o caso de EMI MAIS DIFÍCIL** que o motor.
  O motor é **indutivo** (freia o di/dt) + corrente **controlada pelo FOC** → ruído bem mais suave.
- **Logo, o reset no chopper NÃO condena o caminho do motor/FFB** — é provável que o motor aguente
  corrente maior sem resetar. O chopper foi o "teste severo". *(a confirmar ao subir a corrente do motor.)*
- A EMI hygiene acima **beneficia os dois** (chopper e motor) → bom investimento independente.

## Fontes
- OpenFFBoard Changelog: https://github.com/Ultrawipf/OpenFFBoard/wiki/Changelog
- OpenFFBoard Hardware: https://github.com/Ultrawipf/OpenFFBoard/wiki/Hardware
- ODrive `Firmware/MotorControl/low_level.cpp` (brake resistor / dead-time / duty)
- ODrive fórum "cuts out when braking": https://discourse.odriverobotics.com/t/odrive-cuts-out-when-braking/6638
- ST Motor Control ref + STM32 BOR (option bytes) — configuração do brown-out reset.

<sub>DriveLab — pesquisa de referência (bancada 2026-07-31).</sub>
