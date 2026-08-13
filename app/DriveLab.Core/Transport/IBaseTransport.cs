// ============================================================================
//  DriveLab
//  IBaseTransport.cs — Contrato de transporte do volante: conexão, telemetria de estado e envio de comandos via HID.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

public interface IBaseTransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }

    /// <summary>
    /// Raised whenever a new device state is available. This event MAY be raised on a
    /// background thread (e.g. a streaming timer thread) rather than the thread that
    /// subscribed to it. Handlers must marshal to their own thread (e.g. a UI dispatcher)
    /// if they need to touch thread-affine state.
    /// </summary>
    event EventHandler<BaseState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(BaseSettingId id, SettingValue value);
    Task<SettingValue> ReadSettingAsync(BaseSettingId id);

    /// <summary>
    /// Pergunta à base qual é o valor de FÁBRICA de um ajuste (report 0x17).
    ///
    /// <para>É uma <b>consulta pura</b>: nada muda na placa. O valor volta para a tela, e só o
    /// Salvar grava — quem decide continua sendo quem está na frente do volante.</para>
    ///
    /// <para>Existe porque os padrões moravam em <b>dois lugares</b>: o array do firmware e o
    /// descritor de cada campo no app. Hoje coincidem, mas nada garantia isso — bastava editar um
    /// lado. O sintoma seria cruel: "Padrão" no app escreveria valores diferentes dos de uma placa
    /// recém-gravada, e os dois pareceriam ser o padrão. Perguntando à base, a resposta é uma só.</para>
    /// </summary>
    Task<SettingValue> ReadSettingDefaultAsync(BaseSettingId id);
    Task SendDirectControlAsync(BaseDirectControl control);
    Task SendCommandAsync(BaseCommand command, byte arg = 0);
}
