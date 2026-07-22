// ============================================================================
//  DriveLab
//  GameBindingViewModel.cs — Linha de UI de um jogo no auto-perfil: perfil escolhido por módulo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Games;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um jogo na tela de auto-perfil: escolhe o perfil (por nome) a carregar em cada módulo. As listas de
/// nomes disponíveis vêm das bibliotecas de perfil de cada módulo; qualquer mudança chama <c>onChanged</c>.</summary>
public partial class GameBindingViewModel : ViewModelBase
{
    private readonly Action _onChanged;
    private bool _loading;

    public GameBindingViewModel(
        KnownGame game, ModuleProfiles? initial,
        ObservableCollection<string> baseProfiles, ObservableCollection<string> wheelProfiles,
        ObservableCollection<string> pedalsProfiles, ObservableCollection<string> handbrakeProfiles,
        Action onChanged)
    {
        GameId = game.Id;
        GameName = game.DisplayName;
        BaseProfiles = baseProfiles;
        WheelProfiles = wheelProfiles;
        PedalsProfiles = pedalsProfiles;
        HandbrakeProfiles = handbrakeProfiles;
        _onChanged = onChanged;

        _loading = true;
        _baseProfile = initial?.Base;
        _wheelProfile = initial?.Wheel;
        _pedalsProfile = initial?.Pedals;
        _handbrakeProfile = initial?.Handbrake;
        _loading = false;
    }

    public string GameId { get; }
    public string GameName { get; }

    /// <summary>Jogo adicionado pelo usuário (pode ser removido; os do catálogo não).</summary>
    public bool IsCustom { get; init; }

    public ObservableCollection<string> BaseProfiles { get; }
    public ObservableCollection<string> WheelProfiles { get; }
    public ObservableCollection<string> PedalsProfiles { get; }
    public ObservableCollection<string> HandbrakeProfiles { get; }

    [ObservableProperty] private string? _baseProfile;
    [ObservableProperty] private string? _wheelProfile;
    [ObservableProperty] private string? _pedalsProfile;
    [ObservableProperty] private string? _handbrakeProfile;

    partial void OnBaseProfileChanged(string? value) => Notify();
    partial void OnWheelProfileChanged(string? value) => Notify();
    partial void OnPedalsProfileChanged(string? value) => Notify();
    partial void OnHandbrakeProfileChanged(string? value) => Notify();

    private void Notify() { if (!_loading) _onChanged(); }

    public ModuleProfiles ToModuleProfiles() => new()
    {
        Base = string.IsNullOrEmpty(BaseProfile) ? null : BaseProfile,
        Wheel = string.IsNullOrEmpty(WheelProfile) ? null : WheelProfile,
        Pedals = string.IsNullOrEmpty(PedalsProfile) ? null : PedalsProfile,
        Handbrake = string.IsNullOrEmpty(HandbrakeProfile) ? null : HandbrakeProfile,
    };

    public bool HasAny =>
        !string.IsNullOrEmpty(BaseProfile) || !string.IsNullOrEmpty(WheelProfile)
        || !string.IsNullOrEmpty(PedalsProfile) || !string.IsNullOrEmpty(HandbrakeProfile);
}
