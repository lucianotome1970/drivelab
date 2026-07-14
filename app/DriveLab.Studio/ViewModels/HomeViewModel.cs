namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Página inicial (dash): agrega os cards de visão geral — o Volante e um
/// resumo ao vivo dos Pedais — lado a lado, no estilo MOZA.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    public DashboardViewModel Wheel { get; }
    public PedalsViewModel Pedals { get; }
    public HandbrakeViewModel? Handbrake { get; }

    // "handbrake" é opcional (nulo) até a Tarefa 11 ligar a DI do freio de mão no
    // CompositionRoot; o card no Home tolera DataContext nulo até lá.
    public HomeViewModel(DashboardViewModel wheel, PedalsViewModel pedals, HandbrakeViewModel? handbrake = null)
    {
        Wheel = wheel;
        Pedals = pedals;
        Handbrake = handbrake;
    }

    public override void Dispose()
    {
        // O Home é dono do card do volante; os Pedais e o Freio de mão são descartados
        // pelas próprias páginas quando existirem.
        Wheel.Dispose();
        base.Dispose();
    }
}
