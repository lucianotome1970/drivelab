// ============================================================================
//  DriveLab
//  HomeViewModel.cs — VM da página inicial: agrega os cards do Volante e o resumo dos Pedais.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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
