using Qtoxide.Services;

namespace Qtoxide.ViewModels;

/// <summary>Everything the view models need from the outside world.</summary>
public sealed record AppServices(
    ProfileManager Profiles,
    IUiDispatcher Dispatcher,
    IPlatformServices Platform,
    INotifier Notifier,
    Func<CancellationToken, Task<List<BootstrapNode>>> LoadBootstrapNodes);
