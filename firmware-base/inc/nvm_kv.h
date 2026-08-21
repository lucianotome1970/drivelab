// ============================================================================
//  DriveLab
//  nvm_kv.h — Persistência por CHAVE/VALOR em flash, gravando só o que mudou.
//
//  POR QUE ESTE MÓDULO EXISTE (e o que ele aposenta)
//
//  Até aqui, "Salvar" empacotava os 58 ajustes num blob, APAGAVA o setor inteiro
//  e reescrevia tudo. Um setor apagado no F405 congela o processador por ~250 ms
//  — e daí decorria, em cadeia, quase tudo o que atrapalhou a bancada:
//
//    · congelar a CPU com as fases energizadas derruba o controle de 8 kHz,
//      então a gravação EXIGIA o motor parado;
//    · numa base ocupada (calibrando, em falha, dirigindo) o motor não para, e o
//      "Salvar" simplesmente não acontecia — sem aviso útil a quem clicou;
//    · gravávamos o que estava na MEMÓRIA da base, não o que o app mandou: um
//      ajuste perdido no caminho virava gravação silenciosamente errada, com o
//      contador de gravações subindo do mesmo jeito. Foi assim que um encoder de
//      2500 pulsos ficou gravado como 1000 três vezes seguidas (18/08/2026);
//    · e não havia como saber QUAL campo falhou, porque era tudo-ou-nada.
//
//  As duas implementações de referência que estudamos resolvem isso do mesmo
//  jeito, e não por esperteza — por terem escolhido o mecanismo certo para a
//  peça: gravam UMA VARIÁVEL POR VEZ, por endereço, e só quando ela mudou.
//
//  COMO FUNCIONA AQUI
//
//  Duas páginas de 16 KB (setores 1 e 2). Escrever é ACRESCENTAR um registro de
//  8 bytes no fim da página ativa — nunca apagar. Ler é varrer a página: vence o
//  ÚLTIMO registro daquela chave, porque é a escrita mais recente.
//
//    registro = [ chave:u16 | marca:u16 ][ valor:u32 ]
//
//  Acrescentar não precisa de erase, então não congela o processador: são duas
//  escritas de 32 bits, na casa dos microssegundos. O motor pode estar armado.
//
//  Quando a página enche (2047 registros — mais de mil "Salvar"), acontece a
//  COMPACTAÇÃO: os valores vigentes são copiados para a outra página e a antiga é
//  apagada. Só aí há erase, e só aí é preciso o motor parado. Um evento raro e
//  previsível, em vez de um risco a cada clique.
//
//  ⚠️ O QUE ISSO NÃO É: um sistema de arquivos. Não há remoção nem chaves
//  variáveis — as chaves são os ids dos ajustes, fixos e conhecidos em tempo de
//  compilação. Simplicidade aqui é proteção: menos estados possíveis, menos
//  maneiras de corromper a configuração de alguém.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_NVM_KV_H
#define DRIVELAB_NVM_KV_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/// O que aconteceu numa tentativa de escrita. Existem quatro porque cada um pede
/// uma reação diferente de quem chamou — devolver só "deu certo/deu errado"
/// esconderia justamente a informação que faltava no modelo antigo.
typedef enum {
    NVM_KV_IGUAL      = 0,  ///< o valor já era esse: nada foi escrito (e está certo)
    NVM_KV_GRAVADO    = 1,  ///< acrescentado à página ativa, confirmado por leitura
    NVM_KV_CHEIO      = 2,  ///< a página encheu: precisa compactar (exige motor parado)
    NVM_KV_ERRO       = 3,  ///< a flash recusou a escrita
} NvmKvResultado;

/// Prepara o módulo: descobre qual página está ativa e onde termina o que já foi
/// escrito. Chamar uma vez no boot, antes de qualquer leitura.
void nvm_kv_init(void);

/// Lê a chave. Devolve 1 e preenche `out` se ela existir; 0 se nunca foi escrita
/// (aí quem chamou usa o padrão dele).
int nvm_kv_read(uint16_t chave, uint32_t* out);

/// Escreve a chave — mas só se o valor for diferente do que já está lá.
///
/// ⚠️ Ler antes de escrever não é economia de flash: é o que torna "Salvar" uma
/// operação barata e repetível. Salvar duas vezes seguidas sem mexer em nada não
/// gasta uma linha de flash, e a página leva muito mais tempo para encher.
NvmKvResultado nvm_kv_write(uint16_t chave, uint32_t valor);

/// Copia os valores vigentes para a outra página e apaga a antiga.
///
/// ⚠️ ESTE é o único caminho que apaga flash, e portanto o único que congela o
/// processador (~250 ms). Chamar SÓ com o motor parado. Devolve 1 se compactou.
int nvm_kv_compact(void);

/// Quanto ainda cabe na página ativa, em registros. Zero = a próxima escrita
/// devolve NVM_KV_CHEIO. Serve para o firmware compactar num momento tranquilo,
/// em vez de ser surpreendido no meio de um "Salvar".
uint32_t nvm_kv_espaco_livre(void);

/// Diagnóstico do último "Salvar": quantas chaves foram escritas de fato, quantas
/// já estavam iguais e quantas falharam. É o que o app mostra em vez de adivinhar
/// — e é o que as implementações de referência expõem no comando de status.
typedef struct {
    uint16_t gravadas;
    uint16_t iguais;
    uint16_t erros;
} NvmKvBalanco;

void         nvm_kv_balanco_zerar(void);
NvmKvBalanco nvm_kv_balanco(void);

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_NVM_KV_H
