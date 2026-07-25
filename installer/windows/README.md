# Instalador Windows (DriveLab Studio)

Como um **criador de DD** gera um `setup.exe` profissional que instala o app **já com a config de hardware
dele junto** — o comprador só dá duplo-clique, sem copiar nada à mão.

O que o instalador faz:
- Instala o app em `C:\Program Files\DriveLab`.
- Coloca o `hardware-profile.json` **ao lado do .exe** → o app **auto-carrega** no start.
- **Não** inclui `advanced.flag` → a aba **Hardware fica escondida** pro usuário final.
- Cria atalhos (Menu Iniciar / Área de trabalho) e desinstalador.

## Pré-requisitos no Windows
- **Só internet** — o script **baixa e instala sozinho** o .NET 8 SDK (local, sem admin) e o Inno Setup se
  faltarem. (A instalação do Inno pode pedir UAC/admin na 1ª vez.)
- Levar a **pasta do projeto** (mantendo `app\` e `installer\` na estrutura do repo).

## Jeito fácil: um comando faz TUDO (recomendado)
1. **Configure o seu DD no app** (modo avançado): rode `DriveLab.Studio.exe --advanced`, abra a aba
   **Hardware**, ajuste tudo e clique **"Exportar perfil de hardware"** → salve como `hardware-profile.json`.
2. **Coloque esse `hardware-profile.json` em `installer\windows\`** (esta pasta).
3. No **PowerShell**, nesta pasta, rode (ou dê duplo-clique em `build-installer.bat`):
   ```powershell
   .\build-installer.ps1 -Version 1.0.0
   ```
   O script **baixa as dependências → publica o app → inclui o seu perfil → compila o instalador**. Sai em
   `installer\windows\output\DriveLab-Setup-1.0.0.exe`.
4. **Envie** esse `setup.exe` pros seus compradores. 🎉 (Antes, edite `DriveLab.iss` p/ pôr sua marca em
   `MyAppPublisher`.)

## Passo a passo manual (equivalente ao script)
1. `dotnet publish app\DriveLab.Studio\DriveLab.Studio.csproj -c Release -r win-x64 --self-contained true -o installer\windows\publish`
2. Copie o seu `hardware-profile.json` pra `installer\windows\publish\` (ao lado do `.exe`).
   ⚠️ **Não** ponha `advanced.flag` no pacote (senão o comprador veria o Hardware).
3. Compile: `ISCC.exe /DMyAppVersion=1.0.0 DriveLab.iss` (ou abra no *Inno Setup Compiler* → Compile).

## Alternativa: CI (GitHub Actions) — gera o .exe sem ter Windows
`.github/workflows/windows-installer.yml` publica + compila o Inno num runner **Windows** e sobe o
`setup.exe` como artefato. Útil se você desenvolve no Mac/Linux. Commite o `hardware-profile.json` em
`installer/windows/` (ou deixe genérico).

## Notas
- **Update:** mantenha o `AppId` (GUID) fixo entre versões — assim o instalador reconhece e atualiza no lugar.
- **Tamanho:** self-contained gera ~80–150 MB (traz o runtime .NET). Se preferir menor, use
  `--self-contained false` (mas aí o comprador precisa do .NET 8 Desktop Runtime).
- **Assinatura de código:** pra não aparecer o aviso "editor desconhecido" do SmartScreen, é preciso um
  certificado de code-signing (pago) e assinar o `.exe` — opcional, mas dá o toque profissional final.
