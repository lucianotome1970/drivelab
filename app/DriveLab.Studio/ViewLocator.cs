// ============================================================================
//  DriveLab
//  ViewLocator.cs — Localiza a View correspondente a um ViewModel por convenção de nomes/namespace.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio;

public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "null" };

        var name = data.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "View não encontrada: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
