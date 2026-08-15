// ============================================================================
//  DriveLab
//  ForceTest.cs — Os testes de força da base: o que cada um manda e o que ele
//  conclui do que voltou.
//
//  POR QUE EXISTE: a tela de teste antiga eram quatro sliders. Servia para o
//  desenvolvedor cutucar o motor, e não servia para quem monta uma base e quer
//  saber se ficou boa. A diferença entre "aplicar força" e "TESTAR o equipamento"
//  é o veredito — e o veredito sai da telemetria que a base já manda: torque,
//  picos de corrente, clipping e temperatura dos FETs.
//
//  Tudo aqui é PURO: recebe tempo, devolve força; recebe amostras, devolve
//  conclusão. Nada de USB, nada de tela. É o que permite testar a régua de
//  avaliação sem motor nenhum — que é bom, porque um veredito errado manda a
//  pessoa mexer no lugar errado do rig.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Testing;

/// <summary>Uma amostra colhida durante o teste: o que foi pedido e o que a base respondeu.</summary>
/// <param name="ElapsedS">Segundos desde o início do teste.</param>
/// <param name="Commanded">Força pedida naquele instante, −1..1.</param>
/// <param name="AngleDeg">Ângulo do volante, em graus (positivo e negativo em torno do centro).</param>
/// <param name="TorqueNm">Torque estimado, em Nm.</param>
/// <param name="CurrentA">Corrente do motor, em ampères.</param>
/// <param name="FetTempC">Temperatura dos FETs, em °C (−128 = sem leitura).</param>
/// <param name="ClippingPct">Fração do tempo saturado, 0-100.</param>
public sealed record ForceTestSample(
    double ElapsedS,
    double Commanded,
    double AngleDeg,
    double TorqueNm,
    double CurrentA,
    double FetTempC,
    double ClippingPct,
    /// <summary>Acionamentos do resistor de freio ACUMULADOS desde o boot. O teste olha a DIFERENÇA
    /// entre o fim e o começo — o valor absoluto carrega tudo o que aconteceu na sessão.</summary>
    uint BrakeActivations = 0,
    /// <summary>Energia dissipada no resistor, em joules, acumulada desde o boot. Mesma regra.</summary>
    double BrakeEnergyJ = 0,
    /// <summary>A guarda de curso excedido agiu neste instante. Vem do flag da telemetria, e não é
    /// inferido do desarme: desarmar tem muitas causas, e um teste que confunde "a proteção agiu"
    /// com "o motor caiu" passa quando não devia.</summary>
    bool OvertravelTripped = false);

/// <summary>O que o teste concluiu. `Ok` falso não é defeito do equipamento — é "olhe isto".</summary>
public sealed record ForceTestResult(bool Ok, string Resumo, IReadOnlyList<string> Detalhes)
{
    public static ForceTestResult SemDados =>
        new(false, "Sem amostras — a base respondeu?", Array.Empty<string>());
}

/// <summary>Forças a enviar num instante do teste. Todas em −1..1.</summary>
/// <remarks>Mola e damper são calculados pelo FIRMWARE contra a posição e a velocidade reais, então
/// mandá-los aqui testa malha fechada de verdade. Constante e periódica são malha aberta.</remarks>
public readonly record struct ForceCommand(double Constant, double Spring, double Periodic, double Damper)
{
    public static ForceCommand Zero => new(0, 0, 0, 0);
    public static ForceCommand Const(double v) => new(v, 0, 0, 0);
}

public interface IForceTest
{
    /// <summary>Chave estável, usada para tradução e para gravar resultado.</summary>
    string Id { get; }

    /// <summary>Duração total, em segundos.</summary>
    double DuracaoS { get; }

    /// <summary>Pico de força que este teste chega a pedir, 0..1. A tela usa para avisar antes.
    /// <para><b>Fração do que a base está CONFIGURADA para entregar</b> — não do fundo de escala do
    /// hardware. Quem regulou a força em 70% tem 70% como o seu máximo, e 30% no controle da tela é
    /// 30% desses 70%. É a leitura certa também para o veredito: a Rampa pergunta se a base entrega
    /// o que a configuração promete, e quem promete é a configuração de cada um.</para>
    /// <para>⚠️ A consequência é que estes números NÃO são torque absoluto: o mesmo teste aplica
    /// menos Nm numa base regulada mais baixo. Ao calibrar um valor daqui a partir de uma medição de
    /// bancada, anote junto o ajuste em que a medição foi feita — senão o número perde o sentido.</para></summary>
    double PicoDeForca { get; }

