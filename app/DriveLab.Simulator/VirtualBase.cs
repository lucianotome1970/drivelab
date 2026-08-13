// ============================================================================
//  DriveLab
//  VirtualBase.cs — Modelo físico simplificado do volante (mola, damper, inércia) usado pelo simulador.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Simulator;

public sealed class VirtualBase
{
    private const double Inertia = 0.05;      // kg·m² virtual
    private const double SpringStiffness = 4;  // ganho de centralização
    private const double DamperCoeff = 2;      // ganho de damping

    private double _constant;
    private double _spring;
    private double _periodic;
    private double _damper;
    private double _forceDrop01;
    private double _lastTorque;

    public double MotionRangeDeg { get; set; } = 900;
    public double SpringGain { get; set; } = 1.0;
    public double DamperGain { get; set; } = 1.0;
    public double TotalStrength01 { get; set; } = 1.0;

    /// <summary>
    /// A curva de resposta da força: cinco pontos (saída em %) para as entradas fixas
    /// 0/25/50/75/100%. Padrão = a reta, então o simulador se comporta como sempre até alguém
    /// moldar a curva.
    ///
    /// <para>Existe aqui para o gráfico deixar de ser só um desenho: arrastar um ponto passa a
    /// mudar o volante da tela na hora. Sem isto a pessoa só descobre o efeito na pista, que é
    /// justamente o que fez a curva achatada passar uma sessão inteira despercebida.</para>
    /// </summary>
    public double[] ForceCurve { get; set; } = Linear;

    /// <summary>A curva neutra: sai o mesmo que entra.</summary>
    public static double[] Linear => Enumerable.Range(0, Pontos).Select(i => (double)(i * 10)).ToArray();

    /// <summary>Quantos pontos a curva tem — 11, de 10 em 10% da força pedida.</summary>
    public const int Pontos = 11;

    /// <summary>Os ids dos pontos, em ORDEM. Lista explícita porque eles NÃO são contíguos: os cinco
    /// primeiros ficaram onde a curva de cinco pontos morava (28-32) e o resto entrou em 49-54.</summary>
    public static readonly DriveLab.Core.Settings.BaseSettingId[] IdsDaCurva =
    {
        DriveLab.Core.Settings.BaseSettingId.FfbCurve0, DriveLab.Core.Settings.BaseSettingId.FfbCurve1,
        DriveLab.Core.Settings.BaseSettingId.FfbCurve2, DriveLab.Core.Settings.BaseSettingId.FfbCurve3,
        DriveLab.Core.Settings.BaseSettingId.FfbCurve4, DriveLab.Core.Settings.BaseSettingId.FfbCurve5,
        DriveLab.Core.Settings.BaseSettingId.FfbCurve6, DriveLab.Core.Settings.BaseSettingId.FfbCurve7,
        DriveLab.Core.Settings.BaseSettingId.FfbCurve8, DriveLab.Core.Settings.BaseSettingId.FfbCurve9,
        DriveLab.Core.Settings.BaseSettingId.FfbCurve10,
    };

    /// <summary>
    /// Motor habilitado. Quando desligado, o volante não recebe torque nenhum
    /// (fica parado no ângulo atual) — espelha o "força habilitada" do dispositivo.
    /// </summary>
    public bool ForceEnabled { get; set; } = true;

    public double AngleRad { get; private set; }
    public double VelocityRad { get; private set; }

    public void SetInputs(double constant, double spring, double periodic, double damper, double forceDrop01)
    {
        _constant = constant;
        _spring = spring;
        _periodic = periodic;
        _damper = damper;
        _forceDrop01 = Math.Clamp(forceDrop01, 0, 1);
    }

    public void ResetCenter()
    {
        AngleRad = 0;
        VelocityRad = 0;
    }

