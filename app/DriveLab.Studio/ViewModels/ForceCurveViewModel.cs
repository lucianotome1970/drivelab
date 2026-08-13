// ============================================================================
//  DriveLab
//  ForceCurveViewModel.cs — A curva de resposta da força, editada arrastando.
//
//  POR QUE ESTE CONTROLE EXISTE, E POR QUE DESENHADO
//
//  A curva diz quanta força SAI para cada força que o jogo PEDE, em cinco pontos
//  (0, 25, 50, 75 e 100%). O firmware ja a aplicava; nao havia como mexer nela.
//
//  Ela nasceu desenhada, e nao como cinco campos numericos, por uma licao cara:
//  em 11/08 descobrimos que a `linearity` estava em 1,59 e achatava as forcas
//  MEDIAS pela metade — o jogo pedia 50% e chegavam 33%. Isso passou uma sessao
//  inteira despercebido, e produzia dois sintomas que pareciam se contradizer:
//  base "fraca" e, ao mesmo tempo, clipping alto. Numero nao denuncia isso.
//  Desenho denuncia na primeira olhada: a linha afunda no meio.
//
//  A DIAGONAL DE REFERENCIA e parte da funcao, nao enfeite: e contra ela que se
//  enxerga o que a curva esta fazendo. Sem ela, uma curva achatada parece uma
//  curva qualquer.
//
//  FIDELIDADE: o firmware interpola em SEGMENTOS LINEARES entre os pontos
//  (ffb_math.h, applyForceCurve). O desenho usa exatamente isso — nada de
//  suavizacao bonita que mostrasse uma curva que a base nao produz.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um ponto da curva: entrada fixa (o que o jogo pede) e saída editável (o que sai).</summary>
public sealed partial class CurvePointViewModel : ObservableObject
{
    /// <summary>Força pedida pelo jogo, em %. Fixa: 0, 25, 50, 75 ou 100.</summary>
    public int Input { get; }

    private readonly Action<int, double> _aoMover;

    [ObservableProperty]
    private double _output;

    public CurvePointViewModel(int indice, int input, double output, Action<int, double> aoMover)
    {
        Indice = indice;
        Input = input;
        _output = output;
        _aoMover = aoMover;
    }

    public int Indice { get; }

    partial void OnOutputChanged(double value) => _aoMover(Indice, value);
}

public sealed partial class ForceCurveViewModel : ViewModelBase
{
    /// <summary>Quantos pontos a curva tem.</summary>
    public const int Pontos = 11;

    /// <summary>O ponto de 0% NAO se move: força pedida zero tem de entregar força zero.
    ///
    /// <para>Não é preciosismo. A curva vale para o MÓDULO da força e o sinal é reaplicado depois;
    /// com o primeiro ponto em 40%, um pedido de +0,001 entregaria +40% e um de −0,001 entregaria
    /// −40% — o volante bateria de lado a lado toda vez que a força do jogo cruzasse o centro, e
    /// ficaria empurrando com o carro parado.</para>
    ///
    /// <para>O ÚLTIMO ponto continua livre: baixar o 100% é como se domam as batidas fortes sem
    /// perder detalhe no resto, e é ajuste legítimo.</para></summary>
    public const int PontoFixo = 0;

    /// <summary>Entradas fixas dos pontos, em % da força pedida: 0, 10, 20 … 100.</summary>
    public static readonly int[] Entradas = Enumerable.Range(0, Pontos).Select(i => i * 10).ToArray();

    /// <summary>A curva neutra: sai o mesmo que entra. É o padrão de fábrica e a referência.</summary>
    public static readonly int[] Linear = Enumerable.Range(0, Pontos).Select(i => i * 10).ToArray();

    /// <summary>Os ids dos pontos, em ORDEM da curva.
    ///
    /// <para>⚠️ Lista explícita, e não aritmética sobre o enum: os ids NÃO são contíguos. Os cinco
    /// primeiros ficaram em 28-32, onde a curva de cinco pontos morava, e os seis que entraram
    /// depois foram para 49-54 — mexer nos antigos trocaria o significado do que já está gravado.
    /// `FfbCurve0 + i` apontaria para settings que não têm nada a ver com a curva.</para></summary>
    public static readonly BaseSettingId[] Ids =
    {
        BaseSettingId.FfbCurve0, BaseSettingId.FfbCurve1, BaseSettingId.FfbCurve2,
        BaseSettingId.FfbCurve3, BaseSettingId.FfbCurve4, BaseSettingId.FfbCurve5,
        BaseSettingId.FfbCurve6, BaseSettingId.FfbCurve7, BaseSettingId.FfbCurve8,
        BaseSettingId.FfbCurve9, BaseSettingId.FfbCurve10,
    };

    private readonly SettingFieldViewModel?[] _campos = new SettingFieldViewModel?[Pontos];
    private readonly BaseSession? _sessao;
    private bool _sincronizando;

    public IReadOnlyList<CurvePointViewModel> PontosDaCurva { get; }

