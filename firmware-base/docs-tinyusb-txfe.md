# Tempestade de IRQ do TXFE (dcd_dwc2) — análise e caminho

Investigação de 2026-08-14. **Nada aqui está aplicado**; é o mapa para quem retomar.

## O defeito

A base congela: o tick do FreeRTOS para, o USB morre, e só a tomada resolve. Sem hard fault.

Capturado com a placa travada:

```
pc         = handle_epin_irq (dcd_dwc2.c)
lr         = 0xffffffed          → dentro de uma exceção
DIEPINT1   = 0x2092              → TXFE pendente E EPDISD (endpoint desabilitado)
DIEPEMPMSK = 0x2                 → máscara do TXFE ainda LIGADA
xTickCount = congelado
```

`TXFE` é **somente leitura**: só se apaga quando o firmware escreve dados no FIFO. Se a transferência
morre com a máscara ligada, a interrupção dispara, o handler não tem o que escrever, retorna, e
dispara de novo — para sempre.

## A diferença entre a nossa versão e a 0.21.0

Nossa (`edpt_schedule_packets`, ~linha 380):

```c
// Enable fifo empty interrupt only if there are something to put in the fifo.
if (total_bytes != 0) {
    dwc2->diepempmsk |= (1 << epnum);
}
```

0.21.0:

```c
const uint16_t xferred_bytes = epin_write_tx_fifo(dwc2, epnum);   // 1º pacote direto
if ((epnum != 0) && (xfer->total_len - xferred_bytes > 0)) {
   dwc2->diepempmsk |= (1u << epnum);                              // só se SOBRAM dados
}
```

**Por que isso resolve o nosso caso:** os reports HID cabem num único pacote de 64 bytes. Na 0.21 a
máscara nunca chega a ser ligada para eles — e sem máscara ligada não há tempestade.

## ⚠️ O que NÃO fazer (já tentado e revertido no mesmo dia)

```c
if ((epin->diepctl & DIEPCTL_EPENA) == 0) dwc2->diepempmsk &= ~(1 << n);   // NÃO
```

**Quebra a enumeração USB.** "Endpoint sem EPENA" não significa "transferência abandonada" — é
também o estado normal *entre pacotes* de uma transferência longa (um descritor grande na
enumeração). Desligar a máscara ali interrompe a transferência no meio e ela nunca completa.

A lição: distinguir "parou de vez" de "está entre dois pacotes" exige a **máquina de estados da
transferência**, não um bit do controlador.

## Por que a migração completa não é trivial

O nosso `dcd_dwc2.c` **não corresponde a nenhuma release limpa**:

| comparado com | linhas diferentes |
|---|---|
| 0.15.0 | 1584 |
| 0.16.0 | 1227 |
| **0.17.0** (a mais próxima) | **1017** |

O `tusb_option.h` diz 0.17.0, mas o driver tem `#define DWC2_DEBUG 2` e outra estrutura de includes
— veio de um ponto intermediário do repositório, ou foi customizado. Trocar os 8 arquivos que
compilamos são ~4.700 linhas, e a pilha USB carrega o **`bInterval` de 1 ms** que sustenta a
compatibilidade com ACC/AMS2/AC EVO.

## Caminhos, do mais barato ao mais completo

1. **Portar só o padrão da 0.21** — extrair a escrita do FIFO para uma função e chamá-la no
   agendamento, ligando a máscara apenas se sobrarem dados. Cirúrgico, mas mexe no fluxo de escrita.
2. **Atualizar só o `dcd_dwc2.c` + `dwc2_common.c`** para a 0.21, mantendo o resto. Exige conferir a
   API interna que `usbd.c` espera.
3. **Atualizar a pilha inteira.** Mais correto a longo prazo, maior risco de regressão nos sims.

Em qualquer um: testar **enumerar → app → os quatro sims**, porque a regressão apareceria nos jogos,
não na bancada.

## Enquanto isso

O **watchdog** (`inc/watchdog.h`) faz a base reiniciar em ~2 s em vez de congelar. ⚠️ Não resolve
para o jogo: reiniciar no meio de uma sessão faz o ACC perder a base até sair e voltar.
