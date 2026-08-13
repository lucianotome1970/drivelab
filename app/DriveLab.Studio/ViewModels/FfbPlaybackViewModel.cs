// ============================================================================
//  DriveLab
//  FfbPlaybackViewModel.cs — Toca uma volta gravada no volante.
//
//  A REGRA (que força sai em cada instante, o teto, o ajuste de sincronia) está
//  em DriveLab.Core/Capture e é testada sem hardware. Aqui fica a execução: o
//  relógio, o envio à base, a contagem regressiva e o estado da tela.
//
//  ⚠️ ISTO É FORÇA SEM PILOTO NO LAÇO. A volta foi gravada com alguém segurando
//  o volante e reagindo a cada carga; na reprodução pode não haver ninguém, e a
//  força que era "peso de curva" contra as mãos vira o volante girando sozinho.
//  Daí o teto começar em 30% e a contagem regressiva existir — ela dá tempo de
//  tirar as mãos, não é enfeite.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Capture;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public sealed partial class FfbPlaybackViewModel : ViewModelBase
{
    /// <summary>Cadência de envio, igual à dos testes de força: 100 Hz é onde o USB ainda é regular.
    /// A gravação é de 1 kHz, e a reprodução INTERPOLA — não perde o formato da volta, só a envia
    /// numa grade mais grossa.</summary>
    private const int PassoMs = 10;

    /// <summary>Segundos de contagem antes de a força começar. É o tempo de tirar as mãos do aro e,
    /// no uso com vídeo, de apertar o play — por isso é anunciada e não instantânea.</summary>
    public const int ContagemS = 3;

    private readonly BaseSession _session;
    private readonly ICaptureFilePicker _seletor;
    private readonly IRelogioDeTeste _relogio;
    private CancellationTokenSource? _cancelamento;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _forceEnabled;
    [ObservableProperty] private string? _aviso;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemVolta), nameof(Descricao), nameof(TemVideo))]
    private FfbPlayback? _volta;

    // NotifyCanExecuteChangedFor não é redundante com NotifyPropertyChangedFor: o primeiro dispara o
    // CanExecuteChanged, que é o que faz a UI RECONSULTAR o comando. Sem ele o botão Parar nasce
    // cinza e continua cinza durante toda a reprodução, mesmo com PodeParar já verdadeiro.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeParar))]
    [NotifyCanExecuteChangedFor(nameof(PararCommand))]
    private bool _tocando;

    /// <summary>Segundos restantes da contagem; 0 = não está contando.</summary>
    [ObservableProperty] private int _contagem;

    [ObservableProperty] private double _progresso;
    [ObservableProperty] private string _relogioTexto = "0,0 s";

    /// <summary>Quanto da força gravada sai. Espelha o teto do reprodutor — ver lá o porquê do 30.</summary>
    [ObservableProperty] private double _tetoPct = 30;

    /// <summary>Ajuste de sincronia com o vídeo, em MILISSEGUNDOS (a tela pensa em ms; o reprodutor,
    /// em segundos).</summary>
    [ObservableProperty] private double _ajusteMs;

    public bool TemVolta => Volta is not null;
    public bool PodeParar => Tocando;
    public bool TemVideo => !string.IsNullOrWhiteSpace(Volta?.Captura.Header.VideoUrl);

    public string Descricao => Volta is null
        ? ""
        : $"{Volta.Captura.Header.Game} · {Volta.Captura.Header.Car} · {Volta.Captura.Header.Track}" +
          (Volta.Captura.Header.LapTimeS > 0 ? $" · {Volta.Captura.Header.LapTimeS:0.0} s" : "") +
          $" · {Volta.DuracaoS:0.0} s gravados";

    public string? UrlDoVideo => Volta?.Captura.Header.VideoUrl;

    public FfbPlaybackViewModel(BaseSession session, ICaptureFilePicker seletor,
                                IRelogioDeTeste? relogio = null)
    {
        _session = session;
        _seletor = seletor;
        _relogio = relogio ?? new RelogioReal();

        _isConnected = session.IsConnected;
        session.Connected += (_, _) => IsConnected = true;
        session.Disconnected += (_, _) => { IsConnected = false; _cancelamento?.Cancel(); };
        session.StateReceived += AoReceberEstado;
    }

    private void AoReceberEstado(object? s, BaseState estado)
    {
        ForceEnabled = estado.Flags.HasFlag(BaseFlags.ForceEnabled);
        // Mesma regra dos testes de força: a base desarmar no meio aborta. Continuar mandando força
        // para uma base desarmada mede nada e volta a aplicar torque quando ela rearmar.
        if (!ForceEnabled) _cancelamento?.Cancel();
    }

    partial void OnTetoPctChanged(double value)
    {
        if (Volta is not null) Volta.TetoPct = value;
    }

    partial void OnAjusteMsChanged(double value)
    {
        if (Volta is not null) Volta.AjusteS = value / 1000.0;
    }

    [RelayCommand]
    private async Task CarregarAsync()
    {
        var caminho = await _seletor.PickCaptureAsync();
        if (caminho is null) return;

        try
        {
            await using var f = File.OpenRead(caminho);
            var captura = FfbCaptureFile.Read(f);
            Volta = new FfbPlayback(captura) { TetoPct = TetoPct, AjusteS = AjusteMs / 1000.0 };
            Aviso = null;
        }
        catch (Exception e)
        {
            // Arquivo de outro tipo, truncado no cabeçalho, corrompido: dizer O QUE houve, e não só
            // "erro" — quem baixou a gravação de outra pessoa não tem como investigar sozinho.
            Volta = null;
            Aviso = e.Message;
        }
    }

    [RelayCommand]
    private void AjustarSincronia(string ms)
    {
        if (double.TryParse(ms, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var passo))
            AjusteMs = Math.Clamp(AjusteMs + passo, -5000, 5000);
    }

    [RelayCommand]
    private async Task TocarAsync()
    {
        if (Tocando || Volta is null) return;

        if (!IsConnected) { Aviso = "Conecte a base antes de tocar."; return; }
        if (!ForceEnabled)
        {
            Aviso = "A força está desligada. Ligue \"Ativar motor\" antes de tocar.";
            return;
        }

        Aviso = null;
        Tocando = true;
        _cancelamento = new CancellationTokenSource();
        var ct = _cancelamento.Token;

        try
        {
            // CONTAGEM: tempo de tirar as mãos do aro e, com vídeo, de apertar o play.
            for (Contagem = ContagemS; Contagem > 0; Contagem--)
            {
                for (var i = 0; i < 100 && !ct.IsCancellationRequested; i++)
                    await _relogio.EsperarAsync(PassoMs, ct);
                if (ct.IsCancellationRequested) break;
            }
            Contagem = 0;

            _relogio.Reiniciar();
            while (!ct.IsCancellationRequested)
            {
                var t = _relogio.DecorridoS;
                if (t >= Volta.DuracaoS) break;

                await _session.SendDirectControlAsync(new BaseDirectControl
                {
                    ConstantForce = (short)Math.Round(Volta.ForcaEm(t) * 10000),
                });

                Progresso = Math.Clamp(t / Volta.DuracaoS, 0, 1);
                RelogioTexto = $"{t:0.0} s · vídeo em {Volta.SegundoDoVideoEm(t):0.0} s";
                await _relogio.EsperarAsync(PassoMs, ct);
            }
        }
        catch (OperationCanceledException) { /* parada pelo usuário ou pela base */ }
        finally
        {
            await PararForcaAsync();
            Tocando = false;
            Contagem = 0;
            Progresso = 0;
            _cancelamento?.Dispose();
            _cancelamento = null;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeParar))]
    private void Parar() => _cancelamento?.Cancel();

    /// <summary>Zera a força. Todo caminho de saída passa por aqui — fim, parada, desconexão, erro.</summary>
    private Task PararForcaAsync() =>
        _session.IsConnected ? _session.SendDirectControlAsync(new BaseDirectControl())
                             : Task.CompletedTask;

    public override void Dispose()
    {
        _cancelamento?.Cancel();
        _session.StateReceived -= AoReceberEstado;
        base.Dispose();
    }
}
