// ============================================================================
//  DriveLab
//  nvm_kv.cpp — Chave/valor por append em duas páginas de flash. Ver nvm_kv.h
//  para o porquê deste mecanismo ter substituído o blob com erase.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include "nvm_kv.h"
#include <stm32f4xx_hal.h>

namespace {

// As duas páginas. São os setores que o projeto já reservava para configuração —
// o linker os mantém fora do código (região FFB_NVM).
struct Pagina { uint32_t base; uint32_t tamanho; uint32_t setor; };
constexpr Pagina kPaginas[2] = {
    { 0x08004000UL, 0x4000UL, FLASH_SECTOR_1 },
    { 0x08008000UL, 0x4000UL, FLASH_SECTOR_2 },
};

// Marca de estado, gravada na PRIMEIRA word da página. Flash apagada é 0xFFFFFFFF,
// e só se pode escrever apagando bits (1→0) — por isso os estados vão "apagando"
// bits em sequência, nunca acendendo. É o que permite mudar de estado sem erase.
constexpr uint32_t kVazia      = 0xFFFFFFFFUL;   // apagada, pronta para receber
constexpr uint32_t kRecebendo  = 0xFFFF0000UL;   // compactação em curso: destino
constexpr uint32_t kAtiva      = 0x0000A5A5UL;   // é aqui que se lê e se escreve

constexpr uint32_t kTamRegistro = 8;             // [chave|marca][valor]
constexpr uint16_t kRegistroBom = 0xC5C5u;       // marca de registro completo

int      s_ativa      = -1;   // índice em kPaginas, -1 = não inicializado
uint32_t s_fim        = 0;    // offset do primeiro espaço livre na página ativa
NvmKvBalanco s_balanco{};

inline uint32_t ler32(uint32_t addr) { return *reinterpret_cast<volatile const uint32_t*>(addr); }

// Escreve uma word e CONFERE lendo de volta. Conferir não é paranoia: a flash pode
// recusar em silêncio (tensão, célula gasta), e uma configuração que "foi gravada"
// sem estar lá é o pior desfecho possível — é justamente o que este módulo veio
// eliminar.
// ⚠️ LIMPAR AS FLAGS DE ERRO ANTES DE QUALQUER OPERAÇÃO.
//
// O STM32 mantém as flags de erro da flash TRAVADAS até alguém limpá-las. Uma flag acesa por uma
// operação anterior — inclusive de outro módulo, inclusive de antes do boot — faz a próxima
// operação falhar sem motivo aparente. As duas implementações de referência fazem isto em toda
// escrita, e não por precaução vaga: sem isso, apagar a página falha silenciosamente.
//
// Medido aqui em 19/08/2026, no primeiro boot com esta persistência: o apagamento falhou, a marca
// da página foi escrita POR CIMA do conteúdo antigo, e o resultado (0x00000404) é exatamente
// 0x0000A5A5 AND o magic do formato velho — flash só apaga bits, então escrever sobre o que não
// foi apagado devolve o E lógico dos dois. A página teria sido reapagada a cada boot, para sempre.
inline void limpar_flags(void) {
    __HAL_FLASH_CLEAR_FLAG(FLASH_FLAG_EOP    | FLASH_FLAG_OPERR  | FLASH_FLAG_WRPERR |
                           FLASH_FLAG_PGAERR | FLASH_FLAG_PGSERR | FLASH_FLAG_PGPERR);
}

bool escrever32(uint32_t addr, uint32_t valor) {
    limpar_flags();
    if (HAL_FLASH_Program(FLASH_TYPEPROGRAM_WORD, addr, valor) != HAL_OK) return false;
    return ler32(addr) == valor;
}

bool marcar(int p, uint32_t estado) {
    HAL_FLASH_Unlock();
    const bool ok = escrever32(kPaginas[p].base, estado);
    HAL_FLASH_Lock();
    return ok;
}

// Onde termina o que já foi escrito nesta página.
//
// ⚠️ O SLOT PRECISA ESTAR INTEIRO LIVRE para ser o fim — cabeçalho E valor. Não
// basta o cabeçalho estar apagado: se a energia cair entre gravar o valor e gravar
// o cabeçalho (a ordem que protege a integridade), sobra um slot com o valor
// escrito e o cabeçalho em branco. Reusá-lo seria tentar escrever por cima de bits
// que já foram a zero — a flash recusa, e a página inteira ficaria travada a partir
// dali. Pulando o slot queimado, perde-se 8 bytes e a página segue viva.
uint32_t achar_fim(int p) {
    uint32_t off = kTamRegistro;   // a primeira word é a marca de estado
    while (off + kTamRegistro <= kPaginas[p].tamanho) {
        const uint32_t cab = ler32(kPaginas[p].base + off);
        const uint32_t val = ler32(kPaginas[p].base + off + 4);
        if (cab == kVazia && val == kVazia) break;
        off += kTamRegistro;
    }
    return off;
}

bool apagar(int p) {
    FLASH_EraseInitTypeDef er{};
    er.TypeErase    = FLASH_TYPEERASE_SECTORS;
    er.Sector       = kPaginas[p].setor;
    er.NbSectors    = 1;
    er.VoltageRange = FLASH_VOLTAGE_RANGE_3;
    uint32_t erro = 0;
    HAL_FLASH_Unlock();
    limpar_flags();
    bool ok = (HAL_FLASHEx_Erase(&er, &erro) == HAL_OK);
    HAL_FLASH_Lock();
    // CONFERE que apagou de verdade. O retorno do HAL diz que o comando foi aceito; só a leitura
    // diz que a página está limpa — e escrever numa página não apagada corrompe em silêncio, que é
    // o pior desfecho possível para uma configuração.
    if (ok) ok = (ler32(kPaginas[p].base) == kVazia);
    return ok;
}

} // namespace

