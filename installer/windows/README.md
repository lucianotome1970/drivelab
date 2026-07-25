# Instalador Windows (DriveLab Studio)

Como um **criador de DD** gera um `setup.exe` profissional que instala o app **já com a config de hardware
dele junto** — o comprador só dá duplo-clique, sem copiar nada à mão.

O que o instalador faz:
- Instala o app em `C:\Program Files\DriveLab`.
- Coloca o `hardware-profile.json` **ao lado do .exe** → o app **auto-carrega** no start.
- **Não** inclui `advanced.flag` → a aba **Hardware fica escondida** pro usuário final.
- Cria atalhos (Menu Iniciar / Área de trabalho) e desinstalador.

## Pré-requisitos
- **.NET 8 SDK** (o `dotnet publish` roda em **qualquer SO**, inclusive macOS — cross-compila pra Windows).
- **Inno Setup 6** (Windows) — só é necessário no passo de **compilar o instalador**. Se você está no Mac,
  use o **CI** (veja o fim) pra gerar o `.exe` sem precisar de Windows.

## Passo a passo (build local no Windows)
1. **Tenha o `hardware-profile.json`** do seu DD (exporte pelo app em modo avançado, ou parta do
   `docs/perfil-hardware.md`). Valores fora da faixa do schema são recusados pelo app.

2. **Publique o app** (self-contained — o comprador não precisa instalar .NET). Da raiz do repo:
   ```
   dotnet publish app/DriveLab.Studio/DriveLab.Studio.csproj -c Release -r win-x64 \
       --self-contained true -o installer/windows/publish
   ```

3. **Copie o SEU `hardware-profile.json`** pra dentro de `installer/windows/publish/` (ao lado do
   `DriveLab.Studio.exe`). ⚠️ **Não** ponha um arquivo `advanced.flag` aqui (senão o comprador veria o Hardware).

4. **Edite `DriveLab.iss`** — ajuste `MyAppVersion`, `MyAppPublisher`, `MyAppURL`.

5. **Compile** com o Inno Setup (Windows): abra o `DriveLab.iss` no *Inno Setup Compiler* e clique **Compile**
   (ou via linha de comando: `ISCC.exe DriveLab.iss`). Sai em `installer/windows/output/DriveLab-Setup-<versão>.exe`.

6. **Envie** esse `setup.exe` pros seus compradores. 🎉

## Sem Windows? Use o CI (GitHub Actions)
`.github/workflows/windows-installer.yml` publica o app + compila o Inno num runner **Windows**, e sobe o
`setup.exe` como artefato — você (no Mac) só dispara o workflow. Coloque o seu `hardware-profile.json` em
`installer/windows/` antes; o workflow o inclui no pacote.

## Notas
- **Update:** mantenha o `AppId` (GUID) fixo entre versões — assim o instalador reconhece e atualiza no lugar.
- **Tamanho:** self-contained gera ~80–150 MB (traz o runtime .NET). Se preferir menor, use
  `--self-contained false` (mas aí o comprador precisa do .NET 8 Desktop Runtime).
- **Assinatura de código:** pra não aparecer o aviso "editor desconhecido" do SmartScreen, é preciso um
  certificado de code-signing (pago) e assinar o `.exe` — opcional, mas dá o toque profissional final.
