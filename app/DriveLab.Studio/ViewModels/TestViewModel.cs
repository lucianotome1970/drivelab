// ============================================================================
//  DriveLab
//  TestViewModel.cs — VM da aba de teste: envia forças (mola, constante, periódica, damper) ao dispositivo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Protocol;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class TestViewModel : ViewModelBase
{
    private readonly BaseSession _session;

    [ObservableProperty] private double _spring;
    [ObservableProperty] private double _constant;
    [ObservableProperty] private double _periodic;
    [ObservableProperty] private double _damper;
    [ObservableProperty] private bool _forceEnabled;

    // ⚠️ A FORÇA PRECISA SER REENVIADA, SENÃO ELA MORRE EM MENOS DE UM SEGUNDO.
    //
    // O firmware tem um cão-de-guarda proposital: se o app parar de mandar, a força decai a zero
    // (500 ms inteira, some em mais 300 ms). É a proteção certa — um app travado não pode deixar a
    // base empurrando o volante para sempre.
    //
    // Só que este teste mandava UMA vez, quando o controle era movido. A mola existia por meio
    // segundo e sumia. Isso explica por que o teste "não tinha força" e, pior, por que ele nunca
    // serviu para diagnosticar nada: passamos noites tentando entender uma mola que a própria
    // proteção apagava antes de alguém conseguir sentir.
    //
    // Enquanto houver algum valor diferente de zero, reenviamos a cada 100 ms — cinco vezes dentro
    // da janela do cão-de-guarda, com folga para perder alguns relatórios sem a força piscar.
    private static readonly TimeSpan kIntervalo = TimeSpan.FromMilliseconds(100);
    private CancellationTokenSource? _reenvio;

    public TestViewModel(BaseSession session) => _session = session;

    /// <summary>Mantém a força viva enquanto o teste estiver ativo. Para sozinho quando tudo volta a
    /// zero — sem tarefa pendurada quando não há o que enviar.</summary>
    private void GarantirReenvio()
    {
        var ativo = Spring != 0 || Constant != 0 || Periodic != 0 || Damper != 0;
        if (!ativo)
        {
            _reenvio?.Cancel();
            _reenvio = null;
            return;
        }
        if (_reenvio is not null) return;

        var cts = new CancellationTokenSource();
        _reenvio = cts;
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await SendAsync();
                try { await Task.Delay(kIntervalo, cts.Token); } catch (TaskCanceledException) { return; }
            }
        });
    }

    public void Parar()
    {
        _reenvio?.Cancel();
        _reenvio = null;
        Spring = Constant = Periodic = Damper = 0;
        _ = SendAsync();   // zera na base agora, sem esperar o cão-de-guarda
    }

    public Task SendAsync()
    {
        if (!_session.IsConnected)
            return Task.CompletedTask;

        return _session.SendDirectControlAsync(new BaseDirectControl
        {
            SpringForce = ToInt16(Spring),
            ConstantForce = ToInt16(Constant),
            PeriodicForce = ToInt16(Periodic),
            DamperForce = ToInt16(Damper),
        });
    }

    partial void OnSpringChanged(double value)   { _ = SendAsync(); GarantirReenvio(); }
    partial void OnConstantChanged(double value) { _ = SendAsync(); GarantirReenvio(); }
    partial void OnPeriodicChanged(double value) { _ = SendAsync(); GarantirReenvio(); }
    partial void OnDamperChanged(double value)   { _ = SendAsync(); GarantirReenvio(); }

    partial void OnForceEnabledChanged(bool value)
    {
        if (!_session.IsConnected)
            return;
        _ = _session.SendCommandAsync(BaseCommand.SetForceEnabled, (byte)(value ? 1 : 0));
    }

    private static short ToInt16(double normalized) =>
        (short)Math.Round(Math.Clamp(normalized, -1, 1) * 10000);
}
