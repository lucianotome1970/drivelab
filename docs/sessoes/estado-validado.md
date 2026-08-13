# Estado validado

## ✅ PONTO DE RETORNO ATUAL — `main`, commit `ba6643b` (2026-08-11, noite)

Continuação do mesmo dia. Depois das 11 voltas em Monza (seção abaixo), a sessão virou para
**força e medição**, com duas descobertas feitas na bancada:

### 🔴 A base entregava ~28% menos força do que a conta dizia

**O `Kt` foi MEDIDO pela primeira vez: 0,397 Nm/A**, contra os **0,55 de catálogo** que o firmware
cravava (fórmula genérica de hoverboard, `8,27/KV`). Método sem risco e sem gravar firmware: motor
armado, torque zero, volante girado à mão, e a tensão que o controlador aplica para cancelar a
back-EMF lida por SWD — 234 amostras até 585 °/s, regressão por origem, erro mediano de 11%.

```
λ = (Vq − R·Iq) / ω_elétrico        Kt = 1,5 × pole_pairs × λ
```

Consequência: com 26,6 A de pico, a base entregava **10,6 Nm** e não os 14,6 Nm que a conta com
0,55 prometia. O sintoma relatado era exatamente esse — *"parece mais fraco que meu Moza R9"*, que
tem 9 Nm.

### 🔴 A curva de resposta achatava as forças MÉDIAS

`linearity` estava em **1,59**, e a força do jogo passa por `|x|^1,59` antes de virar torque:

| jogo pede | chegava | linear |
|---|---|---|
| 30% | **15%** | 30% |
| 50% | **33%** | 50% |

Isso explicava os dois sintomas que pareciam se contradizer: base fraca **e** 31% de clipping. As
forças médias vinham pela metade, o usuário compensava subindo o ganho do jogo, e só os picos
passavam inteiros. **O 159 nunca foi uma decisão** — era o valor que estava na placa no dia em que
a pista rodou bem, e virou "padrão de fábrica" junto com o resto da config daquele dia.

Voltou para **100 (linear)**, que é o que as bases comerciais fazem. Resultado na pista: *"a
sensação melhorou e o clipping diminuiu"*.

### O que mais entrou

- **fundo de escala do torque 10 → 15 Nm**, e a constante deixou de estar duplicada (o divisor da
  telemetria era um `10.0f` digitado à mão, que já dessincronizou uma vez e fez a barra do app
  marcar o dobro do real)
- **`Kt` deixou de ser órfão** — o campo existia no app, era salvo, voltava na tela e **nada o lia**
- **temperatura dos FETs ligada**: o termistor existe, o ODrive já o lia e convertia; quem mandava
  `-128` ("sem sensor") era a nossa telemetria. Mede **43 °C** em repouso
- **medidor de clipping refeito** — ver a seção abaixo
- **roda do mouse não altera mais setting** no app (bug que fazia 8 A virar 10 A ao rolar a tela)

### 🧭 O medidor de clipping estava errado de três formas

Todas achadas pelo usuário desconfiando dos números, e todas defeitos da métrica, não da leitura:

1. **Contava amostra solta.** Um `255` isolado é ambíguo — pode ser pico legítimo da física ou
   corte. O que separa é a **duração**: agora só conta platô (≥3 ms cravado no talo), como o áudio
   faz há décadas. Os 31% e 18% medidos antes estavam inflados.
2. **Comparava com o teto errado.** A base tem **dois** tetos: o de configuração (15 Nm) e o que a
   corrente permite (`current_lim × Kt` = 25 × 0,39 = **9,75 Nm**). Como o pedido máximo é
   exatamente 15, `pedido > 15` nunca disparava: **zero por construção**, depois de voltas inteiras,
   enquanto a base cortava de verdade na corrente.
3. **O "pico da sessão" era lido errado pelo caminho mais natural.** "28%" parecia dizer "um terço
   da volta teve clipping" quando dizia "no pior meio-segundo, 28% dele". Virou **razão direta entre
   ticks** — "3%" agora significa "3% do tempo que eu dirigi".

A tela mostra **só a parcela da base** (decisão do usuário): o clipping do jogo acontece dentro do
jogo, não temos gerência sobre ele, e cada título se comporta de um jeito. Continua medido e
trafegado (bytes 35 e 37) para diagnóstico.

### ⚠️ O que NÃO foi validado nesta parte

- ~~as últimas mudanças do medidor não passaram por pista~~ **foram** — com o teto real valendo, a
  pista mediu **28%** do tempo saturado, que é o esperado para uma base pedindo 15 Nm e entregando
  9,75. O número novo faz sentido em uso real.
- **temperatura do motor depois das voltas** — segue sem sensor, e a corrente subiu
- **teste 2 do encoder** (MT6701 em SSI) continua pendente

---

## Histórico — 11 voltas em Monza, `6649bb5` (2026-08-11, tarde)

