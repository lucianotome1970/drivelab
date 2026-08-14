// ============================================================================
//  DriveLab
//  BaseProfile.cs — Perfil de settings da base do volante, para exportar e importar em arquivo.
//
//  POR QUE EXISTE: pedal, freio de mao e aro ja exportavam os seus perfis; a BASE, nao. Justamente
//  ela, cujos ajustes sao os que mais custam a acertar — forca, amortecimento, curva, batente — so
//  existiam dentro da placa. Nao havia como guardar um ajuste bom antes de experimentar outro, nem
//  compartilhar, nem recuperar depois de apagar a flash.
//
//  POR QUE UM DICIONARIO, e nao um registro com campos fixos: sao dezenas de settings e a lista
//  cresce. Um registro fixo precisaria ser editado a cada setting novo, e quem esquecesse teria uma
//  exportacao silenciosamente incompleta — o mesmo tipo de falha que os settings orfaos ja nos
//  custaram. Com dicionario por CHAVE ESTAVEL (o nome do BaseSettingId), setting novo entra sozinho,
//  arquivo antigo continua legivel, e setting removido e ignorado em vez de quebrar a importacao.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

namespace DriveLab.Core.Settings;

/// <summary>Os settings da base num arquivo: chave estável (nome do <see cref="BaseSettingId"/>) → valor.</summary>
public sealed class BaseProfile
{
    public Dictionary<string, double> Settings { get; set; } = new();
}

/// <summary>Resultado de preparar uma importação: o que aplicar e o que ficou de fora — com o motivo.
///
/// <para>A separação existe para a tela poder CONTAR a verdade. "Importado com sucesso" depois de
/// aplicar 3 de 40 campos é pior que um erro: a pessoa vai pilotar achando que está com o ajuste que
/// escolheu.</para></summary>
public sealed record BaseProfileImport(
    IReadOnlyDictionary<BaseSettingId, double> Aplicar,
    /// <summary>Chaves que o schema não conhece — arquivo de uma versão mais nova, ou editado à mão.</summary>
    IReadOnlyList<string> Desconhecidos,
    /// <summary>Chaves válidas mas que esta tela não expõe. Na prática são os campos de HARDWARE
    /// quando o app está no modo cliente: eles descrevem a máquina (pares de polos, variante da
    /// placa, constante de torque) e não podem chegar vindos do arquivo de outra pessoa.</summary>
    IReadOnlyList<string> ForaDestaTela,
    /// <summary>Quantos valores precisaram ser limitados à faixa do schema.</summary>
    int Ajustados);

public static class BaseProfileExchange
{
    /// <summary>Nome do módulo no envelope — impede importar perfil de pedal na tela da base.</summary>
    public const string Module = "wheelbase";

    /// <summary>Monta o perfil a partir dos valores em tela.
    ///
    /// <para>Só entram campos JÁ LIDOS da base. Um campo que ainda não carregou exibe "—" e guarda o
    /// padrão do schema por dentro; exportá-lo gravaria um valor que a placa nunca teve, e na volta
    /// ele seria aplicado como se fosse escolha de alguém.</para></summary>
    public static BaseProfile Criar(IEnumerable<(BaseSettingId Id, double Valor, bool Lido)> campos)
    {
        var perfil = new BaseProfile();
        foreach (var (id, valor, lido) in campos)
            if (lido)
                perfil.Settings[id.ToString()] = valor;
        return perfil;
    }

    /// <summary>Confere o perfil contra o schema e contra os campos que esta tela edita.
    ///
    /// <para>Todo valor é limitado à faixa do descritor. Isto não é preciosismo: o arquivo é texto e
    /// pode ser editado à mão, e valor fora de faixa em corrente ou ganho não pode chegar cru ao
    /// firmware. O <c>Clamp</c> é o mesmo que a escrita já usa, então importar não consegue produzir
    /// um estado que a tela não produziria.</para></summary>
    public static BaseProfileImport Preparar(BaseProfile perfil, IReadOnlySet<BaseSettingId> editaveis)
    {
        var aplicar = new Dictionary<BaseSettingId, double>();
        var desconhecidos = new List<string>();
        var foraDestaTela = new List<string>();
        var ajustados = 0;

        foreach (var (chave, valor) in perfil.Settings)
        {
            if (!Enum.TryParse<BaseSettingId>(chave, ignoreCase: false, out var id) ||
                !Enum.IsDefined(typeof(BaseSettingId), id))
            {
                desconhecidos.Add(chave);
                continue;
            }

            if (!editaveis.Contains(id))
            {
                foraDestaTela.Add(chave);
                continue;
            }

            var limitado = BaseSettingsSchema.Get(id).Clamp(valor);
            // Comparação exata é o que se quer aqui: Clamp devolve o MESMO double quando não mexeu.
            if (limitado != valor) ajustados++;
            aplicar[id] = limitado;
        }

        return new BaseProfileImport(aplicar, desconhecidos, foraDestaTela, ajustados);
    }
}
