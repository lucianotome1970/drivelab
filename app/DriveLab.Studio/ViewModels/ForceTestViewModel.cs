// ============================================================================
//  DriveLab
//  ForceTestViewModel.cs — Roda os testes de força na base e mostra o veredito.
//
//  A REGRA de cada teste (o que mandar, o que concluir) mora em
//  DriveLab.Core/Testing e é testada sem hardware. Aqui fica só a execução: o
//  relógio, o envio, a coleta da telemetria e o estado da tela.
//
//  ⚠️ ISTO APLICA TORQUE REAL num volante que pode estar com a mão de alguém.
//  Três coisas são inegociáveis e estão implementadas abaixo:
//    · nunca começa sem a força habilitada e a base conectada;
//    · o botão de parar sempre disponível, e parar ZERA a força na hora;
//    · terminar, abortar ou dar erro passam todos pelo mesmo caminho de parada —
//      não existe saída que deixe força aplicada.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Testing;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um teste na lista, com o resultado da última execução.</summary>
public sealed partial class ForceTestItemViewModel : ObservableObject
{
    public IForceTest Teste { get; }

    public string Id => Teste.Id;
    public double DuracaoS => Teste.DuracaoS;

    /// <summary>Nome e explicação vêm dos resx pela chave do teste — acrescentar um teste novo só
    /// exige as duas chaves, sem tocar na tela.</summary>
    public string Nome => L.Get($"ForceTest_{Teste.Id}");
    public string Descricao => L.Get($"ForceTest_{Teste.Id}_Desc");

    /// <summary>Pico de força em %, para a tela avisar antes de aplicar.</summary>
    public int PicoPct => (int)Math.Round(Teste.PicoDeForca * 100);

    [ObservableProperty] private bool _rodando;
    /// <summary>0..1 enquanto roda. Fica no ITEM, e nao na pagina, porque o retorno tem de
    /// aparecer onde a pessoa clicou — barra no topo para um botao la embaixo faz procurar.</summary>
    [ObservableProperty] private double _progresso;
    [ObservableProperty] private string? _resumo;
    [ObservableProperty] private bool _passou;
    [ObservableProperty] private bool _temResultado;
    [ObservableProperty] private IReadOnlyList<string> _detalhes = Array.Empty<string>();

    public ForceTestItemViewModel(IForceTest teste) => Teste = teste;

    public void Aplicar(ForceTestResult r)
    {
        Passou = r.Ok;
        Resumo = r.Resumo;
        Detalhes = r.Detalhes;
        TemResultado = true;
    }
}


/// <summary>O relógio do executor. Existe para os testes poderem rodar sem esperar 8 segundos por
/// teste — e, mais importante, para poderem INTERLEAVAR duas execuções de propósito e provar que a
/// segunda é recusada. Com o relógio real e um transporte que responde na hora, a primeira execução
/// roda inteira de forma síncrona e o teste de concorrência não testa nada.</summary>
public interface IRelogioDeTeste
{
    double DecorridoS { get; }
    void Reiniciar();
    Task EsperarAsync(int ms, CancellationToken ct);
}

internal sealed class RelogioReal : IRelogioDeTeste
{
    private readonly System.Diagnostics.Stopwatch _sw = new();
    public double DecorridoS => _sw.Elapsed.TotalSeconds;
    public void Reiniciar() => _sw.Restart();
    public Task EsperarAsync(int ms, CancellationToken ct) => Task.Delay(ms, ct);
}

public sealed partial class ForceTestViewModel : ViewModelBase
{
    /// <summary>Cadência de envio. 100 Hz dá 10 ms de passo — bom para as ondas até 25 Hz que os
    /// testes usam (quatro pontos por ciclo no pior caso). Acima disso a regularidade do USB deixa
    /// de ser confiável e o teste passaria a medir o transporte, não o volante.</summary>
    private const int PassoMs = 10;

    private readonly BaseSession _session;
    private readonly IRelogioDeTeste _relogio;

    private BaseState? _ultimoEstado;
    private CancellationTokenSource? _cancelamento;

    public IReadOnlyList<ForceTestItemViewModel> Testes { get; }

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _forceEnabled;
    // NotifyCanExecuteChangedFor não é redundante com NotifyPropertyChangedFor: o primeiro dispara o
    // CanExecuteChanged, que é o que faz a UI RECONSULTAR o comando. Sem ele o botão Parar aparece
    // (o IsVisible funciona) mas nasce cinza e continua cinza durante todo o teste.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeParar))]
    [NotifyCanExecuteChangedFor(nameof(PararCommand))]
    private ForceTestItemViewModel? _emExecucao;
    [ObservableProperty] private string? _aviso;

    /// <summary>Kt da placa, para converter corrente em torque. Zero = ainda não medido, e aí o
    /// torque não é exibido em vez de ser inventado a partir de um valor de catálogo.</summary>
    [ObservableProperty] private double _torqueConstant;

    /// <summary>Quanto da força de cada teste é realmente aplicada, em %. Multiplica o que o teste
    /// pede — um teste de "100% de pico" com este controle em 10% aplica 10%.
    /// <para><b>Nasce em 10 de propósito.</b> Os testes foram escritos pedindo 50% a 100% do fundo
    /// de escala, e enquanto o firmware descartava os efeitos do report 0x10 isso não tinha
    /// consequência nenhuma. Quando passou a aplicar de verdade (2026-08-12), a primeira execução
    /// foi violenta. Um volante DD com 10 Nm arranca da mão de quem não esperava — o padrão precisa
    /// ser fraco o bastante para a surpresa ser inofensiva, e quem quiser mais sobe sabendo.</para></summary>
    [ObservableProperty] private double _forcaPct = 10;

