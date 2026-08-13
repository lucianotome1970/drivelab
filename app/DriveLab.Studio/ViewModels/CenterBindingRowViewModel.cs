// ============================================================================
//  DriveLab
//  CenterBindingRowViewModel.cs — Uma linha da lista de atalhos de centralizar (estilo ACC): o rótulo
//    do mapeamento + o comando de removê-lo (o "×").
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Item da lista de mapeamentos do atalho de centralizar: rótulo + remover.</summary>
public sealed class CenterBindingRowViewModel
{
    public string Label { get; }
    public IRelayCommand RemoveCommand { get; }

    public CenterBindingRowViewModel(CenterBinding binding, Action remove)
    {
        Label = binding.Describe();
        RemoveCommand = new RelayCommand(remove);
    }
}
