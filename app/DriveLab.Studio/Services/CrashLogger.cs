// ============================================================================
//  DriveLab
//  CrashLogger.cs — Handler global de log: grava exceções fatais e tasks não observadas em crash.log.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;

namespace DriveLab.Studio.Services;

/// <summary>Log global de erros em arquivo (%AppData%/DriveLab/crash.log). Captura exceções não
/// tratadas (qualquer thread) e tasks não observadas, para diagnosticar crashes em campo (ex.:
/// hardware no Windows). Nunca lança — logar não pode ser fatal.</summary>
public static class CrashLogger
{
    private static readonly object Gate = new();

    /// <summary>Caminho padrão do log: %AppData%/DriveLab/crash.log.</summary>
    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DriveLab", "crash.log");

    /// <summary>Instala os hooks globais (chamar no início do Main).</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteTo(DefaultLogPath, "UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteTo(DefaultLogPath, "UnobservedTaskException", e.Exception);
            e.SetObserved(); // impede escalonamento; já registramos
        };
    }

    /// <summary>Log manual de um erro (ex.: falha de escrita HID engolida).</summary>
    public static void Log(string context, Exception? ex) => WriteTo(DefaultLogPath, context, ex);

    /// <summary>Núcleo testável: anexa uma entrada com timestamp ao arquivo indicado.</summary>
    public static void WriteTo(string path, string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex?.ToString() ?? "(sem exceção)"}{Environment.NewLine}{Environment.NewLine}";
            lock (Gate)
                File.AppendAllText(path, entry);
        }
        catch
        {
            // Logar nunca pode derrubar o app.
        }
    }
}
