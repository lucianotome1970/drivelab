# Análise profunda do OpenFFBoard Firmware → conhecimento para o DriveLab

**Data:** 2026-07-13
**Fonte:** `github.com/Ultrawipf/OpenFFBoard` (branch `master`) — código-fonte lido diretamente (raw), não paráfrase.
**Objetivo:** extrair o máximo de conhecimento reutilizável para o firmware do DriveLab (Trilho B: SimpleFOC na ODESC v4.2 / STM32F405, **placa única**).

> **Licença — DECIDIDO: Opção A (MIT).** Implementação própria informada por esta análise; reaproveitamos **conhecimento**, não código GPL verbatim. OpenFFBoard é GPLv3, mas: os *IDs/structs do HID PID* ≈ exemplo da spec USB-IF (seguros) e o **descriptor será gerado do zero a partir da spec** (não copiar o arquivo `usb_hid_ffb_desc.h` deles); a matemática de efeitos reescrevemos em float/Nm/SimpleFOC. Todo o projeto (firmware + app + libs) permanece **MIT**. **Ver §6.**

---

## 1. Visão geral — por que o OpenFFBoard é a referência

É o firmware open-source mais maduro em **USB HID Force Feedback (DirectInput PID)** — exatamente a parte mais difícil e arriscada do nosso firmware. Roda em **STM32F4 (F407) + TinyUSB + FreeRTOS**, praticamente a mesma plataforma do nosso alvo (ODESC = STM32F405). Prova que dá pra ter um dispositivo HID FFB robusto num F4 full-speed a **1 kHz**.

**Divisão de responsabilidades (e o que aproveitamos):**

| Subsistema | O que é | Uso no DriveLab |
|---|---|---|
| **Pilha USB HID FFB** (`HidFFB`, `ffb_defs.h`, descriptor PID) | Descriptor, parsing de reports, pool de efeitos | ⭐ **Reaproveitar quase literal** (§2) — de-risca o risco nº 1 |
| **EffectsCalculator** | Transforma efeitos ativos em torque/tick | **Portar a matemática**, recalibrar constantes (§3) |
| **Axis + loop** | Pipeline encoder→efeitos→endstop→limitadores→driver | **Adotar a ordem/estrutura** (§4) |
| **MotorDriver / Encoder** | Interface minimalista de motor/sensor | **Adotar a interface**, implementar com SimpleFOC (§4) |
| **CommandHandler / config** | Registro de comandos + protocolo agnóstico de transporte + flash | **Adotar o padrão** — valida nosso protocolo A0 (§4) |
| **Drivers CAN (ODrive/VESC/TMC4671)** | Backends de motor externos | ❌ Não usamos (fazemos FOC local com SimpleFOC) |

---

## 2. Pilha USB HID FFB (o ouro) — reaproveitar quase literal

### 2.1 Como o descriptor é montado
O descriptor **não é um array estático** — é montado por blocos de macro (`usb_hid_ffb_desc.h`), cada bloco emitindo os bytes de **um report PID**, com uma macro `_SIZE` companheira → o tamanho total é calculado em **tempo de compilação**. Tudo concatenado plano dentro de **uma** Application Collection (não uma por report). É, na prática, uma transcrição do exemplo canônico da USB HID PID Usage Table.

Montagem típica de um volante: gamepad (botões + eixos) + canal de comando vendor (page 0xFF00) + os reports PID abaixo.

### 2.2 Tabela de Report IDs (de `ffb_defs.h`)
```
OUTPUT (host → device):
  0x01 Set Effect            0x02 Set Envelope        0x03 Set Condition
  0x04 Set Periodic          0x05 Set Constant Force  0x06 Set Ramp Force
  0x0A Effect Operation      0x0B PID Block Free      0x0C PID Device Control
  0x0D Device Gain
INPUT (device → host):
  0x02 PID State
FEATURE:
  0x11 Create New Effect     0x12 Block Load (GET)    0x13 PID Pool (GET)
```
Tipos de efeito (byte `effectType` **1..11**, índice na coleção, NÃO o usage cru):
`1 Constant · 2 Ramp · 3 Square · 4 Sine · 5 Triangle · 6 SawtoothUp · 7 SawtoothDown · 8 Spring · 9 Damper · 10 Inertia · 11 Friction`.

