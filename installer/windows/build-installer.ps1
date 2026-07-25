# ============================================================================
#  DriveLab - build TUDO-EM-UM do instalador Windows (para o CRIADOR de DD).
#  Leve a pasta do projeto pro Windows e rode este script. Ele:
#    1) baixa/instala o .NET 8 SDK (local, sem admin) se faltar;
#    2) baixa/instala o Inno Setup se faltar;
#    3) publica o app (self-contained x64);
#    4) inclui o SEU hardware-profile.json;
#    5) compila o setup.exe.
#
#  Uso (PowerShell, nesta pasta installer\windows\):
#     .\build-installer.ps1 -Version 1.0.0
#  Coloque o SEU hardware-profile.json nesta pasta antes de rodar (exporte pelo app com --advanced).
#  Requisito de rede: acesso a internet (pra baixar as ferramentas na 1a vez).
#  NOTA: mantido em ASCII puro de proposito (o Windows PowerShell 5.1 quebra com acentos sem BOM).
#  Autor: Luciano Tome - Licenca MIT
# ============================================================================
param(
    [string]$Version = "1.0.0",
    [string]$Profile = "hardware-profile.json"
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest muito mais rapido sem a barra de progresso
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$repoRoot = (Resolve-Path "$here\..\..").Path
$proj = Join-Path $repoRoot "app\DriveLab.Studio\DriveLab.Studio.csproj"
if (-not (Test-Path $proj)) {
    throw "Projeto nao encontrado em '$proj'. Leve a pasta 'app' junto, mantendo a estrutura do repo (app\ e installer\)."
}

# ---------------------------------------------------------------------------
# 1) .NET 8 SDK - usa o que houver no PATH; senao instala local (sem admin).
# ---------------------------------------------------------------------------
function Test-Sdk8 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    try { return [bool]((& dotnet --list-sdks 2>$null) -match '^8\.') } catch { return $false }
}
$dotnet = "dotnet"
if (-not (Test-Sdk8)) {
    Write-Host "==> .NET 8 SDK nao encontrado. Instalando localmente em .dotnet\ (sem admin)..." -ForegroundColor Cyan
    $ins = Join-Path $here "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $ins -UseBasicParsing -UserAgent "Mozilla/5.0"
    & $ins -Channel 8.0 -InstallDir (Join-Path $here ".dotnet") -NoPath
    $dotnet = Join-Path $here ".dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) { throw "Falha ao instalar o .NET 8 SDK." }
}
Write-Host "==> .NET: usando '$dotnet'" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# 2) Inno Setup - acha o ISCC; senao tenta winget; senao baixa e instala silencioso.
# ---------------------------------------------------------------------------
function Find-Iscc {
    foreach ($p in @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                     "C:\Program Files\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { return $p }
    }
    # registro (chave de desinstalacao do Inno Setup 6) - pega o InstallLocation onde quer que tenha ido.
    foreach ($k in @("HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
                     "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1")) {
        $r = Get-ItemProperty $k -ErrorAction SilentlyContinue
        if ($r -and $r.InstallLocation) {
            $p = Join-Path $r.InstallLocation "ISCC.exe"
            if (Test-Path $p) { return $p }
        }
    }
    $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}
$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "==> Inno Setup nao encontrado. Instalando..." -ForegroundColor Cyan
    # (a) winget, se existir.
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        try {
            winget install -e --id JRSoftware.InnoSetup --silent --accept-source-agreements --accept-package-agreements | Out-Null
        } catch { Write-Warning "winget falhou; tentando o proximo metodo." }
        $iscc = Find-Iscc
    }
    # (b) Chocolatey, se existir.
    if (-not $iscc -and (Get-Command choco -ErrorAction SilentlyContinue)) {
        try { choco install innosetup -y --no-progress | Out-Null } catch { Write-Warning "choco falhou; tentando download direto." }
        $iscc = Find-Iscc
    }
    # (c) download direto do site oficial.
    if (-not $iscc) {
        $isSetup = Join-Path $here "innosetup-latest.exe"
        # GitHub releases = binario direto e confiavel (primeira opcao). O '?site=N' do jrsoftware faz o
        # download.php REDIRECIONAR pro mirror (sem ele, devolve HTML) - fallback.
        $urls = @(
            "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe",
            "https://jrsoftware.org/download.php/is.exe?site=2",
            "https://jrsoftware.org/download.php/is.exe?site=1"
        )
        $ok = $false
        foreach ($u in $urls) {
            try {
                Write-Host "    baixando Inno de $u ..." -ForegroundColor DarkGray
                Invoke-WebRequest -Uri $u -OutFile $isSetup -UseBasicParsing -UserAgent "Mozilla/5.0"
                # valida que baixou um EXECUTAVEL de verdade (PE comeca com 'MZ' = 0x4D 0x5A).
                $b = [System.IO.File]::ReadAllBytes($isSetup)
                if ($b.Length -gt 2 -and $b[0] -eq 0x4D -and $b[1] -eq 0x5A) { $ok = $true; break }
                Write-Warning "    o arquivo baixado nao e um instalador valido; tentando a proxima URL."
            } catch { Write-Warning "    falhou: $($_.Exception.Message)" }
        }
        if ($ok) {
            Write-Host "    instalando o Inno Setup (APROVE o UAC se aparecer)..." -ForegroundColor DarkGray
            # -Verb RunAs: lanca JA elevado, e ai o -Wait realmente espera a instalacao terminar (senao o
            # instalador se auto-eleva, o lancador sai na hora e o Find-Iscc roda cedo demais).
            try {
                Start-Process -FilePath $isSetup -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/SP-" -Verb RunAs -Wait
            } catch {
                Write-Warning "    a instalacao elevada falhou ou o UAC foi negado: $($_.Exception.Message)"
            }
        }
        Remove-Item $isSetup -ErrorAction SilentlyContinue
        $iscc = Find-Iscc
    }
    if (-not $iscc) {
        Write-Host ""
        Write-Host "!! Nao consegui instalar o Inno Setup automaticamente (rede/mirror)." -ForegroundColor Yellow
        Write-Host "   Instale manualmente (1 minuto): https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        Write-Host "   Depois rode este script de novo - ele detecta o Inno e continua do publish." -ForegroundColor Yellow
        try { Start-Process "https://jrsoftware.org/isdl.php" } catch {}
        exit 1
    }
}
Write-Host "==> Inno Setup: usando '$iscc'" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
# 3) Publica o app (self-contained -> o comprador nao precisa instalar .NET).
# ---------------------------------------------------------------------------
Write-Host "==> Publicando o app (win-x64, self-contained)..." -ForegroundColor Cyan
& $dotnet publish $proj -c Release -r win-x64 --self-contained true -o (Join-Path $here "publish")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

# ---------------------------------------------------------------------------
# 4) Inclui o perfil de hardware do criador (ao lado do .exe -> auto-carrega; aba Hardware escondida).
# ---------------------------------------------------------------------------
$profPath = Join-Path $here $Profile
if (Test-Path $profPath) {
    Copy-Item $profPath (Join-Path $here "publish\hardware-profile.json") -Force
    Write-Host "==> Perfil de hardware incluido: $Profile" -ForegroundColor Green
} else {
    Write-Warning "Perfil '$Profile' nao encontrado nesta pasta - instalador SEM config (aba Hardware so com --advanced)."
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
