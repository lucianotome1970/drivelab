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
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