    /// <summary>A curva está na neutra? Usado pela tela para dizer que nada foi moldado ainda.</summary>
    public bool IsLinear => PontosDaCurva.Select((p, i) => (int)Math.Round(p.Output) == Linear[i]).All(x => x);

    public ForceCurveViewModel(IEnumerable<SettingFieldViewModel> campos, BaseSession? sessao = null)
    {
        _sessao = sessao;
        var lista = campos.ToList();
        for (var i = 0; i < Pontos; i++)
            _campos[i] = lista.FirstOrDefault(f => f.SettingId == Ids[i]);

        PontosDaCurva = Enumerable.Range(0, Pontos)
            .Select(i => new CurvePointViewModel(i, Entradas[i], _campos[i]?.Value ?? Linear[i], AoMoverPonto))
            .ToList();

        // A base é a fonte de verdade: quando o campo muda (carregou da placa, ou o botão "Padrão"
        // preencheu), o desenho acompanha. Sem isto o gráfico mostraria o que o usuário arrastou
        // da última vez, e não o que está na placa — a divergência silenciosa que este projeto já
        // pagou caro para aprender a evitar.
        foreach (var campo in _campos)
            if (campo is not null)
                campo.PropertyChanged += AoMudarCampo;
    }

    private void AoMudarCampo(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingFieldViewModel.Value)) return;
        if (_sincronizando) return;

        _sincronizando = true;
        for (var i = 0; i < Pontos; i++)
            if (_campos[i] is { } campo)
                PontosDaCurva[i].Output = campo.Value;
        _sincronizando = false;
        OnPropertyChanged(nameof(IsLinear));
    }

    private void AoMoverPonto(int indice, double saida)
    {
        if (_campos[indice] is { } campo && !_sincronizando)
        {
            _sincronizando = true;
            campo.Value = Math.Clamp(Math.Round(saida), 0, 100);
            _sincronizando = false;
        }
        OnPropertyChanged(nameof(IsLinear));
    }

    /// <summary>Move um ponto para o valor dado, limitado à faixa útil, e reforça a subida.
    /// A matemática de tela (pixel → %) fica na View; a regra, aqui.</summary>
    public void MoverPonto(int indice, double saidaPct)
    {
        if (indice <= 0 || indice >= Pontos) return;   // o ponto 0 e fixo — ver PontoFixo
        PontosDaCurva[indice].Output = Math.Clamp(Math.Round(saidaPct), 0, 100);
        ForcarSubida(indice);
    }

    // ── Arrasto: os vizinhos acompanham ─────────────────────────────────────────────────────
    //
    // Mover um ponto sozinho produz bico, e bico no FFB não vira textura: vira solavanco, porque a
    // força salta de um valor para outro num intervalo curto de pedido. Os vizinhos acompanham com
    // peso decrescente — metade no vizinho imediato, um quinto no seguinte —, o que amacia a
    // vizinhança sem trair a intenção: o ponto que a pessoa pegou vai exatamente aonde ela levou.

    private static readonly double[] PesoVizinho = { 1.0, 0.5, 0.2 };

    private double[]? _fotografia;

    /// <summary>Guarda a curva no instante em que o arrasto começa.</summary>
    ///
    /// <remarks>É o que faz o arrasto ser estável. Se o deslocamento fosse aplicado aos valores
    /// CORRENTES a cada evento do mouse, os vizinhos somariam o mesmo empurrão dezenas de vezes
    /// num arrasto só e a curva inteira derreteria. Contra a fotografia, o deslocamento é sempre
    /// o total desde o começo — mover e voltar devolve a curva de origem.</remarks>
    public void IniciarArrasto()
        => _fotografia = PontosDaCurva.Select(p => p.Output).ToArray();

    public void TerminarArrasto() => _fotografia = null;

    /// <summary>Leva o ponto ao valor dado, arrastando os vizinhos junto.</summary>
    public void Arrastar(int indice, double saidaPct)
    {
        if (indice <= 0 || indice >= Pontos) return;   // o ponto 0 e fixo — ver PontoFixo
        var origem = _fotografia;
        if (origem is null) { MoverPonto(indice, saidaPct); return; }

        var alvo = Math.Clamp(Math.Round(saidaPct), 0, 100);
        var deslocamento = alvo - origem[indice];

        // Repõe a curva INTEIRA a partir da fotografia, e não só a vizinhança. A trava de subida
        // empurra pontos que podem estar fora do alcance dos vizinhos; sem repor todos, esses
        // ficariam para trás e o arrasto deixaria de ser reversível — mover e voltar não devolveria
        // a curva de origem. Durante um arrasto nada mais mexe na curva, então repor é seguro.
        for (var i = 0; i < Pontos; i++)
        {
            var distancia = Math.Abs(i - indice);
            var peso = distancia < PesoVizinho.Length ? PesoVizinho[distancia] : 0.0;
            PontosDaCurva[i].Output = i == PontoFixo
                ? 0
                : Math.Clamp(Math.Round(origem[i] + deslocamento * peso), 0, 100);
        }
        ForcarSubida(indice);
    }

    // ── A curva não pode descer ─────────────────────────────────────────────────────────────
    //
    // Um trecho descendente significa "pedi mais força e recebi menos". No volante isso é
    // incoerência física: o piloto aumenta a carga na curva e sente a direção aliviar, o que lê como
    // perda de aderência que não existe. Não é um ajuste exótico que alguém possa querer — é um
    // acidente de arrasto.
    //
    // A correção empurra a partir do ponto que a pessoa moveu, e nunca move o ponto dela: quem sobe
    // leva os de cima junto; quem desce leva os de baixo. Assim a intenção é sempre respeitada.
    private void ForcarSubida(int movido)
    {
        for (var i = movido + 1; i < Pontos; i++)
            if (PontosDaCurva[i].Output < PontosDaCurva[i - 1].Output)
                PontosDaCurva[i].Output = PontosDaCurva[i - 1].Output;

        for (var i = movido - 1; i >= 0; i--)
            if (PontosDaCurva[i].Output > PontosDaCurva[i + 1].Output)
                PontosDaCurva[i].Output = PontosDaCurva[i + 1].Output;
    }

    /// <summary>Restaura a curva de FÁBRICA, perguntando à base quais são os valores.
    ///
    /// <para>Pergunta em vez de usar a constante daqui pelo mesmo motivo do botão "Padrão": o que é
    /// fábrica está gravado no firmware, e a cópia do app é só uma cópia. Se as duas se separarem —
    /// bastava alguém editar um lado —, este botão devolveria uma curva que a placa não considera
    /// padrão, e os dois pareceriam certos.</para>
    ///
    /// <para>Se a base não responder (firmware antigo, sem o report 0x17), cai na reta local. É o
    /// comportamento anterior, e melhor que deixar o botão sem efeito.</para></summary>
    [RelayCommand]
    public async Task AplanarAsync()
    {
        for (var i = 0; i < Pontos; i++)
        {
            double valor;
            try
            {
                valor = _sessao is null
                    ? Linear[i]
                    : (await _sessao.ReadSettingDefaultAsync(Ids[i])).AsDouble;
            }
            catch (Exception)
            {
                valor = Linear[i];
            }
            PontosDaCurva[i].Output = Math.Clamp(Math.Round(valor), 0, 100);
        }
    }

    /// <summary>O que a base entrega para uma força pedida, em %.
    ///
    /// <para>Espelha a interpolação do firmware (ffb_math.h): Hermite cúbico com as inclinações de
    /// Fritsch-Carlson. Ligar os pontos com retas fazia de cada ponto um canto, e canto na curva de
    /// força vira degrau no volante — some aumentando o número de pontos? Não: só ficam menores, e
    /// a tela vira impossível de ajustar.</para>
    ///
    /// <para>Esta regra em particular PRESERVA A MONOTONICIDADE. Uma spline comum estufa entre os
    /// pontos e pode criar uma barriga que desce — exatamente o que a tela proíbe ao arrastar.</para></summary>
    public double Avaliar(double entradaPct)
    {
        var y = PontosDaCurva.Select(p => p.Output).ToArray();
        var m = Inclinacoes(y);
        const double h = 1.0 / (Pontos - 1);

        var a = Math.Clamp(entradaPct, 0, 100) / 100.0;
        var x = a * (Pontos - 1);
        var i = (int)x;
        if (i > Pontos - 2) i = Pontos - 2;
        var t = x - i;

        double t2 = t * t, t3 = t2 * t;
        var h00 =  2 * t3 - 3 * t2 + 1;
        var h10 =      t3 - 2 * t2 + t;
        var h01 = -2 * t3 + 3 * t2;
        var h11 =      t3 -     t2;

        return h00 * y[i] + h10 * h * m[i] + h01 * y[i + 1] + h11 * h * m[i + 1];
    }

    /// <summary>Inclinação em cada ponto (Fritsch-Carlson). Onde a secante troca de sentido a
    /// inclinação vira zero; nos demais é a média harmônica, que nunca ultrapassa o dobro da menor
    /// secante vizinha — é isso que impede a curva de estufar para baixo entre dois pontos.</summary>
    private static double[] Inclinacoes(double[] y)
    {
        const double h = 1.0 / (Pontos - 1);
        var d = new double[Pontos - 1];
        for (var i = 0; i < Pontos - 1; i++) d[i] = (y[i + 1] - y[i]) / h;

        var m = new double[Pontos];
        m[0] = d[0];
        m[Pontos - 1] = d[Pontos - 2];
        for (var i = 1; i < Pontos - 1; i++)
            m[i] = d[i - 1] * d[i] <= 0 ? 0 : 2 * d[i - 1] * d[i] / (d[i - 1] + d[i]);
        return m;
    }

    public override void Dispose()
    {
        foreach (var campo in _campos)
            if (campo is not null)
                campo.PropertyChanged -= AoMudarCampo;
        base.Dispose();
    }
}
