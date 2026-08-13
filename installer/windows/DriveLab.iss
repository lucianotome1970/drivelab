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
; Driver do modo DFU: necessario para atualizar o firmware da base pela USB. Marcado por padrao,
; mas OPCIONAL — quem nao quiser mexer em driver desmarca e a atualizacao continua possivel por
; ST-Link ou pelo Zadig depois (ver docs/firmware-update-windows.md).
Name: "dfudriver"; Description: "Instalar o driver de atualizacao de firmware (USB/DFU)"; GroupDescription: "Atualizacao de firmware:"

[Files]
; Todo o app publicado — inclui o hardware-profile.json que o criador colocou na PublishDir (fica ao lado
; do .exe em Program Files\DriveLab, e o app auto-carrega no start). NÃO inclua um advanced.flag aqui.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; ---------------------------------------------------------------------------------------------
; DRIVER DO MODO DFU (0483:DF11) — instalado ANTES de abrir o app.
;
; POR QUE PRECISA: o bootloader do STM32 e gravado na ROM da ST e nao declara WCID, entao o Windows
; nao escolhe driver sozinho. Sem isto, na hora de atualizar o firmware a placa entra em DFU e
; aparece com ERRO CODIGO 28 ("nenhum driver"), e a atualizacao para ali. Diagnosticado na bancada
; em 2026-08-10, quando todo o resto ja funcionava.
;
; POR QUE NA INSTALACAO E NAO NA HORA DE ATUALIZAR: o wdi-simple registra o driver mesmo com o
; dispositivo AUSENTE. Pre-registrando aqui, a primeira atualizacao ja funciona e o usuario nunca
; ve erro nenhum — que e a experiencia das bases comerciais.
;
; runascurrentuser: o instalador ja roda elevado; instalar driver exige isso.
Filename: "{app}\wdi-simple.exe";   Parameters: "--vid 0x0483 --pid 0xDF11 --type 0 --name ""STM32 BOOTLOADER (DriveLab)"" --silent";   StatusMsg: "Instalando o driver de atualizacao de firmware...";   Flags: runhidden waituntilterminated runascurrentuser; Tasks: dfudriver; Check: FileExists(ExpandConstant('{app}\wdi-simple.exe'))

Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