### 2.3 Structs de report (empacotados, `__attribute__((packed))`)
- **Set Effect (0x01):** `effectBlockIndex(u8,1..40) effectType(u8,1..11) duration(u16 ms, 0=infinito) triggerRepeatInterval(u16) samplePeriod(u16) startDelay(u16) gain(u8,0..255) triggerButton(u8) enableAxis(u8: bit0=X,bit1=Y,bit2=DirectionEnable) directionX(u16,0..36000 centideg) directionY(u16)`.
- **Set Condition (0x03)** (spring/damper/inertia/friction, por eixo): `effectBlockIndex(u8) parameterBlockOffset(u8) cpOffset(i16) positiveCoefficient(i16) negativeCoefficient(i16) positiveSaturation(u16) negativeSaturation(u16) deadBand(u16)`.
- **Set Periodic (0x04):** `effectBlockIndex(u8) magnitude(u16,0..32767) offset(i16) phase(u16,0..35999 centideg) period(u32,ms)`.
- **Set Constant Force (0x05):** `effectBlockIndex(u8) magnitude(i16,-32767..32767)`.
- **Set Ramp (0x06):** `effectBlockIndex(u8) startLevel(u16) endLevel(u16)`.
- **Effect Operation (0x0A):** `effectBlockIndex(u8) state(u8: 1=Start,2=StartSolo,3=Stop) loopCount(u8)`.
- **PID Device Control (0x0C):** 1 byte, **8 bits-lane** → `0x01`=EnableActuators, `0x02`=Disable, `0x04`=StopAllEffects, `0x08`=DeviceReset(+freeAll), `0x10`=Pause, `0x20`=Continue.
- **Device Gain (0x0D):** 1 byte 0..255 (é o slider "Effects Strength" do Windows).
- **Create New Effect (0x11, Feature):** `effectType(u8) byteCount(u16)` → firmware aloca slot; host então faz `GET_FEATURE(0x12)` pra saber o índice.
- **Block Load (0x12, GET):** `effectBlockIndex(u8) loadStatus(u8: 1=Success,2=Full,3=Error) ramPoolAvailable(u16)`.
- **PID Pool (0x13, GET):** `ramPoolSize(u16=MAX_EFFECTS) maxSimultaneousEffects(u8) memoryManagement(u8)`.

### 2.4 Struct interno de efeito (`FFB_Effect`)
Campos: `state, type(0=slot livre), offset, gain(0..255), magnitude, startLevel/endLevel, axisMagnitudes[MAX_AXIS], conditions[MAX_AXIS], phase, period, duration(0xffff=infinito), attack/fadeLevel, attack/fadeTime, filter[Biquad], startDelay, startTime(HAL_GetTick), useEnvelope, useSingleCondition`.
`FFB_Effect_Condition`: `cpOffset, positive/negativeCoefficient, positive/negativeSaturation, deadBand`.

### 2.5 Pool de efeitos (sem alocação dinâmica)
`MAX_EFFECTS = 40`, `std::array<FFB_Effect, 40>`. Alocar = varredura linear por `type==0`. **Convenção crítica:** `effectBlockIndex` na wire é **1-based**, índice do array é `-1`; todo handler começa com `if(index==0 || index>MAX) return;`.

### 2.6 Dispatch (glue TinyUSB)
```c
tud_hid_set_report_cb(itf, report_id, type, buffer, bufsize){
    if((type==INVALID||type==OUTPUT) && report_id==0) report_id = *buffer; // ID vem no byte 0
    globalHidHandler->hidOut(report_id, type, buffer, bufsize);
}
tud_hid_get_report_cb(...) → hidGet(...);
tud_hid_descriptor_report_cb(...) → getHidDesc();
```
`hidOut()` faz `switch(report_id)` e faz cast do buffer cru pro struct empacotado.

### 2.7 USB / TinyUSB
- VID/PID **0x1209 / 0xFFB0** (0x1209 = VID compartilhado do **pid.codes**, open-source).
- Composto via **IAD**: `bDeviceClass=0xEF, subclass=0x02, protocol=0x01` (CDC debug + HID). EP size **64**, `HID_BINTERVAL=1` → **polling 1 ms / 1 kHz**.
- `tusb_config.h`: `CFG_TUD_HID=1, CFG_TUD_CDC=1, CFG_TUSB_OS=OPT_OS_FREERTOS`, device full-speed no F4.
- `USBdevice::Run()` = `tusb_init(); while(1) tud_task();` numa thread FreeRTOS.

