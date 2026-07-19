// ============================================================================
//  DriveLab
//  DeviceAutoConnector.cs — Conecta/desconecta a base automaticamente via polling de presença USB.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DriveLab.Studio.Services;

/// <summary>Conexão automática (modo real): faz polling da presença do dispositivo (cabo USB) e
/// conecta/desconecta sozinho — sem botão Conectar. Agnóstico de sessão: recebe as ações por
/// delegates (serve base, pedais e freio de mão). O connect/disconnect roda na thread de UI
/// (via <see cref="IUiDispatcher"/>) para os eventos de conexão atualizarem VMs com segurança.
/// Quando o firmware ainda não enumera, a presença é falsa e ele só aguarda. Um tick por vez.</summary>
public sealed class DeviceAutoConnector : IDisposable
{
    private readonly Func<bool> _isConnected;
    private readonly Func<Task> _connect;
    private readonly Func<Task> _disconnect;
    private readonly Func<bool> _isPresent;
    private readonly IUiDispatcher _dispatcher;
    private Timer? _timer;
    private volatile bool _busy;
    private volatile bool _paused;

    public DeviceAutoConnector(
        Func<bool> isConnected, Func<Task> connect, Func<Task> disconnect,
        Func<bool> isPresent, IUiDispatcher dispatcher)
    {
        _isConnected = isConnected;
        _connect = connect;
        _disconnect = disconnect;
        _isPresent = isPresent;
        _dispatcher = dispatcher;
    }

    public void Start(int periodMs = 1000)
    {
        _timer?.Dispose();
        // Poll na thread do timer; a ação (connect/disconnect) é marshalada p/ a UI.
        _timer = new Timer(_ => _dispatcher.Post(async () => await PollOnceAsync()), null, 0, periodMs);
    }

    /// <summary>Suspende o polling (não conecta nem desconecta) — usado durante uma atualização de
    /// firmware, quando o fluxo de update precisa de controle exclusivo da USB do dispositivo para
    /// deixá-lo re-enumerar como bootloader (DFU) sem que este auto-connector reabra o handle.</summary>
    public void Pause() => _paused = true;

    /// <summary>Retoma o polling (o próximo tick reconecta o dispositivo assim que ele reaparecer).</summary>
    public void Resume() => _paused = false;

    /// <summary>Um passo do polling: conecta se o dispositivo apareceu, desconecta se sumiu.</summary>
    public async Task PollOnceAsync()
    {
        if (_paused || _busy)
            return;
        _busy = true;
        try
        {
            bool present;
            try { present = _isPresent(); }
            catch { present = false; }

            if (present && !_isConnected())
                await _connect();
            else if (!present && _isConnected())
                await _disconnect();
        }
        catch
        {
            // Transiente (dispositivo sumiu no meio, I/O): tenta de novo no próximo tick.
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
