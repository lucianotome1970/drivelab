// ============================================================================
//  DriveLab
//  Program.cs — Ponto de entrada: detecta idioma do sistema e inicia o Avalonia com lifetime desktop clássico.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using Avalonia;
using DriveLab.Studio.Localization;
using DriveLab.Studio.Services;

namespace DriveLab.Studio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Log global de erros (%AppData%/DriveLab/crash.log) — 1ª coisa, p/ capturar tudo.
        CrashLogger.Install();
        // Erros de I/O HID (engolidos) também vão pro log.
        DriveLab.Hid.HidSharpChannel.OnError = CrashLogger.Log;

        // Idioma pelo sistema (Windows): pt → Português; qualquer outro → Inglês.
        LocalizationManager.DetectFromSystem();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Crash fatal que escapou do loop do Avalonia: registra antes de morrer.
            CrashLogger.Log("Fatal", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