**11 voltas em Monza no ACC, sem perder FFB, sem desarme e sem falha.** Monza não é uma pista
qualquer para este projeto: foram as chicanes dela que expuseram a perda de força por regeneração
em 04/08. Onze voltas limpas ali é o teste mais duro que temos.

O que estava sendo validado nesta sessão, tudo de uma vez:

| commit | o quê | resultado |
|---|---|---|
| `a39ddb6` | trava do encoder — combinação sem driver não aplica NADA | ✅ A/B/Z segue idêntico |
| `af37254` | fundo de escala do torque 10 → **15 Nm** | ✅ 11 voltas |
| `5e06b2d` | Kt deixa de ser órfão | ✅ (inerte com o campo em 0,55) |
| `6649bb5` | Kt sai de fábrica com 0,55 em vez de zero | ✅ |
| `15c7112` | app: roda do mouse não muda mais setting | ✅ |

**Medido por SWD durante 5 minutos com o motor armado**, antes das voltas:

- erros de eixo / motor / encoder: **0 / 0 / 0** nas 23 leituras
- latch da primeira falha (`g_fail_dbg`): **vazio** — é ele que cobre as lacunas de leitura
- `Iq` com o volante parado: **0,03 A**; sob força, oscilando de +1,4 a −5,6 A **trocando de sinal**
  (o padrão saudável; o patológico de 06/08 era valor alto **travado**)
- vbus 27,06 – 27,22 V, firme

⚠️ **Sete leituras saíram vazias** por o ST-Link travar (`init mode failed`), não a placa — só o
replug do adaptador na USB resolve. Isso aconteceu duas vezes na sessão, inclusive impedindo uma
gravação. Não confundir com falha da base: o latch vazio prova que nada desarmou no período.

### O que ainda NÃO foi respondido nesta validação

- **temperatura do motor depois das 11 voltas** — subimos a escala 50% e não há sensor no motor
  (o corte térmico não existe); a única medição possível hoje é a mão
- **quanto o clipping caiu** dos 38% que motivaram a mudança
- **teste 2 do encoder**: escolher MT6701 em SSI, salvar, reiniciar, e confirmar que a base
  continua normal em A/B/Z

---

## Histórico — ponto de retorno anterior: `f24afab` (2026-08-10, à noite)

**Toda a `main` daquela noite foi validada em bancada pelo usuário**, na máquina Windows. Não é um
commit específico dentro dela: é o estado inteiro do repositório ao fim do dia 10/08.

O que entrou entre o ponto de retorno anterior e este, em firmware:

- **o batente estava DESLIGADO** por uma flag de teste esquecida (`b83b547`)
- **watchdog de USB derrubava a base dentro do jogo** — desligado (`2173056`)
- `EnterDfu` pelo app: atualizar sem ST-Link e sem jumper (`687288c`)
- limite de corrente do motor deixa de ser cravado, vira o setting 48 (`b0185d2`), e a corrente de
  calibração nunca passa dele (`9d5544a`)
- pares de polos e corrente de calibração passam a vir dos settings (`d134c7d`)
- consulta do valor padrão de um setting, report `0x17` (`03ae9e7`)
- telemetria a 40 ms, e sem morrer de fome enquanto o app lê settings (`562122f`, `64d2b94`)

⚠️ **A release `v0.2.1-alpha` é mais velha que isto** — ela aponta para `843d6f6`, antes dos dez
commits de firmware acima. O binário publicado tem o batente desligado. Vale cortar uma release
nova a partir de `f24afab`.

---

## Histórico — ponto de retorno anterior: `cc591c4` (2026-08-10, de manhã)

**Firmware testado e validado pelo usuário.** É o merge que reuniu duas linhas que
tinham se separado: a base validada na pista em 09/08 e o trabalho de 07-08/08, que
ficara órfão na main local sem nunca ser publicado.

Com isso, o código ativo passa a ter ao mesmo tempo:

- o que rodou na pista — batente ajustável e calibrado, trava "Ativar motor", reboot
  pelo app, migração de settings, ajuda por campo
- **e as proteções que estavam de fora**: torque a zero ao perder o host (o motor
  acelerava sem parar com o USB fora), guarda de coerência do ângulo elétrico, guarda de
  sobrevelocidade, centro único do volante, Pool Report em efeitos, uma calibração por
  boot

Boot verificado por SWD: sobe **desarmada** (a trava zera sozinha quando o `build_id`
muda), erros 0/0, nenhuma guarda disparada, vbus 27,16 V. Testes do app passando (eram 227 no
`Studio.Tests` naquele momento; hoje a suíte inteira soma 504).

O estado anterior ao merge segue em `backup/main-local-2026-08-10`. (Este deixou de ser o ponto de
retorno em 10/08 à noite — ver o topo.)

---

## Histórico — 2026-08-09

