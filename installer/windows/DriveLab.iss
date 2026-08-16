; ============================================================================
;  DriveLab — Inno Setup script (instalador Windows profissional).
;  Empacota o app PUBLICADO + o hardware-profile.json do criador. O comprador roda o setup.exe:
;  instala tudo e a config de hardware já vem junta — nada manual. Sem advanced.flag => a aba
;  Hardware fica escondida pro usuário final.
;  Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
; ============================================================================

; ---- O CRIADOR edita estes campos ----
#define MyAppName "DriveLab Studio"
; ⚠️ MESMA versao do app, do firmware e da release — os QUATRO sobem juntos ao cortar uma release.
; Era "1.0.0" enquanto todo o resto estava em 0.2.3, e um quarto numero solto so gera a duvida que a
; regra existe para evitar: "preciso atualizar?". A fonte do firmware e firmware-base/inc/fw_version.h
; e a do app e o <Version> do DriveLab.Studio.csproj.
#define MyAppVersion "0.2.7"
#define MyAppPublisher "Sua Marca DD"
#define MyAppURL "https://exemplo.com"
#define MyAppExeName "DriveLab.Studio.exe"
; Pasta com o app PUBLICADO (dotnet publish). DEVE conter o .exe e o hardware-profile.json do criador.
#define PublishDir "publish"

[Setup]
; AppId identifica "DriveLab" (mantenha fixo entre versões p/ o update reconhecer). {{ = literal {.
AppId={{7A9E4C21-3B5D-4F8A-9E2C-1D6F0B4A8E31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\DriveLab
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=DriveLab-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Todo o app publicado — inclui o hardware-profile.json que o criador colocou na PublishDir (fica ao lado
; do .exe em Program Files\DriveLab, e o app auto-carrega no start). NÃO inclua um advanced.flag aqui.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; Pacote de driver do modo de atualizacao (ver [Run] e driver\LEIAME.md). Vai para uma subpasta
; propria porque o INF referencia os coinstaladores por caminho relativo (amd64\...), e o pnputil
; le o INF de onde ele estiver.
Source: "driver\*"; DestDir: "{app}\driver"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; ---------------------------------------------------------------------------------------------
; DRIVER DO MODO DE ATUALIZACAO — automatizado desde 14/08/2026. O Zadig sai do caminho.
;
; POR QUE PRECISA DE DRIVER: o bootloader do STM32 e gravado na ROM da ST e nao declara WCID, entao
; o Windows nao escolhe driver sozinho. Sem ele a placa entra em DFU e aparece com ERRO CODIGO 28
; ("nenhum driver"), e a atualizacao para ali. Diagnosticado na bancada em 2026-08-10.
;
; POR QUE AGORA DA, E ANTES NAO DAVA: a tentativa anterior queria gerar o pacote de driver NA
; MAQUINA DE QUEM INSTALA, o que exigia o 'wdi-simple.exe' — que o libwdi nao distribui pronto e
; obrigaria a compilar a biblioteca dentro do nosso build. Mas gerar em tempo real so faz sentido
; para o Zadig, que nao sabe de antemao qual dispositivo o usuario vai escolher. NOS SABEMOS: e
; sempre 0483:DF11. Entao o pacote e gerado UMA VEZ, entra no repositorio (installer\windows\driver)
; e aqui so resta instala-lo com o pnputil, que ja vem no Windows.
;
; A confianca no certificado e TEMPORARIA: o script adiciona, instala e remove. Ver o LEIAME.md
; naquela pasta para a procedencia, as licencas e por que isto e seguro.
;
; A placa NAO precisa estar conectada: sem ela, o driver fica no DriverStore e o Windows o aplica
; sozinho no dia em que ela aparecer.
;
; ⚠️ SEM 'runascurrentuser' — e a flag faz o CONTRARIO do que o nome sugere aqui. O instalador ja
; roda elevado (PrivilegesRequired=admin), e por padrao as entradas [Run] herdam essa elevacao.
; 'runascurrentuser' REBAIXA o processo para o usuario original, sem privilegio — e registrar driver
; exige administrador. Custou uma rodada inteira do teste de implantacao em 14/08/2026: o script era
; copiado, era chamado, recusava por falta de privilegio e a instalacao seguia como se nada fosse.
; O sintoma final era a placa em modo de atualizacao com CM_PROB_FAILED_INSTALL e o dfu-util dizendo
; LIBUSB_ERROR_NOT_SUPPORTED — que parece problema de driver e era problema de permissao.
;
; -WindowStyle Hidden para nao piscar um console preto no meio da instalacao. Falhar aqui NAO aborta
; a instalacao do app — quem so quer ajustar a base nao depende deste passo, e o caminho manual
; continua documentado.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""{app}\driver\instalar-driver.ps1"" -Silencioso"; \
  StatusMsg: "Preparando o modo de atualizacao da placa..."; \
  Flags: waituntilterminated
; O 'dfu-util', que e quem de fato grava, VAI no pacote (ver build-installer.ps1, etapa 4b).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
