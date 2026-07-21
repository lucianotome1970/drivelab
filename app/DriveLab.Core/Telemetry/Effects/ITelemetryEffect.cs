// ============================================================================
//  DriveLab
//  ITelemetryEffect.cs — Contrato de um efeito de FFB sintetizado a partir da telemetria (rumble, ABS, slip…).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Telemetry.Effects;

/// <summary>
/// Um efeito de FFB gerado a partir da telemetria do jogo (estilo SimHub ShakeIt): dado o quadro atual e um
/// instante (segundos), devolve uma contribuição de força em −1..1 (antes do <see cref="Gain"/>). O mixer soma
/// os efeitos ligados. Alguns são com estado (ex.: troca de marcha detecta a borda); mesmo assim o cálculo é
/// determinístico dado (telemetria, tempo), então roda idêntico no host (testes) e no app.
/// </summary>
public interface ITelemetryEffect
{
    /// <summary>Nome curto do efeito (para UI/telemetria).</summary>
    string Name { get; }

    /// <summary>Liga/desliga o efeito.</summary>
    bool Enabled { get; set; }

    /// <summary>Escala do usuário (0..1) aplicada pelo mixer sobre a saída.</summary>
    float Gain { get; set; }

    /// <summary>Contribuição de força em −1..1 (antes do Gain). 0 quando o efeito não se aplica.</summary>
    float Compute(GameTelemetry telemetry, double timeSeconds);
}
