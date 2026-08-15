# ============================================================================
#  DriveLab
#  instalar-driver.ps1 — Deixa o Windows falar com a placa em modo de
#  atualizacao, sem o usuario precisar do Zadig.
#
#  O QUE ESTE PROBLEMA E: o modo de atualizacao (DFU) da placa vem gravado na
#  ROM da ST e nao pode ser mudado por nos. Ele nao declara os descritores que
#  fariam o Windows escolher um driver sozinho, entao a placa entra em DFU e
#  aparece com ERRO CODIGO 28 ("nenhum driver instalado"). Ate hoje a saida era
#  mandar a pessoa baixar o Zadig e apontar o dispositivo certo a mao — no
#  primeiro contato dela com a base, com um aviso de que apontar errado derruba
#  outro dispositivo do computador.
#
#  O QUE ESTE SCRIPT FAZ: exatamente o que o Zadig faria, so que decidido por
#  nos e sem a pessoa escolher nada. Nao estamos instalando um driver nosso: o
#  WinUSB ja vem no Windows e e assinado pela Microsoft. O que falta e um
#  arquivo INF dizendo "o dispositivo 0483:DF11 usa aquele driver que voce ja
#  tem", e um catalogo assinado para o Windows aceitar esse INF.
#
#  POR QUE CONFIAR E DEPOIS DESCONFIAR: o catalogo esta assinado por um
#  certificado autoassinado, entao o Windows so o aceita se confiar nele. Mas
#  ele so precisa confiar no INSTANTE da instalacao — depois que o driver entra
#  no DriverStore, ele continua valendo sozinho. Entao confiamos, instalamos, e
#  retiramos a confianca. A janela sao os segundos entre um passo e outro.
#
#  SOBRE A CHAVE: quem gerou este certificado foi a libwdi (a biblioteca por
#  tras do Zadig), que assina e DESTROI a chave privada em seguida — politica de
#  nao-reuso, um certificado por dispositivo. A chave que assinou este catalogo
#  nao existe mais em lugar nenhum, nem conosco. O certificado consegue validar
#  este catalogo e mais nada.
#
#  Autor: Luciano Tome <lucianotome1970@gmail.com>
#  Copyright (c) 2026 Luciano Tome — Licenca MIT
# ============================================================================
[CmdletBinding()]
param(
    # A placa NAO precisa estar conectada nem em modo de atualizacao. Sem ela presente o driver
    # fica no DriverStore e o Windows o aplica sozinho no dia em que ela aparecer — que e o caso
    # normal, ja que a instalacao acontece antes de a pessoa mexer no hardware.
    [switch]$Silencioso
)

$ErrorActionPreference = "Stop"
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
$inf  = Join-Path $aqui "STM32__BOOTLOADER.inf"
$cer  = Join-Path $aqui "drivelab-dfu.cer"

function Diga($msg) { if (-not $Silencioso) { Write-Host $msg } }

if (-not (Test-Path $inf)) { throw "Pacote de driver incompleto: nao achei $inf" }
if (-not (Test-Path $cer)) { throw "Pacote de driver incompleto: nao achei $cer" }

# Sem direito de administrador nada disto funciona, e falhar com a mensagem certa vale mais que
# quatro erros seguidos do Windows que ninguem consegue interpretar.
$souAdmin = ([Security.Principal.WindowsPrincipal] `
             [Security.Principal.WindowsIdentity]::GetCurrent()
            ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $souAdmin) { throw "Este passo precisa ser executado como administrador." }

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
$digital = $cert.Thumbprint
Diga "==> Instalando o driver do modo de atualizacao (0483:DF11)..."

# Guarda se o certificado JA era confiavel antes de mexermos. Numa maquina que ja passou pelo
# Zadig ele esta la, e remover no final apagaria algo que nao pusemos.
$jaEraConfiavel = @{}
foreach ($store in "Root", "TrustedPublisher") {
    $jaEraConfiavel[$store] = $null -ne (Get-ChildItem "Cert:\LocalMachine\$store" `
                                         -ErrorAction SilentlyContinue |
                                         Where-Object { $_.Thumbprint -eq $digital })
}

try {
    # 1) confiar — Root valida a cadeia, TrustedPublisher evita o aviso de editor desconhecido
    foreach ($store in "Root", "TrustedPublisher") {
        if (-not $jaEraConfiavel[$store]) {
            $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store, "LocalMachine")
            $s.Open("ReadWrite"); $s.Add($cert); $s.Close()
        }
    }

    # 2) instalar. /install aplica a dispositivos ja presentes; sem a placa, fica no DriverStore
    #    esperando — e o Windows o usa assim que ela aparecer.
    $saida = & pnputil.exe /add-driver $inf /install 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "pnputil falhou (codigo $LASTEXITCODE):`n$($saida -join "`n")"
    }
    Diga "    driver instalado."
}
finally {
    # 3) desconfiar — SEMPRE, mesmo se algo acima falhou. Deixar uma raiz confiavel para tras por
    #    causa de um erro no meio e o pior resultado possivel deste script.
    foreach ($store in "Root", "TrustedPublisher") {
        if (-not $jaEraConfiavel[$store]) {
            $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store, "LocalMachine")
            $s.Open("ReadWrite")
            $s.Certificates | Where-Object { $_.Thumbprint -eq $digital } | ForEach-Object { $s.Remove($_) }
            $s.Close()
        }
    }
}

Diga "==> Pronto. A placa em modo de atualizacao ja e reconhecida por este computador."
