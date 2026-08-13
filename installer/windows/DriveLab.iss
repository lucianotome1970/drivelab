; ============================================================================
;  DriveLab — Inno Setup script (instalador Windows profissional).
;  Empacota o app PUBLICADO + o hardware-profile.json do criador. O comprador roda o setup.exe:
;  instala tudo e a config de hardware já vem junta — nada manual. Sem advanced.flag => a aba
;  Hardware fica escondida pro usuário final.
;  Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
; ============================================================================

; ---- O CRIADOR edita estes campos ----
#define MyAppName "DriveLab Studio"
#define MyAppVersion "1.0.0"
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

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; ---------------------------------------------------------------------------------------------
; O DRIVER DO MODO DFU NAO E INSTALADO AQUI — e uma etapa manual, com o Zadig.
;
; POR QUE PRECISA DE DRIVER: o bootloader do STM32 e gravado na ROM da ST e nao declara WCID, entao
; o Windows nao escolhe driver sozinho. Sem ele a placa entra em DFU e aparece com ERRO CODIGO 28
; ("nenhum driver"), e a atualizacao para ali. Diagnosticado na bancada em 2026-08-10.
;
; POR QUE NAO AUTOMATIZAMOS: a automacao exigia o 'wdi-simple.exe', e o libwdi NAO o distribui —
; a release publica so o Zadig. O binario que existia aqui foi compilado a mao uma vez e nunca
; entrou no repositorio; como a linha era protegida por Check: FileExists, ela simplesmente NUNCA
; RODOU e nunca reclamou. Um passo que finge existir e pior que um passo assumidamente manual.
;
; Retomar a automacao significa compilar o libwdi dentro do build. Enquanto isso nao se justificar,
; o caminho e o Zadig, documentado em docs/firmware-update-windows.md.
;
; O 'dfu-util', que e quem de fato grava, VAI no pacote (ver build-installer.ps1, etapa 4b).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
