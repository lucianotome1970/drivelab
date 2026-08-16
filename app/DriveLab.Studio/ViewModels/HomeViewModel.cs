// ============================================================================
//  DriveLab
//  HomeViewModel.cs — VM da página inicial: agrega os cards do Volante e o resumo dos Pedais.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Página inicial (dash): agrega os cards de visão geral — o Volante e um
/// resumo ao vivo dos Pedais — lado a lado, no estilo MOZA.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    public DashboardViewModel Wheel { get; }
    public BaseViewModel? Base { get; }
    public PedalsViewModel Pedals { get; }
    public HandbrakeViewModel? Handbrake { get; }

    // ── PREFERÊNCIAS DO APP ─────────────────────────────────────────────────────────────────────
    // Ficam à vista, e não escondidas num menu, porque descrevem comportamento que a pessoa PRECISA
    // saber: o app inicia sozinho e continua vivo depois que a janela fecha. Software que faz isso
    // sem dizer é o que dá má fama a quem escuta teclado — e o Studio escuta, para os atalhos.

    /// <summary>Abrir o Studio junto com o Windows. Escreve na chave Run do usuário na hora, para o
    /// efeito ser imediato em vez de valer só no próximo boot.</summary>
    public bool IniciarComWindows
    {
        get => App.Preferencias.IniciarComWindows;
        set
        {
            if (App.Preferencias.IniciarComWindows == value) return;
            App.Preferencias.IniciarComWindows = value;
            App.Preferencias.Save();
            if (OperatingSystem.IsWindows()) WindowsStartup.Aplicar(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Fechar a janela deixa o app na bandeja. Desligado, o X encerra — e os atalhos de
    /// centralizar param junto, porque é o Studio quem os escuta.</summary>
    public bool ManterNaBandeja
    {
        get => App.Preferencias.ManterNaBandeja;
        set
        {
            if (App.Preferencias.ManterNaBandeja == value) return;
            App.Preferencias.ManterNaBandeja = value;
            App.Preferencias.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Navegação por clique no card (key do módulo → página). Ligada pelo CompositionRoot,
    /// que é quem conhece a lista de páginas e a janela principal.</summary>
    public Action<string>? ModuleNavigator { get; set; }

    /// <summary>Clique no card abre a tela do módulo — só quando o dispositivo está detectado/conectado.
    /// Cliques em botões/sliders dentro do card são filtrados na view (não chegam aqui).</summary>
    public void OpenModule(string key)
    {
        var connected = key switch
        {
            "pedals" => Pedals.IsConnected,
            "handbrake" => Handbrake?.IsConnected == true,
            "base" => Base?.IsConnected == true,
            "wheel" => Wheel.IsConnected,
            _ => false,
        };
        if (connected)
            ModuleNavigator?.Invoke(key);
    }

    // "handbrake"/"baseWheel" são opcionais (nulos) até o CompositionRoot ligar a DI;
    // os cards no Home toleram DataContext nulo até lá.
    public HomeViewModel(DashboardViewModel wheel, PedalsViewModel pedals,
        HandbrakeViewModel? handbrake = null, BaseViewModel? baseWheel = null)
    {
        Wheel = wheel;
        Pedals = pedals;
        Handbrake = handbrake;
        Base = baseWheel;
    }

    public override void Dispose()
    {
        // O Home é dono dos cards do volante e da base (mesma sessão); os Pedais e o
        // Freio de mão são descartados pelas próprias páginas quando existirem.
        Wheel.Dispose();
        Base?.Dispose();
        base.Dispose();
    }
}