### 2.8 ⚠️ Gotchas (as pegadinhas que quebram tudo)
1. **Assimetria GET vs SET no buffer do report ID:** em SET com `report_id==0`, o ID está no byte 0 do buffer; em GET (Feature reply), o ID **não** está no buffer (o TinyUSB cuida). Errar isso = "device detectado, efeitos não tocam". É o bug nº 1 de quem faz do zero.
2. **Validar `bufsize` antes de castar** pro struct empacotado em **todo** handler.
3. **Unit / Unit-Exponent tags** têm que bater por campo (×10⁻³ pra ms, ×10⁻² pra centideg) — errado = Windows escala 10×/100× em silêncio.
4. **Tamanho total do descriptor** via macro `_SIZE` em tempo de compilação — constante contada à mão desatualizada = "Unknown HID Device", sem aba FFB.
5. **Não pular PID Pool (0x13)** — o painel do Windows e vários jogos consultam no connect.
6. `logical minimum = -32767` (não -32768) — bate com a spec.
7. Se juntar CDC de debug, usar o padrão IAD (`0xEF/0x02/0x01`) pra Windows não instalar driver errado na interface 0.

### 2.9 Conjunto mínimo de reports pra um volante (v1)
**Outputs:** 0x01 Set Effect, 0x03 Set Condition (self-centering/damper), 0x04 Set Periodic, **0x05 Set Constant Force (o mais usado nos sims)**, 0x06 Ramp, 0x0A Effect Operation, 0x0B Block Free, 0x0C Device Control, 0x0D Device Gain. **Features:** 0x11 Create New Effect, 0x12 Block Load (GET), 0x13 PID Pool (GET). Envelope (0x02) opcional; Custom Force (0x07/0x08/0x0E) pode ficar stub.

---

## 3. Matemática do motor de efeitos (portar; recalibrar constantes)

**Regra de ouro do loop:** soma **todos** os efeitos ativos por eixo em `int32` (sem clampear), e **clampeia uma vez só no fim** para `[-32767, 32767]`. Efeitos condition (spring/damper/friction/inertia) calculam sua força inteira dentro do `calcComponentForce`; os demais (constant/ramp/periodic) produzem um escalar. **Não há acumulador de fase persistente** — cada efeito recalcula seu valor instantâneo a partir de `(now - startTime)`, `period`, `phase` a cada tick (imune a jitter).

**Fórmula condition genérica (spring/damper/inertia) — a peça mais valiosa:**
```
se |metric - cpOffset| > deadBand:
    coeff = (metric > cpOffset ? positiveCoeff : negativeCoeff) / 0x7fff
    delta = metric - (cpOffset + deadBand*sign(metric - cpOffset))
    force = clip(coeff * gainFactor * typeScale * delta, -negSat, +posSat)
senão: force = 0
torque = -force * axisDirection
```
- **Spring:** metric = **posição**. **Damper:** metric = **velocidade** (filtrada). **Inertia:** metric = **aceleração** (filtrada).
- **Friction:** NÃO usa a genérica — é força de magnitude ~constante opondo-se ao sinal da velocidade, com um **rampup raised-cosine perto de v≈0** pra evitar chatter/notch no centro:
  ```
  rampupFactor = (1 + sin(π*(|v|/rampCeil - 0.5))) / 2   // suave 0→1 dentro de rampCeil
  force = coeff * rampupFactor * sign(v)
  ```
  (rampCeil ≈ 25% da velocidade máx). **Reaproveitar** — resolve um problema real.
- **Envelope:** rampa linear `attackLevel→|magnitude|` em `[0,attackTime]`, sustenta, `|magnitude|→fadeLevel` em `[dur-fadeTime, dur]`; reaplica o sinal. Modula constant/periodic (não ramp).
- **Periódicos:** Sine = `magnitude*sin(2π(t/period + phase/360°)) + offset`; Square/Triangle/Sawtooth = fórmulas fechadas sobre `(elapsed+phase) % period`.

**Estimativa de velocidade/aceleração (importante):**
```
speed = lowpass( (pos - pos_prev) * sampleRate )              // Fc≈30Hz
accel = lowpass( (speed_raw - speed_raw_prev) * sampleRate )   // Fc≈15Hz, derivado da speed NÃO filtrada
```
Ordem importa: filtrar a velocidade e derivar de novo dela dobraria o atraso e ficaria ruidoso pra inertia. Para MVP sem biquad, um IIR 1-polo (`y += α(x−y)`) na mesma Fc serve.

**Montagem final do torque por eixo (Axis::updateTorque) — copiar a ordem:**
```
torque = effectTorque                          // efeitos do jogo (HID)
if expo≠1: torque = sign(t)*|t/max|^expo * max  // curva de resposta opcional
torque *= fx_ratio                              // reserva headroom pro endstop (default 80%)
torque += axisEffectTorque                      // damper/friction/inertia/mola-idle sempre ativos
torque += endstop()                             // mola progressiva além de ±range/2
torque *= power/0x7fff                           // escala de potência global
[limitador de velocidade PI — só REDUZ torque, nunca inverte]
[limitador de slew-rate: clip vs torque do tick anterior ± maxRate]
if outOfBounds: torque = 0
if fade-in: torque *= rampMultiplier            // rampa 0→1 no start/resume
clip(torque, -power, +power)
```