    /// <summary>O que a pessoa precisa FAZER antes de rodar — vazio quando não há preparo.
    ///
    /// <para>Diferente da descrição, que explica o que o teste mede: isto é uma instrução, e a tela
    /// mostra em destaque. Existe porque um teste pode ser seguro para a base e inconveniente para o
    /// rig: o de regeneração sacode o volante de lado a lado de propósito, e com o aro acoplado isso
    /// balança o suporte inteiro. Pedir para desacoplar é mais honesto que descobrir sacudindo.</para></summary>
    string PreparoKey => "";

    /// <summary>O que enviar no instante `t` (segundos desde o início).</summary>
    ForceCommand ForcaEm(double t);

    /// <summary>O veredito, a partir do que foi colhido.</summary>
    ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras);
}

// ─────────────────────────────────────────────────────────────────────────────
// RAMPA — a base entrega o que promete?
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Sobe a força devagar até o máximo e observa a corrente acompanhar.
///
/// <para>Responde a pergunta que mais custou a este projeto: pedir 15 Nm e receber 9,75, porque o
/// teto real é <c>corrente máxima × Kt</c> e não o número da configuração. Se a corrente parar de
/// crescer enquanto a força pedida continua subindo, é isso que está acontecendo.</para></summary>
public sealed class RampTest : IForceTest
{
    public string Id => "Ramp";
    public double DuracaoS => 8.0;
    public double PicoDeForca => 1.0;

    // Este é o teste que pede força CHEIA, e o que ele mede depende de o eixo ficar mais parado que
    // solto — por isso pede o aro montado, ao contrário do de regeneração, que pede o contrário.
    public string PreparoKey => "ForceTest_Ramp_Prep";

    /// <summary>Três idas e voltas por segundo — cada meio-ciclo empurra por ~167 ms.
    /// <para><b>Era 1 Hz e o volante girava rápido demais</b> (bancada, 14/08/2026). A velocidade
    /// que o eixo atinge é proporcional ao TEMPO que a força passa empurrando para o mesmo lado, e
    /// meio segundo é muito: a 3 Hz esse tempo cai a um terço, e a velocidade junto.</para>
    /// <para>E isso melhora a medição em vez de piorá-la, o que não é óbvio: girando, o motor gera
    /// back-EMF e a corrente CAI. Um teste que deveria medir o teto de corrente estava medindo o
    /// motor fugindo dele. Quanto menos o eixo corre, mais perto do rotor bloqueado — que é a
    /// condição em que o teto de corrente realmente aparece.</para></summary>
    private const double Hz = 3.0;

