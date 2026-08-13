# Atualizar o firmware da base pela USB (Windows)

> **PT abaixo · English below**

A base pode ser atualizada **pela mesma USB de dados**, sem ST-Link e sem abrir a caixa. O app manda
o comando, a placa reinicia no bootloader da ST e o app grava o firmware novo.

Isso depende de **duas peças do lado do Windows** que não vêm com o sistema: o utilitário
`dfu-util` e um driver para o dispositivo em modo DFU. O instalador do DriveLab já traz o
`dfu-util`; o driver é uma etapa única, feita uma vez por computador.

---

## 🇧🇷 Português

### Como funciona

1. no app, aba **Atualização**, escolha o arquivo `.bin` do firmware
2. clique em **Enviar**
3. a placa **desarma o motor**, reinicia e aparece no Windows como **STM32 BOOTLOADER**
4. o app grava e a placa volta sozinha para o firmware novo

### O driver

**Se você instalou pelo instalador do DriveLab, já está feito** — ele registra o driver do modo DFU
durante a instalação (marque a opção "Instalar o driver de atualização de firmware"). A primeira
atualização já funciona, sem passo nenhum.

O resto desta seção é para quem **compila do código** ou desmarcou a opção.

Sem o driver, o Windows enumera a placa em DFU mas marca com **erro código 28** ("nenhum driver
instalado") — o app vê o dispositivo e não consegue falar com ele.

1. baixe o **Zadig** em <https://zadig.akeo.ie/> (não precisa instalar, é um `.exe` só)
2. deixe a placa **em modo DFU** — clique em Enviar no app, ou use a chave SW1 e faça power-cycle
3. rode o Zadig **como administrador**
4. menu **Options → List All Devices**
5. selecione **`STM32 BOOTLOADER`** na lista — confira que o USB ID mostra **`0483 DF11`**
6. escolha o driver **WinUSB** e clique em **Install Driver**

> ⚠️ **Selecione exatamente esse dispositivo.** O Zadig troca o driver do item que estiver
> selecionado; apontar para outro (o ST-Link, um teclado, um mouse) deixa aquele dispositivo sem
> funcionar até você reverter pelo Gerenciador de Dispositivos.

Feito isso, o Windows passa a reconhecer a placa em DFU e o app grava direto.

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

O `dfu-util` é software livre sob **GPLv2**, distribuído junto como executável separado. O código
e a licença estão em <https://dfu-util.sourceforge.net/>.

### Quando algo não funciona

| sintoma | o que é |
|---|---|
| o app diz que a placa não entrou em DFU sozinha | pode ter entrado: veja no Gerenciador de Dispositivos se existe **STM32 BOOTLOADER**. Se existir com erro, falta o driver (acima) |
| **STM32 BOOTLOADER** com erro código 28 | driver não instalado — rode o Zadig |
| a placa some e não volta | power-cycle. A placa detecta que veio de um DFU e reinicia no firmware normal |
| nada acontece ao clicar em Enviar | o `dfu-util` não foi encontrado — veja a ordem de busca acima |

**Recuperação:** se uma gravação for interrompida e a placa ficar sem firmware válido, a saída é a
chave **SW1 → DFU** com power-cycle (entra no bootloader por hardware, sempre funciona) ou gravar
por **ST-Link**.

---

## 🇬🇧 English

### How it works

1. in the app, **Update** tab, pick the firmware `.bin`
2. click **Send**
3. the board **disarms the motor**, reboots and shows up in Windows as **STM32 BOOTLOADER**
4. the app flashes it and the board returns to the new firmware on its own

### The driver

**If you used the DriveLab installer, this is already done** — it registers the DFU driver during
installation (keep the "Install the firmware update driver" option checked). The first update just
works.

The rest of this section is for people who **build from source** or unchecked that option.

Without the driver Windows enumerates the board in DFU but flags **error code 28** ("no driver
installed") — the app sees the device and cannot talk to it.

1. get **Zadig** from <https://zadig.akeo.ie/> (single `.exe`, no install needed)
2. put the board **in DFU mode** — click Send in the app, or use the SW1 switch plus a power cycle
3. run Zadig **as administrator**
4. **Options → List All Devices**
5. select **`STM32 BOOTLOADER`** — check that the USB ID reads **`0483 DF11`**
6. choose the **WinUSB** driver and click **Install Driver**

> ⚠️ **Select exactly that device.** Zadig replaces the driver of whatever is selected; pointing it
> at something else (the ST-Link, a keyboard, a mouse) will break that device until you roll it back
> from Device Manager.

### `dfu-util`

Ships with the DriveLab installer, next to the executable. The app looks for it in this order:

1. **next to the app itself**
2. `tools\dfu-util.exe` inside the app folder
3. common developer installs (Homebrew on macOS, MSYS2 on Windows)
4. the system `PATH`

If you build from source and don't use the installer, install it yourself (`pacman -S
mingw-w64-x86_64-dfu-util` on MSYS2, or the official binary from
<https://dfu-util.sourceforge.net/>).

`dfu-util` is free software under **GPLv2**, shipped as a separate executable. Source and license at
<https://dfu-util.sourceforge.net/>.

### Troubleshooting

| symptom | what it means |
|---|---|
| app says the board didn't enter DFU by itself | it may have: check Device Manager for **STM32 BOOTLOADER**. If it's there with an error, the driver is missing |
| **STM32 BOOTLOADER** with error code 28 | driver not installed — run Zadig |
| board disappears and doesn't come back | power cycle. It detects it came from DFU and reboots into normal firmware |
| nothing happens on Send | `dfu-util` wasn't found — see the search order above |

**Recovery:** if a flash is interrupted and the board is left without valid firmware, use the
**SW1 → DFU** switch plus a power cycle (hardware path into the bootloader, always works) or flash
over **ST-Link**.
