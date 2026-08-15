// ============================================================================
//  DriveLab
//  EncoderHealth.cs — traduz o erro medido no encoder em CONSEQUÊNCIA: quanta
//  força se perde e quanto calor sobra.
//
//  POR QUE ISTO EXISTE SEPARADO DA MEDIÇÃO: o firmware mede graus mecânicos, e
//  graus mecânicos não dizem nada a quem monta. "±1,7°" soa desprezível — mas
//  num motor de 15 pares de polos vira 25° ELÉTRICOS, e o torque cai com o
//  cosseno do erro elétrico. A pessoa sente "o FFB está fraco" e vai mexer no
//  ganho, que é o lugar errado: o motor já está recebendo a corrente inteira,
//  ela é que está sendo aplicada no ângulo errado. Medimos isso em 15/08/2026 e
//  foi exatamente o que aconteceu conosco antes de fazer esta conta.
//
//  A CONTA, e por que é esta:
//    erro elétrico  = erro mecânico × pares de polos
//    torque útil    = cos(erro elétrico)      — a projeção da corrente no eixo
//                                               que de fato produz binário
//    corrente extra = 1 / cos(erro)           — para entregar o MESMO torque
//    calor          = (1 / cos(erro))²        — perda ôhmica vai com o quadrado
//
//  O calor é a parte que mais surpreende: 25° elétricos custam 10% de torque
//  mas 22% mais calor; 50° custam 35% de torque e mais que o DOBRO de calor.
//  É por isso que um encoder mal montado aparece primeiro como motor quente.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Testing;

namespace DriveLab.Core.Diagnostics;

/// <summary>O que a varredura do firmware mediu. Ângulos em graus MECÂNICOS (o que se vê girando o
/// volante); a conversão para elétricos é feita aqui.</summary>
public sealed record EncoderMeasurement(
    bool Valido,
    double CoberturaVolta,
    double ExcentricidadeGraus,
    double ResiduoGraus,
    double FaseGraus,
    int PolePairs);

/// <summary>Um número junto do que ele significa, para a tela não ter de recalcular nada.</summary>
public sealed record EncoderImpacto(
    double ErroEletricoGraus,
    double TorqueEntreguePct,
    double PerdaTorquePct,
    double CorrenteExtraPct,
    double CalorExtraPct);

public static class EncoderHealth
{
    /// <summary>Acima disto o erro deixa de ser detalhe. 5% de perda equivale a ~18° elétricos —
    /// abaixo disso ninguém sente, e reprovar seria alarme falso: nenhuma montagem real dá zero.</summary>
    private const double PerdaAceitavelPct = 5.0;

    /// <summary>Quanto da volta a varredura precisa cobrir para a medição valer. O erro de
    /// excentricidade tem período de UMA volta; ajustar uma senoide a um quarto dela dá um número
    /// que parece medição e não é (errei nisso em 15/08/2026 e o firmware hoje se recusa a
    /// responder abaixo deste valor — aqui só explicamos o porquê a quem lê).</summary>
    public const double CoberturaMinima = 0.75;

    /// <summary>Converte um erro mecânico no que ele custa. É a função inteira do arquivo; o resto é
    /// texto.</summary>
    public static EncoderImpacto Impacto(double erroMecanicoGraus, int polePairs)
    {
        var eletrico = Math.Abs(erroMecanicoGraus) * polePairs;

        // Além de 90° elétricos a corrente empurra o rotor PARA TRÁS. O cosseno já diz isso ficando
        // negativo, mas um "torque de −20%" na tela não comunica nada — travamos em zero e o texto
        // passa a falar em comutação invertida, que é o que de fato está acontecendo.
        var cos = Math.Cos(eletrico * Math.PI / 180.0);
        var torquePct = Math.Max(0, cos) * 100.0;

        // Sem torque útil não existe "corrente para o mesmo torque": nenhuma corrente entrega. Fica
        // em zero e o texto trata o caso à parte, em vez de imprimir infinito.
        var correnteExtraPct = cos > 0.05 ? (1.0 / cos - 1.0) * 100.0 : 0;
        var calorExtraPct = cos > 0.05 ? (1.0 / (cos * cos) - 1.0) * 100.0 : 0;

        return new EncoderImpacto(eletrico, torquePct, 100.0 - torquePct, correnteExtraPct, calorExtraPct);
    }

    /// <summary>O pior ponto da volta: a excentricidade é senoidal, então em algum lugar ela se soma
    /// ao erro de outra origem. É este ponto que produz o tremor — o resto da volta vai bem, e é por
    /// isso que o defeito parece intermitente para quem só sente o volante.</summary>
    public static double PiorErroMecanico(EncoderMeasurement m) =>
        Math.Abs(m.ExcentricidadeGraus) + Math.Abs(m.ResiduoGraus);

