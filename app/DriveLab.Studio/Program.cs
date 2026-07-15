// ============================================================================
//  DriveLab
//  Program.cs — Ponto de entrada: detecta idioma do sistema e inicia o Avalonia com lifetime desktop clássico.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using DriveLab.Studio.Localization;

namespace DriveLab.Studio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Idioma pelo sistema (Windows): pt → Português; qualquer outro → Inglês.
        LocalizationManager.DetectFromSystem();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
