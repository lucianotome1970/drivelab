using System;
using System.Threading;
using System.Threading.Tasks;

namespace DriveLab.Studio.Services;

/// <summary>Conexão automática (modo real): faz polling da presença do dispositivo (cabo USB)
/// e conecta/desconecta a <see cref="DeviceSession"/> sozinho — sem botão Conectar. Quando o
/// firmware não enumera ainda, a presença é sempre falsa e ele apenas aguarda. Um tick por vez
/// (guarda <c>_busy</c>) para não sobrepor conexões.</summary>
public sealed class DeviceAutoConnector : IDisposable
{
    private readonly DeviceSession _session;
    private readonly Func<bool> _isPresent;
    private Timer? _timer;
    private volatile bool _busy;

    public DeviceAutoConnector(DeviceSession session, Func<bool> isPresent)
    {
        _session = session;
        _isPresent = isPresent;
    }

    public void Start(int periodMs = 1000)
    {
        _timer?.Dispose();
        _timer = new Timer(async _ => await PollOnceAsync(), null, 0, periodMs);
    }

    /// <summary>Um passo do polling: conecta se o dispositivo apareceu, desconecta se sumiu.</summary>
    public async Task PollOnceAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            bool present;
            try { present = _isPresent(); }
            catch { present = false; }

            if (present && !_session.IsConnected)
                await _session.ConnectAsync();
            else if (!present && _session.IsConnected)
                await _session.DisconnectAsync();
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
