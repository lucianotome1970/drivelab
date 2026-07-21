// ============================================================================
//  DriveLab
//  EmergencyStopViewModel.cs — Parada de emergência: corta a força da base na hora (SetForceEnabled=0) + rearmar.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// E-stop do app: um comando que desliga a força da base imediatamente (<see cref="BaseCommand.SetForceEnabled"/>
/// = 0). Como isso gateia TODO o pipeline no firmware (a máquina de partida não libera torque), é um kill switch
/// real assim que o motor entrar (Stage 1). Rearmar reenvia SetForceEnabled=1. Sempre acessível no topo do app.
/// </summary>
public partial class EmergencyStopViewModel : ViewModelBase
{
    private readonly BaseSession _session;

    public EmergencyStopViewModel(BaseSession session) => _session = session;

    /// <summary>True enquanto a parada está engatada (força cortada até rearmar).</summary>
    [ObservableProperty] private bool _engaged;

    [RelayCommand]
    private async Task StopAsync()
    {
        try { await _session.SendCommandAsync(BaseCommand.SetForceEnabled, 0); }
        catch { /* base desconectada: o estado de UI ainda reflete a intenção de parar */ }
        Engaged = true;
    }

    [RelayCommand]
    private async Task RearmAsync()
    {
        try { await _session.SendCommandAsync(BaseCommand.SetForceEnabled, 1); }
        catch { /* idem */ }
        Engaged = false;
    }
}
