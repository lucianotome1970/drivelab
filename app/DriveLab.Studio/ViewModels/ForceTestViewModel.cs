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
using DriveLab.Core.Diagnostics;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
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

    /// <summary>Instrução de preparo, quando o teste exige algo da pessoa antes de rodar. Vazio na
    /// maioria — só o de regeneração pede, porque sacode o volante de lado a lado e com o aro
    /// acoplado isso balança o rig inteiro.</summary>
    public string Preparo =>
        Teste.PreparoKey.Length == 0 ? "" : L.Get(Teste.PreparoKey);
    public bool TemPreparo => Preparo.Length > 0;

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

    /// <summary>Disparado quando um teste termina e tem veredito. A TELA escuta para rolar até ele.
    ///
    /// <para>Existe por um tropeço real: o resultado nasce ABAIXO do cartão, fora da área visível
    /// quando a descrição e o preparo são longos. Quem clicou em Rodar fica olhando o topo, não vê
    /// nada mudar e conclui que o teste não respondeu — foi o que aconteceu com o de encoder em
    /// 15/08/2026. Escrever o resultado num lugar que ninguém está olhando é o mesmo que não
    /// escrever.</para></summary>
    public event Action<ForceTestItemViewModel>? ResultadoPronto;

    /// <summary>Único caminho por onde um veredito chega à tela. Centralizado para que nenhum teste
    /// novo possa publicar resultado sem avisar quem precisa rolar até ele.</summary>
    private void PublicarResultado(ForceTestItemViewModel item, ForceTestResult r)
    {
        item.Aplicar(r);
        ResultadoPronto?.Invoke(item);
    }

    public ForceTestViewModel(BaseSession session, IRelogioDeTeste? relogio = null)
    {
        _session = session;
        _relogio = relogio ?? new RelogioReal();
        Testes = ForceTests.Todos.Select(t => new ForceTestItemViewModel(t)).ToList();

        _isConnected = session.IsConnected;
        // A base pode JÁ estar conectada quando esta aba é criada (a sessão sobe antes das telas).
        // Nesse caso o evento Connected nunca dispara, e sem isto o Kt ficaria zero justamente na
        // situação mais comum — abrir o app com a base ligada.
        if (session.IsConnected) _ = CarregarKtAsync();
        session.Connected += AoConectar;
        session.Disconnected += AoDesconectar;
        session.StateReceived += AoReceberEstado;
    }

    private void AoConectar(object? s, EventArgs e)
    {
        IsConnected = true;
        _ = CarregarKtAsync();
    }

    /// <summary>Busca o Kt na base. Sem ele o torque estimado sai ZERO e todo veredito que fala de
    /// torque fica cego — a Rampa reportava "torque máximo 0,00 Nm" com a base entregando força, e
    /// o Impacto media o corte sem saber contra o quê.
    ///
    /// <para>O campo existia e ninguém o preenchia: nasceu para ser injetado de fora e o
    /// CompositionRoot nunca o injetou. Perguntar à base é melhor que injetar — o Kt vive lá, muda
    /// quando o usuário o mede, e assim a tela não precisa lembrar de repassar.</para></summary>
    private async Task CarregarKtAsync()
    {
        try
        {
            var v = await _session.ReadSettingAsync(BaseSettingId.TorqueConstant);
            TorqueConstant = v.AsDouble;
        }
        catch
        {
            // Base que não respondeu: fica zero e os vereditos dizem o que sabem, como antes.
            // Falhar aqui não pode impedir os testes que não dependem de torque de rodar.
        }
    }

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
        // ⚠️ Só para os testes que APLICAM força. O de encoder exige o motor desarmado — quem
        // manda nele é a base —, e cancelar por "força desligada" o mataria no berço. PicoDeForca
        // é o critério certo porque descreve exatamente isso: um teste que não pede força não tem
        // o que perder quando ela cai.
        if (!ForceEnabled && EmExecucao?.Teste.PicoDeForca > 0) _cancelamento?.Cancel();
    }

    [RelayCommand]
    private async Task RodarAsync(ForceTestItemViewModel item)
    {
        if (EmExecucao is not null) return;

        // O teste de encoder não manda força nenhuma: quem gira o motor é a base. Ele tem execução
        // própria logo abaixo — inclusive com a exigência OPOSTA à dos outros quanto ao motor.
        if (item.Teste is EncoderTest)
        {
            await RodarTesteEncoderAsync(item);
            return;
        }

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

        // PREPARO DA BASE — hoje só o teste de curso excedido pede, e o que ele pede é abaixar a
        // parede do fim de curso para o volante poder ATRAVESSAR o limite sem impacto.
        //
        // ⚠️ O valor original é guardado ANTES e restaurado no `finally`, junto com a parada da
        // força. Não pode existir caminho de saída que deixe a base sem batente: cancelar, perder a
        // conexão, o motor desarmar (que é justamente o que este teste provoca) ou dar exceção.
        int? batenteOriginal = null;
        if (item.Teste.Id == "Overtravel" && _session.IsConnected)
        {
            try
            {
                var v = await _session.ReadSettingAsync(BaseSettingId.SoftStopStrength);
                batenteOriginal = (int)Math.Round(v.AsDouble);
                await _session.WriteSettingAsync(BaseSettingId.SoftStopStrength,
                                                 new SettingValue(SettingType.UInt8, 0));
            }
            catch
            {
                // Não conseguiu preparar: aborta ANTES de aplicar força. Rodar sem baixar a parede
                // viraria justamente a queda de braço contra o batente que este desenho evita.
                Aviso = "Não foi possível preparar a base para este teste.";
                item.Rodando = false;
                EmExecucao = null;
                return;
            }
        }

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
            await RestaurarBatenteAsync(batenteOriginal);
            item.Rodando = false;
            item.Progresso = 0;
            EmExecucao = null;
            _cancelamento?.Dispose();
            _cancelamento = null;
        }

        PublicarResultado(item, item.Teste.Avaliar(amostras));
    }

    /// <summary>Dispara a medição de alinhamento do encoder e espera a base devolvê-la.
    ///
    /// <para>⚠️ ESTE TESTE PEDE O MOTOR DESLIGADO — o contrário de todos os outros. Ele gira o motor
    /// em malha aberta, e malha aberta com o controle armado põe dois donos no mesmo motor. O
    /// firmware recusa nesse caso; avisamos antes para a pessoa não ver um teste que "não faz nada".</para>
    ///
    /// <para>COMO SABEMOS QUE O RESULTADO É NOVO: a base zera a medição ao começar a varredura, e só
    /// aceitamos um resultado válido DEPOIS de ter visto esse zero. Sem isso, uma base que já mediu
    /// antes devolveria o resultado velho no mesmo instante do clique — e ele pareceria novo.</para></summary>
    private async Task RodarTesteEncoderAsync(ForceTestItemViewModel item)
    {
        if (!IsConnected)
        {
            Aviso = "Conecte a base antes de testar.";
            return;
        }
        if (ForceEnabled)
        {
            Aviso = "Desligue \"Ativar motor\" antes deste teste: quem gira o motor aqui é a base.";
            return;
        }

        Aviso = null;
        EmExecucao = item;
        item.Rodando = true;
        item.Progresso = 0;
        _cancelamento = new CancellationTokenSource();
        var ct = _cancelamento.Token;

        // Se a base JÁ está com uma medição válida na telemetria, ela é de antes deste clique.
        var viuZerar = _ultimoEstado?.EncoderTestValido != true;
        BaseState? medicao = null;

        try
        {
            await _session.SendCommandAsync(BaseCommand.TestEncoder);
            _relogio.Reiniciar();

            while (_relogio.DecorridoS < item.Teste.DuracaoS && !ct.IsCancellationRequested)
            {
                if (_ultimoEstado is { } e)
                {
                    if (!viuZerar)
                    {
                        if (!e.EncoderTestValido) viuZerar = true;
                    }
                    else if (e.EncoderTestValido)
                    {
                        medicao = e;
                        break;
                    }
                }

                // A barra é ESTIMATIVA: a varredura leva ~30 s, mas quem decide o fim é a base.
                // Travamos em 95% para não mostrar "100%" a um teste que ainda está rodando.
                item.Progresso = Math.Min(0.95, _relogio.DecorridoS / 30.0);
                await _relogio.EsperarAsync(100, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Abortado: cai no veredito de "sem medição" abaixo.
        }
        finally
        {
            item.Rodando = false;
            item.Progresso = 0;
            EmExecucao = null;
            _cancelamento?.Dispose();
            _cancelamento = null;
        }

        if (medicao is null)
        {
            PublicarResultado(item, new ForceTestResult(false, "A base não devolveu a medição", new[]
            {
                "A varredura não terminou dentro do tempo. As causas mais comuns:",
                "  • o motor não está energizado (a fonte precisa estar ligada, não só o USB)",
                "  • o motor ainda não foi calibrado — calibre antes de medir o alinhamento",
                "  • firmware antigo na base, que ainda não tem este teste",
            }));
            return;
        }

        PublicarResultado(item, EncoderHealth.Avaliar(new EncoderMeasurement(
            Valido: medicao.EncoderTestValido,
            CoberturaVolta: medicao.EncoderCoberturaVolta,
            ExcentricidadeGraus: medicao.EncoderExcentricidadeGraus,
            ResiduoGraus: medicao.EncoderResiduoGraus,
            FaseGraus: medicao.EncoderFaseGraus,
            PolePairs: medicao.EncoderPolePairs)));
    }

    [RelayCommand(CanExecute = nameof(PodeParar))]
    private void Parar() => _cancelamento?.Cancel();

    /// <summary>Devolve a força do batente ao valor de antes do teste. Nada a fazer quando o teste
    /// não mexeu nele (o caso de todos os outros).
    ///
    /// <para>Roda no mesmo `finally` que zera a força, e por isso vale para todo caminho de saída.
    /// Se nem assim der — a base sumiu do USB no meio —, o valor original volta no próximo boot,
    /// porque o teste NÃO salva na flash: escreve só o valor em uso.</para></summary>
    private async Task RestaurarBatenteAsync(int? original)
    {
        if (original is not { } valor || !_session.IsConnected) return;
        try
        {
            await _session.WriteSettingAsync(BaseSettingId.SoftStopStrength,
                                             new SettingValue(SettingType.UInt8, valor));
        }
        catch
        {
            // Avisar é melhor que falhar em silêncio: quem acabou de rodar o teste precisa saber
            // que a base pode ter ficado sem batente até reiniciar.
            Aviso = "Não foi possível restaurar a força do batente — reinicie a base antes de usar.";
        }
    }

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
            ClippingPct: e.ClippingPercent,
            OvertravelTripped: e.Flags.HasFlag(BaseFlags.OvertravelTripped),
            // Contadores do resistor de freio, ACUMULADOS desde o boot da base. Quem olha a
            // diferença entre o começo e o fim é o veredito do teste — aqui só se transporta.
            BrakeActivations: e.BrakeActivations,
            BrakeEnergyJ: e.BrakeEnergyJoules);
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
