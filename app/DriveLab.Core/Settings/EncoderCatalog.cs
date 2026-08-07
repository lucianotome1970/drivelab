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
    public const int Generico = 0;
    public const int E6b2     = 1;
    public const int Mt6701   = 2;
    public const int Mt6835   = 3;
    public const int As5047p  = 4;

    public static IReadOnlyList<EncoderModel> Models { get; } = new[]
    {
        new EncoderModel(Generico, "Incremental genérico"),
        new EncoderModel(E6b2,     "Omron E6B2-CWZ6C"),
        new EncoderModel(Mt6701,   "MagnTek MT6701"),
        new EncoderModel(Mt6835,   "MagnTek MT6835"),
        new EncoderModel(As5047p,  "AMS AS5047P"),
    };

    private static readonly Dictionary<int, EncoderTech[]> Techs = new()
    {
        [Generico] = new[] { EncoderTech.Abz },
        [E6b2]     = new[] { EncoderTech.Abz },
        [Mt6701]   = new[] { EncoderTech.Abz, EncoderTech.Ssi },
        [Mt6835]   = new[] { EncoderTech.Abz, EncoderTech.Spi },
        [As5047p]  = new[] { EncoderTech.Abz, EncoderTech.Spi },
    };

    public static IReadOnlyList<EncoderTech> TechnologiesFor(int modelId) =>
        Techs.TryGetValue(modelId, out var t) ? t : new[] { EncoderTech.Abz };

    /// <summary>Resolução de fábrica em CONTAGENS por volta (já com o ×4 do ABZ aplicado).
    /// Zero significa "não há valor de fábrica — a pessoa digita".</summary>
    /// <remarks>⚠️ <b>Limitação conhecida:</b> o campo `encoder_cpr` do protocolo é <b>u16</b> (máx
    /// 65535), então a resolução do MT6835 em SPI (2.097.152, 21 bits) <b>não é transportável hoje</b>.
    /// Usar o MT6835 em ABZ funciona; o modo SPI depende de o campo virar 32 bits, o que é mudança de
    /// protocolo e de firmware — está na parte 2 do trabalho de encoder.
    ///
    /// ⚠️ Os valores de ABZ dos três magnéticos são PROGRAMÁVEIS no chip. Estes são os
    /// de fábrica; se alguém reprogramar o sensor, precisa corrigir o campo na mão. Confirmar cada
    /// um no datasheet quando a peça chegar — valor errado aqui reproduz exatamente o problema que
    /// este catálogo existe para eliminar.</remarks>
    public static int DefaultResolution(int modelId, EncoderTech tech) => (modelId, tech) switch
    {
        (E6b2,    EncoderTech.Abz) => 10000,   // 2500 PPR × 4
        (Mt6701,  EncoderTech.Abz) => 4096,    // 1024 PPR × 4 (programável)
        (Mt6701,  EncoderTech.Ssi) => 16384,   // 14 bits, sem multiplicação
        (Mt6835,  EncoderTech.Abz) => 16384,   // 4096 PPR × 4 (programável)
        (Mt6835,  EncoderTech.Spi) => 2097152, // 21 bits — ⚠️ NÃO CABE no campo atual (u16, máx 65535)
        (As5047p, EncoderTech.Abz) => 4000,    // 1000 PPR × 4 (programável)
        (As5047p, EncoderTech.Spi) => 16384,   // 14 bits
        _ => 0,                                // genérico: digitado
    };
}
