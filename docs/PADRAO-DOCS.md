# Padrão da documentação / Documentation standard

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> Este arquivo é o próprio exemplo do padrão que descreve. Copie a estrutura dele.
>
> This file is itself an example of the standard it describes. Copy its structure.

---

## 🇬🇧 English

### One file, two languages

**Never two files.** A `README.md` plus a `README.pt.md` looks tidy and rots fast: someone edits one
and forgets the other, and from then on the two disagree without anyone noticing. One file with two
sections cannot drift — the difference is visible in the same diff.

**English first, Portuguese last.** English is what reaches the most people on GitHub; whoever wants
Portuguese takes one click.

### The skeleton

```markdown
# Title in English / Título em português

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> Optional: one or two lines that matter in both languages (a warning,
> what the document is for). Keep it short.

---

## 🇬🇧 English

...the whole content in English...

---

## 🇧🇷 Português

...the whole content in Portuguese...
```

### Rules that are not obvious

**The anchors are exactly `#-english` and `#-português`.** GitHub builds them from the heading:
`## 🇬🇧 English` becomes `#-english` because the emoji turns into the leading dash. Writing
`#english` breaks the link. The Portuguese one carries the accent — `#-português` — and that is
correct, GitHub accepts it.

**`---` separates the language blocks**, and only them. Inside a language, use headings.

**Both sections carry the same content.** Not a summary in one and detail in the other. If something
only makes sense in one language (a link to a Brazilian store, for instance), it can appear in one
alone — but the reasoning, the numbers and the warnings appear in both.

**Say what the document does not know.** A wiring guide that omits "the connector order comes from
the manufacturer" is worse than no guide: it reads as authoritative and burns someone's board.

### When a document is monolingual

Some are internal and stay in one language — audit notes, roadmaps, bench logs. That is fine, but
then **do not** put the language selector at the top: an inert link is worse than none.

---

## 🇧🇷 Português

### Um arquivo, duas línguas

**Nunca dois arquivos.** Um `README.md` mais um `README.pt.md` parece organizado e apodrece rápido:
alguém edita um e esquece o outro, e a partir dali os dois se contradizem sem ninguém perceber. Um
arquivo com duas seções não tem como divergir — a diferença aparece no mesmo diff.

**Inglês primeiro, português no fim.** O inglês é o que alcança mais gente no GitHub; quem quer
português dá um clique.

### O esqueleto

```markdown
# Title in English / Título em português

**🇬🇧 [English](#-english) · 🇧🇷 [Português](#-português)**

> Opcional: uma ou duas linhas que valem nas duas línguas (um aviso,
> para que serve o documento). Curto.

---

## 🇬🇧 English

...o conteúdo inteiro em inglês...

---

## 🇧🇷 Português

...o conteúdo inteiro em português...
```

### Regras que não são óbvias

**As âncoras são exatamente `#-english` e `#-português`.** O GitHub as constrói a partir do título:
`## 🇬🇧 English` vira `#-english` porque o emoji se transforma no traço inicial. Escrever `#english`
quebra o link. A do português leva o acento — `#-português` — e está certo assim, o GitHub aceita.

**`---` separa os blocos de língua**, e só eles. Dentro de uma língua, use títulos.

**As duas seções trazem o mesmo conteúdo.** Não um resumo numa e o detalhe na outra. Se algo só faz
sentido numa língua (um link de loja brasileira, por exemplo), pode aparecer só nela — mas o
raciocínio, os números e os avisos aparecem nas duas.

**Diga o que o documento NÃO sabe.** Um guia de montagem que omite "a ordem do conector é do
fabricante" é pior que nenhum guia: soa autoritativo e queima a placa de alguém.

### Quando um documento é monolíngue

Alguns são internos e ficam numa língua só — notas de auditoria, roadmaps, registros de bancada.
Tudo bem, mas aí **não** ponha o seletor de idioma no topo: um link inerte é pior que nenhum.
