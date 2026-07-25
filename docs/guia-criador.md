# Guia do Criador de DD — configurar o hardware e gerar o instalador

Este guia é para quem **monta e vende** volantes Direct Drive usando o DriveLab. Explica:
1. a **diferença** entre usar o app como **criador** e como **usuário final**;
2. como o criador **configura o hardware** e **exporta** o perfil;
3. como gerar um **instalador Windows** que entrega o app **já configurado** ao comprador.

---

## 1. Dois papéis, dois usos do mesmo app

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

### As duas camadas de configuração
- **Hardware** (perigoso, fixo — definido pelo criador): variante da placa (24V/56V), tensão da fonte, tipo/
  direção/CPR do encoder, pares de polo, ganhos de corrente (P/I), corrente de calibração.
- **Feel** (livre — o usuário mexe à vontade): força total, damper, soft-stop, efeitos por telemetria,
  curvas de força, **ângulo de giro (DOR)**.

Mexer no feel **nunca** altera o hardware. São separados de propósito.

---

## 2. Como o CRIADOR usa o app (modo avançado)

### 2.1. Abrir em modo avançado
Duas formas (qualquer uma):
- **Flag:** abra pela linha de comando com `--advanced`
  ```
  DriveLab.Studio.exe --advanced
  ```
- **Arquivo marcador:** crie um arquivo vazio chamado **`advanced.flag`** na pasta do `.exe`. Enquanto ele
  existir, o app abre em modo avançado.

> O **usuário final nunca faz isso** — sem flag/arquivo, a aba Hardware fica escondida. É opção só sua.

### 2.2. Configurar o hardware
1. Conecte a base do volante.
2. Vá na aba **Hardware**.
3. Preencha conforme o **seu** DD: **Variante da placa** (24V/56V), **Tensão da fonte**, **Tipo/CPR/direção do
   encoder**, **Pares de polo**, **Current P/I**, **Corrente de calibração**.

### 2.3. Exportar o perfil de hardware
Ainda na aba Hardware, no rodapé:
1. Preencha **Marca (vendor)**, **Modelo (device)** e **Notas** (ex.: "Motor X, brake 2Ω").
2. Clique **"Exportar perfil de hardware"** → salve como **`hardware-profile.json`**.

Esse arquivo é a "carteira de identidade" do seu DD — é o que vai embutido no instalador.

---

## 3. Gerar o INSTALADOR Windows

O instalador entrega ao comprador o app **+ a sua config junto** — ele só dá duplo-clique, nada manual. Como
não inclui o `advanced.flag`, a aba Hardware já vem **escondida**.

### 3.1. Pré-requisitos
- Um **Windows** (o comprador é Windows; o instalador é Windows).
- **Internet** — o script **baixa e instala sozinho** o que faltar (.NET 8 SDK e Inno Setup).
- A **pasta do projeto** (mantendo `app\` e `installer\` juntos, como no repositório).

### 3.2. Passo a passo (um comando faz tudo)
1. Copie o seu **`hardware-profile.json`** (do passo 2.3) para a pasta **`installer\windows\`**.
2. Abra o **PowerShell** nessa pasta e rode (ou dê **duplo-clique** em `build-installer.bat`):
   ```powershell
   .\build-installer.ps1 -Version 1.0.0
   ```
3. O script faz, em ordem:
   1. baixa/instala o **.NET 8 SDK** (local, sem admin) se faltar;
   2. baixa/instala o **Inno Setup** se faltar;
   3. **publica** o app (self-contained — o comprador não precisa de .NET);
   4. **inclui** o seu `hardware-profile.json` e **remove** qualquer `advanced.flag`;
   5. **compila** o instalador.
4. Resultado: **`installer\windows\output\DriveLab-Setup-1.0.0.exe`**.
5. (Opcional, recomendado) Antes, edite `installer\windows\DriveLab.iss` e troque `MyAppPublisher` pela **sua
   marca**. Pra um `.exe` sem o aviso do SmartScreen, é preciso um **certificado de code-signing** (pago).

> **Sem Windows?** Há um workflow de CI (`.github/workflows/windows-installer.yml`) que compila o instalador
> num runner Windows do GitHub e te dá o `.exe` como artefato — útil pra quem desenvolve no Mac/Linux.

### 3.3. Distribuir
Envie o **`DriveLab-Setup-<versão>.exe`** ao comprador. Só isso.

---

## 4. Como o USUÁRIO FINAL usa (o comprador)

1. Roda o **`setup.exe`** → instala o app (Menu Iniciar, atalho, desinstalador).
2. Abre o app **normalmente** (duplo-clique).
3. O app **auto-carrega** a config de hardware que veio junto → o volante já funciona certo.
4. A aba **Hardware não aparece** — ele ajusta só o **feel** (força, efeitos, curvas, ângulo).

O comprador **não precisa** de .NET, nem copiar arquivo, nem entender de motor. Plug-and-play.

---

## 5. Como funciona por dentro (resumo técnico)

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

---

## 6. Referências
- **Detalhes técnicos do perfil:** [`docs/perfil-hardware.md`](perfil-hardware.md)
- **Build do instalador (passos e alternativas):** [`installer/windows/README.md`](../installer/windows/README.md)
- **Script de build:** [`installer/windows/build-installer.ps1`](../installer/windows/build-installer.ps1)
