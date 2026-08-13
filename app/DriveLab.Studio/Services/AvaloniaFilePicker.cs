// ============================================================================
//  DriveLab
//  AvaloniaFilePicker.cs — Implementação real de IFilePicker usando
//  Avalonia.IStorageProvider (janela principal do app em execução).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace DriveLab.Studio.Services;

/// <summary>Abre o diálogo nativo de "Abrir arquivo" filtrado para firmware DriveLab (.bin/.uf2).</summary>
public sealed class AvaloniaFilePicker : IFilePicker
{
    public async Task<string?> PickFirmwareFileAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel?.StorageProvider is not { } storage)
            return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar firmware",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Firmware DriveLab (*.bin, *.uf2)")
                {
                    Patterns = new[] { "*.bin", "*.uf2" },
                },
            },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private static TopLevel? GetTopLevel() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? TopLevel.GetTopLevel(desktop.MainWindow)
            : null;
}
