using Avalonia;
using Avalonia.Markup.Xaml;

namespace DriveLab.Studio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
