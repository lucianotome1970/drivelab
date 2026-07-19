// ============================================================================
//  DriveLab
//  HostDeviceAccessCoordinator.cs — Implementação de IDeviceAccessCoordinator
//  para o app: durante um update, pausa o DeviceAutoConnector da base e
//  desconecta a sessão (solta o handle HID), e ao final retoma o auto-connect
//  (que reconecta a base já com o firmware novo). Hoje só a base é gerenciada;
//  os demais dispositivos são no-op até ganharem seu updater.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Threading.Tasks;
using DriveLab.Core.Update;

namespace DriveLab.Studio.Services;

/// <summary><see cref="IDeviceAccessCoordinator"/> backed by the host's per-device auto-connector
/// and session-disconnect. Wired in <c>CompositionRoot</c> for the real (non-simulator) base.</summary>
public sealed class HostDeviceAccessCoordinator : IDeviceAccessCoordinator
{
    private readonly DeviceAutoConnector _baseAutoConnector;
    private readonly Func<Task> _baseDisconnect;

    public HostDeviceAccessCoordinator(DeviceAutoConnector baseAutoConnector, Func<Task> baseDisconnect)
    {
        _baseAutoConnector = baseAutoConnector;
        _baseDisconnect = baseDisconnect;
    }

    public async Task BeginExclusiveAsync(DeviceKind kind)
    {
        if (kind != DeviceKind.Base)
            return;

        // Order matters: pause the poller FIRST so it can't race a reconnect after we release the
        // handle, then drop the HID handle so macOS can re-enumerate the device as DFU.
        _baseAutoConnector.Pause();
        try
        {
            await _baseDisconnect();
        }
        catch
        {
            // Device may already be gone (mid-reset) — that's fine, the handle is released either way.
        }
    }

    public Task EndExclusiveAsync(DeviceKind kind)
    {
        if (kind == DeviceKind.Base)
            _baseAutoConnector.Resume();
        return Task.CompletedTask;
    }
}
