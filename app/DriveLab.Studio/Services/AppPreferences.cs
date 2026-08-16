// ============================================================================
//  DriveLab
//  AppPreferences.cs — as preferências de quem usa o app: iniciar com o Windows
//  e continuar na bandeja ao fechar.
//
//  POR QUE ISTO EXISTE: os atalhos de centralizar — tecla, botão do aro — só
//  funcionam com o Studio aberto. Ele é quem escuta o teclado e lê os botões, e
//  quem manda o comando para a base. Fechado, ninguém está escutando, e o atalho
//  simplesmente não existe mais.
//
//  A saída não é um serviço do Windows: serviços rodam na sessão 0, isolados da
//  sessão de quem está logado, e NÃO enxergam teclado nem HID do usuário. Um hook
//  global dentro de um serviço não recebe nada. O que funciona é o próprio app
//  continuar vivo — que é também o desenho mais simples, porque evita dois
//  processos disputando a base (abrir um segundo handle no mesmo endpoint já
//  derrubou a base do USB neste projeto).
//
//  ⚠️ AS DUAS PREFERÊNCIAS NASCEM LIGADAS, e isso é decisão de produto: quem
//  instala um app de volante espera que o volante funcione sem abrir nada. Mas
//  ficam VISÍVEIS e desligáveis — software que inicia sozinho e não morre quando
//  se fecha a janela precisa dizer isso na cara, não escondido.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using System.Text.Json;

namespace DriveLab.Studio.Services;

public sealed class AppPreferences
{
    private readonly string _path;

    public AppPreferences(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "app-preferences.json");
    }

    /// <summary>Abrir o Studio no logon, com a janela à vista.</summary>
    public bool IniciarComWindows { get; set; } = true;

    /// <summary>Fechar a janela deixa o app na bandeja em vez de encerrar. Desligado, o X encerra
    /// mesmo — e os atalhos param junto, que é o que a tela precisa avisar.</summary>
    public bool ManterNaBandeja { get; set; } = true;

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var d = JsonSerializer.Deserialize<Dados>(File.ReadAllText(_path));
            if (d is null) return;
            IniciarComWindows = d.IniciarComWindows;
            ManterNaBandeja   = d.ManterNaBandeja;
        }
        catch
        {
            // Preferência é conveniência: arquivo corrompido volta ao padrão em vez de impedir o app
            // de abrir. Um app que não sobe porque não conseguiu ler uma opção é pior que a opção.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(
                new Dados { IniciarComWindows = IniciarComWindows, ManterNaBandeja = ManterNaBandeja },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // idem: não derruba nada.
        }
    }

    private sealed class Dados
    {
        public bool IniciarComWindows { get; set; } = true;
        public bool ManterNaBandeja { get; set; } = true;
    }
}
