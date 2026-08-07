// ============================================================================
//  DriveLab
//  BaseSettingsSchema.cs — Schema (descritores) dos settings do volante: chave, faixa, unidade, aba e valor default.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public static class BaseSettingsSchema
{
    public static IReadOnlyList<SettingDescriptor> All { get; } = new List<SettingDescriptor>
    {
        new(BaseSettingId.MotionRange, "motion_range", "Ângulo total de giro", SettingType.UInt16, 90, 2000, "°", SettingTab.Basic, 900,
            "Quantos graus o volante gira de um batente ao outro. Cada categoria pede um valor: fórmula perto de 400°, GT perto de 900°."),
        new(BaseSettingId.SoftStopRange, "soft_stop_range", "Range do batente", SettingType.UInt8, 0, 30, "°", SettingTab.Basic, 5,
            "Onde o batente por software começa a agir, antes do fim do curso. Dá um aviso progressivo em vez de uma parede."),
        new(BaseSettingId.SoftStopStrength, "soft_stop_strength", "Força do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 80,
            "Quanta força o batente por software aplica. Alto demais dá um tranco no fim do curso."),
        new(BaseSettingId.TotalStrength, "total_strength", "Força total", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 100,
            "O volume geral do force feedback. É este que você ajusta no dia a dia."),
        new(BaseSettingId.SpringStrength, "spring_strength", "Mola do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 0,
            "Força que puxa o volante de volta ao centro. Nos simuladores modernos o jogo já faz isso — subir aqui costuma mascarar o detalhe da pista."),
        new(BaseSettingId.DamperStrength, "damper_strength", "Damper do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 10,
            "Resistência ao movimento, como se o volante estivesse dentro de um óleo. Suaviza, mas em excesso engole a informação fina."),
        new(BaseSettingId.StaticDamping, "static_damping", "Damping estático", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 5,
            "Atrito constante, presente mesmo parado. Um pouco tira a sensação de solto; muito deixa a direção pesada e sem detalhe."),
        new(BaseSettingId.MaxTorqueLimit, "max_torque_limit", "Limite de torque", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 80,
            "Teto de torque, em Nm. É o limite de segurança do conjunto, não o volume do FFB — para ajustar o quanto você sente, use a força total."),
        new(BaseSettingId.ForceDirection, "force_direction", "Direção da força", SettingType.Int8, -1, 1, "", SettingTab.Advanced, 1,
            "Inverte o sentido da força. Se o volante puxa para o lado errado ao sair de curva, é aqui."),
        new(BaseSettingId.EncoderDirection, "encoder_direction", "Direção do encoder", SettingType.Int8, -1, 1, "", SettingTab.Hardware, 1,
            "Inverte o sentido de contagem do encoder. Se o volante gira para um lado e a tela mostra o outro, é isto."),
        new(BaseSettingId.EncoderCpr, "encoder_cpr", "CPR do encoder", SettingType.UInt32, 100, 2097152, "contagens", SettingTab.Hardware, 4000,
            "Digite o número impresso no encoder, sem fazer conta. Nos incrementais ele vem no código do modelo, antes do P/R (em E6B2-CWZ6C 1000P/R, é 1000); nos magnéticos já vem preenchido ao escolher o modelo. Em ABZ o app multiplica por 4 sozinho. Errar aqui faz a amplitude de giro e os batentes ficarem no lugar errado.", Numeric: true),
        new(BaseSettingId.PolePairs, "pole_pairs", "Pares de polos", SettingType.UInt8, 1, 50, "", SettingTab.Hardware, 15,
            "Quantos pares de ímã existem dentro do motor. A maioria dos motores de hoverboard tem 15. Errar faz o motor esquentar entregando pouca força, porque a corrente entra na bobina errada."),
        new(BaseSettingId.CurrentP, "current_p", "Ganho P (corrente)", SettingType.Float, 0, 10, "", SettingTab.Hardware, 0.05,
            "Ganho proporcional da malha de corrente. Mexer sem medir costuma piorar: alto demais faz o motor assobiar e vibrar."),
        new(BaseSettingId.CurrentI, "current_i", "Ganho I (corrente)", SettingType.Float, 0, 1000, "", SettingTab.Hardware, 10,
            "Ganho integral da malha de corrente. Anda junto com o ganho P — os dois vêm do R e do L do motor, não de tentativa e erro."),
        new(BaseSettingId.CalibrationCurrent, "calibration_current", "Corrente de calibração", SettingType.UInt8, 0, 30, "A", SettingTab.Hardware, 3,
            "Corrente usada na rotina de calibração. Alta demais faz o motor se jogar entre posições e a calibração falhar; baixa demais não vence o encaixe dos ímãs do hoverboard."),
        new(BaseSettingId.PositionSmoothing, "position_smoothing", "Suavização de posição", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0,
            "Suavização da leitura de posição. Ajuda com encoder ruidoso, mas atrasa a resposta — use o mínimo que resolver."),
        new(BaseSettingId.PowerLimit, "power_limit", "Limite de potência", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100,
            "Teto de potência que a base pode puxar da fonte. Serve para não afundar uma fonte pequena: se a tensão cai nas curvas fortes e o FFB some, baixe isto."),
        new(BaseSettingId.BrakingLimit, "braking_limit", "Limite de frenagem", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100,
            "Quanta energia a base pode devolver à fonte ao frear. Muito alto sem resistor de freio faz a tensão do barramento subir e a placa cortar no meio da curva."),
        new(BaseSettingId.EncoderType, "encoder_type", "Modelo do encoder", SettingType.UInt8, 0, 4, "", SettingTab.Hardware, 0,
            "O sensor de posição instalado no motor. Escolha o modelo que você comprou; se ele não estiver na lista, use \"Incremental genérico\"."),
        new(BaseSettingId.ReconstructionSteps, "reconstruction_steps", "Reconstrução (passos, 0=auto)", SettingType.UInt8, 0, 32, "", SettingTab.Advanced, 0),
        new(BaseSettingId.ReconstructionLpf, "reconstruction_lpf", "Reconstrução (suavização)", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.OutputFilterHz, "output_filter_hz", "Filtro de saída (corte)", SettingType.UInt16, 0, 2000, "Hz", SettingTab.Advanced, 0,
            "Filtro de saída da força. Quanto mais baixo, mais suave e mais lento; filtro demais é a causa mais comum de FFB 'sem detalhe'."),
        new(BaseSettingId.OscGuardEnable, "osc_guard_enable", "Anti-oscilação", SettingType.UInt8, 0, 1, "", SettingTab.Advanced, 0,
            "Proteção contra oscilação: corta o ciclo quando a base começa a tremer sozinha. Ligue se o volante vibrar em linha reta."),
        new(BaseSettingId.EndstopDamping, "endstop_damping", "Amortecimento do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 25,
            "Amortecimento aplicado dentro do batente, para ele parar sem repicar."),
        new(BaseSettingId.Linearity, "linearity", "Linearidade da resposta", SettingType.UInt8, 50, 200, "%", SettingTab.Advanced, 100,
            "Curva de resposta da força. Abaixo de 100% amplifica as forças pequenas — é o ajuste que faz zebra e perda de aderência aparecerem."),
        new(BaseSettingId.CoggingEnable, "cogging_enable", "Compensação de cogging", SettingType.UInt8, 0, 1, "", SettingTab.Advanced, 0),
        new(BaseSettingId.SlewRate, "slew_rate", "Limite de variação (slew)", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0,
            "Limite de quão rápido a força pode variar. Protege contra degraus bruscos vindos do jogo."),
        // OBSOLETO: fora da UI desde 2026-08-06 (ver BaseSettingId.BusNominalV). Fica no schema porque o
        // descritor define o layout do blob de settings — removê-lo mudaria o formato gravado na NVM.
        new(BaseSettingId.BusNominalV, "bus_nominal_v", "Tensão da fonte (nominal)", SettingType.UInt8, 12, 56, "V", SettingTab.Hardware, 56),
        new(BaseSettingId.FfbCurve0, "ffb_curve_0", "Curva de força — 0%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.FfbCurve1, "ffb_curve_1", "Curva de força — 25%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 25),
        new(BaseSettingId.FfbCurve2, "ffb_curve_2", "Curva de força — 50%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 50),
        new(BaseSettingId.FfbCurve3, "ffb_curve_3", "Curva de força — 75%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 75),
        new(BaseSettingId.FfbCurve4, "ffb_curve_4", "Curva de força — 100%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100),
        new(BaseSettingId.BoardVariant, "board_variant", "Variante da placa", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 1,
            "Confira o adesivo QC PASS da placa (24V ou 56V) ou a cor do LED: roxo é 24 V, verde é 56 V. Os capacitores são iguais nas duas e não servem para identificar."),
        new(BaseSettingId.TorqueConstant, "torque_constant", "Constante de torque (Kt) — 0 = não medido", SettingType.Float, 0, 2, "Nm/A", SettingTab.Hardware, 0,
            "Quantos Nm o motor entrega por ampere. Serve só para o monitor estimar o torque — não muda a força. Zero significa 'não medido', e aí o torque estimado aparece como '—' em vez de mostrar um chute.", Numeric: true),
        new(BaseSettingId.ThermalContinuousPct, "thermal_continuous_pct", "Torque contínuo (% do pico)", SettingType.UInt8, 30, 100, "%", SettingTab.Hardware, 100,
            "Quanto da força máxima a base sustenta indefinidamente sem esquentar demais."),
        new(BaseSettingId.ThermalPeakSeconds, "thermal_peak_seconds", "Duração do pico", SettingType.UInt8, 0, 60, "s", SettingTab.Hardware, 0,
            "Por quantos segundos a base pode passar do limite contínuo antes de recuar."),
        new(BaseSettingId.FetTempLimitC, "fet_temp_limit_c", "Corte de temp. dos FETs", SettingType.UInt8, 50, 110, "°C", SettingTab.Hardware, 85,
            "Temperatura em que a placa reduz ou corta para se proteger. Não suba sem saber o limite dos transistores."),
        new(BaseSettingId.MotorTempLimitC, "motor_temp_limit_c", "Corte de temp. do motor", SettingType.UInt8, 60, 125, "°C", SettingTab.Hardware, 100,
            "Temperatura em que o motor é protegido. Exige sensor instalado — sem ele, a leitura aparece como '—'."),
        new(BaseSettingId.SpringGain, "spring_gain", "Ganho de Mola (jogo)", SettingType.UInt8, 0, 200, "%", SettingTab.Feel, 100),
        new(BaseSettingId.DamperGain, "damper_gain", "Ganho de Damper (jogo)", SettingType.UInt8, 0, 200, "%", SettingTab.Feel, 100),
        new(BaseSettingId.FrictionGain, "friction_gain", "Ganho de Friction (jogo)", SettingType.UInt8, 0, 200, "%", SettingTab.Feel, 100),
        new(BaseSettingId.InertiaGain, "inertia_gain", "Ganho de Inertia (jogo)", SettingType.UInt8, 0, 200, "%", SettingTab.Feel, 100),
        new(BaseSettingId.SoftPowerEnable, "soft_power_enable", "Soft-power/contator (0=como hoje · 1=contator ativo)", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 0,
            "Liga o contator que isola as fases do motor com a base desligada. Protege contra a tensão que o motor gera ao ser girado sem energia."),
        new(BaseSettingId.PowerButtonEnable, "power_button_enable", "Soft-power por botão (0=como hoje · 1=botão tap-liga/segura-desliga)", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 0,
            "Liga o botão de energia: toque liga, segurar desliga. Precisa do hardware do contator instalado."),
        new(BaseSettingId.EncoderInterface, "encoder_interface", "Tecnologia do encoder", SettingType.UInt8, 0, 2, "", SettingTab.Hardware, 0,
            "Como você ligou o sensor. ABZ usa os fios A, B e Z no conector ABZ; SSI e SPI usam o conector SPI da placa. Só aparecem as opções que o SEU sensor oferece."),
    };

    private static readonly Dictionary<byte, SettingDescriptor> ById =
        All.ToDictionary(d => (byte)d.Id);

    public static SettingDescriptor Get(BaseSettingId id) => ById[(byte)id];

    public static bool TryGet(byte fieldId, out SettingDescriptor descriptor) =>
        ById.TryGetValue(fieldId, out descriptor!);
}
