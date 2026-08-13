// ============================================================================
//  DriveLab
//  BaseSettingId.cs — IDs dos settings configuráveis do volante (force feedback, encoder, corrente, etc.).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public enum BaseSettingId : byte
{
    MotionRange = 0,
    SoftStopRange = 1,
    SoftStopStrength = 2,
    TotalStrength = 3,
    SpringStrength = 4,
    DamperStrength = 5,
    StaticDamping = 6,
    MaxTorqueLimit = 7,
    ForceDirection = 8,
    EncoderDirection = 9,
    EncoderCpr = 10,
    PolePairs = 11,
    // 12 e 13 eram CurrentP e CurrentI. Saíram: o ODrive não aceita ganhos P/I, ele os DERIVA do
    // motor e da banda — ver CurrentBandwidthHz. Os ids ficam vagos de propósito, para não
    // reinterpretarem valores já salvos em placas.
    CalibrationCurrent = 14,
    PositionSmoothing = 15,
    PowerLimit = 16,
    BrakingLimit = 17,
    EncoderType = 18,
    ReconstructionSteps = 19,
    ReconstructionLpf = 20,
    OutputFilterHz = 21,
    OscGuardEnable = 22,
    EndstopDamping = 23,
    Linearity = 24,
    CoggingEnable = 25,
    SlewRate = 26,
    /// <summary>OBSOLETO (2026-08-06) — tirado da UI: declarar a tensão da fonte era pedir o que a base
    /// já mede, e o firmware sempre ignorou o valor. O ID fica RESERVADO, nunca reciclado: bases em campo
    /// têm esse byte no blob salvo na NVM, e reaproveitar o número faria uma base antiga ler o valor
    /// errado no lugar do novo setting.</summary>
    BusNominalV = 27,
    FfbCurve0 = 28,
    FfbCurve1 = 29,
    FfbCurve2 = 30,
    FfbCurve3 = 31,
    FfbCurve4 = 32,
    BoardVariant = 33,
    TorqueConstant = 34,
    ThermalContinuousPct = 35,
    ThermalPeakSeconds = 36,
    FetTempLimitC = 37,
    MotorTempLimitC = 38,
    SpringGain = 39,
    DamperGain = 40,
    FrictionGain = 41,
    InertiaGain = 42,
    SoftPowerEnable = 43,
    PowerButtonEnable = 44,

    /// <summary>Trava de bring-up: a base sobe DESARMADA e só responde a comando com isto ligado.
    /// Firmware novo não sabe qual motor/encoder está ligado — armar no boot com dados errados
    /// esquenta o motor ou o faz disparar. Desligar desarma na hora (parada de emergência).</summary>
    MotorEnable = 45,

    /// <summary>Como o encoder está ligado: 0=ABZ, 1=SSI, 2=SPI. O MODELO do sensor vem em
    /// <see cref="EncoderType"/>; este campo diz por qual interface ele foi ligado. São duas
    /// perguntas diferentes: o modelo é o que a pessoa comprou, a tecnologia é como ela fiou.
    /// Ficou em 46, e não em 45, porque a trava de bring-up já ocupava o 45 na NVM das bases
    /// gravadas em 09/08 — renumerar a trava apagaria os ajustes salvos dessas placas.</summary>
    EncoderInterface = 46,

    /// <summary>Limite de corrente do MOTOR, em ampères. Descreve o motor de cada um — estava
    /// cravado em 25 A, o valor desta bancada. Lido no boot; é também o teto da corrente de
    /// calibração. (47 é o build_id, interno do firmware.)</summary>
    CurrentLim = 48,

    /// <summary>Pontos 5 a 10 da CURVA de resposta da força — as entradas 50, 60, 70, 80, 90 e 100%.
    ///
    /// <para>A curva tinha cinco pontos, de 25 em 25%. O trecho onde se dirige a maior parte de uma
    /// volta (25% a 75%) tinha UM único ponto de controle, então não dava para levantar o começo do
    /// meio sem levantar o fim junto. Com 10 em 10% são cinco pontos ali.</para>
    ///
    /// <para>Os cinco primeiros (entradas 0 a 40%) continuam nos ids 28-32, que já existiam.</para></summary>
    FfbCurve5 = 49,
    FfbCurve6 = 50,
    FfbCurve7 = 51,
    FfbCurve8 = 52,
    FfbCurve9 = 53,
    FfbCurve10 = 54,

    /// <summary>Banda da malha de corrente, em Hz.
    ///
    /// <para>Substitui os antigos CurrentP (12) e CurrentI (13), que eram órfãos por um motivo de
    /// CONCEITO e não de implementação: o ODrive não aceita ganhos P e I — ele os DERIVA do motor.
    /// <c>p_gain = banda × indutância</c> e <c>i_gain = (resistência / indutância) × p_gain</c>.
    /// Escrever os ganhos direto seria sobrescrito na próxima vez que ele recalculasse.</para>
    ///
    /// <para>A banda é o parâmetro que ele realmente aceita, e é um número com significado físico:
    /// quão rápido a malha persegue a corrente pedida. Ficava cravada em 200 Hz no bring-up.</para>
    ///
    /// <para>Id novo, e não reaproveitado: uma placa com CurrentP = 0,05 salvo passaria a ler
    /// "banda de 0,05 Hz" — um valor absurdo que entraria em silêncio.</para></summary>
    CurrentBandwidthHz = 55,
}
