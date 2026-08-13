# firmware-base — Wheelbase Direct-Drive

Firmware da **base direct-drive** (o estágio do motor de FFB). Roda em placas
**STM32F405 classe ODrive v3.6** — validado numa **MKS ODRIVE-S V3.6-S6V**.

O jogo enxerga a base como um volante DirectInput com force feedback: nenhum plugin,
nenhum driver extra.

## Arquitetura

Em vez de escrever uma FOC do zero, o firmware monta duas fundações prontas e maduras, e
a camada de FFB é nossa:

| Camada | Origem | Licença |
|---|---|---|
| FOC, controle do motor, calibração | **ODrive v0.5.6**, vendorizado em `vendor/odrive-fw` | MIT |
| Pilha USB | **TinyUSB**, vendorizado em `vendor/tinyusb` | MIT / Apache |
| Ponte com o ODrive, task de FFB, descritores USB, persistência | **nosso** (`src/`, `inc/`) | MIT |
| Efeitos de FFB (mola, damper, atrito, inércia) | **nosso** (`engine.step`) | MIT |
| Configuração e telemetria | **nosso** app DriveLab Studio | MIT |

O projeto inteiro é MIT, e cada arquivo-fonte traz o cabeçalho declarando isso.

## Estado (2026-08-12)

**Funcionando e validado em bancada:**

- Motor rodando em FOC, armando de forma repetível.
- Force feedback validado em jogo: **11 voltas em Monza no Assetto Corsa Competizione** sem perder
  FFB e sem desarme, mais AMS2 e EVO. Monza importa aqui — foram as chicanes dela que expuseram a
  perda de força por regeneração.
- **Constante de torque medida** (0,397 Nm/A) no lugar dos 0,55 de uma fórmula genérica de
  catálogo, que superestimava a força entregue em 39%.
- **Curva de resposta de volta ao linear.** Estava em 1,59, o que achatava as forças médias pela
  metade — o jogo pedia 50% e chegavam 33%.
- Persistência dos ajustes na flash da própria placa (`CMD_SAVE`) — a base é a fonte da verdade,
  não o app.
- Telemetria ao vivo: tensão do barramento, corrente do motor com picos positivo e negativo,
  **temperatura dos FETs** (43 °C em repouso), clipping separado entre a parcela do jogo e a da base.
- Proteções: chopper do resistor de freio, corte por sobrecorrente na ISR, batente por software,
  trips de sub e sobretensão dimensionados pela fonte medida, guarda de coerência do ângulo
  elétrico, guarda de sobrevelocidade.

**Feito e aguardando validação em bancada:**

- Corte térmico dos FETs configurável (a placa passa a recuar a partir de 85 °C, em vez dos 100 °C
  padrão do ODrive).
- Filtro de saída da força.
- Trava do encoder: combinação de sensor e interface que o firmware não sabe acionar passa a não
  aplicar **nada**, em vez de aplicar a resolução sobre uma leitura A/B/Z.
- Curva de resposta da força com 11 pontos e interpolação suave.

**Pendente:**

- **O motor não tem sensor de temperatura.** O `MotorTempC` reporta -128 e não existe corte térmico
  para o motor — a única proteção é a sua mão nele. Instalar um NTC é o que destrava subir o limite
  de corrente com segurança.
- A temperatura do MCU está **desligada de propósito**: lê-la compartilhava o ADC1 com o sensor de
  corrente e custou o FFB. Ver a nota em `src/motor_link.cpp`.
- O encoder ainda é incremental, então o centro não sobrevive ao desligar. Ver
  [`../docs/encoders.md`](../docs/encoders.md).

## Build

Precisa do **ARM GCC** (`arm-none-eabi-`) e do Python com `pyyaml`, `jinja2` e
`jsonschema` — o ODrive gera código na hora do build.

O toolchain do PlatformIO serve e é o que usamos, pra o build sair idêntico no Mac e no
Windows:

```bash
export PATH="$HOME/.platformio/packages/toolchain-gccarmnoneeabi/bin:$PATH"
cd firmware-base
make -j8
```

Saída em `build/`: `drivelab-base.elf`, `.bin` e `.hex`.

## Gravar

