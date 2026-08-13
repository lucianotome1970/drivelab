// ============================================================================
//  DriveLab
//  ICaptureFilePicker.cs — Escolhe o arquivo de uma volta gravada (.dlffb).
//
//  Separado de IFilePicker (firmware) e de IProfileFilePicker (perfis) pelo mesmo
//  motivo dos outros dois: cada um filtra por uma extensão diferente e tem um
//  título diferente no diálogo, e juntá-los obrigaria quem só precisa de um a
//  implementar os três nos testes.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;   // TryGetLocalPath é EXTENSÃO daqui — sem este using não compila

namespace DriveLab.Studio.Services;

/// <summary>Escolhe um arquivo de volta gravada para reproduzir. Null = a pessoa desistiu.</summary>
public interface ICaptureFilePicker
{
    Task<string?> PickCaptureAsync();
}

/// <summary>Diálogo nativo, filtrado para as gravações do DriveLab.</summary>
public sealed class AvaloniaCaptureFilePicker : ICaptureFilePicker
{
    public const string Extensao = "dlffb";

    public async Task<string?> PickCaptureAsync()
    {
        var topLevel = Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? TopLevel.GetTopLevel(desktop.MainWindow)
            : null;

        if (topLevel?.StorageProvider is not { } storage) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar volta gravada",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType($"Volta gravada DriveLab (*.{Extensao})")
                {
                    Patterns = new[] { $"*.{Extensao}" },
                },
            },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
