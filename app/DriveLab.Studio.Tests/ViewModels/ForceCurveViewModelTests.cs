// ============================================================================
//  DriveLab
//  ForceCurveViewModelTests.cs — Testes da curva de resposta da forca.
//
//  O teste que mais importa aqui e o de FIDELIDADE: o desenho tem de dizer o
//  mesmo que a base entrega. Um grafico bonito que mostra uma curva diferente da
//  que o firmware aplica e pior que grafico nenhum — a pessoa ajusta olhando uma
//  coisa e sente outra.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class ForceCurveViewModelTests
{
    private static (ForceCurveViewModel curva, SettingsGroupViewModel grupo) Make()
    {
        var transport = new FakeTransport();
        transport.ConnectAsync().GetAwaiter().GetResult();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var grupo = new SettingsGroupViewModel(session, "Curva", ForceCurveViewModel.Ids);

        // Parte de uma placa com a curva de FABRICA. Sem declarar isto, os campos ficariam com o
        // valor unico que o transporte falso devolve para qualquer leitura, e os testes estariam
        // medindo o dublê em vez da curva.
        for (var i = 0; i < ForceCurveViewModel.Pontos; i++)
            grupo.Fields[i].Value = ForceCurveViewModel.Linear[i];

        return (new ForceCurveViewModel(grupo.Fields, session), grupo);
    }

    [Fact]
    public void Nasce_Na_Neutra_Com_As_Entradas_Fixas()
    {
        var (curva, _) = Make();
        Assert.Equal(new[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }, curva.PontosDaCurva.Select(p => p.Input));
        Assert.True(curva.IsLinear);
    }

    // ── FIDELIDADE: o desenho tem de bater com o firmware ────────────────────────────────────
    // O firmware interpola em SEGMENTOS LINEARES entre os cinco pontos (ffb_math.h,
    // applyForceCurve). Estes valores foram calculados a mao pela formula de la.

    [Fact]
    public void Na_Neutra_Sai_O_Mesmo_Que_Entra()
    {
        var (curva, _) = Make();
        foreach (var pedido in new double[] { 0, 10, 25, 37.5, 50, 80, 100 })
            Assert.Equal(pedido, curva.Avaliar(pedido), 3);
    }

    [Fact]
    public void A_Curva_Passa_Exatamente_Pelos_Pontos()
    {
        // A interpolacao e SUAVE (Hermite cubico), mas nao pode "escapar" dos pontos: onde a pessoa
        // largou a bolinha e onde a base tem de entregar. Suavizar entre os pontos, nunca por cima.
        var (curva, _) = Make();
        curva.MoverPonto(8, 90);

        for (var i = 0; i < ForceCurveViewModel.Pontos; i++)
            Assert.Equal(curva.PontosDaCurva[i].Output, curva.Avaliar(i * 10), 3);
    }

    [Fact]
    public void Entre_Os_Pontos_A_Curva_Nao_Estufa_Para_Baixo()
    {
        // Spline comum cria barriga entre pontos e pode DESCER, que e o que a tela proibe ao
        // arrastar. Fritsch-Carlson nao faz isso — e este teste e o que garante a escolha.
        var (curva, _) = Make();
        curva.MoverPonto(3, 60);      // degrau forte, o caso que faz spline comum oscilar
        curva.MoverPonto(4, 62);

        var anterior = -1.0;
        for (var pct = 0; pct <= 100; pct++)
        {
            var y = curva.Avaliar(pct);
            Assert.True(y >= anterior - 0.001, $"a curva desceu em {pct}%: {y} depois de {anterior}");
            anterior = y;
        }
    }

    [Fact]
    public void O_Sintoma_De_11_08_Fica_Visivel_Na_Curva()
    {
        // A linearity em 1,59 fazia o jogo pedir 50% e chegarem 33%. Uma curva que reproduza isso
        // tem de mostrar o mesmo numero — e o grafico afunda no meio, que e o ponto do controle.
        var (curva, _) = Make();
        curva.MoverPonto(5, 33);   // 50% pedido -> 33%

        Assert.Equal(33, curva.Avaliar(50), 3);
        Assert.True(curva.Avaliar(50) < 50, "forca media achatada aparece abaixo da diagonal");
        Assert.False(curva.IsLinear);
    }

    // ── A base e a fonte de verdade ──────────────────────────────────────────────────────────

    [Fact]
    public void Mover_Um_Ponto_Escreve_No_Setting()
    {
        var (curva, grupo) = Make();
        curva.MoverPonto(7, 88);
        Assert.Equal(88, grupo.Fields.First(f => f.SettingId == BaseSettingId.FfbCurve7).Value);
    }

    [Fact]
    public void Setting_Alterado_Por_Fora_Move_O_Desenho()
    {
        // "Padrao" (que pergunta a base) e o carregamento inicial mexem nos campos, nao no grafico.
        // Sem esta sincronia o desenho mostraria o ultimo arrasto em vez do que esta na placa.
        var (curva, grupo) = Make();
        grupo.Fields.First(f => f.SettingId == BaseSettingId.FfbCurve5).Value = 55;
        Assert.Equal(55, curva.PontosDaCurva[5].Output);
    }

    [Fact]
    public void Ponto_Nunca_Sai_Da_Faixa_Util()
    {
        var (curva, _) = Make();
        curva.MoverPonto(0, -30);
        Assert.Equal(0, curva.PontosDaCurva[0].Output);
        curva.MoverPonto(10, 250);
        Assert.Equal(100, curva.PontosDaCurva[10].Output);
    }

    [Fact]
    public async Task Restaurar_Pergunta_A_BASE_Qual_E_O_Padrao()
    {
        // O que e "fabrica" esta gravado no firmware; a constante do app e so uma copia. Se as duas
        // se separarem, este botao devolveria uma curva que a placa nao considera padrao — e as
        // duas pareceriam certas.
        var transport = new FakeTransport();
        await transport.ConnectAsync();
        transport.DefaultToReturn = new SettingValue(SettingType.UInt8, 42);   // o que a placa diz
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var grupo = new SettingsGroupViewModel(session, "Curva", ForceCurveViewModel.Ids);
        var curva = new ForceCurveViewModel(grupo.Fields, session);

        await curva.AplanarCommand.ExecuteAsync(null);

        Assert.Equal(BaseSettingId.FfbCurve10, transport.LastDefaultAsked);   // perguntou os 11
        Assert.All(curva.PontosDaCurva, p => Assert.Equal(42, p.Output));     // e adotou a resposta
    }

    [Fact]
    public async Task Restaurar_Sem_Base_Cai_Na_Reta_Local()
    {
        // Firmware antigo (sem o report 0x17) ou sem sessao: melhor a reta do app que um botao mudo.
        var (curva, _) = Make();
        curva.MoverPonto(5, 10);
        Assert.False(curva.IsLinear);

        var semBase = new ForceCurveViewModel(new List<SettingFieldViewModel>());
        await semBase.AplanarCommand.ExecuteAsync(null);

        Assert.True(semBase.IsLinear);
    }

    // ── As duas protecoes contra curva ruim ──────────────────────────────────────────────────

    [Fact]
    public void Arrastar_Leva_Os_Vizinhos_Junto()
    {
        // Ponto movido sozinho produz bico, e bico no FFB vira solavanco: a forca salta de um valor
        // para outro num intervalo curto de pedido. Os vizinhos acompanham com peso decrescente.
        var (curva, _) = Make();
        curva.IniciarArrasto();
        curva.Arrastar(5, 70);          // +20 no ponto 5 (era 50)

        Assert.Equal(70, curva.PontosDaCurva[5].Output);        // a intencao e respeitada por inteiro
        Assert.Equal(50, curva.PontosDaCurva[4].Output);        // vizinho imediato: metade (40 + 10)
        Assert.Equal(74, curva.PontosDaCurva[7].Output);        // o seguinte: um quinto (70 + 4)
        Assert.Equal(80, curva.PontosDaCurva[8].Output);        // fora do alcance: intacto
        curva.TerminarArrasto();
    }

    [Fact]
    public void Arrastar_E_Voltar_Devolve_A_Curva_De_Origem()
    {
        // O arrasto trabalha contra uma FOTOGRAFIA tirada ao apertar. Sem ela, cada evento do mouse
        // somaria outro empurrao nos vizinhos e a curva derreteria num arrasto so.
        var (curva, _) = Make();
        curva.IniciarArrasto();
        curva.Arrastar(5, 90);
        curva.Arrastar(5, 20);
        curva.Arrastar(5, 50);          // de volta ao valor original
        curva.TerminarArrasto();

        Assert.True(curva.IsLinear);
    }

    [Fact]
    public void A_Curva_Nunca_Desce()
    {
        // Trecho descendente = "pedi mais forca e recebi menos". No volante isso e incoerencia
        // fisica: o piloto aumenta a carga e sente a direcao aliviar, o que le como perda de
        // aderencia que nao existe.
        var (curva, _) = Make();
        curva.MoverPonto(5, 30);        // afunda o meio, abaixo dos pontos anteriores

        for (var i = 1; i < ForceCurveViewModel.Pontos; i++)
            Assert.True(curva.PontosDaCurva[i].Output >= curva.PontosDaCurva[i - 1].Output,
                        $"ponto {i} ficou abaixo do {i - 1}");
        Assert.Equal(30, curva.PontosDaCurva[5].Output);   // o ponto movido fica onde a pessoa levou
    }

    [Fact]
    public void Quem_Sobe_Leva_Os_De_Cima_E_Quem_Desce_Leva_Os_De_Baixo()
    {
        var (curva, _) = Make();
        curva.MoverPonto(2, 80);        // sobe o 20% acima do que vem depois
        Assert.Equal(80, curva.PontosDaCurva[2].Output);
        Assert.True(curva.PontosDaCurva[5].Output >= 80, "os de cima acompanharam");

        var (outra, _) = Make();
        outra.MoverPonto(8, 5);         // desce o 80% abaixo do que vem antes
        Assert.Equal(5, outra.PontosDaCurva[8].Output);
        Assert.True(outra.PontosDaCurva[3].Output <= 5, "os de baixo acompanharam");
    }

    // ── Só o 0% é fixo ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_Ponto_De_Zero_Nao_Se_Move()
    {
        // Forca pedida zero TEM de entregar zero. A curva vale para o modulo e o sinal e reaplicado
        // depois: com o primeiro ponto em 40, um pedido de +0,001 entregaria +40% e um de -0,001
        // entregaria -40% — o volante bateria de lado a lado toda vez que a forca cruzasse o centro,
        // e empurraria com o carro parado.
        var (curva, _) = Make();

        curva.MoverPonto(0, 40);
        Assert.Equal(0, curva.PontosDaCurva[0].Output);

        curva.IniciarArrasto();
        curva.Arrastar(0, 80);
        curva.TerminarArrasto();
        Assert.Equal(0, curva.PontosDaCurva[0].Output);
        Assert.Equal(0, curva.Avaliar(0), 3);
    }

    [Fact]
    public void O_Zero_Fica_Parado_Quando_O_Vizinho_E_Arrastado()
    {
        // O vizinho imediato arrasta os de lado junto — o ponto fixo tem de resistir a isso tambem.
        var (curva, _) = Make();
        curva.IniciarArrasto();
        curva.Arrastar(1, 60);
        curva.TerminarArrasto();

        Assert.Equal(0, curva.PontosDaCurva[0].Output);
        Assert.Equal(60, curva.PontosDaCurva[1].Output);   // a intencao no ponto pego e respeitada
    }

    [Fact]
    public void O_Ultimo_Ponto_E_LIVRE()
    {
        // Baixar o 100% e como se domam as batidas fortes sem perder detalhe no resto: ajuste
        // legitimo, e travar o ultimo ponto tiraria isso.
        var (curva, _) = Make();
        curva.MoverPonto(10, 70);
        Assert.Equal(70, curva.PontosDaCurva[10].Output);
        Assert.Equal(70, curva.Avaliar(100), 3);
    }
}
