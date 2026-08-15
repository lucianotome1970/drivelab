// ============================================================================
//  DriveLab
//  BaseState.cs — Estado de telemetria do volante (firmware, flags, posição, ângulo, torque, temperaturas) serializado para bytes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Buffers.Binary;

namespace DriveLab.Core.Protocol;

public sealed class BaseState
{
    public FirmwareVersion Firmware { get; set; }
    public BaseFlags Flags { get; set; }
    public short Position { get; set; }
    public short AngleDeciDeg { get; set; }
    public short Torque { get; set; }
    public short MotorCurrentMa { get; set; }
    public sbyte FetTempC { get; set; }
    public byte ErrorCode { get; set; }
    public ushort BusVoltageMv { get; set; }
    public sbyte MotorTempC { get; set; }
    public sbyte McuTempC { get; set; }

    /// <summary>Nível de clipping do FFB (0-255): quanto a força pedida pelo jogo passou do teto de torque e
    /// foi cortada. 0 = sem corte. Preenchido pelo firmware (medidor no engine).</summary>
    public byte Clipping { get; set; }

    /// <summary>Clipping em 0..100% (derivado de <see cref="Clipping"/>).</summary>
    public int ClippingPercent => (int)System.Math.Round(Clipping / 255.0 * 100);

    /// <summary>Fração do tempo ARMADO em que a base saturou (0-255). O nível ao vivo dura 500 ms e
    /// some antes de quem está pilotando conseguir olhar; este sobrevive à sessão inteira, e é o
    /// número com que se decide depois de sair do jogo.
    /// <para>Carregava o PICO de uma janela de 500 ms, e aquele número era lido errado justamente
    /// pelo caminho mais natural: "28%" parecia dizer "um terço da volta teve clipping" quando dizia
    /// "no PIOR meio-segundo, 28% daquele meio-segundo". Agora é razão direta entre ticks, então
    /// "3%" significa mesmo "3% do tempo que eu dirigi".</para></summary>
    public byte ClippingPeak { get; set; }

    /// <summary>Maior corrente positiva vista desde o boot, em mA. Junto com
    /// <see cref="CurrentPeakNegMa"/> revela ASSIMETRIA: um lado muito maior que o outro aponta
    /// referência de posição deslocada (o sintoma "força só de um lado").</summary>
    public short CurrentPeakPosMa { get; set; }

    /// <summary>Menor corrente negativa vista desde o boot, em mA (valor negativo).</summary>
    public short CurrentPeakNegMa { get; set; }

    /// <summary>Pico de clipping da sessão em 0..100% (derivado de <see cref="ClippingPeak"/>).</summary>
    public int ClippingPeakPercent => (int)System.Math.Round(ClippingPeak / 255.0 * 100);

    /// <summary>Parcela do clipping vinda do JOGO (0-255): a força chegou já cortada no talo do
    /// protocolo, porque o ganho dentro do jogo estourou a escala dele. A informação se perdeu ANTES
    /// de entrar na base, e nenhum ajuste daqui recupera — o único remédio é baixar o ganho no jogo.
    /// Medida por PLATÔ (amostras consecutivas no talo), não por amostra solta: um 255 isolado pode
    /// ser pico legítimo da física do carro.</summary>
    public byte ClippingGame { get; set; }

    /// <summary>Parcela do clipping vinda da BASE (0-255): a base recebeu o pedido inteiro e o teto
    /// de torque cortou. Esta cede com mais torque ou menos ganho.</summary>
    public byte ClippingBase { get; set; }

    /// <summary>Clipping do jogo em 0..100%.</summary>
    public int ClippingGamePercent => (int)System.Math.Round(ClippingGame / 255.0 * 100);

    /// <summary>Clipping da base em 0..100%.</summary>
    public int ClippingBasePercent => (int)System.Math.Round(ClippingBase / 255.0 * 100);

    /// <summary>As mesmas duas parcelas, mas no PICO da sessão. É este o número consultado depois de
    /// sair da pista — o valor ao vivo dura 500 ms e some antes de dar tempo de olhar. Sem a origem
    /// aqui, a decomposição não serviria justamente na hora em que ela é usada para decidir.</summary>
    public byte ClippingPeakGame { get; set; }
    public byte ClippingPeakBase { get; set; }

    /// <summary>Resultado da última varredura do teste de encoder. Aqui são só os graus MECÂNICOS
    /// crus que o firmware mediu; quem os traduz em perda de força e calor é EncoderHealth.</summary>
    public bool EncoderTestValido { get; set; }
    public double EncoderCoberturaVolta { get; set; }
    public double EncoderExcentricidadeGraus { get; set; }
    public double EncoderResiduoGraus { get; set; }
    public double EncoderFaseGraus { get; set; }
    public byte EncoderPolePairs { get; set; }

    /// <summary>Pico de clipping do jogo na sessão, em 0..100%.</summary>
    public int ClippingPeakGamePercent => (int)System.Math.Round(ClippingPeakGame / 255.0 * 100);

