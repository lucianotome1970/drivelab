# ============================================================================
#  DriveLab - build TUDO-EM-UM do instalador Windows (para o CRIADOR de DD).
#  Leve a pasta do projeto pro Windows e rode este script. Ele:
#    1) garante o .NET 8 SDK (usa o do PATH, ou instala local sem admin);
#    2) garante o Inno Setup (acha o instalado, ou baixa/instala);
#    3) publica o app (self-contained x64) FORCANDO a fonte nuget.org;
#    4) inclui o SEU hardware-profile.json;
#    5) compila o setup.exe.
#
#  Uso (PowerShell, nesta pasta installer\windows\):
#     .\build-installer.ps1 -Version 1.0.0
#  Opcionais:
#     -HwProfile <arquivo>   perfil de hardware a embutir (padrao: hardware-profile.json nesta pasta)
#     -IsccPath  <caminho>   caminho do ISCC.exe, se a deteccao do Inno falhar
#     -NuGetSource <url>     fonte NuGet (padrao: nuget.org). Use um feed interno se preciso.
#
#  Requisito de rede: internet (GitHub + nuget.org). Se usar PROXY, defina $env:HTTPS_PROXY antes de rodar.
#  NOTA: ASCII puro de proposito (o Windows PowerShell 5.1 quebra com acentos sem BOM).
#  Autor: Luciano Tome - Licenca MIT
# ============================================================================
param(
    [string]$Version     = "1.0.0",
    [string]$HwProfile   = "hardware-profile.json",
    [string]$IsccPath    = "",
    [string]$NuGetSource = "https://api.nuget.org/v3/index.json"
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest muito mais rapido sem a barra de progresso
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Info($m) { Write-Host $m -ForegroundColor Cyan }
function Ok($m)   { Write-Host $m -ForegroundColor Green }
function Dim($m)  { Write-Host $m -ForegroundColor DarkGray }
function Die($lines) {
    Write-Host ""
    foreach ($l in @($lines)) { Write-Host $l -ForegroundColor Yellow }
    exit 1
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here
$repoRoot = (Resolve-Path "$here\..\..").Path
$proj     = Join-Path $repoRoot "app\DriveLab.Studio\DriveLab.Studio.csproj"
$pubDir   = Join-Path $here "publish"

Info "==> DriveLab - build do instalador Windows (v$Version)"
Dim  "    PowerShell $($PSVersionTable.PSVersion) | OS $([Environment]::OSVersion.Version) | 64-bit=$([Environment]::Is64BitOperatingSystem)"
if (-not (Test-Path $proj)) {
    Die @("Projeto nao encontrado em: $proj",
          "Leve a pasta 'app' junto, mantendo a estrutura do repo (app\ e installer\ lado a lado).")
}

# ---------------------------------------------------------------------------
# 1) .NET 8 SDK - usa o do PATH; senao instala local (sem admin).
# ---------------------------------------------------------------------------
function Test-Sdk8 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { return $false }
    try { return [bool]((& dotnet --list-sdks 2>$null) -match '^8\.') } catch { return $false }
}
$dotnet = "dotnet"
if (-not (Test-Sdk8)) {
    Info "==> .NET 8 SDK nao encontrado. Instalando localmente em .dotnet\ (sem admin)..."
    $insScript = Join-Path $here "dotnet-install.ps1"
    try {
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $insScript -UseBasicParsing -UserAgent "Mozilla/5.0"
        & $insScript -Channel 8.0 -InstallDir (Join-Path $here ".dotnet") -NoPath
    } catch {
        Die @("Falha ao baixar/instalar o .NET 8 SDK: $($_.Exception.Message)",
              "Instale manualmente: https://dotnet.microsoft.com/download/dotnet/8.0 e rode de novo.")
    }
    $dotnet = Join-Path $here ".dotnet\dotnet.exe"
    if (-not (Test-Path $dotnet)) { Die @("Falha ao instalar o .NET 8 SDK.") }
}
Dim "==> .NET: '$dotnet' (SDK $(& $dotnet --version 2>$null))"

# ---------------------------------------------------------------------------
# 2) Inno Setup - acha o ISCC (Program Files, per-user, registro, PATH, busca);
#    senao tenta winget/choco/download. Override manual: -IsccPath.
# ---------------------------------------------------------------------------
function Find-Iscc {
    foreach ($p in @("$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                     "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
                     "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    foreach ($k in @("HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
                     "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
                     "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1")) {
        $r = Get-ItemProperty $k -ErrorAction SilentlyContinue
        if ($r -and $r.InstallLocation) {
            $p = Join-Path $r.InstallLocation "ISCC.exe"
            if (Test-Path $p) { return $p }
        }
    }
    $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, "$env:LOCALAPPDATA\Programs")) {
        if ($root -and (Test-Path $root)) {
            $hit = Get-ChildItem -Path $root -Filter "ISCC.exe" -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    return $null
}
$iscc = if ($IsccPath -and (Test-Path $IsccPath)) { $IsccPath } else { Find-Iscc }
if (-not $iscc) {
    Info "==> Inno Setup nao encontrado. Instalando..."
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        try { winget install -e --id JRSoftware.InnoSetup --silent --accept-source-agreements --accept-package-agreements | Out-Null } catch {}
        $iscc = Find-Iscc
    }
    if (-not $iscc -and (Get-Command choco -ErrorAction SilentlyContinue)) {
        try { choco install innosetup -y --no-progress | Out-Null } catch {}
        $iscc = Find-Iscc
    }
    if (-not $iscc) {
        $isSetup = Join-Path $here "innosetup-latest.exe"
        $urls = @(
            "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe",
            "https://jrsoftware.org/download.php/is.exe?site=2",
            "https://jrsoftware.org/download.php/is.exe?site=1"
        )
        $ok = $false
        foreach ($u in $urls) {
            try {
                Dim "    baixando Inno de $u ..."
                Invoke-WebRequest -Uri $u -OutFile $isSetup -UseBasicParsing -UserAgent "Mozilla/5.0"
                $b = [System.IO.File]::ReadAllBytes($isSetup)   # valida PE ('MZ')
                if ($b.Length -gt 2 -and $b[0] -eq 0x4D -and $b[1] -eq 0x5A) { $ok = $true; break }
                Write-Warning "    arquivo baixado nao e um instalador valido; proxima URL."
            } catch { Write-Warning "    falhou: $($_.Exception.Message)" }
        }
        if ($ok) {
            Dim "    instalando o Inno Setup (APROVE o UAC se aparecer)..."
            # -Verb RunAs: lanca JA elevado -> o -Wait espera de verdade a instalacao terminar.
            try { Start-Process -FilePath $isSetup -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/SP-" -Verb RunAs -Wait }
            catch { Write-Warning "    instalacao elevada falhou/UAC negado: $($_.Exception.Message)" }
        }
        Remove-Item $isSetup -ErrorAction SilentlyContinue
        $iscc = Find-Iscc
    }
    if (-not $iscc) {
        Die @("Inno Setup nao encontrado (nem depois de instalar).",
              "Ache o ISCC.exe:  Get-ChildItem C:\ -Filter ISCC.exe -Recurse -ErrorAction SilentlyContinue",
              "E rode apontando:  .\build-installer.ps1 -IsccPath 'C:\...\Inno Setup 6\ISCC.exe'",
              "(Se ainda nao instalou o Inno: https://jrsoftware.org/isdl.php )")
    }
}
Dim "==> Inno Setup: '$iscc'"

# ---------------------------------------------------------------------------
# 3) Restaura (FORCANDO a fonte nuget.org - a maquina pode nao ter fonte configurada) e publica.
# ---------------------------------------------------------------------------
Info "==> Restaurando pacotes NuGet (fonte: $NuGetSource)..."
& $dotnet restore $proj -r win-x64 --source $NuGetSource
if ($LASTEXITCODE -ne 0) {
    Die @("Falha ao restaurar os pacotes NuGet (erro NU1100 acima).",
          "Causas comuns:",
          "  - a maquina nao tinha fonte NuGet -> este script ja forca $NuGetSource;",
          "  - sem acesso de rede a api.nuget.org (firewall/proxy corporativo).",
          "Diagnostico rapido:",
          "  Test-NetConnection api.nuget.org -Port 443",
          "  $dotnet nuget list source",
          "Se usa PROXY: defina antes de rodar ->  `$env:HTTPS_PROXY = 'http://proxy:porta'")
}
Info "==> Publicando o app (win-x64, self-contained)..."
& $dotnet publish $proj -c Release -r win-x64 --self-contained true --no-restore -o $pubDir
if ($LASTEXITCODE -ne 0) { Die @("dotnet publish falhou (veja o erro acima).") }
if (-not (Test-Path (Join-Path $pubDir "DriveLab.Studio.exe"))) {
    Die @("Publish terminou mas nao gerou DriveLab.Studio.exe em '$pubDir'.")
}

# ---------------------------------------------------------------------------
# 4) Inclui o perfil de hardware do criador (ao lado do .exe -> auto-carrega; aba Hardware escondida).
# ---------------------------------------------------------------------------
$profPath = Join-Path $here $HwProfile
if (Test-Path $profPath) {
    Copy-Item $profPath (Join-Path $pubDir "hardware-profile.json") -Force
    Ok "==> Perfil de hardware incluido: $HwProfile"
} else {
    Write-Warning "Perfil '$HwProfile' nao encontrado nesta pasta - instalador SEM config (aba Hardware so com --advanced)."
}
# Garante que NAO vai um advanced.flag no pacote (senao o comprador veria o Hardware).
Remove-Item (Join-Path $pubDir "advanced.flag") -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# 5) Compila o instalador.
# ---------------------------------------------------------------------------
Info "==> Compilando o instalador..."
& $iscc "/DMyAppVersion=$Version" (Join-Path $here "DriveLab.iss")
if ($LASTEXITCODE -ne 0) { Die @("Inno Setup (ISCC) falhou (veja o erro acima).") }

$outExe = Join-Path $here "output\DriveLab-Setup-$Version.exe"
Write-Host ""
if (Test-Path $outExe) { Ok "==> PRONTO! Instalador em: $outExe" }
else { Ok "==> PRONTO! Instalador gerado em: $here\output\" }