    public bool PodeParar => EmExecucao is not null;

    public ForceTestViewModel(BaseSession session, IRelogioDeTeste? relogio = null)
    {
        _session = session;
        _relogio = relogio ?? new RelogioReal();
        Testes = ForceTests.Todos.Select(t => new ForceTestItemViewModel(t)).ToList();

        _isConnected = session.IsConnected;
        session.Connected += AoConectar;
        session.Disconnected += AoDesconectar;
        session.StateReceived += AoReceberEstado;
    }

    private void AoConectar(object? s, EventArgs e) => IsConnected = true;

    private void AoDesconectar(object? s, EventArgs e)
    {
        IsConnected = false;
        // Perder a base no meio de um teste não pode deixar o executor girando às cegas.
        _cancelamento?.Cancel();
    }

    private void AoReceberEstado(object? s, BaseState estado)
    {
        _ultimoEstado = estado;
        // A trava de força vem da BASE, não de um botão da tela: é ela que sabe se o motor está
        // liberado. Deixar a tela adivinhar levaria ao pior caso — testar achando que está armado.
        ForceEnabled = estado.Flags.HasFlag(BaseFlags.ForceEnabled);

        // Perder a força NO MEIO do teste aborta. A base desarmou por algum motivo — parada de
        // emergência, guarda de coerência, desarme por erro — e continuar mandando força para uma
        // base desarmada é, na melhor das hipóteses, medir nada; na pior, voltar a aplicar torque
        // no instante em que ela rearmar.
        if (!ForceEnabled) _cancelamento?.Cancel();
    }

    [RelayCommand]
    private async Task RodarAsync(ForceTestItemViewModel item)
    {
        if (EmExecucao is not null) return;

        if (!IsConnected)
        {
            Aviso = "Conecte a base antes de testar.";
            return;
        }
        if (!ForceEnabled)
        {
            // Sem isto o teste "roda", não acontece nada, e a pessoa conclui que a base está morta.
            Aviso = "A força está desligada. Ligue \"Ativar motor\" antes de testar.";
            return;
        }

        Aviso = null;
        EmExecucao = item;
        item.Rodando = true;
        item.Progresso = 0;

        var amostras = new List<ForceTestSample>();
        _cancelamento = new CancellationTokenSource();
        var ct = _cancelamento.Token;

        try
        {
            _relogio.Reiniciar();
            while (true)
            {
                var t = _relogio.DecorridoS;
                if (t >= item.Teste.DuracaoS || ct.IsCancellationRequested) break;

                var f = item.Teste.ForcaEm(t);
                await _session.SendDirectControlAsync(ParaControle(f, ForcaPct / 100.0));

                if (_ultimoEstado is { } estado)
                    amostras.Add(Amostrar(t, f, estado));

                item.Progresso = Math.Clamp(t / item.Teste.DuracaoS, 0, 1);
                await _relogio.EsperarAsync(PassoMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Abortado pelo usuário ou pela desconexão: o veredito abaixo usa o que deu tempo de colher.
        }
        finally
        {
            await PararForcaAsync();
            item.Rodando = false;
            item.Progresso = 0;
            EmExecucao = null;
            _cancelamento?.Dispose();
            _cancelamento = null;
        }

        item.Aplicar(item.Teste.Avaliar(amostras));
    }

    [RelayCommand(CanExecute = nameof(PodeParar))]
    private void Parar() => _cancelamento?.Cancel();

    /// <summary>Zera a força. Passa por aqui todo caminho de saída — fim normal, aborto e erro.</summary>
    private Task PararForcaAsync() =>
        _session.IsConnected
            ? _session.SendDirectControlAsync(new BaseDirectControl())
            : Task.CompletedTask;

    /// <summary>Aplica o multiplicador da tela a TODOS os quatro campos, inclusive os ganhos de mola
    /// e amortecimento. Escalar só as forças deixaria a mola no valor cheio e o teste dela — o mais
    /// suave da lista — seria o único imune ao controle de segurança.</summary>
    private static BaseDirectControl ParaControle(ForceCommand f, double escala) => new()
    {
        ConstantForce = Normalizado(f.Constant * escala),
        SpringForce   = Normalizado(f.Spring   * escala),
        PeriodicForce = Normalizado(f.Periodic * escala),
        DamperForce   = Normalizado(f.Damper   * escala),
    };

    private static short Normalizado(double v) => (short)Math.Round(Math.Clamp(v, -1, 1) * 10000);

    private ForceTestSample Amostrar(double t, ForceCommand f, BaseState e)
    {
        var correnteA = e.MotorCurrentMa / 1000.0;
        return new ForceTestSample(
            ElapsedS: t,
            Commanded: f.Constant,
            AngleDeg: e.AngleDeciDeg / 10.0,
            // Torque = Kt × corrente, o mesmo cálculo do monitor. Sem Kt medido fica zero, e o
            // veredito que depende de torque diz o que sabe em vez de inventar um número.
            TorqueNm: TorqueConstant > 0 ? TorqueConstant * correnteA : 0,
            CurrentA: correnteA,
            FetTempC: e.FetTempC,
            ClippingPct: e.ClippingPercent);
    }

    public override void Dispose()
    {
        _cancelamento?.Cancel();
        _session.Connected -= AoConectar;
        _session.Disconnected -= AoDesconectar;
        _session.StateReceived -= AoReceberEstado;
        base.Dispose();
    }
}
