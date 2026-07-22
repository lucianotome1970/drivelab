// ============================================================================
//  DriveLab
//  AvaloniaProfileFilePicker.cs — IProfileFilePicker real (salvar/abrir .json de perfis) via IStorageProvider.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DriveLab.Studio.Services;

/// <summary>Diálogos nativos de salvar/abrir para os arquivos de perfil (.json).</summary>
public sealed class AvaloniaProfileFilePicker : IProfileFilePicker
{
    private static readonly FilePickerFileType JsonType = new("Perfis DriveLab (*.json)")
    {
        Patterns = new[] { "*.json" },
    };

    public async Task<string?> PickSaveAsync(string suggestedFileName)
    {
        if (GetTopLevel()?.StorageProvider is not { } storage)
            return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar perfis",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { JsonType },
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenAsync()
    {
        if (GetTopLevel()?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar perfis",
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonType },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static TopLevel? GetTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? TopLevel.GetTopLevel(desktop.MainWindow)
            : null;
}
