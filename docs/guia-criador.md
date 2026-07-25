# DriveLab — DD Maker's Guide / Guia do Criador de DD

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

---

## 🇬🇧 English

This guide is for people who **build and sell** Direct Drive wheels using DriveLab. It explains:
1. the **difference** between using the app as a **maker** vs an **end user**;
2. how the maker **configures the hardware** and **exports** the profile;
3. how to generate a **Windows installer** that ships the app **already configured** to the buyer.

### 1. Two roles, two ways to use the same app

The **same app** (DriveLab Studio) behaves in two ways, depending on who opens it:

| | 🛠️ **Maker** (you) | 🎮 **End user** (buyer) |
|---|---|---|
| **How the app opens** | with **advanced mode** on (flag `--advanced` or an `advanced.flag` file) | **normal** — double-click |
| **"Hardware" tab** | **visible** — configure everything | **hidden** — never sees it |
| **What they adjust** | **hardware** (board variant, motor, encoder, current) **+ feel** | **feel only** (force, effects, curves, angle) |
| **Hardware config** | **defines and exports** it | **comes ready** (bundled in the installer) |
| **Goal** | prepare the product | use the wheel |

> **Why is the Hardware tab hidden from the buyer?** Because it holds the **dangerous** parameters (pole pairs,
> current, board variant). A wrong value can damage the motor/board. The one who knows what they did is **you**;
> the buyer shouldn't (and doesn't need to) touch it.

**The two config layers**
- **Hardware** (dangerous, fixed — set by the maker): board variant (24V/56V), supply voltage, encoder
  type/direction/CPR, pole pairs, current-loop gains (P/I), calibration current.
- **Feel** (free — the user tweaks freely): total force, damper, soft-stop, telemetry effects, force curves,
  **steering angle (DOR)**.

Changing the feel **never** alters the hardware. They're separate by design.

### 2. How the MAKER uses the app (advanced mode)

**2.1. Open in advanced mode** — either way works:
- **Flag:** launch from the command line with `--advanced`
  ```
  DriveLab.Studio.exe --advanced
  ```
- **Marker file:** create an empty file named **`advanced.flag`** next to the `.exe`. While it exists, the app
  opens in advanced mode.

> The **end user never does this** — without the flag/file the Hardware tab stays hidden. It's your option only.

**2.2. Configure the hardware** — connect the base, open the **Hardware** tab, and fill in for **your** DD:
board variant (24V/56V), supply voltage, encoder type/CPR/direction, pole pairs, Current P/I, calibration current.

**2.3. Export the hardware profile** — at the bottom of the Hardware tab:
1. Fill in **Vendor**, **Device** and **Notes** (e.g., "Motor X, 2Ω brake").
2. Click **"Export hardware profile"** → save as **`hardware-profile.json`**.

That file is your DD's "ID card" — it's what goes bundled into the installer.

### 3. Generate the Windows installer

The installer hands the buyer the app **+ your config bundled** — they just double-click, nothing manual.
Since it doesn't include `advanced.flag`, the Hardware tab is already **hidden**.

