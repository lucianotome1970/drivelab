# ============================================================================
#  DriveLab — build do instalador Windows (para o CRIADOR de DD, no Windows).
#  Faz tudo: publica o app (self-contained x64), inclui o hardware-profile.json do criador e compila o
#  setup.exe com o Inno Setup. O criador só roda este script.
#
#  Pré-requisitos no Windows: .NET 8 SDK  +  Inno Setup 6.
#  Uso (PowerShell, nesta pasta):
#     .\build-installer.ps1 -Version 1.0.0
#  Coloque o SEU hardware-profile.json nesta pasta (installer\windows\) antes de rodar.
#  Autor: Luciano Tomé — Licença MIT
# ============================================================================
param(
    [string]$Version = "1.0.0",
    [string]$Profile = "hardware-profile.json"
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$repoRoot = (Resolve-Path "$here\..\..").Path
$proj = Join-Path $repoRoot "app\DriveLab.Studio\DriveLab.Studio.csproj"

# 1) Publica o app (self-contained → o comprador não precisa instalar .NET).
if ((Test-Path $proj) -and ($null -ne (Get-Command dotnet -ErrorAction SilentlyContinue))) {
    Write-Host "==> Publicando o app (win-x64, self-contained)..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -r win-x64 --self-contained true -o "$here\publish"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }
}
elseif (-not (Test-Path "$here\publish\DriveLab.Studio.exe")) {
    throw "Sem o codigo-fonte/.NET SDK e sem 'publish\' pronto. Rode a partir do repo, ou coloque o app publicado em 'publish\'."
}

# 2) Inclui o perfil de hardware do criador (ao lado do .exe → o app auto-carrega, aba Hardware escondida).
if (Test-Path "$here\$Profile") {
    Copy-Item "$here\$Profile" "$here\publish\hardware-profile.json" -Force
    Write-Host "==> Perfil de hardware incluido: $Profile" -ForegroundColor Green
}
else {
    Write-Warning "Perfil '$Profile' nao encontrado nesta pasta — instalador SEM config (a aba Hardware so aparece com --advanced)."
}
# Garante que NAO vai um advanced.flag no pacote (senao o comprador veria o Hardware).
Remove-Item "$here\publish\advanced.flag" -ErrorAction SilentlyContinue

# 3) Compila o instalador com o Inno Setup.
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source } else { throw "Inno Setup (ISCC.exe) nao encontrado. Instale o Inno Setup 6." }
}
Write-Host "==> Compilando o instalador (v$Version)..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" "$here\DriveLab.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou." }

Write-Host "==> Pronto! Instalador em: $here\output\DriveLab-Setup-$Version.exe" -ForegroundColor Green
