# Contrato A0 — arquivado

> **Este documento foi substituído por [PROTOCOL.md](PROTOCOL.md).**
>
> 🇧🇷 *Use o `PROTOCOL.md`: ele é o contrato mantido, cobre os quatro equipamentos (base, aro,
> pedais e freio de mão) e é conferido contra o código.*

## Por que ele saiu

Ele nasceu como **ordem de serviço** para escrever o firmware da base, num tempo em que o app já
existia e a placa ainda não respondia. Por isso falava na primeira pessoa e listava correções a
fazer ("meu `a0_channel.cpp` atual usa Position ±32767 — CORRIGIR").

Cumprida a função, ele passou a envelhecer sozinho e a contradizer o código:

- listava os settings até o **44**; hoje são **48**
- dava `encoder_cpr` como `u16`; hoje é `u32`, porque um encoder magnético de 21 bits reporta
  2.097.152 contagens por volta
- não conhecia o report `0x17`, a consulta do valor de fábrica

Dois documentos de protocolo, cada um incompleto de um jeito, é pior que um. Quem for implementar
o protocolo numa placa própria tem um endereço só.

<sub>DriveLab — Autor: Luciano Tomé — Licença MIT</sub>