**3.1. Prerequisites**
- A **Windows** machine (the buyer is on Windows; the installer is Windows).
- **Internet** — the script **downloads and installs by itself** whatever is missing (.NET 8 SDK and Inno Setup).
- The **project folder** (keeping `app\` and `installer\` together, as in the repo).

**3.2. Step by step (one command does it all)**
1. Copy your **`hardware-profile.json`** (from 2.3) into **`installer\windows\`**.
2. Open **PowerShell** there and run (or **double-click** `build-installer.bat`):
   ```powershell
   .\build-installer.ps1 -Version 1.0.0
   ```
3. The script, in order: downloads/installs the **.NET 8 SDK** (local, no admin) if missing → downloads/installs
   **Inno Setup** if missing → **publishes** the app (self-contained — the buyer needs no .NET) → **bundles**
   your `hardware-profile.json` and **removes** any `advanced.flag` → **compiles** the installer.
4. Result: **`installer\windows\output\DriveLab-Setup-1.0.0.exe`**.
5. (Optional, recommended) Edit `installer\windows\DriveLab.iss` and set `MyAppPublisher` to **your brand**. To
   avoid the SmartScreen warning you need a **code-signing certificate** (paid).

> **No Windows?** A CI workflow (`.github/workflows/windows-installer.yml`) compiles the installer on a GitHub
> Windows runner and hands you the `.exe` as an artifact — useful if you develop on Mac/Linux.

**3.3. Distribute** — send the buyer the **`DriveLab-Setup-<version>.exe`**. That's it.

### 4. How the END USER uses it (the buyer)

1. Runs the **`setup.exe`** → installs the app (Start Menu, shortcut, uninstaller).
2. Opens the app **normally** (double-click).
3. The app **auto-loads** the bundled hardware config → the wheel just works right.
4. The **Hardware tab doesn't appear** — they tweak only the **feel** (force, effects, curves, angle).

The buyer needs **no** .NET, no file copying, no motor knowledge. Plug and play.

### 5. How it works under the hood

- **Advanced mode:** enabled by the `--advanced` flag **or** an `advanced.flag` file next to the `.exe`. Without
  it, the Hardware tab is omitted (fail-safe: hidden by default).
- **Hardware profile:** a JSON. The app looks in two places, in this order:
  1. **next to the `.exe`** (what the installer puts in `Program Files\DriveLab`) — takes precedence;
  2. `ApplicationData\DriveLab\hardware-profile.json` (manual/dev tweak).
- **Validation:** every value is checked against the schema ranges — out-of-range is **rejected** (not applied)
  and logged. It never applies anything dangerous.
- **Application:** on connecting the base, the profile is **written to the device** (authoritative), in any mode.
- **Durability:** a firmware update can re-seed the board's config; the app **re-applies** the profile
  automatically — the maker's config isn't lost.

### 6. References
- **Profile technical details:** [`docs/perfil-hardware.md`](perfil-hardware.md)
- **Installer build (steps & alternatives):** [`installer/windows/README.md`](../installer/windows/README.md)
- **Build script:** [`installer/windows/build-installer.ps1`](../installer/windows/build-installer.ps1)

---

## 🇧🇷 Português

Este guia é para quem **monta e vende** volantes Direct Drive usando o DriveLab. Explica:
1. a **diferença** entre usar o app como **criador** e como **usuário final**;
2. como o criador **configura o hardware** e **exporta** o perfil;
3. como gerar um **instalador Windows** que entrega o app **já configurado** ao comprador.

### 1. Dois papéis, dois usos do mesmo app

O **mesmo app** (DriveLab Studio) se comporta de dois jeitos, dependendo de quem abre:

| | 🛠️ **Criador** (você) | 🎮 **Usuário final** (comprador) |
|---|---|---|
| **Como abre o app** | com o **modo avançado** ligado (flag `--advanced` ou arquivo `advanced.flag`) | **normal** — duplo-clique |
| **Aba "Hardware"** | **aparece** — configura tudo | **escondida** — nem vê |
| **O que ajusta** | **hardware** (variante, motor, encoder, corrente) **+ feel** | **só o feel** (força, efeitos, curvas, ângulo) |
| **Config de hardware** | **define e exporta** | **vem pronta** (embutida no instalador) |
| **Objetivo** | preparar o produto | usar o volante |

> **Por que a aba Hardware fica escondida do comprador?** Porque ali estão os parâmetros **perigosos** (pares
> de polo, corrente, variante da placa). Um valor errado pode danificar o motor/placa. Quem sabe o que fez é
> **você**; o comprador não deve (nem precisa) mexer.

**As duas camadas de configuração**
- **Hardware** (perigoso, fixo — definido pelo criador): variante da placa (24V/56V), tensão da fonte, tipo/
  direção/CPR do encoder, pares de polo, ganhos de corrente (P/I), corrente de calibração.
- **Feel** (livre — o usuário mexe à vontade): força total, damper, soft-stop, efeitos por telemetria,
  curvas de força, **ângulo de giro (DOR)**.

Mexer no feel **nunca** altera o hardware. São separados de propósito.

### 2. Como o CRIADOR usa o app (modo avançado)

**2.1. Abrir em modo avançado** — qualquer uma das formas:
- **Flag:** abra pela linha de comando com `--advanced`
  ```
  DriveLab.Studio.exe --advanced
  ```
- **Arquivo marcador:** crie um arquivo vazio chamado **`advanced.flag`** na pasta do `.exe`. Enquanto ele
  existir, o app abre em modo avançado.

> O **usuário final nunca faz isso** — sem flag/arquivo, a aba Hardware fica escondida. É opção só sua.

**2.2. Configurar o hardware** — conecte a base, vá na aba **Hardware** e preencha conforme o **seu** DD:
variante da placa (24V/56V), tensão da fonte, tipo/CPR/direção do encoder, pares de polo, Current P/I,
corrente de calibração.

**2.3. Exportar o perfil de hardware** — no rodapé da aba Hardware:
1. Preencha **Marca (vendor)**, **Modelo (device)** e **Notas** (ex.: "Motor X, brake 2Ω").
2. Clique **"Exportar perfil de hardware"** → salve como **`hardware-profile.json`**.

Esse arquivo é a "carteira de identidade" do seu DD — é o que vai embutido no instalador.

### 3. Gerar o INSTALADOR Windows

O instalador entrega ao comprador o app **+ a sua config junto** — ele só dá duplo-clique, nada manual. Como
não inclui o `advanced.flag`, a aba Hardware já vem **escondida**.

**3.1. Pré-requisitos**
- Um **Windows** (o comprador é Windows; o instalador é Windows).
- **Internet** — o script **baixa e instala sozinho** o que faltar (.NET 8 SDK e Inno Setup).
- A **pasta do projeto** (mantendo `app\` e `installer\` juntos, como no repositório).

**3.2. Passo a passo (um comando faz tudo)**
1. Copie o seu **`hardware-profile.json`** (do passo 2.3) para a pasta **`installer\windows\`**.
2. Abra o **PowerShell** nessa pasta e rode (ou dê **duplo-clique** em `build-installer.bat`):
   ```powershell
   .\build-installer.ps1 -Version 1.0.0
   ```
3. O script faz, em ordem: baixa/instala o **.NET 8 SDK** (local, sem admin) se faltar → baixa/instala o
   **Inno Setup** se faltar → **publica** o app (self-contained — o comprador não precisa de .NET) →
   **inclui** o seu `hardware-profile.json` e **remove** qualquer `advanced.flag` → **compila** o instalador.
4. Resultado: **`installer\windows\output\DriveLab-Setup-1.0.0.exe`**.
5. (Opcional, recomendado) Edite `installer\windows\DriveLab.iss` e troque `MyAppPublisher` pela **sua marca**.
   Pra um `.exe` sem o aviso do SmartScreen, é preciso um **certificado de code-signing** (pago).

> **Sem Windows?** Há um workflow de CI (`.github/workflows/windows-installer.yml`) que compila o instalador
> num runner Windows do GitHub e te dá o `.exe` como artefato — útil pra quem desenvolve no Mac/Linux.

**3.3. Distribuir** — envie o **`DriveLab-Setup-<versão>.exe`** ao comprador. Só isso.

### 4. Como o USUÁRIO FINAL usa (o comprador)

1. Roda o **`setup.exe`** → instala o app (Menu Iniciar, atalho, desinstalador).
2. Abre o app **normalmente** (duplo-clique).
3. O app **auto-carrega** a config de hardware que veio junto → o volante já funciona certo.
4. A aba **Hardware não aparece** — ele ajusta só o **feel** (força, efeitos, curvas, ângulo).

O comprador **não precisa** de .NET, nem copiar arquivo, nem entender de motor. Plug-and-play.

### 5. Como funciona por dentro

- **Modo avançado:** ligado pela flag `--advanced` **ou** pelo arquivo `advanced.flag` ao lado do `.exe`. Sem
  isso, a aba Hardware é omitida (fail-safe: escondida por padrão).
- **Perfil de hardware:** um JSON. O app procura em dois lugares, nesta ordem:
  1. **ao lado do `.exe`** (o que o instalador coloca em `Program Files\DriveLab`) — precedência;
  2. `ApplicationData\DriveLab\hardware-profile.json` (ajuste manual/dev).
- **Validação:** todo valor é conferido contra as faixas do schema — valor fora da faixa é **recusado** (não
  aplica) e logado. Nunca aplica algo perigoso.
- **Aplicação:** ao conectar a base, o perfil é **gravado no dispositivo** (autoritativo), em qualquer modo.
- **Durabilidade:** um update de firmware pode re-semear a config da placa; o app **reaplica** o perfil
  automaticamente — a config do criador não se perde.

### 6. Referências
- **Detalhes técnicos do perfil:** [`docs/perfil-hardware.md`](perfil-hardware.md)
- **Build do instalador (passos e alternativas):** [`installer/windows/README.md`](../installer/windows/README.md)
- **Script de build:** [`installer/windows/build-installer.ps1`](../installer/windows/build-installer.ps1)
