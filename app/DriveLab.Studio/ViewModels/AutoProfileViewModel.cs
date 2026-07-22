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
using CommunityToolkit.Mvvm.Input;
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

        _baseProfiles = baseProfiles;
        _wheelProfiles = wheelProfiles;
        _pedalsProfiles = pedalsProfiles;
        _handbrakeProfiles = handbrakeProfiles;

        foreach (var g in GameCatalog.WithCustom(map.CustomGames))
            Games.Add(NewBinding(g, map.CustomGames.Any(c => c.Id == g.Id)));

        _currentGame = service.CurrentGame?.DisplayName ?? "—";
        _service.GameChanged += (_, game) => _dispatcher.Post(() =>
        {
            CurrentGame = game?.DisplayName ?? "—";
            // Se a troca foi pulada por alterações não salvas, avisa em vez de falhar em silêncio.
            SkippedMessage = _service.LastSwitchSkipped
                ? "Troca automática pausada: há alterações não salvas em algum módulo. Salve ou descarte para trocar."
                : "";
        });
    }

    private readonly ObservableCollection<string> _baseProfiles;
    private readonly ObservableCollection<string> _wheelProfiles;
    private readonly ObservableCollection<string> _pedalsProfiles;
    private readonly ObservableCollection<string> _handbrakeProfiles;

    private GameBindingViewModel NewBinding(KnownGame g, bool isCustom) =>
        new(g, _map.For(g.Id), _baseProfiles, _wheelProfiles, _pedalsProfiles, _handbrakeProfiles, Persist)
        { IsCustom = isCustom };

    /// <summary>Liga/desliga a troca automática.</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>Jogo detectado ao vivo.</summary>
    [ObservableProperty] private string _currentGame = "—";

    /// <summary>Uma linha por jogo (catálogo embutido + os que o usuário adicionou).</summary>
    public ObservableCollection<GameBindingViewModel> Games { get; } = new();

    /// <summary>Aviso quando a troca automática foi pulada (alterações não salvas em algum módulo).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSkippedMessage))]
    private string _skippedMessage = "";
    public bool HasSkippedMessage => !string.IsNullOrEmpty(SkippedMessage);

    // ---- adicionar um jogo que não está no catálogo ----
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomGameCommand))]
    private string _newGameName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCustomGameCommand))]
    private string _newGameExe = "";

    [RelayCommand(CanExecute = nameof(CanAddCustomGame))]
    private void AddCustomGame()
    {
        var exe = NewGameExe.Trim();
        var name = string.IsNullOrWhiteSpace(NewGameName) ? exe : NewGameName.Trim();
        var custom = new CustomGame
        {
            Id = "custom:" + exe.ToLowerInvariant(),
            DisplayName = name,
            ProcessName = exe,
        };
        if (_map.CustomGames.Any(c => c.Id == custom.Id)) return;   // já mapeado

        _map.CustomGames.Add(custom);
        Games.Add(NewBinding(new KnownGame(custom.Id, custom.DisplayName, new[] { custom.ProcessName }), isCustom: true));
        NewGameName = "";
        NewGameExe = "";
        Persist();
    }
    private bool CanAddCustomGame() => !string.IsNullOrWhiteSpace(NewGameExe);

    [RelayCommand]
    private void RemoveCustomGame(GameBindingViewModel? game)
    {
        if (game is null || !game.IsCustom) return;
        _map.CustomGames.RemoveAll(c => c.Id == game.GameId);
        _map.Bindings.Remove(game.GameId);
        Games.Remove(game);
        Persist();
    }

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
