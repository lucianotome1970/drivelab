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
        new(BaseSettingId.SoftStopRange, "soft_stop_range", "Range do batente", SettingType.UInt8, 0, 30, "°", SettingTab.Basic, 8,
            "Onde o batente por software começa a agir, antes do fim do curso. Dá um aviso progressivo em vez de uma parede."),
        new(BaseSettingId.SoftStopStrength, "soft_stop_strength", "Força do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 70,
            "Quanta força o batente por software aplica. Alto demais dá um tranco no fim do curso."),
        new(BaseSettingId.TotalStrength, "total_strength", "Força total", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 100,
            "O volume geral do force feedback. É este que você ajusta no dia a dia."),
        new(BaseSettingId.SpringStrength, "spring_strength", "Mola do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 5,
            "Força que puxa o volante de volta ao centro. Nos simuladores modernos o jogo já faz isso — subir aqui costuma mascarar o detalhe da pista."),
        new(BaseSettingId.DamperStrength, "damper_strength", "Damper do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 0,
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
        new(BaseSettingId.CurrentBandwidthHz, "current_bandwidth_hz", "Banda da malha de corrente", SettingType.UInt16, 50, 2000, "Hz", SettingTab.Hardware, 200,
            "Quão rápido a malha de corrente persegue o valor pedido. Os ganhos saem daqui e do R/L do motor — não se ajustam à mão. Ex.: 200 Hz nesta bancada; alto demais faz o motor assobiar e vibrar, baixo demais deixa a força mole e atrasada. Só vale depois de reiniciar a base."),
        // Numérico, e não slider: é o valor impresso no resistor que a pessoa comprou (2, 5, 12 Ω…),
        // não algo que se busca arrastando. Faixa 0,5-50 Ω cobre do resistor de potência baixo às
        // montagens de 12 Ω; abaixo de 0,5 Ω a corrente de frenagem passaria do que a placa aguenta.
        new(BaseSettingId.BrakeResistanceOhm, "brake_resistance_ohm", "Resistor de freio", SettingType.Float, 0.5, 50, "Ω", SettingTab.Hardware, 2.0,
            "A resistência do resistor de freio que VOCÊ montou — o valor impresso nele. A placa não mede: ela acredita neste número para calcular a corrente e a potência que passam pelo resistor. Ex.: o resistor desta bancada é de 2 Ω; há montagens com 12 Ω. Declarar 2 tendo montado 12 erra essa conta por seis vezes, e é ela que decide quando cortar por aquecimento. Só vale depois de reiniciar a base.",
            Numeric: true),
        new(BaseSettingId.CalibrationCurrent, "calibration_current", "Corrente de calibração", SettingType.UInt8, 1, 30, "A", SettingTab.Hardware, 5,
            "Corrente usada na rotina de calibração. Alta demais faz o motor se jogar entre posições e a calibração falhar; baixa demais não vence o encaixe dos ímãs do hoverboard."),
        new(BaseSettingId.PositionSmoothing, "position_smoothing", "Suavização de posição", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0,
            "Suavização da leitura de posição. Ajuda com encoder ruidoso, mas atrasa a resposta — use o mínimo que resolver."),
        new(BaseSettingId.PowerLimit, "power_limit", "Limite de potência", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100,
            "Teto de potência que a base pode puxar da fonte. Serve para não afundar uma fonte pequena: se a tensão cai nas curvas fortes e o FFB some, baixe isto."),
        new(BaseSettingId.BrakingLimit, "braking_limit", "Limite de frenagem", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100,
            "Quanta energia a base pode devolver à fonte ao frear. Muito alto sem resistor de freio faz a tensão do barramento subir e a placa cortar no meio da curva."),
        new(BaseSettingId.EncoderType, "encoder_type", "Modelo do encoder", SettingType.UInt8, 0, 4, "", SettingTab.Hardware, EncoderCatalog.E6b2,
            "O sensor de posição instalado no motor. Escolha o modelo que você comprou — é o nome que diz por quais interfaces ele pode ser ligado e qual resolução vem do próprio chip."),
        new(BaseSettingId.ReconstructionSteps, "reconstruction_steps", "Reconstrução (passos, 0=auto)", SettingType.UInt8, 0, 32, "", SettingTab.Advanced, 0),
        new(BaseSettingId.ReconstructionLpf, "reconstruction_lpf", "Reconstrução (suavização)", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.OutputFilterHz, "output_filter_hz", "Filtro de saída (corte)", SettingType.UInt16, 0, 2000, "Hz", SettingTab.Advanced, 0,
            "Filtro de saída da força. Quanto mais baixo, mais suave e mais lento; filtro demais é a causa mais comum de FFB 'sem detalhe'."),
        new(BaseSettingId.OscGuardEnable, "osc_guard_enable", "Anti-oscilação", SettingType.UInt8, 0, 1, "", SettingTab.Advanced, 0,
            "Proteção contra oscilação: corta o ciclo quando a base começa a tremer sozinha. Ligue se o volante vibrar em linha reta."),
        new(BaseSettingId.EndstopDamping, "endstop_damping", "Amortecimento do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 35,
            "Amortecimento aplicado dentro do batente, para ele parar sem repicar."),
        // 100 = LINEAR, e é o padrão desde 2026-08-11 (era 159). Ver a nota longa em a0_load_defaults
        // no firmware: acima de 100 as forças MÉDIAS são achatadas, que é onde se dirige.
        new(BaseSettingId.Linearity, "linearity", "Linearidade da resposta", SettingType.UInt8, 50, 200, "%", SettingTab.Advanced, 100,
            "Curva de resposta da força. 100% é linear: o jogo pede metade da força e chega metade. Abaixo de 100% amplifica as forças pequenas — é o ajuste que faz zebra e perda de aderência aparecerem. Acima de 100% acontece o contrário: a 159%, o jogo pedir 50% entrega só 33%, e a base parece fraca no meio da volta mesmo com o pico inteiro."),
        new(BaseSettingId.CoggingEnable, "cogging_enable", "Compensação de cogging", SettingType.UInt8, 0, 1, "", SettingTab.Advanced, 0),
        new(BaseSettingId.SlewRate, "slew_rate", "Limite de variação (slew)", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0,
            "Limite de quão rápido a força pode variar. Protege contra degraus bruscos vindos do jogo."),
        // OBSOLETO: fora da UI desde 2026-08-06 (ver BaseSettingId.BusNominalV). Fica no schema porque o
        // descritor define o layout do blob de settings — removê-lo mudaria o formato gravado na NVM.
        new(BaseSettingId.BusNominalV, "bus_nominal_v", "Tensão da fonte (nominal)", SettingType.UInt8, 12, 56, "V", SettingTab.Hardware, 56),
        // Os 11 pontos da curva de resposta, de 10 em 10% da força pedida. Nao aparecem como
        // sliders: quem os edita e o grafico da aba Avancado (ForceCurveView).
        new(BaseSettingId.FfbCurve0, "ffb_curve_0", "Curva de força — 0%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.FfbCurve1, "ffb_curve_1", "Curva de força — 10%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 10),
        new(BaseSettingId.FfbCurve2, "ffb_curve_2", "Curva de força — 20%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 20),
        new(BaseSettingId.FfbCurve3, "ffb_curve_3", "Curva de força — 30%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 30),
        new(BaseSettingId.FfbCurve4, "ffb_curve_4", "Curva de força — 40%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 40),
        new(BaseSettingId.FfbCurve5, "ffb_curve_5", "Curva de força — 50%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 50),
        new(BaseSettingId.FfbCurve6, "ffb_curve_6", "Curva de força — 60%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 60),
        new(BaseSettingId.FfbCurve7, "ffb_curve_7", "Curva de força — 70%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 70),
        new(BaseSettingId.FfbCurve8, "ffb_curve_8", "Curva de força — 80%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 80),
        new(BaseSettingId.FfbCurve9, "ffb_curve_9", "Curva de força — 90%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 90),
        new(BaseSettingId.FfbCurve10, "ffb_curve_10", "Curva de força — 100%", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100),
        new(BaseSettingId.BoardVariant, "board_variant", "Variante da placa", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 1,
            "Confira o adesivo QC PASS da placa (24V ou 56V) ou a cor do LED: roxo é 24 V, verde é 56 V. Os capacitores são iguais nas duas e não servem para identificar."),
        // Padrão 0,55 Nm/A: é o valor de catálogo do motor de hoverboard, e era exatamente o que o
        // firmware cravava antes deste campo ser lido. Vir preenchido com ele deixa a base saindo de
        // fábrica com o comportamento conhecido, em vez de um zero que parece campo esquecido.
        // Zero continua sendo aceito e significa "usa o padrão do firmware" — dá no mesmo hoje.
        new(BaseSettingId.TorqueConstant, "torque_constant", "Constante de torque (Kt)", SettingType.Float, 0, 2, "Nm/A", SettingTab.Hardware, 0.55,
            "Quantos Nm o motor entrega por ampere. É o câmbio entre a força pedida e a corrente enviada: pedir 12 Nm com 0,55 manda 21,8 A. O padrão 0,55 é de CATÁLOGO, não medido — se o Kt real do seu motor for menor, chega menos força do que a tela mostra, sem nenhum erro aparecer. Só vale ao reiniciar a base.", Numeric: true),
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
        new(BaseSettingId.MotorEnable, "motor_enable", "Ativar motor", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 0,
            "Trava de bring-up: a base sobe com o motor desligado e só aplica força depois que você liga aqui. Desligar desarma na hora, e serve como parada de emergência."),
        new(BaseSettingId.EncoderInterface, "encoder_interface", "Tecnologia do encoder", SettingType.UInt8, 0, 2, "", SettingTab.Hardware, 0,
            "Como você ligou o sensor. ABZ usa os fios A, B e Z no conector ABZ; SSI e SPI usam o conector SPI da placa. Só aparecem as opções que o SEU sensor oferece."),
        new(BaseSettingId.CurrentLim, "current_lim", "Limite de corrente do motor", SettingType.UInt8, 5, 40, "A", SettingTab.Hardware, 25,
            "Corrente máxima que chega às bobinas. Descreve o SEU motor: acima do que ele aguenta vira calor, abaixo é torque desperdiçado. Também limita a corrente de calibração. Vale só no próximo boot."),
    };

    private static readonly Dictionary<byte, SettingDescriptor> ById =
        All.ToDictionary(d => (byte)d.Id);

    public static SettingDescriptor Get(BaseSettingId id) => ById[(byte)id];

    public static bool TryGet(byte fieldId, out SettingDescriptor descriptor) =>
        ById.TryGetValue(fieldId, out descriptor!);
}
