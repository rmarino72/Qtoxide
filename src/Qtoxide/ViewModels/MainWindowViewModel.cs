using CommunityToolkit.Mvvm.ComponentModel;
using Qtoxide.Services;

namespace Qtoxide.ViewModels;

/// <summary>Shell: shows the login page, then the main page of the open profile.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly AppServices _services;
    private readonly AppSettings _appSettings;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _appSettings = JsonFiles.Load<AppSettings>(services.Profiles.Paths.AppSettingsFile);
        _current = new LoginViewModel(services, OnLoggedIn, _appSettings.LastProfile);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private ViewModelBase _current;

    public string Title => Current is MainViewModel main ? $"Qtoxide — {main.ProfileName}" : "Qtoxide";

    private void OnLoggedIn(ProfileSession session)
    {
        _appSettings.LastProfile = session.Name;
        JsonFiles.Save(_services.Profiles.Paths.AppSettingsFile, _appSettings);
        Current = new MainViewModel(session, _services, Logout);
    }

    private void Logout()
    {
        if (Current is MainViewModel main)
            main.Dispose();
        Current = new LoginViewModel(_services, OnLoggedIn, _appSettings.LastProfile);
    }

    public void Dispose()
    {
        if (Current is MainViewModel main)
            main.Dispose();
    }
}
