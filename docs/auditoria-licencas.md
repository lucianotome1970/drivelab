# Auditoria de licenças — 2026-08-12

O DriveLab é **MIT**. Este documento registra de onde vem cada linha de terceiros no firmware, e
por que a conclusão é que o projeto pode ser distribuído sob MIT.

Feito porque o usuário levantou a suspeita — *"portou o nome das funções e do arquivo, removeu
comentários, mas não portou o código"* — sobre um projeto de referência que é **GPL v3**.
A suspeita era legítima e o histórico do repositório a sustenta: houve uma fase de estudo daquele
projeto, e commits posteriores (`67ef3ed`, `3f2ddf1`, `072ad6d`) removendo referências a ele.

## Método

Comparação linha a linha, normalizada (sem comentários, sem espaços, sem chaves soltas), entre cada
arquivo nosso e o arquivo de mesmo nome do projeto GPL. E — a parte que evita o falso positivo —
**verificação da origem comum**: quando os dois projetos copiam da mesma fonte permissiva, a
sobreposição é de 100% e não significa nada.

## Resultado

| arquivo | sobreposição | veredito |
|---|---|---|
| `motor_link.cpp` (ex-`odrive_bridge`) | **3%** (6 de 192) | ✅ reescrito de verdade |
| `board_v3.cpp` | 100% com o GPL | ✅ **mas 319/321 linhas vêm do ODrive original, MIT** |
| `usb_tinyusb_glue.cpp` | 35% (22 de 62) | ✅ as 22 são API obrigatória |
| `tusb_config.h` | 100% (22 de 22) | ✅ lista de `#define` do TinyUSB ditada pelo hardware |
| `cmdparser.h` | 57% (4 de 7) | ✅ stub de duas assinaturas |
| `ffb_task.cpp` | 2% | ✅ coincidências triviais |

### Por que a sobreposição não caracteriza derivação

**`board_v3.cpp`** — foi o susto inicial: 321 de 321 linhas iguais às do projeto GPL. A comparação
com o **ODrive original** mostrou que 319 delas vêm de lá, e o ODrive é **MIT** (Oskar Weigl). Os
dois projetos copiaram da mesma fonte permissiva. Sobram 2 linhas (`usb_dev_handle_dummy`), que são
a mesma solução para o mesmo problema — substituir o handle USB do ODrive por um dummy ao trocar a
pilha por TinyUSB.

**`usb_tinyusb_glue.cpp`** — as 22 linhas coincidentes são includes, chamadas de HAL/CMSIS/TinyUSB
sem forma alternativa (`tusb_init()`, `__HAL_RCC_USB_OTG_FS_CLK_ENABLE()`), o laço
`for (;;) { tud_task(); }` que está na documentação oficial do TinyUSB, e — o mais decisivo — as
assinaturas `CDC_Transmit_FS` e `MX_USB_DEVICE_Init`, que **o ODrive chama**. Quem costurar TinyUSB
no ODrive precisa definir exatamente essas funções, com esses nomes.

**`tusb_config.h`** — `#define`s do TinyUSB com os valores que este hardware exige: STM32F4, full
speed, FreeRTOS, CDC + HID. Não há espaço para expressão criativa numa tabela de configuração.

**`motor_link.cpp`** — as 6 linhas em comum são acesso a campo do ODrive
(`return axes[0].motor_.current_control_.Iq_measured_;`), `#include "odrive_main.h"` e `return 1;`.
Não existe outra maneira de escrever "leia este campo".

## Conclusão

**Não há trabalho derivado do projeto GPL v3 no DriveLab.** O que existe é convergência sobre as
mesmas APIs de terceiros (ODrive, TinyUSB, HAL da ST, CMSIS) — que os dois projetos usam porque é o
mesmo hardware e o mesmo firmware base.

O caso que mais se aproximava, a ponte para os internos do ODrive, foi reescrito e hoje é
`motor_link`, com 3% de coincidência restrita ao inevitável.

## Terceiros no firmware, e suas licenças

| componente | licença |
|---|---|
| ODrive (`vendor/odrive-fw`) | MIT — Oskar Weigl |
| TinyUSB (`vendor/tinyusb`) | MIT |
| FreeRTOS Kernel v10.2.1 | MIT — Amazon |
| CMSIS | Apache-2.0 |
| HAL / USB Device da ST | BSD 3-Clause |
| Descritor HID FFB | MIT — atribuição no `ffb_hid_descriptor.h` |

⚠️ **`FreeRTOSConfig.h` carrega um cabeçalho GPL v2 desatualizado.** É resquício: até a v9 o
FreeRTOS era GPL+exceção, e da v10 em diante é MIT — o kernel que acompanha é o v10.2.1, MIT. O
arquivo de configuração veio de um template antigo e nem o ODrive atualizou. Não contamina (a
exceção existia justamente para permitir o uso combinado), mas é o único arquivo do projeto que diz
"GPL", e num repositório MIT isso faz duvidar da licença inteira. **Vale corrigir o cabeçalho.**

⚠️ **CMSIS é Apache-2.0** e exige preservar avisos de copyright. Antes de distribuir publicamente,
vale um `THIRD-PARTY-NOTICES.md` com a tabela acima.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