**Endstop (mola progressiva além do range):** `addtorque = (posDeg - range/2) * endstopStrength * gain`, empurrando de volta, clampeado. Mapeia 1:1 no nosso soft-stop.

⚠️ **Constantes mágicas (damper=40, friction=45, inertia=4, scaler.spring=16…) NÃO são físicas** — existem só pra casar a faixa int16 do HID com o motor deles. **Não portar**: calibrar direto contra torque real (Nm) e unidades do SimpleFOC (rad/s, rad/s²), pulando a normalização int16 intermediária. Fazer tudo em **float** (Nm), clampeando só no limite final de corrente/torque.

---

## 4. Arquitetura & segurança (adotar os padrões)

**Loop FFB como thread própria:** dirigida por **timer ISR** (não `Delay` busy) → `WaitForNotification()`; prioridade alta. Taxa **125 Hz–8 kHz** (default 1 kHz), independente da taxa de report USB. Ao mudar a taxa, recomputa os coeficientes dos filtros.
Ordem por ciclo: **1)** lê todos encoders + deriva speed/accel filtrados; **2)** roda EffectsCalculator (escreve `effectTorque`); **3)** só então envia torque ao driver (evita skew entre eixos). Otimização: `updateTorque()` retorna `bool changed` (dirty-flag) — só manda ao driver se mudou.

**Interface MotorDriver (minúscula) — adotar, implementar com SimpleFOC:**
```cpp
virtual void turn(int16_t power);   // comando de torque (escala já aplicada pelo Axis)
virtual void stopMotor(); virtual void startMotor();
virtual void emergencyStop(bool reset=false);
virtual bool motorReady();          // gate antes do turn()
virtual Encoder* getEncoder(); virtual bool hasIntegratedEncoder();
```
`Encoder`: `getPos()/getPos_f()/getPosAbs()/setPos()/getCpr()/getEncoderType()`. **Ganho:** o loop FFB nunca toca no SimpleFOC direto → testável com driver mock; e abre porta pra um 2º backend sem refactor. Recomendação: definir `IMotorDriver`/`IEncoder` já agora, com um único `SimpleFOCDriver` por trás.

**CommandHandler / config — valida e inspira nosso protocolo A0:**
- Cada subsistema **auto-registra** uma tabela `{name, id, flags(GET/SET/…), help}` em vez de um switch gigante por mensagem. Templates genéricos `handleGetSet(...)` colapsam get/set numa linha (elimina bugs de divergência get/set).
- `ParsedCommand{cmdId, adr, val, instance, type}` / `CommandReply` são **agnósticos de transporte** — o **mesmo core** roda sobre CDC-ASCII, UART-ASCII e **HID-binário**. Isso é exatamente a ideia do nosso `ITransport`: o protocolo A0 (settings via vendor-HID) é o análogo do `HidCommandInterface` deles. **Nosso design está validado.**
- **Persistência** é mixin ortogonal (`saveFlash/restoreFlash`) separado da lógica de comando. Adotar a **separação** (não o bit-packing manual de 16-bit — temos NVS folgada). **Copiar** o guard de versão: mismatch de `FLASH_VERSION` → reformata + grava defaults (protege contra config corrompida pós-update).
- `ClassChooser` (factory registry, `#ifdef` por backend) permite **trocar driver/encoder em runtime** dentro de critical section. Pra nós (1 backend) é overkill, mas o `replyAvailableClasses()` ("quais modos/sensores existem e estão disponíveis") é um jeito elegante de o **DriveLab Studio** popular dropdowns a partir do firmware.

**FreeRTOS / segurança (adotar):**
- **Watchdog (IWDG) alimentado numa thread/loop de baixa dependência**, NÃO na thread do loop FFB → se o FFB travar, o watchdog reseta (não mascara).
- **Out-of-bounds cutoff:** posição escalada fora do limite → `stopMotor()` + erro; auto-recupera ao voltar.
- **E-stop** por GPIO (debounce) → `emergencyStop()`; flag `emergency` checada no nível do loop antes de mandar torque.
- **Torque limiting multi-estágio:** power clamp + fx_ratio headroom + limitador PI de velocidade + slew-rate + fade-in.
- **Fade-in** de força no start/resume/motor-ready (evita degrau de torque).
- **Critical section** ao trocar driver/config (loop não roda contra objeto meio-construído).
- Guard de **DEVID do chip** (não flashear firmware errado no hardware errado).

