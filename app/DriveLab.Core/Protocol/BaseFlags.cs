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

    /// <summary>A guarda de CURSO EXCEDIDO agiu: o volante apareceu muito além do fim do curso (45°
    /// além), onde nenhum giro humano o leva. Ela freia primeiro, com amortecimento e sem parede; se
    /// o eixo não obedecer, desarma.
    ///
    /// <para>Diferente da <see cref="AngleGuardTripped"/>, esta age por POSIÇÃO — a pergunta mais
    /// simples e mais verdadeira que a base tem sobre si mesma. As outras duas guardas olham
    /// velocidade e corrente, e ambas são cegas para o disparo LENTO: um torque parasita fraco gira
    /// meia volta por segundo para sempre sem cruzar limiar nenhum.</para>
    ///
    /// <para>Se o ajuste estiver em "travar" (padrão), a base fica desarmada até reiniciar. Em
    /// "re-armar", ela volta sozinha quando o volante estiver de novo dentro do curso e parado.
    /// Ver g_overtravel_trip no firmware.</para></summary>
    OvertravelTripped = 128,
}
