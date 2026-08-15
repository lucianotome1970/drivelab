# Atualizar o firmware da base pela USB (Windows)

> **PT abaixo · English below**

A base é gravada **pela mesma USB de dados**, sem ST-Link e sem abrir a caixa. Vale para os dois
casos: atualizar uma base que já roda o DriveLab, e **gravar uma placa de fábrica pela primeira vez**.

Isso depende de **duas peças do lado do Windows** que não vêm com o sistema: o utilitário
`dfu-util`, que acompanha o instalador do DriveLab, e um driver para o dispositivo em modo DFU, que
é uma etapa única feita uma vez por computador.

---

## 🇧🇷 Português

### Placa que já roda o DriveLab

1. no app, aba **Atualização**, escolha o arquivo `.bin` do firmware
2. clique em **Enviar**
3. a placa **desarma o motor**, reinicia e aparece no Windows como **STM32 BOOTLOADER**
4. o app grava e a placa volta sozinha para o firmware novo

### Placa NOVA, de fábrica

Aqui o passo 2 acima não tem como funcionar: o comando "entre em modo de atualização" é enviado ao
nosso firmware, que ainda não está lá. Então o modo de atualização é acionado **na própria placa**.

1. **desligue de verdade** — tire a energia, conte até cinco. Não é reset: o que vem a seguir só
   vale no instante em que a placa acorda
2. force o modo de atualização: segure o botão `SW1`/`BOOT0`, **ou** mexa no jumper de BOOT — em
   muitas dessas placas ele precisa ser **removido**, e não colocado. Anote como estava antes
3. **ligue** nesse estado. A placa aparece no Windows como **STM32 BOOTLOADER**
4. no app, aba **Atualização**, escolha o `.bin` e clique em **Enviar**. O comando inicial cai no
   vazio, o app encontra a placa já em modo de atualização e grava
5. **solte o botão / devolva o jumper antes de religar.** Placa esquecida em modo de atualização
   nunca roda o firmware — e isso é idêntico a uma gravação que falhou

> Se o app não encontrar a placa, ele mostra as instruções e um botão **Continuar**: refaça o
> power-cycle e clique nele.

### O driver

**O instalador do DriveLab cuida disso.** Você não precisa fazer nada — nem baixar o Zadig, nem
escolher dispositivo em lista nenhuma. A placa não precisa nem estar ligada na hora: o driver fica
guardado e o Windows o usa sozinho no dia em que ela aparecer em modo de atualização.

<details>
<summary>Por que um driver é preciso, e o que o instalador faz</summary>

O modo de atualização vem gravado na ROM da ST e não pode ser mudado por nós. Ele não declara os
descritores que fariam o Windows escolher um driver sozinho, então a placa aparece com **erro
código 28** ("nenhum driver instalado") — o app vê o dispositivo e não consegue falar com ele.

O driver em si é o **WinUSB**, que já vem no Windows e é assinado pela Microsoft. O que faltava era
um arquivo dizendo "o dispositivo `0483:DF11` usa aquele driver que você já tem". O instalador
registra esse arquivo e pronto.

Ele também **confia no certificado, instala, e retira a confiança** em seguida: o Windows só precisa
confiar no instante da instalação, e nada nosso fica marcado como confiável na sua máquina depois.
Detalhes de procedência e licença em `installer/windows/driver/LEIAME.md`.

</details>

#### Se você compila o projeto e não usa o instalador

Rode uma vez, como administrador, a partir da pasta do projeto:

```powershell
powershell -ExecutionPolicy Bypass -File installer\windows\driver\instalar-driver.ps1
```

O caminho manual pelo [Zadig](https://zadig.akeo.ie/) continua funcionando (**Options → List All
Devices**, selecionar `STM32 BOOTLOADER` com USB ID `0483 DF11`, driver **WinUSB**), mas só é
necessário se o passo acima falhar.

> ⚠️ Se for de Zadig, **selecione exatamente esse dispositivo.** Ele troca o driver do item que
> estiver selecionado; apontar para outro (o ST-Link, um teclado, um mouse) deixa aquele dispositivo
> sem funcionar até você reverter pelo Gerenciador de Dispositivos. É justamente esse risco que o
> instalador elimina.

### O `dfu-util`

Vem junto com o instalador do DriveLab, ao lado do executável. O app o procura nesta ordem:

1. **ao lado do próprio app** (é o caso de quem instalou normalmente)
2. `tools\dfu-util.exe` dentro da pasta do app
3. instalações comuns de quem desenvolve (Homebrew no macOS, MSYS2 no Windows)
4. o `PATH` do sistema

Se você compila o projeto e não usa o instalador, instale por conta própria:

```
# MSYS2 (o mesmo ambiente usado para compilar o firmware)
pacman -S mingw-w64-x86_64-dfu-util

# ou baixe o binário oficial
# https://dfu-util.sourceforge.net/
```

O `dfu-util` é software livre sob **GPLv2 ou posterior**, e vai no pacote **sem modificação
nenhuma**, como executável separado que o Studio apenas invoca — o DriveLab não é obra derivada
dele. A licença e a procedência ficam na pasta `licencas`, ao lado do app, e a fonte correspondente
acompanha as releases do DriveLab. Projeto: <https://dfu-util.sourceforge.net/>.

### Quando algo não funciona

