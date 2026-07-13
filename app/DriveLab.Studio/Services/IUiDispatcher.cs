namespace DriveLab.Studio.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
