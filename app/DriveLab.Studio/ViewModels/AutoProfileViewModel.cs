// ============================================================================
//  DriveLab
//  AutoProfileViewModel.cs — Tela do auto-perfil: liga/desliga, jogo atual e o mapa jogo→perfis por módulo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Games;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Tela do auto-perfil por jogo: um master (liga/desliga) + o jogo detectado ao vivo + uma linha por sim, onde
/// o usuário escolhe o perfil a carregar em cada módulo. Toda mudança persiste no <see cref="GameProfileMap"/>
/// e reflete no <see cref="AutoProfileService"/>.
/// </summary>
public partial class AutoProfileViewModel : ViewModelBase
{
    private readonly AutoProfileService _service;
    private readonly GameProfileMap _map;
    private readonly JsonGameProfileMapStore _store;
    private readonly IUiDispatcher _dispatcher;

    public AutoProfileViewModel(
        AutoProfileService service, GameProfileMap map, JsonGameProfileMapStore store, IUiDispatcher dispatcher,
        ObservableCollection<string> baseProfiles, ObservableCollection<string> wheelProfiles,
        ObservableCollection<string> pedalsProfiles, ObservableCollection<string> handbrakeProfiles)
    {
        _service = service;
        _map = map;
        _store = store;
        _dispatcher = dispatcher;
        _enabled = map.Enabled;

        Games = GameCatalog.All.Select(g => new GameBindingViewModel(
            g, map.For(g.Id), baseProfiles, wheelProfiles, pedalsProfiles, handbrakeProfiles, Persist)).ToList();

        _currentGame = service.CurrentGame?.DisplayName ?? "—";
        _service.GameChanged += (_, game) =>
            _dispatcher.Post(() => CurrentGame = game?.DisplayName ?? "—");
    }

    /// <summary>Liga/desliga a troca automática.</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>Jogo detectado ao vivo.</summary>
    [ObservableProperty] private string _currentGame = "—";

    public IReadOnlyList<GameBindingViewModel> Games { get; }

    partial void OnEnabledChanged(bool value)
    {
        _service.Enabled = value;
        if (value) _service.Start(); else _service.Stop();
        Persist();
    }

    /// <summary>Reconstrói o mapa a partir das linhas e salva.</summary>
    private void Persist()
    {
        _map.Bindings.Clear();
        foreach (var g in Games)
            if (g.HasAny)
                _map.Set(g.GameId, g.ToModuleProfiles());
        _map.Enabled = Enabled;
        _store.Save(_map);
    }
}
