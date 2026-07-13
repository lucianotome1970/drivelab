using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    public virtual void Dispose()
    {
    }
}
