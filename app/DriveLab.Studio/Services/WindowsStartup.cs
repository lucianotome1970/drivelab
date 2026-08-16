// ============================================================================
//  DriveLab
//  WindowsStartup.cs — registra (ou tira) o Studio da inicialização do Windows.
//
//  ONDE E POR QUÊ: na chave Run do usuário atual (HKCU), e não na da máquina.
//  HKCU não pede administrador, vale só para quem configurou, e some junto com o
//  perfil. Um app de volante não tem por que se instalar para todos os usuários
//  do computador nem pedir elevação para uma conveniência.
//
//  ⚠️ TUDO AQUI ENGOLE FALHA. Registro bloqueado por política de empresa, perfil
//  sem permissão, antivírus implicando com escrita em Run — nada disso pode
//  impedir o app de abrir. A preferência fica marcada, o registro não muda, e o
//  pior resultado é o app não subir sozinho; quem depende disso percebe e liga de
//  novo. Derrubar o app por causa de uma conveniência seria a troca errada.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace DriveLab.Studio.Services;

public static class WindowsStartup
{
    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Nome  = "DriveLab Studio";

    /// <summary>Caminho do executável, entre aspas — sem elas, um caminho com espaço ("Program
    /// Files") faz o Windows tentar rodar só o primeiro pedaço e o app não sobe.</summary>
    private static string? ComandoDoExecutavel()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        return string.IsNullOrEmpty(exe) ? null : $"\"{exe}\"";
    }

    [SupportedOSPlatform("windows")]
    public static void Aplicar(bool ligado)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Chave, writable: true);
            if (run is null) return;
            if (ligado)
            {
                var cmd = ComandoDoExecutavel();
                if (cmd is not null) run.SetValue(Nome, cmd);
            }
            else
            {
                run.DeleteValue(Nome, throwOnMissingValue: false);
            }
        }
        catch
        {
            // ver o cabeçalho: conveniência nunca derruba o app.
        }
    }

    [SupportedOSPlatform("windows")]
    public static bool EstaRegistrado()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Chave);
            return run?.GetValue(Nome) is not null;
        }
        catch { return false; }
    }
}