**Por DFU (pelo USB, sem gravador):** é o caminho normal, e o DriveLab Studio faz isso
sozinho. Se a placa não entrar em DFU, veja o
[FAQ](../docs/faq-hoverboard.md#não-consigo-gravar-o-firmware) — em muitas placas o modo
manual é obrigatório, e em algumas o jumper precisa ser **retirado**, não colocado.

**Por ST-Link (SWD):**

```bash
openocd -f interface/stlink.cfg -f target/stm32f4x.cfg \
        -c "program build/drivelab-base.elf verify reset exit"
```

> ⚠️ Com o motor **armado**, não pare o núcleo pelo SWD — isso derruba o motor no meio do
> controle. Para acompanhar o firmware rodando, use a serial USB (CDC).

## Fonte de alimentação

Todos os números abaixo foram **medidos na nossa bancada** ou lidos do código do firmware — nenhum
veio de datasheet. O motor é de hoverboard; a placa é da classe v3.6, variante 56 V.

| Medido | Valor | De onde vem |
|---|---|---|
| Resistência de fase | **0,2016 Ω** | medida neste motor (`motor_link.cpp`) |
| Constante de torque Kt | **0,397 Nm/A** | medida em 11/08/2026 (234 amostras, regressão pela origem) |
| Fonte da bancada | 27 V / 30 A | |
| Pico de regeneração, sem resistor de freio | **33,5 V** com fonte de 27,1 V | na pista |
| O mesmo pico, com resistor de 2 Ω | **27,5 V** (3,3 W médios) | na pista |

### O que a fonte precisa entregar de verdade

A corrente que a **fonte** fornece não é a corrente que o **motor** puxa. O motor puxa a corrente
dele através da resistência do enrolamento, e é só isso que a fonte tem que cobrir:

```
P = 1,5 × R × I²        (o 1,5 é da conversão trifásica: Iq é a amplitude da corrente de fase)
P = 1,5 × 0,2016 × 25²  = 189 W no limite padrão de 25 A
```

A 27 V isso são **7 A da fonte**, enquanto o motor vê 25 A nas bobinas.

A fórmula é confirmada pela nossa própria bancada: uma falha travou o motor em 18,05 A, e
`1,5 × 0,2016 × 18,05² = 98,5 W`, batendo com os ~98 W de calor registrados na época.

### Tensão não compra torque

Torque é `corrente × Kt`. A tensão só precisa ser suficiente para empurrar essa corrente pelo
enrolamento — `R × I = 0,2016 × 25 ≈ 5 V` com o volante parado.

Ela só passa a importar em velocidade, onde a geração do próprio motor come a margem. Calculando
onde um barramento de 27 V se esgotaria:

```
tensão de fase disponível ≈ 27 / √3 ≈ 15,6 V
back-EMF = λ × ω_elétrico,  λ = Kt / (1,5 × pares_de_polos) = 0,0176 V·s/rad
15,6 = 0,0176 × 15 × ω_mecânico   →   ω ≈ 59 rad/s ≈ 9,4 voltas/s
```

**9,4 voltas por segundo.** Num volante de duas voltas e meia de batente a batente, isso nunca
acontece. Um barramento de 48 V levaria para 16,7 voltas/s — margem que você jamais vai usar.

### O que uma tensão maior custa

A regeneração soma em cima da fonte. Ao reverter o volante rápido o motor devolve energia e o
barramento sobe. Na nossa bancada isso foi **+6,5 V** (fonte de 27,1 V, pico de 33,5 V, sem
resistor de freio).

O firmware dimensiona os limites pela fonte que mede no arme
(`motor_link_autoscale_bus_limits`):

| | fonte de 27 V | fonte de 48 V |
|---|---|---|
| rampa do chopper começa | 29 V | 50 V |
| rampa termina | 31 V | 52 V |
| corte por sobretensão | 33 V | 54 V |
| pico de regeneração medido (sem resistor) | 33,5 V | ~54,5 V |

A 27 V o pico cai dentro da rampa e o resistor o absorve. **A 48 V o mesmo evento cai em cima do
corte.** O teto da própria placa é 55 V (capacitores de barramento ~63 V), então a margem passa de
confortável a nenhuma — e se aquele +6,5 V crescer com a tensão em vez de ser fixo (temos um único
ponto de medição, então não sabemos), passa do limite.

### Recomendação para este tipo de motor

- **24 a 30 V é o ponto ideal.** Entrega o torque inteiro, mantém margem larga de regeneração e é
  leve com os capacitores.
- **Dimensione a fonte pela POTÊNCIA, não pela corrente do motor.** ~200 W cobrem o padrão de 25 A;
  uma fonte de 30 A é cerca de quatro vezes o necessário.
- **Resistor de freio não é opcional**, e acima de ~36 V ele deixa de ser rede de segurança e passa
  a fazer parte do funcionamento normal.
- **Mais força vem de mais corrente, não de mais volts** — e corrente custa calor pelo *quadrado*:
  25 A dão 9,75 Nm e 189 W; 30 A dão 11,7 Nm e 272 W. Instale o termistor no motor antes de subir.

### Subtensão também derruba

Fonte que afunda sob carga desarma a base tanto quanto uma que estoura. O firmware mantém o corte
por subtensão fixo em **8 V** justamente por isso: um limite de 14,79 V já causou cortes repetidos
em curvas lentas de corrente alta, porque o barramento caía a 14,79 V sob carga e a base desarmava.
Cabos longos ou finos pioram isso.

## Segurança

Leia [`../docs/faq-hoverboard.md#segurança`](../docs/faq-hoverboard.md#segurança) antes de
energizar um motor. Os dois pontos que mais custam caro:

- **Case a tensão da fonte com a variante da placa** (24 V ou 56 V). Passar disso destrói
  a placa sem aviso.
- **Resistor de freio é obrigatório** antes de qualquer torque em malha fechada.