---

## 5. Impacto no plano do DriveLab (Trilho B)

Esta análise **de-risca o maior risco do firmware** (USB HID composto/PID): o OpenFFBoard prova que TinyUSB + HID PID a 1 kHz roda num STM32F4 — nosso caso. Concretamente, o firmware do DriveLab deve:

1. **USB HID PID:** reusar (respeitando licença) o descriptor por blocos + `ffb_defs.h` (IDs/structs) + o padrão de dispatch TinyUSB. Interface joystick com PID (jogos) — igual ao spec §5.1. **Nosso canal vendor de config (A0)** pode ser: (a) uma interface HID separada (design atual), **ou** (b) reports de vendor-usage na MESMA interface HID (como o `HIDDESC_CTRL_REPORTS` deles). Avaliar (b) — simplifica o composto.
2. **Motor de efeitos (nosso, autoral):** portar a fórmula condition (spring/damper/inertia) + friction rampup + estimativa speed/accel + endstop + ordem de montagem do torque — **em float/Nm**, calibrado ao motor real, sem as constantes mágicas deles. Para o MVP (settings spring/damper/força/soft-stop) podemos até dispensar periódicos/envelope no começo.
3. **Camada de motor:** `IMotorDriver`/`IEncoder` finos por cima do SimpleFOC (`BLDCMotor`/`FOCMotor` + `Encoder` do E6B2).
4. **Config/settings:** manter nosso protocolo A0 (schema-driven, vendor-HID) — já é o padrão certo; adicionar o guard de versão de flash e persistência ortogonal.
5. **Loop & segurança:** loop FFB em thread por timer ISR; watchdog em contexto separado; out-of-bounds cutoff; e-stop; fade-in; torque limiting multi-estágio. Casa com os itens de segurança do nosso spec (§5 hardware) e brake resistor.

Atualiza a matriz de risco do spec (§10): o **risco nº 1 (USB HID composto no STM32duino/F405)** cai de "alto" para "médio/baixo" — há um blueprint completo e comprovado. Confirma também usar **TinyUSB via PlatformIO** (não a pilha HID limitada do STM32duino).

---

## 6. Decisão de licença (a definir com o usuário)

- **Opção A — MIT (implementação própria):** escrevemos nosso firmware informados por esta análise. Structs/IDs/descriptor PID (≈ exemplo da spec USB-IF) são seguros; a matemática de efeitos reescrevemos com nossas unidades (o que já íamos fazer, pois recalibramos as constantes). Mantém o projeto todo MIT.
- **Opção B — Firmware GPLv3:** copiamos trechos do OpenFFBoard (descriptor, EffectsCalculator) mais literalmente e liberamos o firmware sob GPLv3; o app (DriveLab Studio) segue MIT (processo separado). Mais rápido, porém "contamina" o firmware com GPL.

**Recomendação:** **Opção A** — como já íamos reescrever a matemática em Nm/SimpleFOC e o descriptor PID é praticamente o exemplo da spec, o custo extra é pequeno e mantemos MIT e independência. Reaproveitamos o *conhecimento* (este doc), não o *código GPL* verbatim.

---

## Arquivos-fonte analisados (referência, no repo OpenFFBoard, `Firmware/FFBoard/`)
`Inc/ffb_defs.h`, `Inc/HidFFB.h` + `Src/HidFFB.cpp` (também em `UserExtensions/Src/HidFFB.cpp`), `UserExtensions/Inc/usb_hid_ffb_desc.h`, `UserExtensions/Inc/usb_descriptors.h` + `Src/usb_descriptors.cpp`, `Src/global_callbacks.cpp`, `Inc/UsbHidHandler.*`, `Inc/USBdevice.*`, `Inc/EffectsCalculator.h` + `Src/EffectsCalculator.cpp`, `Inc/Filters.h`, `Inc/Axis.*`, `Inc/AxesManager.*`, `Inc/MotorDriver.h`, `Inc/Encoder.h`, `Inc/CommandHandler.h`, `Inc/CommandInterface.h`, `Inc/ClassChooser.h`, `Inc/PersistentStorage.h`, `Src/cppmain.cpp`, `Src/FFBoardMainCommandThread.cpp`, `UserExtensions/*/FFBHIDMain.*`, `UserExtensions/Inc/ODriveCAN.h`, `Targets/F407VG/Core/Inc/tusb_config.h`.