    // A força sobe em 6 s e segura 2 s no talo — a corrente precisa de tempo para estabilizar antes
    // de concluir qualquer coisa sobre ela. Mas ela ALTERNA de sentido enquanto sobe.
    //
    // POR QUE ALTERNAR: a versão anterior empurrava sempre para o mesmo lado com força crescente. O
    // volante corria até o fim do curso e ali ficava a disputa — o teste com força cheia contra o
    // batente, que segura com bem menos. O teste ganhava, o volante passava do curso, e a guarda de
    // curso excedido desarmava o motor, corretamente. Medido na bancada em 14/08/2026: 495°, exatos
    // 45° além do curso de 450° por lado, que é o limiar da guarda.
    //
    // Nenhum dos dois estava errado. A Rampa PRECISA de força cheia para medir o teto real da base,
    // e a guarda PRECISA desarmar quando o volante vai parar onde não deveria; eram incompatíveis
    // porque este teste foi escrito num mundo sem aquela guarda. Indo e voltando, o volante oscila
    // em torno de onde começou em vez de acumular curso, e a medição é a mesma — o que interessa
    // aqui é a corrente acompanhar a AMPLITUDE, não o lado para onde ela empurra.
    //
    // POR QUE A AMPLITUDE SOBE EM DEGRAUS, e não continuamente: com amplitude subindo DENTRO do
    // ciclo, o segundo meio-ciclo é sempre mais forte que o primeiro, e o volante ainda anda para um
    // lado só — mais devagar, mas anda. Mantendo a amplitude fixa ao longo de cada ciclo inteiro, as
    // duas metades se cancelam e o deslocamento líquido é exatamente zero. O degrau cai no
    // cruzamento por zero, onde a força já é nula, então não há salto nenhum a sentir.
    public ForceCommand ForcaEm(double t)
    {
        var ciclo     = Math.Floor(t * Hz);
        var amplitude = Math.Clamp((ciclo + 1) / (6.0 * Hz), 0, 1);
        return ForceCommand.Const(amplitude * Math.Sin(2 * Math.PI * Hz * t));
    }

    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras)
    {
        if (amostras.Count == 0) return ForceTestResult.SemDados;

        var picoCorrente = amostras.Max(a => Math.Abs(a.CurrentA));
        var picoTorque   = amostras.Max(a => Math.Abs(a.TorqueNm));
        var detalhes = new List<string>
        {
            $"Corrente máxima: {picoCorrente:0.0} A",
            $"Torque máximo: {picoTorque:0.00} Nm",
        };

        // O sinal de saturação: na última parte da rampa a força ainda sobe e a corrente já não.
        // Em módulo, porque o comando alterna de sentido — o que compara é a AMPLITUDE pedida.
        var fim    = amostras.Where(a => Math.Abs(a.Commanded) >= 0.8).ToList();
        var comeco = amostras.Where(a => Math.Abs(a.Commanded) is >= 0.4 and < 0.6).ToList();
        if (fim.Count > 0 && comeco.Count > 0)
        {
            var cFim    = fim.Average(a => Math.Abs(a.CurrentA));
            var cComeco = comeco.Average(a => Math.Abs(a.CurrentA));
            // Dobrando a força pedida, a corrente deveria dobrar. Menos de 1,5× é achatamento.
            if (cComeco > 0.5 && cFim / cComeco < 1.5)
            {
                detalhes.Add($"A corrente parou de acompanhar: {cComeco:0.0} A a meia força, " +
                             $"{cFim:0.0} A no final. O teto real está na corrente, não na configuração.");
                return new ForceTestResult(false, "A base satura antes da força pedida", detalhes);
            }
        }

        return new ForceTestResult(true, $"Entregou até {picoTorque:0.00} Nm com {picoCorrente:0.0} A", detalhes);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// IMPACTO — o pico sai inteiro?
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Três pulsos curtos e fortes, como bater numa zebra. Verifica se o pico sai inteiro ou
/// se o caminho de força o corta — e a medida do corte é o próprio clipping.</summary>
public sealed class ImpactTest : IForceTest
{
    public string Id => "Impact";
    public double DuracaoS => 4.0;
    public double PicoDeForca => 1.0;

    public ForceCommand ForcaEm(double t)
    {
        // Pulsos de 120 ms no segundo 1, 2 e 3, alternando o lado: bater sempre para o mesmo lado
        // empurraria o volante contra o batente e mediria o batente, não o impacto.
        foreach (var (inicio, lado) in new[] { (1.0, 1.0), (2.0, -1.0), (3.0, 1.0) })
            if (t >= inicio && t < inicio + 0.12)
                return ForceCommand.Const(lado);
        return ForceCommand.Zero;
    }

    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras)
    {
        if (amostras.Count == 0) return ForceTestResult.SemDados;

        var picoTorque   = amostras.Max(a => Math.Abs(a.TorqueNm));
        var picoClipping = amostras.Max(a => a.ClippingPct);
        var detalhes = new List<string>
        {
            $"Torque de pico: {picoTorque:0.00} Nm",
            $"Clipping no pico: {picoClipping:0}%",
        };

        if (picoClipping >= 50)
        {
            detalhes.Add("Metade ou mais do impacto foi cortada. Ou a força total está alta demais " +
                         "para o teto da base, ou o teto precisa subir.");
            return new ForceTestResult(false, "O impacto sai cortado", detalhes);
        }
        return new ForceTestResult(true, $"Pico de {picoTorque:0.00} Nm com {picoClipping:0}% de corte", detalhes);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VIBRAÇÃO — onde o conjunto ressoa?
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Varre de 5 a 25 Hz com amplitude baixa. O que se procura NÃO é a base falhar: é
/// descobrir se alguma frequência faz o rig inteiro chacoalhar — folga de suporte, mesa fina,
/// parafuso solto. É informação que não aparece dirigindo, porque na pista tudo acontece junto.
///
/// <para>A amplitude fica em 30% de propósito: ressonância mecânica se revela pelo movimento, não
/// pela força bruta, e vibrar forte um rig com folga solta parafuso.</para></summary>
public sealed class VibrationTest : IForceTest
{
    public const double HzInicial = 5.0;
    public const double HzFinal   = 25.0;

    public string Id => "Vibration";
    public double DuracaoS => 12.0;
    public double PicoDeForca => 0.30;

    /// <summary>Frequência instantânea da varredura no instante `t`.</summary>
    public double HzEm(double t) =>
        HzInicial + (HzFinal - HzInicial) * Math.Clamp(t / DuracaoS, 0, 1);

    public ForceCommand ForcaEm(double t)
    {
        // Fase integrada, não `sin(2π·f(t)·t)`: com a frequência variando, multiplicar por t faz a
        // fase saltar e a varredura vira uma sequência de degraus — que testa o degrau, não a
        // frequência. A integral de uma rampa de frequência é o termo quadrático abaixo.
        var f0 = HzInicial;
        var k  = (HzFinal - HzInicial) / DuracaoS;
        var fase = 2 * Math.PI * (f0 * t + k * t * t / 2);
        return new ForceCommand(0.30 * Math.Sin(fase), 0, 0, 0);
    }

    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras)
    {
        if (amostras.Count == 0) return ForceTestResult.SemDados;

        // Onde o volante mais se moveu para a MESMA força pedida = onde o conjunto amplifica.
        var janelas = amostras.GroupBy(a => (int)(a.ElapsedS / DuracaoS * 8))
                              .Where(g => g.Count() >= 3)
                              .Select(g => (
                                  Hz: HzEm(g.Average(a => a.ElapsedS)),
                                  Excursao: g.Max(a => a.AngleDeg) - g.Min(a => a.AngleDeg)))
                              .ToList();
        if (janelas.Count == 0) return ForceTestResult.SemDados;

        var detalhes = janelas.Select(j => $"{j.Hz:0} Hz: {j.Excursao:0.0}°").ToList();

        // ⚠️ NORMALIZAR PELA FÍSICA ANTES DE COMPARAR. Comparar cada janela com a MÉDIA das
        // excursões — como esta análise fazia — é errado por construção: com força de amplitude
        // fixa, o deslocamento de uma inércia vai com 1/f², então a baixa frequência move MUITO
        // mais sem que nada esteja solto. Medido na bancada em 2026-08-12: 112° a 6 Hz contra 2,2°
        // a 24 Hz, e o teste acusou "ressonância, procure folga no suporte" — mandando caçar um
        // defeito que não existia. O 11 Hz daquela mesma captura bateu em 33° contra 33,4°
        // previstos pela curva teórica: era inércia livre, comportando-se como o livro manda.
        //
        // O que ressonância de VERDADE faz é destoar da curva 1/f², não estar acima da média.
        // Normalizamos cada janela por f² e procuramos o desvio no que sobra.
        var normalizadas = janelas.Select(j => (j.Hz, Valor: j.Excursao * j.Hz * j.Hz)).ToList();
        var mediaNorm = normalizadas.Average(n => n.Valor);
        var piorNorm  = normalizadas.OrderByDescending(n => n.Valor).First();

        if (mediaNorm > 0.05 && piorNorm.Valor > mediaNorm * 2)
        {
            var vezes = piorNorm.Valor / mediaNorm;
            detalhes.Insert(0, $"Perto de {piorNorm.Hz:0} Hz o volante se move {vezes:0.0}× mais do que " +
                               "a física prevê para aquela frequência. Isso é amplificação do conjunto — " +
                               "procure folga no suporte e nos parafusos.");
            return new ForceTestResult(false, $"Ressonância perto de {piorNorm.Hz:0} Hz", detalhes);
        }

        detalhes.Insert(0, "A excursão cair com a frequência é o esperado: com a mesma força, o " +
                           "deslocamento vai com 1/f². O que se procura aqui é uma frequência que " +
                           "destoe dessa curva, e não houve.");
        return new ForceTestResult(true, "Resposta coerente em toda a faixa", detalhes);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MOLA — o volante volta ao centro?
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Liga uma mola e observa o volante voltar. Diferente dos outros, este é MALHA FECHADA: a
/// mola é calculada pelo firmware contra a posição real, então o teste exercita o caminho inteiro —
/// encoder, centro e força. Se o encoder estiver invertido, aqui o volante corre PARA LONGE do
/// centro, e é o jeito mais rápido de descobrir isso sem entrar num jogo.</summary>
public sealed class SpringTest : IForceTest
{
    public string Id => "Spring";
    public double DuracaoS => 6.0;
    public double PicoDeForca => 0.5;

    public ForceCommand ForcaEm(double t) => new(0, 0.5, 0, 0.15);

    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras)
    {
        if (amostras.Count < 4) return ForceTestResult.SemDados;

        var inicio = Math.Abs(amostras.First().AngleDeg);
        var fim    = Math.Abs(amostras.Last().AngleDeg);
        var detalhes = new List<string>
        {
            $"Começou a {inicio:0.0}° do centro, terminou a {fim:0.0}°",
        };

        // Começar já no centro não prova nada — a mola não teve o que fazer.
        if (inicio < 5)
            return new ForceTestResult(true, "Volante já estava no centro — gire-o e repita", detalhes);

        if (fim > inicio * 1.2)
        {
            detalhes.Add("O volante AFASTOU-SE do centro. O sinal mais provável é o sentido do " +
                         "encoder invertido: a base empurra para onde deveria puxar.");
            return new ForceTestResult(false, "A mola empurra para o lado errado", detalhes);
        }
        // NÃO SE MEXEU — precisa vir ANTES do teste de distância, senão um volante parado a 6° cai
        // no "voltou de 6,0° para 6,0°" e é APROVADO. Foi o que aconteceu em 2026-08-12: o firmware
        // descartava os efeitos do report 0x10, nenhuma força saía, e o teste dava ✅ dizendo
        // exatamente que o ângulo não mudou. Um teste que aprova a ausência do que ele mede é pior
        // que teste nenhum: some com o sintoma e ainda dá confiança.
        if (Math.Abs(fim - inicio) < 0.5)
        {
            detalhes.Add("O ângulo não mudou — a base não aplicou força nenhuma. Confira se o motor " +
                         "está ativado; se estiver, a força não está chegando do app até o firmware.");
            return new ForceTestResult(false, "O volante não se moveu", detalhes);
        }
        if (fim > 15)
        {
            detalhes.Add("Parou longe do centro. Atrito mecânico alto ou mola fraca demais para vencê-lo.");
            return new ForceTestResult(false, "Não voltou ao centro", detalhes);
        }
        return new ForceTestResult(true, $"Voltou de {inicio:0.0}° para {fim:0.0}°", detalhes);
    }
}

/// <summary>Os testes, na ordem em que fazem sentido rodar: dos mais suaves aos mais fortes.</summary>
// ─────────────────────────────────────────────────────────────────────────────
// REGENERAÇÃO — o resistor de freio ainda trabalha quando é chamado?
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Reverte a força de lado a lado, rápido, para forçar o motor a devolver energia — e
/// confere se o resistor de freio entra.
///
/// <para>POR QUE EXISTE: o resistor quase nunca aparece nos números, e "frio e zerado" é ambíguo —
/// pode ser que ele nunca precisou trabalhar, ou que ele não trabalha mais. Depois que a base ganhou
/// uma banda morta de regeneração (para o chopper parar de chavear com o volante parado), essa
/// dúvida ficou concreta: e se a banda morta tiver ficado alta demais e o silenciado for ele?</para>
///
/// <para>Reversão rápida é o que mais regenera: o motor precisa frear a inércia do volante antes de
/// acelerá-la para o outro lado, e essa frenagem devolve energia ao barramento. Se o resistor não
/// entra NEM AQUI, ele não vai entrar em lugar nenhum.</para></summary>
public sealed class RegenTest : IForceTest
{
    public string Id => "Regen";
    public double DuracaoS => 8.0;

    // 0,7 era demais, e o número veio da bancada em 14/08/2026: com a base regulada em 12 Nm o teste
    // jogava 8,4 Nm num eixo DESACOPLADO — sem a inércia do aro para segurá-lo, ele chegou a 5,12
    // voltas/s e a guarda de sobrevelocidade desarmou o motor, corretamente.
    //
    // Da velocidade medida sai a inércia do eixo nu (~0,05 kg·m²), e dela o torque que mantém o pico
    // perto de 2,5 voltas/s: ~4 Nm, que nesse mesmo ajuste de 12 Nm dá 0,33 do que a base entrega.
    // Arredondado para baixo — a folga interessa mais que o último décimo de energia regenerada.
    //
    // ⚠️ Fração do que a BASE ENTREGA, então quem regular mais baixo aplica menos Nm que estes ~4.
    // O teste continua válido: o resistor de freio entra proporcionalmente menos, e é isso que o
    // veredito lê. O ajuste da medição acima fica anotado porque sem ele o 0,3 não diz torque nenhum.
    public double PicoDeForca => 0.3;
    public string PreparoKey => "ForceTest_Regen_Prep";

    /// <summary>2,5 Hz: rápido o bastante para a reversão regenerar de verdade, devagar o bastante
    /// para o volante chegar a se mover entre uma e outra — a 10 Hz ele mal sai do lugar e a energia
    /// devolvida some.</summary>
    private const double Hz = 2.5;

    public ForceCommand ForcaEm(double t)
    {
        // Onda QUADRADA, não senoidal: a reversão brusca é o que regenera. Uma senoide passa pelo
        // zero suavemente e devolve muito menos.
        var lado = Math.Sin(2 * Math.PI * Hz * t) >= 0 ? 1.0 : -1.0;
        return ForceCommand.Const(lado * PicoDeForca);
    }

    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras)
    {
        if (amostras.Count == 0) return ForceTestResult.SemDados;

        // Os contadores da base são ACUMULADOS desde o boot — a diferença é o que este teste causou.
        var acionou = amostras[^1].BrakeActivations - amostras[0].BrakeActivations;
        var energia = amostras[^1].BrakeEnergyJ - amostras[0].BrakeEnergyJ;
        var picoTorque = amostras.Max(a => Math.Abs(a.TorqueNm));

        var detalhes = new List<string>
        {
            $"Acionamentos do resistor: {acionou}",
            $"Energia dissipada: {energia:0.0} J",
            $"Torque de pico: {picoTorque:0.00} Nm",
        };

        if (acionou == 0)
        {
            detalhes.Add("Reversão rápida é o que mais devolve energia — se o resistor não entrou " +
                         "aqui, ele não entra em lugar nenhum. Ou a banda morta de regeneração está " +
                         "alta demais, ou o resistor não está ligado.");
            return new ForceTestResult(false, "O resistor de freio não entrou", detalhes);
        }

        // Muita ação com energia quase nula é o outro defeito: o chopper "picando" no limiar em vez
        // de dissipar de verdade. Foi o que a base fazia parada, com 674 mil acionamentos e 27 J.
        if (energia < 0.05 && acionou > 500)
        {
            detalhes.Add("Muitos acionamentos para tão pouca energia: o resistor está chaveando no " +
                         "limiar em vez de dissipar. A banda morta de regeneração está baixa demais.");
            return new ForceTestResult(false, "O resistor está picando, não dissipando", detalhes);
        }

        return new ForceTestResult(true, $"Resistor entrou {acionou}× e dissipou {energia:0.0} J", detalhes);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CURSO EXCEDIDO — a guarda que desarma o motor realmente age?
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Baixa a parede do fim de curso, atravessa o limite devagar e confere se a guarda de
/// curso excedido faz o trabalho dela.
///
/// <para>POR QUE ASSIM: a guarda é a única proteção que DESARMA o motor, e nunca tinha sido vista
/// agindo. O jeito óbvio de testá-la — empurrar o volante contra o batente até vencê-lo — é uma
/// queda de braço entre duas forças no fim do curso, e se a guarda falhar o resultado é exatamente o
/// disparo que ela existe para evitar.</para>
///
/// <para>Baixando a força do batente para zero, o volante ATRAVESSA o limite sem nada resistindo:
/// não há impacto, não há disputa, e a guarda encontra a condição que ela procura — o volante muito
/// além de onde deveria estar. A ideia é do usuário, e é melhor que a minha.</para>
///
/// <para>⚠️ O ajuste do batente é restaurado ao fim, SEMPRE — inclusive se o teste for cancelado ou
/// falhar no meio. Ver como o executor trata isto.</para></summary>
public sealed class OvertravelTest : IForceTest
{
    public string Id => "Overtravel";
    public double DuracaoS => 12.0;
    public double PicoDeForca => 0.30;
    public string PreparoKey => "ForceTest_Overtravel_Prep";

    /// <summary>Força baixa e constante: o objetivo é ATRAVESSAR o fim do curso devagar, não chegar
    /// rápido. Depressa demais e a guarda de sobrevelocidade age antes — e aí o teste mede a guarda
    /// errada e passa achando que provou o que não provou.</summary>
    public ForceCommand ForcaEm(double t) => ForceCommand.Const(PicoDeForca);

    public ForceTestResult Avaliar(IReadOnlyList<ForceTestSample> amostras)
    {
        if (amostras.Count == 0) return ForceTestResult.SemDados;

        var chegou = amostras.Max(a => Math.Abs(a.AngleDeg));
        var disparou = amostras.Any(a => a.OvertravelTripped);
        var detalhes = new List<string>
        {
            $"Ângulo máximo atingido: {chegou:0} graus",
            $"Guarda de curso excedido: {(disparou ? "AGIU" : "não agiu")}",
        };

        if (disparou)
        {
            // O QUE FAZER AGORA, e não só o que aconteceu. O teste termina com o volante FORA do
            // curso e o motor desarmado — e é o próprio sucesso que tira o meio de desfazer isso:
            // sem motor armado não há torque, então o app não tem como trazer o volante de volta.
            // Deixar a pessoa descobrir sozinha que o volante ficou torto é uma falha da tela, não
            // do teste.
            detalhes.Add("A base freou e desarmou sozinha — é o comportamento esperado.");
            detalhes.Add("PARA VOLTAR AO NORMAL: gire o volante com a mão até perto do centro (ele " +
                         "está solto, sem motor) e reinicie a base. Com o ajuste em \"travar\", ela " +
                         "só volta a armar depois de reiniciar.");
            return new ForceTestResult(true, "A guarda agiu — a proteção funciona", detalhes);
        }

        // Não disparar pode ser as duas coisas, e a diferença importa: se o volante nem chegou perto
        // do fim do curso, o teste não chegou a exercitar a guarda — dizer "falhou" seria mentira.
        if (chegou < 90)
        {
            detalhes.Add("O volante mal saiu do lugar: a força do teste não venceu o atrito, ou o " +
                         "motor não estava armado. O teste não chegou a exercitar a guarda.");
            return new ForceTestResult(false, "O teste não chegou a rodar", detalhes);
        }

        detalhes.Add("O volante passou do fim do curso e a guarda NÃO agiu. É a proteção que deveria " +
                     "pegar um motor girando sozinho — vale investigar antes de usar a base.");
        return new ForceTestResult(false, "A guarda não agiu", detalhes);
    }
}

public static class ForceTests
{
    public static IReadOnlyList<IForceTest> Todos { get; } = new IForceTest[]
    {
        new SpringTest(),
        new VibrationTest(),
        new RampTest(),
        new ImpactTest(),
        new RegenTest(),
        new OvertravelTest(),
        new EncoderTest(),
    };
}
