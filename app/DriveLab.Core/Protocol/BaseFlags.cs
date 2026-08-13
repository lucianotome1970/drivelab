// ============================================================================
//  DriveLab
//  BaseFlags.cs — Bits de flags de estado do dispositivo (força habilitada, calibrado, erro, simulador).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

[Flags]
public enum BaseFlags : byte
{
    None = 0,
    ForceEnabled = 1,
    Calibrated = 2,
    Error = 4,
    UsingSimulator = 8,

    /// <summary>Aviso (não fatal): a tensão do barramento lida não bate com a variante/nominal
    /// selecionada — provável 24V/56V escolhido errado no app. Ver busVoltageImplausible no firmware.</summary>
    VoltageImplausible = 16,

    /// <summary>Há uma tabela de cogging válida gravada na flash do dispositivo e ativa no engine (a
    /// compensação de ripple está funcionando). A aba Hardware mostra "tabela presente". Ver
    /// kFlagCoggingLoaded no firmware.</summary>
    CoggingLoaded = 32,

    /// <summary>🔴 GRAVE: a guarda de coerência do ângulo elétrico disparou e o motor foi DESARMADO.
    /// Significa que havia corrente alta com o firmware sem pedir torque e o volante parado — ou seja,
    /// a corrente não estava virando torque, estava virando calor. Causa típica: a calibração concluiu
    /// mas gravou um ângulo errado (fases religadas em outra ordem, encoder remontado). Medido na
    /// bancada: 18 A parado, ~98 W no motor, e giro descontrolado. O motor NÃO re-arma sozinho — é
    /// preciso um power-cycle, que dispara uma calibração nova. Ver g_guard_trip no firmware.</summary>
    AngleGuardTripped = 64,
}
