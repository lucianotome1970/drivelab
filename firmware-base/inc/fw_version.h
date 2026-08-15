// ============================================================================
//  DriveLab
//  fw_version.h — A versão do firmware. UMA declaração, dois consumidores.
//
//  POR QUE ESTE ARQUIVO EXISTE: a versão era declarada em DOIS lugares — o
//  carimbo dentro do .bin (fw_signature.c, que o app lê do ARQUIVO antes de
//  gravar) e o report de estado (a0_channel.cpp, que a BASE informa depois de
//  gravada). O segundo tinha os números escritos à mão.
//
//  Eles divergiram, e o jeito como isso apareceu é o que interessa: a tela de
//  atualização mostrava "arquivo versão 0.4.0" e, depois de gravar com sucesso,
//  "base v0.2.3". Parece gravação que não pegou — o pior susto possível logo
//  depois de atualizar um firmware. Constatado em 14/08/2026, no teste de
//  implantação do zero.
//
//  O comentário no fw_signature.c já mandava "casar com o report 0x21". Pedir
//  não bastou: enquanto forem duas declarações, elas voltam a divergir. Agora é
//  uma só, e os dois lados a incluem.
//
//  ⚠️ AO SUBIR A VERSÃO, mude AQUI e em lugar nenhum mais.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_FW_VERSION_H
#define DRIVELAB_FW_VERSION_H

// Casa com a versão da RELEASE e com o <Version> do Studio — não é um número interno do firmware.
// A regra ja estava escrita no a0_channel.cpp: os tres sobem juntos ao cortar uma release. O
// carimbo do .bin era o unico que ninguem subia, e por isso ficou preso em 0.4.0 enquanto o resto
// andava para 0.2.3. Numero de firmware "proprio" e o que produziu a divergencia; nao existe mais.
#define DRVLAB_FW_VER_MAJOR  0u
#define DRVLAB_FW_VER_MINOR  2u
#define DRVLAB_FW_VER_PATCH  3u

#endif // DRIVELAB_FW_VERSION_H
