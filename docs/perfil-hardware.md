# Perfil de Hardware (vendor profile)

> Permite que um **construtor** de DD configure o hardware (variante, motor, encoder, corrente…) e
> **distribua** essa config num `.json` junto com o produto. O app do **comprador** detecta, valida,
> confirma e aplica — sem o leigo precisar mexer em nada perigoso.

## Duas camadas (a fronteira já existe: `SettingTab`)
- **Perfil de hardware** = os settings `SettingTab.Hardware` (variante, tensão nominal, encoder dir/CPR/tipo,
  pares de polo, Current P/I, corrente de calibração). Perigosos/fixos — definidos por quem construiu.
- **Perfil de feel** = Basic/Advanced (força, efeitos, curvas, **MotionRange/DOR**). Livre pro usuário.
- Editar o feel nunca toca o hardware.

## Formato (JSON, camelCase; keys de settings = `Key` do schema)
```json
{
  "version": 1,
  "kind": "hardware-profile",
  "vendor": "Fulano DD",
  "device": "Fulano DD 56V 12Nm",
  "createdAt": "2026-07-25T12:00:00Z",
  "notes": "Motor XYZ, brake 2Ω",
  "settings": {
    "board_variant": 1, "bus_nominal_v": 56,
    "pole_pairs": 20, "encoder_cpr": 10000, "encoder_type": 0, "encoder_direction": 1,
    "current_p": 0.05, "current_i": 10, "calibration_current": 30
  }
}
```

## Fluxo (detect → validar → confirmar → aplicar)
1. **No start**, o app procura `hardware-profile.json` na **pasta do app** (`ApplicationData/DriveLab/`).
2. Se achar e ainda não aplicado: **valida** cada valor contra o schema (`kind`/`version`, key existe e é de
   hardware, valor em `[min,max]`). Fora da faixa → recusa/avisa.
3. **Mostra o que vai setar** com a identidade (*"Perfil de [Fulano DD] — Placa 56V, 20 polos, CPR 10000…"*)
   — **nunca aplica calado**.
4. Confirmou → grava via `BaseSession.WriteSettingAsync` (e no controlador).

## Modo avançado (opção só do criador)
- Por **padrão (sem flag)** a aba **Hardware fica ESCONDIDA** — fail-safe: o usuário final que só roda o app
  nunca vê/mexe nos parâmetros perigosos (eles vêm do perfil de hardware do criador).
- O **CRIADOR** ativa o modo avançado explicitamente pra ver/editar o Hardware. Não há toggle na UI.
- Ativado por: flag `--advanced` **ou** um arquivo marcador `advanced.flag` na pasta do executável (que o
  criador deixa só no ambiente dele). O usuário final nunca passa o flag → aba escondida.
- Em qualquer modo o perfil é **gravado no dispositivo** (direto na sessão por BID, ao conectar), mesmo com
  a aba escondida.

## Durabilidade (o ponto forte)
Um update de firmware pode **re-semear a flash** (o flash magic muda quando a struct cresce). O app **guarda**
o perfil e **reaplica** após o update — a config de hardware não se perde.

## Segurança
- **Sempre validar** contra as faixas do schema — nunca aplicar valor fora.
- **Confirmar com o usuário** (mostrar os valores) — nunca aplicar em silêncio.
- Identidade textual (`vendor`) pra o usuário saber a origem.

## Fora do v1
- "Lock" forte (impedir edição dos campos de hardware) — v1 só separa + esconde a aba no modo simples.
- Assinatura criptográfica do perfil.
- Bundle automático no instalador do construtor (por ora: arquivo na pasta do app / import manual).

## Estado
- [x] Núcleo: `HardwareProfile` + `HardwareProfileService` (build/validate/serialize) — DriveLab.Core, testado.
- [x] Store (`ApplicationData/DriveLab/hardware-profile.json`) + auto-load no start.
- [x] Aplicar (autoritativo na sessão ao conectar; reflete na UI do criador).
- [x] Modo distribuição (`--distribution` / `distribution.flag`) — esconde a aba Hardware.
- [ ] Diálogo de confirmação ("Aplicar perfil de [vendor]?") — hoje aplica + loga.
- [ ] Botão "Exportar perfil de hardware" (no app do criador).