extern "C" void nvm_kv_init(void) {
    // Estado normal: uma página ativa. Os demais casos são interrupções de energia
    // no meio de uma compactação, e cada um tem uma saída segura.
    const uint32_t e0 = ler32(kPaginas[0].base);
    const uint32_t e1 = ler32(kPaginas[1].base);

    if (e0 == kAtiva && e1 != kAtiva)      s_ativa = 0;
    else if (e1 == kAtiva && e0 != kAtiva) s_ativa = 1;
    else if (e0 == kAtiva && e1 == kAtiva) {
        // Duas ativas: a energia caiu depois de promover o destino e antes de apagar
        // a origem. A que RECEBEU é a boa (tem tudo), mas não há como distingui-las
        // por estado — então fica a 0 e a 1 é apagada, que é o pior caso conhecido e
        // não perde nada: a compactação copia tudo antes de promover.
        s_ativa = 0;
        apagar(1);
    } else if (e0 == kRecebendo || e1 == kRecebendo) {
        // Caiu no meio da cópia: o destino está incompleto. Descarta o destino e
        // segue com a origem, que continua íntegra — ninguém apaga a origem antes
        // de o destino estar pronto.
        const int destino = (e0 == kRecebendo) ? 0 : 1;
        apagar(destino);
        s_ativa = 1 - destino;
        if (ler32(kPaginas[s_ativa].base) != kAtiva) {
            // Nem origem válida: primeira vez (ou flash virgem). Começa limpo.
            apagar(0); marcar(0, kAtiva); s_ativa = 0;
        }
    } else {
        // Nenhuma página válida: primeiro boot deste firmware, ou flash apagada.
        //
        // ⚠️ SÓ ASSUME A PÁGINA SE ELA DE FATO FICOU PRONTA. Ignorar estes retornos foi o que
        // permitiu, em 19/08/2026, a página ser dada como ativa depois de um apagamento que
        // falhou — e a partir daí toda gravação ia para uma página suja.
        if (apagar(0) && marcar(0, kAtiva)) s_ativa = 0;
        else if (apagar(1) && marcar(1, kAtiva)) s_ativa = 1;   // a outra página como segunda chance
        else { s_ativa = -1; return; }                          // sem flash utilizável: não finge
    }
    s_fim = achar_fim(s_ativa);
}

extern "C" int nvm_kv_read(uint16_t chave, uint32_t* out) {
    if (s_ativa < 0 || out == nullptr) return 0;
    // Varre do começo ao fim guardando a ÚLTIMA ocorrência: como só acrescentamos,
    // a mais recente é a que vale. Varrer para trás seria mais rápido, mas exigiria
    // confiar em `s_fim`; ler para a frente reencontra a verdade na própria flash.
    // ⚠️ PULA o que não presta, NÃO para. Parar no primeiro slot em branco parece
    // uma otimização óbvia — e esconde um registro queimado por queda de energia:
    // tudo o que foi gravado DEPOIS dele ficaria invisível, e o ajuste da pessoa
    // reapareceria com o valor antigo sem explicação. Varrer até o fim custa
    // microssegundos e não tem esse buraco.
    int achou = 0;
    for (uint32_t off = kTamRegistro; off + kTamRegistro <= s_fim; off += kTamRegistro) {
        const uint32_t cab = ler32(kPaginas[s_ativa].base + off);
        if ((uint16_t)(cab >> 16) != kRegistroBom) continue;   // vazio ou incompleto: ignora
        if ((uint16_t)(cab & 0xFFFFu) != chave)    continue;
        *out = ler32(kPaginas[s_ativa].base + off + 4);
        achou = 1;
    }
    return achou;
}

