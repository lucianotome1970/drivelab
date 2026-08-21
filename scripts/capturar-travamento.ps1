# ============================================================================
#  DriveLab
#  capturar-travamento.ps1 — Grava o barramento USB da base enquanto voce joga.
#
#  POR QUE EXISTE: quando a base derruba o USB, nenhum contador do firmware pega
#  nada — a falha nao passa por onde instrumentamos. O PC reinicia o barramento
#  tres vezes, desiste e suspende, levando junto o controlador USB inteiro. A
#  captura ve TUDO que trafega, inclusive os erros que disparam esse reinicio.
#
#  COMO USAR: clique com o botao direito no PowerShell -> Executar como
#  administrador (o driver de captura exige elevacao) e rode este script.
#  Depois jogue normalmente. Quando travar, FECHE A JANELA da captura.
#
#  ⚠️ Enquanto a captura roda o arquivo fica travado — e por isso que "sem
#  permissao para ler" nao e permissao, e arquivo em uso.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
$ErrorActionPreference = "Stop"

$eu = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($eu)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Este script precisa de PowerShell como ADMINISTRADOR." -ForegroundColor Yellow
    Write-Host "Feche esta janela, abra o PowerShell como administrador e rode de novo."
    exit 1
}

# Descobre em qual barramento a base esta — ele muda se ela trocar de porta.
$base = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -match '^USB\VID_1209' } | Select-Object -First 1
if (-not $base) { Write-Host "Base nao encontrada. Ela esta ligada?" -ForegroundColor Yellow; exit 1 }
Write-Host "Base encontrada: $($base.FriendlyName)" -ForegroundColor Green

$destino = Join-Path $PSScriptRoot "..\capturas"
New-Item -ItemType Directory -Force -Path $destino | Out-Null
$arquivo = Join-Path $destino ("travamento-" + (Get-Date -Format "MMdd-HHmm") + ".pcap")

Write-Host ""
Write-Host "Gravando em: $arquivo"
Write-Host "Jogue normalmente. Quando TRAVAR, feche esta janela." -ForegroundColor Cyan
Write-Host ""
& "C:\Program Files\USBPcap\USBPcapCMD.exe" -d \.\USBPcap1 -A --inject-descriptors -o $arquivo
