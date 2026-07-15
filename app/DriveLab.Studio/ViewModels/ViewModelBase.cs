// ============================================================================
//  DriveLab
//  ViewModelBase.cs — Classe base abstrata de ViewModel, com Dispose virtual.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    public virtual void Dispose()
    {
    }
}
