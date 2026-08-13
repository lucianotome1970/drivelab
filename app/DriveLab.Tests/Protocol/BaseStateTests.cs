// ============================================================================
//  DriveLab
//  BaseStateTests.cs — Testes de round-trip do BaseState.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class BaseStateTests
{
    [Fact]
    public void ToBytes_Has_ReportSize_Length()
    {
        var state = new BaseState();
        Assert.Equal(ReportConstants.ReportSize, state.ToBytes().Length);
    }

    [Fact]
    public void ToBytes_Then_Parse_RoundTrips_All_Fields()
    {
        var state = new BaseState
        {
            Firmware = new FirmwareVersion(0, 26, 7, 12),
            Flags = BaseFlags.ForceEnabled | BaseFlags.UsingSimulator,
            Position = -4200,
            AngleDeciDeg = 1350,
            Torque = 9000,
            MotorCurrentMa = -1500,
            FetTempC = 41,
            ErrorCode = 0,
            BusVoltageMv = 23950,
            MotorTempC = 55,
            McuTempC = -128,
            Clipping = 200,
        };

        var parsed = BaseState.Parse(state.ToBytes());

        Assert.Equal(state.Firmware, parsed.Firmware);
        Assert.Equal(state.Flags, parsed.Flags);
        Assert.Equal(state.Position, parsed.Position);
        Assert.Equal(state.AngleDeciDeg, parsed.AngleDeciDeg);
        Assert.Equal(state.Torque, parsed.Torque);
        Assert.Equal(state.MotorCurrentMa, parsed.MotorCurrentMa);
        Assert.Equal(state.FetTempC, parsed.FetTempC);
        Assert.Equal(state.ErrorCode, parsed.ErrorCode);
        Assert.Equal((ushort)23950, parsed.BusVoltageMv);
        Assert.Equal(55, parsed.MotorTempC);
        Assert.Equal(-128, parsed.McuTempC);
        Assert.Equal(200, parsed.Clipping);
        Assert.Equal(78, parsed.ClippingPercent);   // 200/255 ≈ 78%
    }

    [Fact]
    public void Negative_Int16_Fields_Survive_RoundTrip()
    {
        var state = new BaseState { Position = -10000, Torque = -10000 };
        var parsed = BaseState.Parse(state.ToBytes());
        Assert.Equal(-10000, parsed.Position);
        Assert.Equal(-10000, parsed.Torque);
    }

    [Fact]
    public void BusVoltage_Above_Int16Max_Survives_RoundTrip()
    {
        // Guards the u16 signedness path: values > 32767 must not wrap negative.
        var state = new BaseState { BusVoltageMv = 65535 };
        var parsed = BaseState.Parse(state.ToBytes());
        Assert.Equal((ushort)65535, parsed.BusVoltageMv);
    }

    [Fact]
    public void RoundTrip_PreservaOsCamposDoBrakeChopper()
    {
        var original = new BaseState
        {
            BrakeEnergyMilliJ = 1_234_567u,
            BrakeActivations  = 4_242u,
            BrakePeakDeciW    = 2_880,
        };

        var voltou = BaseState.Parse(original.ToBytes());

        Assert.Equal(1_234_567u, voltou.BrakeEnergyMilliJ);
        Assert.Equal(4_242u, voltou.BrakeActivations);
        Assert.Equal((ushort)2_880, voltou.BrakePeakDeciW);
    }

    [Fact]
    public void RoundTrip_SuportaOsValoresMaximosDoBrakeChopper()
    {
        var original = new BaseState
        {
            BrakeEnergyMilliJ = uint.MaxValue,
            BrakeActivations  = uint.MaxValue,
            BrakePeakDeciW    = ushort.MaxValue,
        };

        var voltou = BaseState.Parse(original.ToBytes());

        Assert.Equal(uint.MaxValue, voltou.BrakeEnergyMilliJ);
        Assert.Equal(uint.MaxValue, voltou.BrakeActivations);
        Assert.Equal(ushort.MaxValue, voltou.BrakePeakDeciW);
    }

    [Fact]
    public void RoundTrip_PreservaAsDuasParcelasDoClipping()
    {
        var original = new BaseState { Clipping = 60, ClippingGame = 51, ClippingBase = 9 };

        var voltou = BaseState.Parse(original.ToBytes());

        Assert.Equal((byte)51, voltou.ClippingGame);
        Assert.Equal((byte)9, voltou.ClippingBase);
        Assert.Equal(20, voltou.ClippingGamePercent);   // 51/255
        Assert.Equal(4, voltou.ClippingBasePercent);    // 9/255
    }

    [Fact]
    public void RoundTrip_PreservaAsParcelasDoPicoDaSessao()
    {
        var original = new BaseState
        {
            ClippingPeak = 59, ClippingPeakGame = 51, ClippingPeakBase = 8,
        };

        var voltou = BaseState.Parse(original.ToBytes());

        Assert.Equal((byte)51, voltou.ClippingPeakGame);
        Assert.Equal((byte)8, voltou.ClippingPeakBase);
        Assert.Equal(20, voltou.ClippingPeakGamePercent);
        Assert.Equal(3, voltou.ClippingPeakBasePercent);
    }

    // As duas parcelas nasceram DEPOIS do resto do report. Uma placa com firmware anterior nao
    // escreve os bytes 35-36, e o app precisa ler "sem clipping" em vez de estourar o indice — senao
    // atualizar o Studio sem atualizar a base derrubaria a telemetria inteira.
    [Fact]
    public void Parse_DeFirmwareAntigo_SemOsBytesDoClippingSeparado_NaoQuebra()
    {
        var completo = new BaseState { Clipping = 60, ClippingGame = 51, ClippingBase = 9 }.ToBytes();
        var antigo = completo.AsSpan(0, 35);   // report que termina onde terminava antes

        var voltou = BaseState.Parse(antigo);

        Assert.Equal((byte)60, voltou.Clipping);       // o total continua chegando
        Assert.Equal((byte)0, voltou.ClippingGame);
        Assert.Equal((byte)0, voltou.ClippingBase);
    }

    [Fact]
    public void RoundTrip_PreservaOsPicosDeCorrente()
    {
        var original = new BaseState
        {
            CurrentPeakPosMa = 12_500,
            CurrentPeakNegMa = -9_800,
            ClippingPeak = 200,
        };

        var voltou = BaseState.Parse(original.ToBytes());

        Assert.Equal((short)12_500, voltou.CurrentPeakPosMa);
        Assert.Equal((short)-9_800, voltou.CurrentPeakNegMa);
        Assert.Equal((byte)200, voltou.ClippingPeak);
    }

    [Fact]
    public void RoundTrip_SuportaOsLimitesDosPicosDeCorrente()
    {
        var original = new BaseState
        {
            CurrentPeakPosMa = short.MaxValue,
            CurrentPeakNegMa = short.MinValue,
        };

        var voltou = BaseState.Parse(original.ToBytes());

        Assert.Equal(short.MaxValue, voltou.CurrentPeakPosMa);
        Assert.Equal(short.MinValue, voltou.CurrentPeakNegMa);
    }


}
