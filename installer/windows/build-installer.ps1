# ============================================================================
#  DriveLab — build TUDO-EM-UM do instalador Windows (para o CRIADOR de DD).
#  Leve a pasta do projeto pro Windows e rode este script. Ele:
#    1) baixa/instala o .NET 8 SDK (local, sem admin) se faltar;
#    2) baixa/instala o Inno Setup se faltar;
#    3) publica o app (self-contained x64);
#    4) inclui o SEU hardware-profile.json;
#    5) compila o setup.exe.
#
#  Uso (PowerShell, nesta pasta installer\windows\):
#     .\build-installer.ps1 -Version 1.0.0
#  Coloque o SEU hardware-profile.json nesta pasta antes de rodar (exporte pelo app em modo --advanced).
#  Requisito de rede: acesso à internet (pra baixar as ferramentas na 1ª vez).
#  Autor: Luciano Tomé — Licença MIT
# ============================================================================
param(
    [string]$Version = "1.0.0",
    [string]$Profile = "hardware-profile.json"
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest MUITO mais rápido sem a barra de progresso
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$repoRoot = (Resolve-Path "$here\..\..").Path
$proj = Join-Path $repoRoot "app\DriveLab.Studio\DriveLab.Studio.csproj"
if (-not (Test-Path $proj)) {
    throw "Projeto nao encontrado em '$proj'. Leve a pasta 'app' junto, mantendo a estrutura do repo (app\ e installer\)."
}

# ---------------------------------------------------------------------------
# 1) .NET 8 SDK — usa o que houver no PATH; senao instala local (sem admin).
# ---------------------------------------------------------------------------
function Test-Sdk8 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    try { return [bool]((& dotnet --list-sdks 2>$null) -match '^8\.') } catch { return $false }
}
$dotnet = "dotnet"
if (-not (Test-Sdk8)) {
    Write-Host "==> .NET 8 SDK nao encontrado. Instalando localmente em .dotnet\ (sem admin)..." -ForegroundColor Cyan
    $ins = Join-Path $here "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $ins
    & $ins -Channel 8.0 -InstallDir (Join-Path $here ".dotnet") -NoPath
    $dotnet = Join-Path $here ".dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) { throw "Falha ao instalar o .NET 8 SDK." }
}
Write-Host "==> .NET: usando '$dotnet'" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# 2) Inno Setup — acha o ISCC; senao tenta winget; senao baixa e instala silencioso.
# ---------------------------------------------------------------------------
$isccDefault = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
function Find-Iscc {
    if (Test-Path $isccDefault) { return $isccDefault }
    $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}
$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "==> Inno Setup nao encontrado. Instalando..." -ForegroundColor Cyan
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        try {
            winget install -e --id JRSoftware.InnoSetup --silent --accept-source-agreements --accept-package-agreements | Out-Null
        } catch { Write-Warning "winget falhou; tentando download direto." }
    }
    $iscc = Find-Iscc
    if (-not $iscc) {
        $isSetup = Join-Path $here "innosetup-latest.exe"
        Invoke-WebRequest -Uri "https://jrsoftware.org/download.php/is.exe" -OutFile $isSetup
        # Instalacao silenciosa (pode pedir UAC/admin).
        Start-Process -FilePath $isSetup -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/SP-" -Wait
        Remove-Item $isSetup -ErrorAction SilentlyContinue
        $iscc = Find-Iscc
    }
    if (-not $iscc) { throw "Nao foi possivel instalar o Inno Setup automaticamente. Instale manualmente: https://jrsoftware.org/isdl.php" }
}
Write-Host "==> Inno Setup: usando '$iscc'" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# 3) Publica o app (self-contained → o comprador nao precisa instalar .NET).
# ---------------------------------------------------------------------------
Write-Host "==> Publicando o app (win-x64, self-contained)..." -ForegroundColor Cyan
& $dotnet publish $proj -c Release -r win-x64 --self-contained true -o (Join-Path $here "publish")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

# ---------------------------------------------------------------------------
# 4) Inclui o perfil de hardware do criador (ao lado do .exe → auto-carrega; aba Hardware escondida).
# ---------------------------------------------------------------------------
$profPath = Join-Path $here $Profile
if (Test-Path $profPath) {
    Copy-Item $profPath (Join-Path $here "publish\hardware-profile.json") -Force
    Write-Host "==> Perfil de hardware incluido: $Profile" -ForegroundColor Green
} else {
    Write-Warning "Perfil '$Profile' nao encontrado nesta pasta — instalador SEM config (aba Hardware so com --advanced)."
}
# Garante que NAO vai um advanced.flag no pacote (senao o comprador veria o Hardware).
Remove-Item (Join-Path $here "publish\advanced.flag") -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# 5) Compila o instalador.
# ---------------------------------------------------------------------------
Write-Host "==> Compilando o instalador (v$Version)..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" (Join-Path $here "DriveLab.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou." }

Write-Host ""
Write-Host "==> PRONTO! Instalador em: $here\output\DriveLab-Setup-$Version.exe" -ForegroundColor Green
