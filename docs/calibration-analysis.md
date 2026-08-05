# Análise: calibração/arme inconsistentes (runaway/notchy) — firmware-base

> FFB estava VALIDADO (mola lisa + ACC ótimo). Ficou inconsistente: às vezes cal boa
> (lisa), às vezes ruim (DISPARA ao armar) ou NOTCHY (agarra/solta). Encoder CONFIRMADO bom.

## CAUSA RAIZ (definitiva, achada no código do ODrive)

`encoder.cpp check_pre_calibrated()`: **num encoder INCREMENTAL SEM index, o ODrive FORÇA
`pre_calibrated=false`.** A posição absoluta se perde no power-off → o offset elétrico NÃO
pode ser fixado na flash → o ODrive é OBRIGADO a re-rodar o offset-cal a CADA boot.

E esse offset-cal é **NÃO-DETERMINÍSTICO num hoverboard** por causa do **cogging forte**:
o lock-in aplica tensão pra travar o rotor na "fase elétrica 0", mas o rotor **estala no
detente de cogging mais próximo** (não na fase 0 real) — e qual detente depende de onde ele
estava. Isso desloca o offset medido de boot pra boot:
- desvio pequeno → mola **NOTCHY** (cogging vaza);
- desvio grande (>~90° elétricos) → torque no sentido errado → **RUNAWAY ao armar**;
- pode até inverter o sinal da direção.

**Por que era "consistente" no validado:** sorte + condições boas (bus firme, rotor em posição
parecida). Fundamentalmente sempre foi não-determinístico sem index — só não tínhamos batido nos
boots ruins ainda. (E o meu **g_arm_gate piorou**: re-armava a MESMA cal ruim em vez de descartar.)

## O FIX (o que AS DUAS referências fazem: MKS-fábrica e odrive-wheel)

**Usar o canal Z (index) do encoder.** O E6B2-**CWZ** TEM index (o "Z" no nome). Com o Z ligado:
1. `encoder.use_index=1` + `startup_encoder_index_search=true` → o motor gira brevemente no boot
   até o pulso Z → **referência absoluta**.
2. Aí `pre_calibrated=true` **COLA** (o check_pre_calibrated deixa de rebaixar) → o offset medido
   1x fica fixo relativo ao Z → **alinhamento IDÊNTICO a cada boot** → sem runaway/notchy.

O odrive-wheel tem um passo dedicado "Configure Z (index pulse)" e um "warning for incremental
encoder without Z" — ou seja, sem Z eles AVISAM que é problema. Ambas as refs convergem pro Z.

## Config recomendada (hoverboard 15pp, E6B2, DRV8301) — das referências

- Motor: `motor_type=HIGH_CURRENT`, `pole_pairs=15`, `torque_constant≈0.55`,
  **`calibration_current=8-10 A`** (hoverboard precisa de corrente alta pra vencer o cogging no
  lock — 5 A é POUCO, foi por isso que falhou), **`resistance_calib_max_voltage=12 V`** (não 2 V),
  `current_lim` 10-15 A (≥ cal current, senão a cal falha).
- Encoder: `mode=0`, `cpr=4000`, **`use_index=1`** (com Z ligado), `bandwidth` 1000.
- Bring-up 1x: `w axis0.requested_state 4` (motor cal) → `motor.pre_calibrated=1` + save →
  `requested_state 6` (index search) → `requested_state 7` (offset) → confere offset≠0/erro=0 →
  `encoder.pre_calibrated=1` + save.
- Startup: `startup_encoder_index_search=true` + `startup_closed_loop_control=true` → auto-arma
  achando o Z, sempre com o mesmo offset.

## Mitigação SEM index (se o Z não puder ser ligado)

Aceitar re-cal a cada boot (`startup_encoder_offset_calibration=true`), mas reduzir a variância:
`calibration_current=8-12 A` (lock vence o cogging), `resistance_calib_max_voltage=12 V`,
sempre bootar com o volante SOLTO. Menos determinístico que com index. + **remover o g_arm_gate**
(não re-armar cal ruim).

## Plano
1. **Remover g_arm_gate** (não re-armar cal ruim) → arme nativo.
2. Ler a NVM por SWD (startup flags, pre_calibrated, current_lim) — verdade do terreno.
3. **Decisão do usuário:** ligar o Z do E6B2 (fix definitivo) OU mitigar sem index (subir cal current).