| sintoma | o que é |
|---|---|
| o app diz que a placa não entrou em DFU sozinha | pode ter entrado: veja no Gerenciador de Dispositivos se existe **STM32 BOOTLOADER**. Se existir com erro, falta o driver (acima) |
| **STM32 BOOTLOADER** com erro código 28 | o driver não entrou. Rode `instalar-driver.ps1` (pasta `driver`, ao lado do app) como administrador |
| a placa some e não volta | power-cycle. A placa detecta que veio de um DFU e reinicia no firmware normal |
| nada acontece ao clicar em Enviar | o `dfu-util` não foi encontrado — veja a ordem de busca acima |

**Recuperação:** se uma gravação for interrompida e a placa ficar sem firmware válido, a saída é a
chave **SW1 → DFU** com power-cycle (entra no bootloader por hardware, sempre funciona) ou gravar
por **ST-Link**.

---

## 🇬🇧 English

### A board already running DriveLab

1. in the app, **Update** tab, pick the firmware `.bin`
2. click **Send**
3. the board **disarms the motor**, reboots and shows up in Windows as **STM32 BOOTLOADER**
4. the app flashes it and the board returns to the new firmware on its own

### A NEW board, straight from the factory

Step 2 above cannot work here: the "enter update mode" command is sent to our firmware, which isn't
on the board yet. So update mode is triggered **on the board itself**.

1. **power it off for real** — pull the power, count to five. Not a reset: what follows only works
   at the instant the board wakes up
2. force update mode: hold the `SW1`/`BOOT0` button, **or** change the BOOT jumper — on many of
   these boards it has to be **removed**, not fitted. Note how it was set first
3. **power up** in that state. The board appears in Windows as **STM32 BOOTLOADER**
4. in the app, **Update** tab, pick the `.bin` and click **Send**. The initial command goes nowhere,
   the app finds the board already in update mode, and flashes it
5. **release the button / put the jumper back before the next power-up.** A board left in update
   mode will never run your firmware — and that looks exactly like a failed flash

> If the app doesn't find the board, it shows the instructions and a **Continue** button: redo the
> power cycle and click it.

### The driver

**The DriveLab installer takes care of it.** Nothing for you to do — no Zadig, no picking a device
from a list. The board doesn't even need to be plugged in at the time: the driver is stored and
Windows applies it by itself the day the board shows up in update mode.

<details>
<summary>Why a driver is needed at all, and what the installer does</summary>

Update mode lives in ST's ROM and cannot be changed by us. It doesn't declare the descriptors that
would let Windows pick a driver on its own, so the board shows up with **error code 28** ("no driver
installed") — the app sees the device and cannot talk to it.

The driver itself is **WinUSB**, which ships with Windows and is signed by Microsoft. What was
missing is a file saying "device `0483:DF11` uses that driver you already have". The installer
registers that file, and that's it.

It also **trusts the certificate, installs, and removes the trust** right after: Windows only needs
to trust it at install time, and nothing of ours stays marked as trusted on your machine afterwards.
Provenance and licensing details in `installer/windows/driver/LEIAME.md`.

</details>

#### If you build the project and don't use the installer

Run once, as administrator, from the project folder:

```powershell
powershell -ExecutionPolicy Bypass -File installer\windows\driver\instalar-driver.ps1
```

The manual [Zadig](https://zadig.akeo.ie/) route still works (**Options → List All Devices**, select
`STM32 BOOTLOADER` with USB ID `0483 DF11`, **WinUSB** driver), but is only needed if the step above
fails.

> ⚠️ If you do use Zadig, **select exactly that device.** It replaces the driver of whatever is
> selected; pointing it at something else (the ST-Link, a keyboard, a mouse) will break that device
> until you roll it back from Device Manager. That risk is precisely what the installer removes.

### `dfu-util`

Ships with the DriveLab installer, next to the executable. The app looks for it in this order:

1. **next to the app itself**
2. `tools\dfu-util.exe` inside the app folder
3. common developer installs (Homebrew on macOS, MSYS2 on Windows)
4. the system `PATH`

If you build from source and don't use the installer, install it yourself (`pacman -S
mingw-w64-x86_64-dfu-util` on MSYS2, or the official binary from
<https://dfu-util.sourceforge.net/>).

`dfu-util` is free software under **GPLv2 or later**, and ships **entirely unmodified**, as a
separate executable that Studio merely invokes — DriveLab is not a derivative work of it. Its
license and provenance live in the `licencas` folder next to the app, and the corresponding source
is attached to DriveLab releases. Project: <https://dfu-util.sourceforge.net/>.

### Troubleshooting

| symptom | what it means |
|---|---|
| app says the board didn't enter DFU by itself | it may have: check Device Manager for **STM32 BOOTLOADER**. If it's there with an error, the driver is missing |
| **STM32 BOOTLOADER** with error code 28 | the driver did not take. Run `instalar-driver.ps1` (the `driver` folder next to the app) as administrator |
| board disappears and doesn't come back | power cycle. It detects it came from DFU and reboots into normal firmware |
| nothing happens on Send | `dfu-util` wasn't found — see the search order above |

**Recovery:** if a flash is interrupted and the board is left without valid firmware, use the
**SW1 → DFU** switch plus a power cycle (hardware path into the bootloader, always works) or flash
over **ST-Link**.
