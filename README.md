# DriveLab

Volante Direct Drive open-source (firmware para ODESC v4.2 + app configurador).

## Build & Test

- macOS/Linux: `./scripts/build.sh` e `./scripts/test.sh`
- Windows: `./scripts/build.ps1` e `./scripts/test.ps1`

## Estrutura
- `app/DriveLab.Core` — contrato de protocolo A0 + settings.
- `app/DriveLab.Simulator` — firmware falso (física de volante) para dev sem hardware.
- `app/DriveLab.Tests` — testes.
- `docs/` — spec e planos.

## Gerar o executável Windows (DriveLab Studio)

- Windows: `./scripts/publish-win.ps1`
- macOS/Linux (cross-publish): `./scripts/publish-win.sh`

Saída: `dist/win-x64/DriveLab.Studio.exe` (self-contained, single-file — não precisa de .NET instalado no PC alvo).