    public void Step(double dt)
    {
        if (!ForceEnabled)
        {
            // Motor desligado: sem torque, sem velocidade. Segura o ângulo atual.
            VelocityRad = 0;
            _lastTorque = 0;
            return;
        }

        var halfRangeRad = HalfRangeRad();
        var position = halfRangeRad > 0 ? AngleRad / halfRangeRad : 0; // −1..+1

        var springTorque = -position * _spring * SpringGain * SpringStiffness;
        var damperTorque = -VelocityRad * _damper * DamperGain * DamperCoeff;
        var netTorque = (_constant + _periodic) + springTorque + damperTorque;

        // A CURVA entra aqui, antes da força total — a mesma ordem do firmware (ffb_math.h): ela
        // molda a força pedida; a força total é o volume geral que vem depois. Trocar a ordem faria
        // o simulador mostrar um efeito que a base não produz.
        netTorque = AplicarCurva(netTorque);

        netTorque *= TotalStrength01 * (1.0 - _forceDrop01);
        _lastTorque = netTorque;

        var accel = netTorque / Inertia;
        VelocityRad += accel * dt;
        AngleRad += VelocityRad * dt;

        if (AngleRad > halfRangeRad)
        {
            AngleRad = halfRangeRad;
            if (VelocityRad > 0) VelocityRad = 0;
        }
        else if (AngleRad < -halfRangeRad)
        {
            AngleRad = -halfRangeRad;
            if (VelocityRad < 0) VelocityRad = 0;
        }
    }

    public short PositionNormalized => ToInt16Normalized(NormalizedPosition());

    public short AngleDeciDeg => (short)Math.Clamp(AngleRad * 180.0 / Math.PI * 10.0, short.MinValue, short.MaxValue);


    /// <summary>Aplica a curva ao MÓDULO da força, preservando o sinal — o FFB é bidirecional e
    /// simétrico. Segmentos lineares entre os cinco pontos, igual ao firmware (applyForceCurve).</summary>
    private double AplicarCurva(double torque)
    {
        var c = ForceCurve;
        if (c is null || c.Length != Pontos) return torque;
        // Curva neutra: pula a conta inteira, como o firmware faz.
        var neutra = true;
        for (var k = 0; k < Pontos; k++) if (c[k] != k * 10) { neutra = false; break; }
        if (neutra) return torque;

        // Hermite cubico com as inclinacoes de Fritsch-Carlson — a MESMA conta do firmware
        // (ffb_math.h) e do grafico. Se o simulador interpolasse diferente, a pessoa ajustaria
        // olhando um comportamento e sentiria outro na pista.
        const double h = 1.0 / (Pontos - 1);
        var d = new double[Pontos - 1];
        for (var k = 0; k < Pontos - 1; k++) d[k] = (c[k + 1] - c[k]) / h;
        var m = new double[Pontos];
        m[0] = d[0];
        m[Pontos - 1] = d[Pontos - 2];
        for (var k = 1; k < Pontos - 1; k++)
            m[k] = d[k - 1] * d[k] <= 0 ? 0 : 2 * d[k - 1] * d[k] / (d[k - 1] + d[k]);

        var a = Math.Min(Math.Abs(torque), 1.0);
        var x = a * (Pontos - 1);
        var i = (int)x;
        if (i > Pontos - 2) i = Pontos - 2;
        var t = x - i;
        double t2 = t * t, t3 = t2 * t;
        var y = ((2 * t3 - 3 * t2 + 1) * c[i] + (t3 - 2 * t2 + t) * h * m[i] +
                 (-2 * t3 + 3 * t2) * c[i + 1] + (t3 - t2) * h * m[i + 1]) / 100.0;
        return torque < 0 ? -y : y;
    }

    public short TorqueNormalized => ToInt16Normalized(Math.Clamp(_lastTorque, -1, 1));

    private double HalfRangeRad() => MotionRangeDeg / 2.0 * Math.PI / 180.0;

    private double NormalizedPosition()
    {
        var halfRangeRad = HalfRangeRad();
        return halfRangeRad > 0 ? Math.Clamp(AngleRad / halfRangeRad, -1, 1) : 0;
    }

    private static short ToInt16Normalized(double value) =>
        (short)Math.Round(Math.Clamp(value, -1, 1) * 10000);
}
