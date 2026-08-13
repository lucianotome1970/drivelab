# Notas de sessão

Documentos que descrevem **um momento** do projeto: onde o trabalho parou, o que foi validado em
que commit, e investigações datadas. Envelhecem por natureza — são substituídos, não mantidos.

Ficam aqui, e não no repositório de memória, porque **estão amarrados ao código**: o
`estado-validado.md` cita o commit que é ponto de retorno e o `HANDOFF.md` cita arquivos e linhas.
Versionados junto com o código, um `git checkout` de um commit antigo traz a nota daquele momento,
coerente com aquele firmware. Separados, sobraria a nota de hoje descrevendo o código de ontem.

| arquivo | o que é |
|---|---|
| `HANDOFF.md` | onde retomar o trabalho em qualquer máquina — **é o primeiro a ler** |
| `estado-validado.md` | o que foi provado em bancada, e em qual commit voltar se algo regredir |
| `diagnostico-motor-2026-08-09.md` | investigação datada do motor |
| `calibration-analysis.md` | investigação da calibração inconsistente |
| `settings-audit.md` | superado — ver `../mapa-settings-app-firmware.md` e `scripts/check-orphan-settings.py` |

O que descreve **o sistema** (protocolo, encoders, como o FFB funciona, guias) fica em `../`.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
