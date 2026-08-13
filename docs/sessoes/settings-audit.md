# Auditoria de settings — arquivada

> **Substituída por [mapa-settings-app-firmware.md](../mapa-settings-app-firmware.md) e pelo
> `scripts/check-orphan-settings.py`.**

## Por que ela saiu

Foi a primeira vez que olhamos para a diferença entre um setting **salvo** e um setting
**aplicado** — a pergunta certa, e ela levou ao verificador que hoje roda na suíte de testes.

Mas era um retrato escrito à mão, e o retrato venceu. Ao ser arquivada, dizia:

- **45 settings**, sendo **~11 aplicados** — os dois números errados
- `encoder_cpr`, `pole_pairs`, `calibration_current` e `encoder_type` como cravados no
  firmware — os quatro passaram a ser aplicados
- quatro valores padrão que já não eram os do código
- `torque_constant` "CORRIGIDO → def 0.55", uma correção que **desfizemos** de propósito: um Kt
  de catálogo faz o monitor exibir um torque que ninguém mediu. Hoje o padrão é 0 e o monitor
  mostra "—" até alguém medir de verdade.

A lição está no lugar dela: a contagem agora sai do script, que roda no `scripts/test.sh` e quebra
a suíte quando alguém acrescenta um controle que o firmware ignora.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
