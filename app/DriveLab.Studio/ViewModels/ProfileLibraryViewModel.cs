// ============================================================================
//  DriveLab
//  ProfileLibraryViewModel.cs — Componente reutilizável de perfis nomeados (lista/aplicar/salvar como/renomear/excluir).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Biblioteca de perfis nomeados embutível num VM de módulo: expõe a lista, o seletor e os
/// comandos (aplicar ao selecionar, salvar como, renomear, excluir). O módulo injeta como CAPTURAR a
/// config atual (<paramref name="capture"/>) e como APLICAR um perfil escrevendo no device
/// (<paramref name="apply"/>). Sem <see cref="INamedProfileStore{T}"/> (ex.: testes) vira no-op.</summary>
public sealed partial class ProfileLibraryViewModel<T> : ObservableObject where T : class
{
    private readonly INamedProfileStore<T>? _store;
    private readonly Func<T> _capture;
    private readonly Action<T> _apply;
    private bool _suppressApply;   // evita aplicar ao repovoar a lista

    public ProfileLibraryViewModel(INamedProfileStore<T>? store, Func<T> capture, Action<T> apply)
    {
        _store = store;
        _capture = capture;
        _apply = apply;
        Refresh();
    }

    public ObservableCollection<string> Profiles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string? _selectedName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    private string _newName = "";

    public void Refresh(string? select = null)
    {
        if (_store is null)
            return;
        _suppressApply = true;
        Profiles.Clear();
        foreach (var n in _store.ListNames())
            Profiles.Add(n);
        SelectedName = select is not null && Profiles.Contains(select) ? select : null;
        _suppressApply = false;
    }

    // Selecionar um perfil aplica-o na hora (a UI é gated por IsConnected no módulo).
    partial void OnSelectedNameChanged(string? value)
    {
        if (_suppressApply || value is null || _store is null)
            return;
        _ = ApplyAsync(value);
    }

    private async Task ApplyAsync(string name)
    {
        var p = await _store!.LoadAsync(name);
        if (p is not null)
            _apply(p);
    }

    /// <summary>Atualiza o perfil selecionado com a config atual (grava por cima do mesmo nome).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Save()
    {
        if (_store is null || SelectedName is null)
            return;
        await _store.SaveAsync(SelectedName, _capture());
    }

    [RelayCommand(CanExecute = nameof(CanSaveAs))]
    private async Task SaveAs()
    {
        var name = NewName.Trim();
        if (_store is null || name.Length == 0)
            return;
        await _store.SaveAsync(name, _capture());
        NewName = "";
        Refresh(select: name);
    }
    private bool CanSaveAs() => _store is not null && !string.IsNullOrWhiteSpace(NewName);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (_store is null || SelectedName is null)
            return;
        _store.Delete(SelectedName);
        Refresh();
    }
    private bool HasSelection() => _store is not null && SelectedName is not null;

    [RelayCommand(CanExecute = nameof(CanRename))]
    private void Rename()
    {
        var name = NewName.Trim();
        if (_store is null || SelectedName is null || name.Length == 0)
            return;
        _store.Rename(SelectedName, name);
        NewName = "";
        Refresh(select: name);
    }
    private bool CanRename() => _store is not null && SelectedName is not null && !string.IsNullOrWhiteSpace(NewName);
}