    /// <summary>Pico de clipping da base na sessão, em 0..100%.</summary>
    public int ClippingPeakBasePercent => (int)System.Math.Round(ClippingPeakBase / 255.0 * 100);

    /// <summary>Energia total dissipada no resistor de freio desde o boot, em millijoules.</summary>
    public uint BrakeEnergyMilliJ { get; set; }

    /// <summary>Quantas vezes o chopper acionou desde o boot (eventos de frenagem, não ciclos de PWM).</summary>
    public uint BrakeActivations { get; set; }

    /// <summary>Maior potência instantânea vista no resistor desde o boot, em décimos de watt.</summary>
    public ushort BrakePeakDeciW { get; set; }

    /// <summary>Energia dissipada em joules (derivada de <see cref="BrakeEnergyMilliJ"/>).</summary>
    public double BrakeEnergyJoules => BrakeEnergyMilliJ / 1000.0;

    /// <summary>Pico de potência em watts (derivado de <see cref="BrakePeakDeciW"/>).</summary>
    public double BrakePeakWatts => BrakePeakDeciW / 10.0;


    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        var span = buffer.AsSpan();
        Firmware.WriteTo(span.Slice(0, 4));
        span[4] = (byte)Flags;
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(5, 2), Position);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(7, 2), AngleDeciDeg);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(9, 2), Torque);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(11, 2), MotorCurrentMa);
        span[13] = (byte)FetTempC;
        span[14] = ErrorCode;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(15, 2), BusVoltageMv);
        span[17] = (byte)MotorTempC;
        span[18] = (byte)McuTempC;
        span[19] = Clipping;
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..24], BrakeEnergyMilliJ);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..28], BrakeActivations);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..30], BrakePeakDeciW);
        span[30] = ClippingPeak;
        BinaryPrimitives.WriteInt16LittleEndian(span[31..33], CurrentPeakPosMa);
        BinaryPrimitives.WriteInt16LittleEndian(span[33..35], CurrentPeakNegMa);
        span[35] = ClippingGame;
        span[36] = ClippingBase;
        span[37] = ClippingPeakGame;
        span[38] = ClippingPeakBase;
        return buffer;
    }

    public static BaseState Parse(ReadOnlySpan<byte> src) => new()
    {
        Firmware = FirmwareVersion.Parse(src.Slice(0, 4)),
        Flags = (BaseFlags)src[4],
        Position = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(5, 2)),
        AngleDeciDeg = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(7, 2)),
        Torque = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(9, 2)),
        MotorCurrentMa = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(11, 2)),
        FetTempC = (sbyte)src[13],
        ErrorCode = src[14],
        BusVoltageMv = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(15, 2)),
        MotorTempC = (sbyte)src[17],
        McuTempC = (sbyte)src[18],
        Clipping = src[19],
        BrakeEnergyMilliJ = BinaryPrimitives.ReadUInt32LittleEndian(src[20..24]),
        BrakeActivations = BinaryPrimitives.ReadUInt32LittleEndian(src[24..28]),
        BrakePeakDeciW = BinaryPrimitives.ReadUInt16LittleEndian(src[28..30]),
        ClippingPeak = src[30],
        CurrentPeakPosMa = BinaryPrimitives.ReadInt16LittleEndian(src[31..33]),
        CurrentPeakNegMa = BinaryPrimitives.ReadInt16LittleEndian(src[33..35]),
        // Firmware antigo (que não escreve estes bytes) manda zero — e zero aqui significa
        // "sem clipping", que é leitura honesta para quem ainda não atualizou a placa.
        ClippingGame = src.Length > 35 ? src[35] : (byte)0,
        ClippingBase = src.Length > 36 ? src[36] : (byte)0,
        ClippingPeakGame = src.Length > 37 ? src[37] : (byte)0,
        ClippingPeakBase = src.Length > 38 ? src[38] : (byte)0,
        // TESTE DO ENCODER. Firmware antigo manda zero, e zero em EncoderTestValido significa
        // "ainda não medi" — que é o estado correto de uma placa que nunca rodou o teste.
        EncoderTestValido = src.Length > 39 && src[39] != 0,
        EncoderCoberturaVolta = src.Length > 40 ? src[40] / 100.0 : 0,
        EncoderExcentricidadeGraus = src.Length > 42 ? BinaryPrimitives.ReadInt16LittleEndian(src[41..43]) / 100.0 : 0,
        EncoderResiduoGraus = src.Length > 44 ? BinaryPrimitives.ReadInt16LittleEndian(src[43..45]) / 100.0 : 0,
        EncoderFaseGraus = src.Length > 46 ? BinaryPrimitives.ReadInt16LittleEndian(src[45..47]) / 10.0 : 0,
        EncoderPolePairs = src.Length > 47 ? src[47] : (byte)0,
    };
}
