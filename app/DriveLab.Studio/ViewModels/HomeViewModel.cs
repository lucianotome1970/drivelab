namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Página inicial (dash): agrega os cards de visão geral — o Volante e um
/// resumo ao vivo dos Pedais — lado a lado, no estilo MOZA.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    public DashboardViewModel Wheel { get; }
    public PedalsViewModel Pedals { get; }

    public HomeViewModel(DashboardViewModel wheel, PedalsViewModel pedals)
    {
        Wheel = wheel;
        Pedals = pedals;
    }

    public override void Dispose()
    {
        // O Home é dono do card do volante; os Pedais são descartados pela página Pedais.
        Wheel.Dispose();
        base.Dispose();
    }
}
