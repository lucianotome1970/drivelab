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
