using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Qtoxide.Services;
using Qtoxide.ViewModels;
using Qtoxide.Views;

namespace Qtoxide;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var paths = new AppPaths();
            var services = new AppServices(
                new ProfileManager(paths),
                new AvaloniaDispatcher(),
                new AvaloniaPlatformServices(window),
                new DesktopNotifier(),
                ct => BootstrapNodes.GetAsync(paths, ct));
            var vm = new MainWindowViewModel(services);
            window.DataContext = vm;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
