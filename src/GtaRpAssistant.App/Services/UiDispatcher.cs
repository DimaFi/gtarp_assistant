namespace GtaRpAssistant.App.Services;

public interface IUiDispatcher
{
    void Invoke(Action action);
}

public sealed class UiDispatcher : IUiDispatcher
{
    public void Invoke(Action action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) action(); else dispatcher.Invoke(action);
    }
}