extern "C" NvmKvResultado nvm_kv_write(uint16_t chave, uint32_t valor) {
    if (s_ativa < 0) return NVM_KV_ERRO;

    // ⚠️ NÃO ESCREVER O QUE NÃO MUDOU. É o que torna "Salvar" barato e repetível:
    // salvar dez vezes sem mexer em nada não gasta uma linha de flash, e a página
    // demora muito mais para encher. As duas referências fazem exatamente isto.
    uint32_t atual = 0;
    if (nvm_kv_read(chave, &atual) && atual == valor) { s_balanco.iguais++; return NVM_KV_IGUAL; }

    if (s_fim + kTamRegistro > kPaginas[s_ativa].tamanho) return NVM_KV_CHEIO;

    const uint32_t addr = kPaginas[s_ativa].base + s_fim;
    HAL_FLASH_Unlock();
    // VALOR PRIMEIRO, cabeçalho depois. A ordem é a garantia de integridade: o
    // registro só passa a existir (marca kRegistroBom) quando o valor já está na
    // flash. Se a energia cair no meio, sobra um registro sem marca, que a leitura
    // ignora — nunca um registro válido apontando para valor pela metade.
    bool ok = escrever32(addr + 4, valor);
    if (ok) ok = escrever32(addr, ((uint32_t)kRegistroBom << 16) | chave);
    HAL_FLASH_Lock();

    if (!ok) { s_balanco.erros++; return NVM_KV_ERRO; }
    s_fim += kTamRegistro;
    s_balanco.gravadas++;
    return NVM_KV_GRAVADO;
}

extern "C" int nvm_kv_compact(void) {
    if (s_ativa < 0) return 0;
    const int origem  = s_ativa;
    const int destino = 1 - s_ativa;

    if (!apagar(destino)) return 0;
    if (!marcar(destino, kRecebendo)) return 0;

    // Copia só o valor VIGENTE de cada chave. Percorre a origem uma vez por chave
    // encontrada; com algumas dezenas de chaves isso é irrelevante, e evita ter de
    // manter uma tabela em RAM que poderia divergir da flash.
    uint32_t off_destino = kTamRegistro;
    for (uint32_t off = kTamRegistro; off + kTamRegistro <= kPaginas[origem].tamanho; off += kTamRegistro) {
        // Pula vazios e queimados pela mesma razão da leitura: parar aqui deixaria
        // para trás tudo o que veio depois de um registro interrompido.
        const uint32_t cab = ler32(kPaginas[origem].base + off);
        if ((uint16_t)(cab >> 16) != kRegistroBom) continue;
        const uint16_t chave = (uint16_t)(cab & 0xFFFFu);

        // Só copia se esta for a última ocorrência da chave na origem.
        bool ultima = true;
        for (uint32_t o2 = off + kTamRegistro; o2 + kTamRegistro <= kPaginas[origem].tamanho; o2 += kTamRegistro) {
            const uint32_t c2 = ler32(kPaginas[origem].base + o2);
            if ((uint16_t)(c2 >> 16) == kRegistroBom && (uint16_t)(c2 & 0xFFFFu) == chave) { ultima = false; break; }
        }
        if (!ultima) continue;

        const uint32_t valor = ler32(kPaginas[origem].base + off + 4);
        const uint32_t addr  = kPaginas[destino].base + off_destino;
        HAL_FLASH_Unlock();
        bool ok = escrever32(addr + 4, valor);
        if (ok) ok = escrever32(addr, ((uint32_t)kRegistroBom << 16) | chave);
        HAL_FLASH_Lock();
        if (!ok) return 0;                 // destino ainda não é ativo: a origem segue valendo
        off_destino += kTamRegistro;
    }

    // A ordem aqui é o que protege contra queda de energia: promove o destino
    // ANTES de apagar a origem. Se cair no meio, ficam duas ativas — caso tratado
    // no init, e que não perde dados.
    if (!marcar(destino, kAtiva)) return 0;
    apagar(origem);
    s_ativa = destino;
    s_fim   = off_destino;
    return 1;
}

extern "C" uint32_t nvm_kv_espaco_livre(void) {
    if (s_ativa < 0) return 0;
    const uint32_t usados = s_fim;
    if (usados >= kPaginas[s_ativa].tamanho) return 0;
    return (kPaginas[s_ativa].tamanho - usados) / kTamRegistro;
}

extern "C" void nvm_kv_balanco_zerar(void) { s_balanco = NvmKvBalanco{}; }
extern "C" NvmKvBalanco nvm_kv_balanco(void) { return s_balanco; }