O que foi provado na sessão anterior (branch `base/quarta-limpa`, commit `d385df1`).

Foram quatro dias com o volante puxando para um lado, perdendo FFB na chicane e
disparando no batente. Esta seção registra o que ficou provado, como foi provado, e o
que ainda não foi.

## Validado NA PISTA (pelo usuário)

- **FFB coerente**, várias voltas, incluindo zigzag, sem desarme e sem perda de força
- **Batente firme, sem catapultar** — com rigidez 70 %, amortecimento 35 %, recuo 8°
- **Trava de bring-up, fluxo completo**: entrou na pista com o motor desligado, rodou
  sem FFB, ativou pelo app no meio da volta e **o FFB entrou com o jogo rodando**, sem o
  jogo perder o dispositivo — normalmente é aí que a conexão cai.
  ⚠️ **Ao ativar NÃO houve calibração** (observado pelo usuário): a varredura já tinha
  rodado no boot, com o flag desligado. Ver a limitação abaixo.
- **Botão de reiniciar a base** funcionando pelo app.

## Validado NA BANCADA (por medição SWD)

| o quê | como foi medido |
|---|---|
| calibração completa e motor arma | `CLOSED_LOOP`, `is_ready=1`, erros de eixo/motor/encoder em 0 |
| envio HID saudável | 930 reports/s sustentados; `stall_ticks` em 0 |
| trava de bring-up | após gravar, a base subiu **desarmada** (`motor_enable = 0`) |
| migração de settings | 45 → 46 campos com `soft_stop_range 8`, `strength 70`, `endstop_damping 35` intactos |
| batente ajustável | `stiffnessNm` e `rangeRad` na RAM refletem os sliders |

## As duas causas raiz dos quatro dias

Nenhuma das duas estava no código que eu vinha revisando.

1. **O motor roçava no suporte** (achado pelo usuário). Atrito mecânico, não software.
2. **`endstop_damping = 0` gravado na NVM.** Amortecimento zero com mola máxima é um
   trampolim: a parede devolve toda a energia. E como os settings **não são apagados
   quando o firmware é gravado**, esse zero atravessou todas as versões e imitou um bug
   de código. Ver `mapa-settings-app-firmware.md`.

Uma terceira, menor, apareceu no caminho: `odrive_bridge_relax_calibration()` (5 A para
vencer o cogging) existia desde 06/08 e **nunca era chamada** — a calibração rodava com
3 A e travava no meio.

## Limitação da trava de bring-up — FECHADA em 2026-08-10 (falta testar)

Como estava: **a trava segurava o ARME, não a CALIBRAÇÃO.** Ao ativar o motor pelo app
não houve varredura — sinal de que ela já tinha acontecido no boot, com `motor_enable = 0`.
Isso furava justamente o caso que a trava existe para proteger: numa placa recém-gravada
com `pole_pairs` / CPR / variante errados, a calibração de offset **já injeta corrente no
motor** antes de qualquer confirmação do usuário.

Fechado por dois commits, **ainda não gravados em hardware**:

- `bb36f3a` — a trava **volta a zero quando o firmware muda**. Os settings sobrevivem à
  gravação (de propósito), então quem já ativou uma vez recebia toda atualização com a
  base armando sozinha. O blob passa a guardar a identidade do binário (campo interno 47);
  se ela não bater no boot, a trava zera **uma vez**. No dia a dia nada muda.
- `fccfe36` — com a trava em zero a base sobe **sem tocar no motor** (`disable_autostart`).
  Quem dá a partida em calibrar + armar é o "Ativar motor" do app.

**Custo:** ao ativar, a força espera a varredura (~9 s) em vez de entrar na hora.

**A testar na bancada:** (1) gravar e confirmar que a base sobe sem o motor se mexer;
(2) ativar pelo app e ver a varredura rodar e a força entrar depois dela; (3) gravar de
novo com a trava salva ligada e confirmar que ela voltou a zero sozinha.

## NÃO validado ainda

- **Desligar "Ativar motor" com a base ARMADA** — o caminho de ligar está validado na
  pista; o desarme imediato (a metade que faz a trava valer como parada de emergência)
  ainda não foi exercitado.
- **Watchdog do endpoint HID** — `g_hid_recoveries` segue em 0, que é o esperado em
  operação normal. Só saberemos se resolve o congelamento quando ele tentar acontecer.
- **`linearity = 159`** virou padrão de fábrica porque foi com esse valor que a pista
  rodou — mas ele nunca chegou à flash (estava só na RAM). Decisão pendente: manter 159
  ou voltar a 100.

## Ainda inertes

15 dos 46 settings aparecem na UI e o firmware ignora — cada um traz o aviso no próprio
texto do "?". A lista e os três destinos possíveis (ligar no firmware / tirar da UI /
expor na tela) estão em `mapa-settings-app-firmware.md`.
