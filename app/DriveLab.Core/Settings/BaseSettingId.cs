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
    CurrentP = 12,
    CurrentI = 13,
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

    /// <summary>Como o encoder está ligado: 0=ABZ, 1=SSI, 2=SPI. O MODELO do sensor vem em
    /// <see cref="EncoderType"/>; este campo diz por qual interface ele foi ligado. São duas
    /// perguntas diferentes: o modelo é o que a pessoa comprou, a tecnologia é como ela fiou.</summary>
    EncoderInterface = 45,
}
