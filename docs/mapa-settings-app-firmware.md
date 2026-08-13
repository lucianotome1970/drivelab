# Mapa dos settings: app ↔ firmware

O app e o firmware são duas pontas que só se encontram no fio. Um ajuste pode nascer na tela com a
melhor intenção e nunca ser lido do outro lado — e nada acusa. O campo aparece, a pessoa move,
salva, o valor volta certo ao reiniciar (porque **é** salvo), e a base não muda de comportamento.

Foi assim com o CPR do encoder, cravado em 4000 no firmware enquanto a tela deixava escolher: quem
tinha um encoder de 2500 PPR rodava com a base lendo uma volta como duas e meia.

**Guardar não é aplicar.** Este documento explica a diferença. Os números ficam no script.

## Onde está a lista de verdade

```bash
python3 scripts/check-orphan-settings.py
```

Ele cruza os ids de `app/DriveLab.Core/Settings/BaseSettingId.cs` com o que o firmware realmente lê
em `firmware-base/src/a0_channel.cpp`, e imprime quantos existem, quantos são lidos e quais são
ignorados. Roda dentro do `scripts/test.sh`.

Nenhuma contagem foi copiada para cá de propósito. Documento com número escrito à mão envelhece em
silêncio — foi o que aconteceu com as três versões anteriores deste mapa, cada uma citando um total
diferente e nenhuma batendo com o código.

O script **impede a lista de crescer**: setting novo que o firmware ignore quebra a suíte com o nome
dele. Os que estão lá hoje são dívida registrada, com o motivo de cada um escrito ao lado. Ao
implementar um, remova a linha.

## Os que o firmware ignora, e por quê

**Ganhos da malha de corrente** — `CurrentP`, `CurrentI`. A placa usa os valores da NVM do ODrive. É
o par mais delicado da placa: mexer neles achando que está afinando a malha é mexer no nada.

**Sentido do encoder** — `EncoderDirection`. Está cravado no bring-up. Quem montar o encoder
invertido não resolve pela tela.

**Térmicos** — `ThermalContinuousPct`, `ThermalPeakSeconds`, `FetTempLimitC`, `MotorTempLimitC`.
Dependem de um NTC instalado no enrolamento. Não aparecem em nenhuma aba, então ninguém é enganado
por eles hoje.

**Recursos que o firmware não tem** — `CoggingEnable` (a compensação de cogging não existe),
`OutputFilterHz`, `OscGuardEnable`, `PositionSmoothing`, `PowerLimit`, `BrakingLimit`. Estes três
últimos são movidos pelos presets, o que faz o preset parecer fazer mais do que faz.

**Esperando hardware** — `SoftPowerEnable`, `PowerButtonEnable`. O groundwork está pronto nas duas
pontas; falta fiar o hardware do bus DC. São opt-in e chegam desligados.

**Caso à parte** — `TorqueConstant`. O firmware ignora mesmo, mas o valor **é usado**, no lado do
app, para o torque estimado do monitor. Não é órfão de verdade: é um ajuste que vive só no app.

## O buraco que o script não vê

O script confere se o valor é **lido**. Não confere se ele tem **consequência**.

`EncoderInterface` (46) passa por ele: o firmware lê o campo, monta a configuração do encoder — e
[`odrive_bridge.cpp`](../firmware-base/src/odrive_bridge.cpp) aplica só a resolução, descartando o
modo. Na prática, quem escolher um sensor magnético em SSI ou SPI recebe a resolução certa aplicada
sobre o caminho A/B/Z, que é o único que o firmware aciona.

Ler sem agir é o mesmo defeito de não ler, e é mais difícil de enxergar. Ver
[encoders.md](encoders.md) para o aviso que vai ao usuário.

## Três destinos para cada linha

1. **Ligar no firmware** — os que representam recurso real que falta.
2. **Tirar da tela** — os que são cravados por decisão de projeto, para o app parar de prometer o
   que não entrega.
3. **Expor na tela** — o caminho inverso: `FfbCurve0–4` e os quatro ganhos do jogo funcionam no
   firmware e não têm controle na interface.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
