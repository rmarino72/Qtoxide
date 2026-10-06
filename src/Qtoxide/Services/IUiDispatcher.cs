namespace Qtoxide.Services;

/// <summary>Runs code on the UI thread (Tox events arrive on the protocol thread).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}
