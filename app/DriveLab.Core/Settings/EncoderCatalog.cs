// ============================================================================
//  DriveLab
//  EncoderCatalog.cs — Catálogo de encoders suportados: que tecnologias cada
//  sensor oferece e qual a resolução de fábrica de cada combinação.
//
//  POR QUE EXISTE: o erro mais recorrente do fórum é CPR errado (68 tópicos),
//  quase sempre por esquecer que ABZ multiplica por 4. Com o catálogo, quem usa
//  um sensor conhecido não digita resolução nenhuma — ela vem daqui.
//
//  ABZ É O DENOMINADOR COMUM: todo sensor da lista o suporta, e é o caminho já
//  validado no firmware. Por isso qualquer sensor novo entra funcionando pelo
//  ABZ, e SSI/SPI viram upgrade em vez de pré-requisito.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

/// <summary>Como o sensor está ligado na placa.</summary>
public enum EncoderTech { Abz = 0, Ssi = 1, Spi = 2 }

public sealed record EncoderModel(int Id, string Name);

public static class EncoderCatalog
{
    /// <summary>APELIDO LEGADO. Foi a opção "Incremental genérico", removida da lista: escolher o
    /// sensor pelo nome é o que permite avisar o que ele oferece (o AS5047P não tem SSI) e travar a
    /// resolução onde ela vem do silício. "Genérico" não permitia nada disso e virava o caminho
    /// fácil para sair da tela sem configurar.
    ///
    /// <para>O id 0 NÃO foi reaproveitado de propósito: renumerar faria uma placa com 1 salvo (E6B2)
    /// passar a ler MT6701 no boot seguinte — o ajuste mudaria de sentido sozinho. Placas gravadas
    /// antes desta versão têm 0 na flash, e aqui ele é lido como E6B2, que é o incremental da
    /// bancada e o comportamento que elas já tinham.</para></summary>
    public const int Generico = 0;

    public const int E6b2     = 1;
    public const int Mt6701   = 2;
    public const int Mt6835   = 3;
    public const int As5047p  = 4;

    /// <summary>Traduz o que veio da placa para um modelo da lista. Só o 0 (legado) precisa de
    /// tradução; o resto passa direto.</summary>
    public static int Normalize(int modelId) => modelId == Generico ? E6b2 : modelId;

    public static IReadOnlyList<EncoderModel> Models { get; } = new[]
    {
        new EncoderModel(E6b2,     "Omron E6B2-CWZ6C"),
        new EncoderModel(Mt6701,   "MagnTek MT6701"),
        new EncoderModel(Mt6835,   "MagnTek MT6835"),
        new EncoderModel(As5047p,  "AMS AS5047P"),
    };

    private static readonly Dictionary<int, EncoderTech[]> Techs = new()
    {
        [Generico] = new[] { EncoderTech.Abz },   // legado: mesmas opções do E6B2
        [E6b2]     = new[] { EncoderTech.Abz },
        [Mt6701]   = new[] { EncoderTech.Abz, EncoderTech.Ssi },
        [Mt6835]   = new[] { EncoderTech.Abz, EncoderTech.Spi },
        [As5047p]  = new[] { EncoderTech.Abz, EncoderTech.Spi },
    };

    public static IReadOnlyList<EncoderTech> TechnologiesFor(int modelId) =>
        Techs.TryGetValue(modelId, out var t) ? t : new[] { EncoderTech.Abz };

    /// <summary>A resolução é imposta pelo silício e NÃO se digita?
    ///
    /// Em SSI/SPI sim: um MT6701 tem 14 bits, exatamente 16384 — não existe 16393 nesse sensor.
    /// Em ABZ não: a resolução é PROGRAMÁVEL nos magnéticos, então quem reprogramou o chip precisa
    /// poder corrigir. No genérico também não, porque aí quem sabe o número é a pessoa.</summary>
    public static bool IsResolutionFixed(int modelId, EncoderTech tech) =>
        modelId != Generico && tech != EncoderTech.Abz && DefaultResolution(modelId, tech) > 0;

    /// <summary>Teto de resolução do sensor. Zero = sem teto conhecido (genérico) — aí vale o
    /// limite do campo.</summary>
    public static int MaxResolution(int modelId, EncoderTech tech) =>
        DefaultResolution(modelId, tech);

    /// <summary>Resolução de fábrica em CONTAGENS por volta (já com o ×4 do ABZ aplicado).
    /// Zero significa "não há valor de fábrica — a pessoa digita".</summary>
    /// <remarks>O campo `encoder_cpr` do protocolo é <b>u32</b>, então os 21 bits do MT6835 em SPI
    /// (2.097.152) cabem. (Era u16, máx 65535, e a resolução em SPI não passava pelo fio; virou 32
    /// bits no firmware e no app.) O que ainda falta para usar o MT6835 em SPI não é o transporte
    /// do número, e sim o firmware LER o sensor nesse modo — hoje só o caminho ABZ está ligado.
    ///
    /// ⚠️ Os valores de ABZ dos três magnéticos são PROGRAMÁVEIS no chip. Estes são os
    /// de fábrica; se alguém reprogramar o sensor, precisa corrigir o campo na mão. Confirmar cada
    /// um no datasheet quando a peça chegar — valor errado aqui reproduz exatamente o problema que
    /// este catálogo existe para eliminar.</remarks>
    public static int DefaultResolution(int modelId, EncoderTech tech) => (modelId, tech) switch
    {
        // O E6B2-CWZ6C e uma FAMILIA: existe de 100, 360, 500, 1000, 1024, 2000, 2500 PPR, e o
        // numero faz parte do codigo do modelo (E6B2-CWZ6C 1000P/R). Nao ha resolucao de fabrica
        // a cravar — quem sabe qual variante tem e a pessoa, lendo a etiqueta.
        (E6b2,    EncoderTech.Abz) => 0,
        // O MT6701 em ABZ nao tem valor de fabrica unico — confirmado no datasheet (Rev. 1.8) agora
        // que a peca chegou. A lista de partes da MagnTek traz a MESMA pastilha com PPR diferente
        // gravado: -STD sai com AB = 1 PPR, -AKD com 1000, -ACD com 1024, mais as variantes de 200
        // a 800. E o valor e regravavel na EEPROM (com VDD > 4,5 V), entao ate a variante deixa de
        // ser garantia. Quem sabe qual e o numero e a pessoa, olhando o que comprou.
        //
        // Preencher 4096 aqui era pior que deixar vazio: o campo PARECE conferido e a pessoa nao
        // questiona. Num modulo -STD daria 4096 contra 4 contagens reais — mil vezes errado, e o
        // volante andaria uma fracao de grau por volta inteira.
        (Mt6701,  EncoderTech.Abz) => 0,
        (Mt6701,  EncoderTech.Ssi) => 16384,   // 14 bits, sem multiplicação
        (Mt6835,  EncoderTech.Abz) => 16384,   // 4096 PPR × 4 (programável)
        (Mt6835,  EncoderTech.Spi) => 2097152, // 21 bits (cabe: o campo é u32)
        (As5047p, EncoderTech.Abz) => 4000,    // 1000 PPR × 4 (programável)
        (As5047p, EncoderTech.Spi) => 16384,   // 14 bits
        _ => 0,                                // genérico: digitado
    };
}