    public static ForceTestResult Avaliar(EncoderMeasurement m)
    {
        if (m.PolePairs <= 0)
            return new ForceTestResult(false,
                "Não sei quantos pares de polos o motor tem",
                new[]
                {
                    "Sem esse número não dá para converter erro do encoder em perda de força.",
                    "Confira o campo de pares de polos na aba do motor.",
                });

        if (!m.Valido)
        {
            var detalhes = new List<string>
            {
                $"Varredura cobriu {m.CoberturaVolta:0.00} volta — preciso de pelo menos {CoberturaMinima:0.00}.",
                "O erro de ímã descentrado se repete uma vez por volta. Medir um pedaço menor que isso",
                "daria um número convincente e errado, então prefiro não responder.",
            };
            if (m.CoberturaVolta <= 0.01)
                detalhes.Add("Cobertura zero costuma ser calibração que não chegou a rodar: confira se o motor está energizado.");
            return new ForceTestResult(false, "Não consegui medir com esta varredura", detalhes);
        }

        var pior = PiorErroMecanico(m);
        var impExc = Impacto(m.ExcentricidadeGraus, m.PolePairs);
        var impPior = Impacto(pior, m.PolePairs);

        var linhas = new List<string>
        {
            "── O QUE FOI MEDIDO (graus mecânicos, girando o volante) ──",
            $"Ímã fora de centro : ±{m.ExcentricidadeGraus:0.00}°  (máximo por volta aos {m.FaseGraus:0}°)",
            $"Erro de outra origem: {m.ResiduoGraus:0.00}°",
            $"Pior ponto da volta : {pior:0.00}°",
            "",
            $"── O MESMO ERRO VISTO PELO MOTOR (× {m.PolePairs} pares de polos) ──",
            $"Ímã fora de centro : {impExc.ErroEletricoGraus:0.0}° elétricos",
            $"Pior ponto da volta: {impPior.ErroEletricoGraus:0.0}° elétricos",
            "",
            "── O QUE ISSO CUSTA, no pior ponto ──",
        };

        if (impPior.TorqueEntreguePct <= 0.01)
        {
            linhas.Add("Torque entregue: NENHUM — passou de 90° elétricos.");
            linhas.Add("Além desse ângulo a corrente empurra o rotor para trás em vez de girá-lo.");
            linhas.Add("Toda a corrente vira calor. Não rode o motor assim.");
        }
        else
        {
            linhas.Add($"Torque entregue : {impPior.TorqueEntreguePct:0}% do que foi pedido  (perde {impPior.PerdaTorquePct:0}%)");
            linhas.Add($"Para o mesmo torque, corrente: +{impPior.CorrenteExtraPct:0}%");
            linhas.Add($"Calor no motor  : +{impPior.CalorExtraPct:0}%  (aquecimento vai com o quadrado da corrente)");
        }

        linhas.Add("");
        linhas.AddRange(Interpretacao(m, pior));

        var ok = impPior.PerdaTorquePct <= PerdaAceitavelPct;
        var resumo = ok
            ? $"Encoder alinhado — perde {impPior.PerdaTorquePct:0.0}% de força, sem impacto prático"
            : $"Perdendo {impPior.PerdaTorquePct:0}% da força e gerando +{impPior.CalorExtraPct:0}% de calor";

        return new ForceTestResult(ok, resumo, linhas);
    }

    /// <summary>O diagnóstico propriamente dito: NOMEAR a causa provável e dizer o que fazer.
    ///
    /// ⚠️ A ordem importa e ela é: o resíduo antes da amplitude. O resíduo responde a pergunta
    /// anterior — "a senoide descreve este erro?". Se não descreve, a amplitude dela é o ajuste de um
    /// modelo que não serve, e culpar o ímã com base nela é dar número a um palpite. Foi o erro que
    /// cometemos na primeira versão desta ferramenta.</summary>
    private static IEnumerable<string> Interpretacao(EncoderMeasurement m, double pior)
    {
        var exc = Math.Abs(m.ExcentricidadeGraus);
        var res = Math.Abs(m.ResiduoGraus);

        yield return "── O QUE PROVAVELMENTE É ──";

        // Tudo pequeno: dizer isso também vale. Metade de um diagnóstico é descartar hipóteses, e
        // quem chegou aqui procurando culpado precisa saber que não está neste componente.
        if (Impacto(pior, m.PolePairs).PerdaTorquePct <= PerdaAceitavelPct)
        {
            yield return "O encoder está bem montado. O erro é o normal de qualquer montagem e não";
            yield return "explica perda de força, tremor nem aquecimento — procure em outro lugar.";
            yield break;
        }

        if (res > exc * 1.5)
        {
            yield return $"O erro NÃO tem cara de ímã descentrado: sobra {res:0.00}° que a senoide não explica.";
            yield return "Ímã fora de centro erra de um jeito só — adianta num setor e atrasa no oposto.";
            yield return "Este erro é mais irregular que isso. Suspeitos, na ordem:";
            yield return "  • distância entre ímã e sensor fora da faixa (o datasheet do sensor dá o valor)";
            yield return "  • ímã frouxo no eixo, ou eixo com folga";
            yield return "  • CPR configurado diferente do que o sensor realmente entrega";
            yield return "  • ruído na fiação do encoder — cabo longo, sem malha ou passando junto das fases";
        }
        else if (exc > res * 1.5)
        {
            yield return $"Cara clássica de ÍMÃ FORA DE CENTRO: o erro se repete uma vez por volta,";
            yield return $"com máximo aos {m.FaseGraus:0}° e amplitude de ±{exc:0.00}°.";
            yield return $"Gire o volante até {m.FaseGraus:0}° e desloque o sensor (ou o ímã) nessa direção:";
            yield return "é ali que a leitura mais se afasta da verdade.";
            yield return "Corrigir o centro resolve sozinho a perda de força e o calor acima.";
        }
        else
        {
            yield return $"Há duas coisas somadas: ±{exc:0.00}° de ímã fora de centro e {res:0.00}° de outra origem,";
            yield return "em intensidade parecida. Centralizar o ímã resolve metade do problema —";
            yield return "vale fazer primeiro, porque é o mais fácil, e medir de novo depois.";
        }
    }
}
